param(
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$appName = 'MoveToMidi'
$repoName = 'MoveToMidi'
$repoFullName = "OnjLouis/$repoName"
$portableDir = Join-Path $repoRoot 'portable'
$stageDir = Join-Path $repoRoot 'release-stage'

function Info([string]$message) { Write-Host "[MoveToMidi release] $message" }
function Fail([string]$message) { throw $message }

function Read-AppVersion {
    $source = Get-Content -LiteralPath (Join-Path $repoRoot 'Program.cs') -Raw
    if ($source -notmatch 'private const string Version\s*=\s*"([^"]+)"') { Fail 'Could not read app version from Program.cs.' }
    return $Matches[1]
}

function Get-GitHubReleaseToken {
    if (!([string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN))) { return $env:GITHUB_TOKEN.Trim() }
    if (!([string]::IsNullOrWhiteSpace($env:GH_TOKEN))) { return $env:GH_TOKEN.Trim() }
    foreach ($path in @((Join-Path (Split-Path -Parent $repoRoot) 'token.txt'), (Join-Path $repoRoot 'token.txt'))) {
        if (![string]::IsNullOrWhiteSpace($path) -and (Test-Path -LiteralPath $path)) {
            $token = (Get-Content -LiteralPath $path -Raw).Trim()
            if (!([string]::IsNullOrWhiteSpace($token))) { return $token }
        }
    }
    return ''
}

function Get-GitHubHeaders {
    $headers = @{ 'User-Agent' = 'MoveToMidi release check'; 'Accept' = 'application/vnd.github+json' }
    $token = Get-GitHubReleaseToken
    if (!([string]::IsNullOrWhiteSpace($token))) { $headers['Authorization'] = "Bearer $token" }
    return $headers
}

function Assert-CleanSourceTree {
    foreach ($name in @('MoveToMidi.ini', 'MoveToMidi failures.log', 'token.txt')) {
        if (Test-Path -LiteralPath (Join-Path $repoRoot $name)) { Fail "Refusing to release with $name in the source folder." }
    }
}

function Assert-PackageClean([string]$zipPath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        foreach ($entry in $zip.Entries) {
            $name = [System.IO.Path]::GetFileName($entry.FullName)
            if ($name -match '\.(ini|log|tmp)$' -or $name -ieq 'token.txt') { Fail "Release ZIP contains forbidden file: $($entry.FullName)" }
        }
    } finally { $zip.Dispose() }
}

function New-ReleasePackage([string]$version) {
    Remove-Item -LiteralPath $portableDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stageDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $portableDir | Out-Null
    New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
    Info 'Building executable.'
    & (Join-Path $repoRoot 'Build.ps1') | Out-Host
    $builtExe = Join-Path $portableDir 'MoveToMidi.exe'
    if (-not (Test-Path -LiteralPath $builtExe)) { Fail "Build did not produce $builtExe." }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination (Join-Path $portableDir 'README.md') -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE.txt') -Destination (Join-Path $portableDir 'LICENSE.txt') -Force
    $zipPath = Join-Path $stageDir ("MoveToMidi-$version-portable.zip")
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Compress-Archive -Path (Join-Path $portableDir '*') -DestinationPath $zipPath -Force
    Assert-PackageClean $zipPath
    return $zipPath
}

function Assert-GitHubActivityChecked {
    Info 'Checking GitHub issues and pull requests.'
    $headers = Get-GitHubHeaders
    try {
        $issues = Invoke-RestMethod -Uri "https://api.github.com/repos/$repoFullName/issues?state=open&per_page=100" -Headers $headers
        $realIssues = @($issues | Where-Object { -not $_.pull_request })
        if ($realIssues.Count -gt 0) { Fail ("Open GitHub issues need review before release: " + (($realIssues | ForEach-Object { "#$($_.number) $($_.title)" }) -join '; ')) }
        Info 'No open GitHub issues.'
        $pulls = @(Invoke-RestMethod -Uri "https://api.github.com/repos/$repoFullName/pulls?state=open&per_page=100" -Headers $headers | Where-Object { $_ -and $_.number })
        if ($pulls.Count -gt 0) { Fail ("Open GitHub pull requests need review before release: " + (($pulls | ForEach-Object { "#$($_.number) $($_.title)" }) -join '; ')) }
        Info 'No open GitHub pull requests.'
    } catch { Info "GitHub activity check skipped or unavailable: $($_.Exception.Message)" }
}

function Publish-GitHubRelease([string]$version, [string]$zipPath) {
    $headers = Get-GitHubHeaders
    if (!$headers.ContainsKey('Authorization')) { Fail 'No GitHub token found in GITHUB_TOKEN, GH_TOKEN, or token.txt.' }
    $tag = "v$version"
    $releaseBody = @"
MoveToMidi $version

- Fixes GitHub update checks in the app.
- Adds Ctrl+F1 and Help > Project on GitHub.
- Adds Help > Donate.
- Aligns About and executable metadata with the companion apps.
"@
    try {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repoFullName/releases/tags/$tag" -Headers $headers
        Info "Release $tag already exists; using it."
    } catch {
        $body = @{ tag_name = $tag; name = "MoveToMidi $version"; body = $releaseBody; draft = $false; prerelease = $false } | ConvertTo-Json
        $release = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$repoFullName/releases" -Headers $headers -Body $body -ContentType 'application/json'
        Info "Created release $tag."
    }
    $assetName = [System.IO.Path]::GetFileName($zipPath)
    if ($release.assets) {
        foreach ($asset in @($release.assets | Where-Object { $_.name -eq $assetName })) {
            Invoke-RestMethod -Method Delete -Uri $asset.url -Headers $headers | Out-Null
            Info "Removed existing release asset $assetName."
        }
    }
    $uploadUrl = ([string]$release.upload_url) -replace '\{\?name,label\}', "?name=$([uri]::EscapeDataString($assetName))"
    Invoke-RestMethod -Method Post -Uri $uploadUrl -Headers $headers -InFile $zipPath -ContentType 'application/zip' | Out-Null
    Info "Uploaded $assetName."
}

Assert-CleanSourceTree
$version = Read-AppVersion
$zip = New-ReleasePackage $version
Assert-GitHubActivityChecked
Info "Release package: $zip"
if ($Publish) { Publish-GitHubRelease $version $zip }

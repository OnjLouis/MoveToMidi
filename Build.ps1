param(
    [string]$OutputPath = "$PSScriptRoot\portable\MoveToMidi.exe",
    [switch]$CreateDefaultIni
)

$ErrorActionPreference = "Stop"

$sourceDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$targetExe = $OutputPath
$targetDir = Split-Path -Parent $targetExe
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) {
    $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

if (-not (Test-Path $csc)) {
    throw "Could not find the .NET Framework C# compiler."
}

New-Item -ItemType Directory -Force $targetDir | Out-Null

& $csc `
    /nologo `
    /target:winexe `
    /platform:x64 `
    /optimize+ `
    /out:$targetExe `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.IO.Compression.dll `
    /reference:System.IO.Compression.FileSystem.dll `
    /reference:System.Web.Extensions.dll `
    /reference:System.Windows.Forms.dll `
    (Join-Path $sourceDir "Program.cs")

if ($LASTEXITCODE -ne 0) {
    throw "C# compile failed with exit code $LASTEXITCODE."
}

Get-Item $targetExe

$iniPath = Join-Path $targetDir "MoveToMidi.ini"
if ($CreateDefaultIni -and -not (Test-Path $iniPath)) {
@"
[Settings]
OutputMode=AlongsideSourceFiles
OutputFolder=
LastInputFolder=
AskForOutputLocationAfterInput=True
AddConvertedToFileNames=True
ExportAutomation=True
AutomationBaseController=20
UpdateCheckFrequency=Startup
InstallUpdatesQuietly=False
LastAutomaticUpdateCheckUtc=
"@ | Set-Content -Path $iniPath -Encoding ASCII
    Get-Item $iniPath
}

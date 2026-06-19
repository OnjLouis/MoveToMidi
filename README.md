# MoveToMidi

Accessible Windows utility for converting Ableton Move and Ableton Note `.ablbundle` sets to standard MIDI files.

Current version: 1.1.

Project page: <https://github.com/OnjLouis/MoveToMidi>

## Build

```powershell
.\Build.ps1
```

The build script writes:

```text
portable\MoveToMidi.exe
```

To choose another output path:

```powershell
.\Build.ps1 -OutputPath "C:\Tools\MoveToMidi\MoveToMidi.exe"
```

The build script does not create an INI file by default. Use `-CreateDefaultIni` when you want a starter `MoveToMidi.ini` beside the executable.

## Release

Run:

```powershell
.\Release.ps1
```

To publish a GitHub release after the package checks pass:

```powershell
.\Release.ps1 -Publish
```

The release package includes `MoveToMidi.exe`, `README.md`, and `LICENSE.txt`. It must not include `MoveToMidi.ini`, logs, temp files, or token files.

## Behavior

- Opens one or more `.ablbundle` files or a folder of bundles.
- Reads `Song.abl` directly from each bundle without extracting the archive.
- Writes standard MIDI files.
- Uses explicit track names when present, otherwise the first device name, otherwise `Track N`.
- Exports clip slots as consecutive scenes because Move/Note bundles do not contain a linear arrangement.
- Preserves notes, velocities, tempo, time signature, and track names.
- Numeric clip envelopes are exported as MIDI controller data on configurable controller numbers.
- Source bundles are never overwritten.
- Failure details are written to `MoveToMidi failures.log` beside the executable.
- `Help > Check for Updates...` checks GitHub Releases.
- `Help > Version History...` shows the latest GitHub release notes.

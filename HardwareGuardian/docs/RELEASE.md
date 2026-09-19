# Release process

A release is produced by a build, never by hand. Every artefact is generated, hashed and described
by the same script run - `scripts/release.ps1`.

## Artefacts

| File | Produced by | Content |
| --- | --- | --- |
| `HardwareGuardian-Portable-x64.exe` | `dotnet publish` (single file, self-contained) | The application, no installation, no registry entry |
| `HardwareGuardianSetup-x64.exe` | Inno Setup (`installer/HardwareGuardian.iss`) | Installer with Start Menu entry, optional desktop shortcut and uninstaller |
| `HardwareGuardian-Checksums.txt` | `scripts/release.ps1` | SHA-256 of every artefact in the release folder |
| `HardwareGuardian-ReleaseNotes.txt` | `scripts/release.ps1` | Version, commit, build date, artefact list, verification steps, safety properties, recent commits |

## Versioning

`build/Version.props` is the single source of truth: `VersionPrefix` and `VersionSuffix` (SemVer).
The commit is injected at build time, never written into a file by hand.

## Release checklist

1. `bash tools/verify-all.sh` - contracts, localisation, XAML, projects, syntax.
2. `pwsh ./scripts/test.ps1` - the automated tests must pass (a failure stops the release).
3. `pwsh ./scripts/release.ps1` - build, package, hash, write the notes.
4. Compare the hash of the portable exe with `HardwareGuardian-Checksums.txt`:
   `Get-FileHash -Algorithm SHA256 .\HardwareGuardian-Portable-x64.exe`
5. Run the portable exe **on the reference machine** and record what it detected. The reference
   machine must not be modified (rule 89).
6. Attach the four artefacts to the release together with the raw output of steps 1-3.

## Verification on a user machine

```powershell
Get-FileHash -Algorithm SHA256 .\HardwareGuardianSetup-x64.exe
# compare with the matching line in HardwareGuardian-Checksums.txt
```

## What a release must never contain

* a "PDF export" that is really a renamed text file (PDF export is blocked by design),
* a driver, firmware or BIOS file - the application never bundles or flashes any of them,
* telemetry or an update check that runs without the user having enabled it,
* a placeholder, a `NotImplementedException` or a TODO in a code path that the UI can reach.

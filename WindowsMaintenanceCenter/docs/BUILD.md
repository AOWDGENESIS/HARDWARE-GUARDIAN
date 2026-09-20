# Building Windows Maintenance Center

This document describes how the application is built, tested and packaged. It is written to be
executed on a Windows machine with the pinned .NET SDK - **not** in a Linux container without a SDK.
The state of the repository in such a container is documented in [STATUS.md](STATUS.md).

## Prerequisites

| Requirement | Version | Notes |
| --- | --- | --- |
| .NET SDK | 10.0.100 (see `global.json`, `rollForward: latestFeature`) | Windows Desktop workload is part of the SDK |
| Windows | 10 1809 or later (x64) | WPF requires Windows |
| Inno Setup | 6.x | Only needed for `scripts/release.ps1` |
| PowerShell | 5.1 or 7.x | The scripts avoid PowerShell 7-only syntax |
| Python | 3.11+ | Only for the checks in `tools/` (they run with and without a SDK) |

## Commands

```powershell
# 1. checks that need no SDK (contracts, localisation, XAML, projects, syntax)
bash tools/verify-all.sh                     # or: python3 tools/check-contracts.py ...

# 2. build
pwsh ./scripts/build.ps1 -Configuration Release

# 3. tests
pwsh ./scripts/test.ps1 -Configuration Release

# 4. release artefacts (portable exe, setup exe, checksums, release notes)
pwsh ./scripts/release.ps1
```

`scripts/build.ps1` injects the commit (`SourceRevisionId`) and the build date (`BuildDate`) from
`eng/Version.props`; the version itself is changed in that single file.

## Dependency policy

* Central Package Management is enabled (`Directory.Packages.props`), so a package version exists
  exactly once in the repository. Versions are exact, never ranges: `dotnet restore` must produce the
  same graph on every machine.
* `packages.lock.json` is enabled per project, and restoring with `--locked-mode` is the way to prove
  that a machine really used the pinned graph.
* NuGet sources are restricted to `nuget.org` (`nuget.config`). No private feed, no build script that
  downloads a zip from a vendor page.
* `TreatWarningsAsErrors` is enabled in CI. A nullable warning is a defect, not a preference.

### Registry access

`Microsoft.Win32.Registry` is referenced explicitly and pinned even though the registry types are
type forwarders on modern .NET. That keeps the class libraries independent of the desktop SDK
(`Microsoft.WindowsDesktop.App`) and therefore testable on a headless build agent.

## Diagnostic modules

`WindowsMaintenanceCenter.Diagnostics` holds the modules that run inside `ScanOrchestrator`. They derive from
`DiagnosticModuleBase`, which performs the timing, the problem registration and the status
derivation. A module only implements the analysis and states how many checks it really performed -
a module that performed no check reports `Unknown` instead of `Healthy`.

## Reproducibility

* The build is deterministic (`Deterministic=true`), produces a `SourceLink`-compatible PDB and
  writes the commit into `InformationalVersion`.
* The release artefacts are the portable single-file executable and the installer built from exactly
  that executable. `scripts/release.ps1` hashes both files and fails if a file is missing, so a
  checksum file always describes files that exist.
* There is no post-processing step: what is hashed is what was built.

## Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| `dotnet` is not found | SDK missing or not on `PATH` | Install the SDK version from `global.json` |
| `NU1101`/`NU1301` during restore | No network access to nuget.org | Use a machine with access; do not add a private feed |
| `ISCC.exe` not found | Inno Setup missing | Install Inno Setup 6 or pass `-InnoSetupPath` |
| `NETSDK1100`/`NETSDK1136` | Building a `-windows` TFM on Linux | Build on Windows |

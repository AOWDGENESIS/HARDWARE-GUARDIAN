# Windows Maintenance Center

**Windows Hardware Diagnostics, Maintenance & Update Center.** A local Windows desktop application
that diagnoses hardware and Windows state, explains what it found, and changes something only when
you approve it - with a backup and a way back.

This is not a driver booster, not a registry cleaner and not an optimiser that promises numbers it
cannot prove. It is a diagnostic tool with a protocol.

## What it does

* **Detects** CPU, mainboard (including board revision), BIOS/UEFI, RAM, GPU, storage (SMART, NVMe
  wear), network, Wi-Fi, Bluetooth, audio, USB, PCI/PCIe, chipset, monitors, printers, battery and
  sensors. Every value carries its origin (`WMI`, `Windows API`, `registry`, `official manufacturer`,
  `local file`, `user input`); a value that cannot be read is `UNKNOWN` with a reason - never a zero.
* **Diagnoses** drivers and PnP health, Windows integrity (DISM/SFC), event log, Defender state and
  Windows Update state, and reports each finding with an id such as `HW-CPU-001`.
* **Maintains** only what is safe: scan → plan → **mandatory dry run** → approval → execute → verify.
  Categories are `SAFE`, `OPTIONAL`, `PROTECTED` or `UNKNOWN`; protected and unknown categories are
  measured and explained but never cleaned.
* **Checks updates** against official sources only (manufacturer → Microsoft → Windows Update →
  verified OEM). Firmware is assessed but never flashed - the manual step stays manual.
* **Documents** everything: live protocol (time, module, action, status, detail) and a complete audit
  trail as machine-readable JSON plus human-readable text.
* **Reports** to JSON, TXT and printable HTML. PDF export is deliberately blocked instead of
  pretending to support it.

## Safety properties

| Property | How it is enforced |
| --- | --- |
| No change without approval | `SystemStateMachine` (`WAITING_FOR_APPROVAL`), `IApprovalService`, `ApprovalRecord` matched by operation id |
| No deletion outside the allow list | `IPathGuard` per cleanup root plus a second containment check per file |
| No guessed update | `UpdateDecisionEngine` blocks without a verified official source, proven hardware match and positive compatibility |
| No invented data | `ValueOrigin` on every value; missing data is `UNKNOWN` with a reason |
| No firmware flash | Firmware assessment is read only; the manual step is always named |
| No cloud dependency | Offline mode; telemetry fixed to off; serial numbers masked in reports |

## Layout

```
src/WindowsMaintenanceCenter.Core             domain, contracts, services, localisation resources
src/WindowsMaintenanceCenter.Infrastructure   paths, registry, processes, persistence, HTTP, security, backup, rollback
src/WindowsMaintenanceCenter.Hardware         WMI-based provider for real hardware
src/WindowsMaintenanceCenter.Sensors          ACPI, performance counter, storage and vendor sensor providers
src/WindowsMaintenanceCenter.Drivers          driver inventory and PnP problem analysis
src/WindowsMaintenanceCenter.Bios             firmware assessment and firmware file inspection
src/WindowsMaintenanceCenter.Windows          DISM, event log, Defender, Windows Update, services
src/WindowsMaintenanceCenter.Maintenance      scan, plan, dry run, execute; software and workload inventory
src/WindowsMaintenanceCenter.Manufacturer     official source adapters and the update centre
src/WindowsMaintenanceCenter.Security         security report aggregation
src/WindowsMaintenanceCenter.Reporting        JSON / TXT / HTML reports
src/WindowsMaintenanceCenter.Simulation       clearly labelled simulation fixture (reference machine)
src/WindowsMaintenanceCenter.Diagnostics      the diagnostic modules used by the scan pipeline
src/WindowsMaintenanceCenter.App              WPF shell: DI root, MVVM, themes, runtime DE/EN
tests/WindowsMaintenanceCenter.Tests          automated tests (xUnit v3)
scripts/                              build, test and release scripts
installer/                            Inno Setup definition
docs/                                 BUILD, RELEASE, SECURITY and the honest STATUS document
tools/                                checks that run without a .NET SDK
```

## Build

```powershell
pwsh ./scripts/build.ps1          # build and publish the portable executable
pwsh ./scripts/test.ps1           # run the tests
pwsh ./scripts/release.ps1        # portable exe + setup exe + checksums + release notes
bash tools/verify-all.sh          # contracts, localisation, XAML, projects, syntax (no SDK needed)
```

See [docs/BUILD.md](docs/BUILD.md) and [docs/RELEASE.md](docs/RELEASE.md).

## Honest status

`docs/STATUS.md` documents exactly which checks have really run. It currently states, in short:

* the code is written and passes every check that works without a .NET SDK,
* **no project has been compiled in the development environment** (no SDK available there),
* **the tests have been written but not executed**,
* **no Windows machine and no real hardware have been used yet** - the reference machine test is
  still outstanding.

Anything else would be a claim this repository cannot back up.

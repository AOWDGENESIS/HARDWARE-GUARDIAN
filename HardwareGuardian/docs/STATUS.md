# Hardware Guardian — Build Status (honest working document)

This file is deliberately blunt. It exists so that no reader can mistake the current
state for a finished product. It is updated after every work session.

Last updated: 2026-09-19 (second session)

---

## 1. What this environment can and cannot do

| Capability | State | Consequence |
| --- | --- | --- |
| .NET SDK / `dotnet build` | **NOT AVAILABLE** (no SDK, no install path: `dot.net` unreachable, no apt package) | **No project in this repository has ever been compiled.** |
| NuGet restore (`dotnet restore`) | **NOT AVAILABLE** (api.nuget.org unreachable from the shell) | Package pins were verified against the NuGet flat-container index, but no package has been downloaded. |
| WPF / WPF designer | **NOT AVAILABLE** (Linux) | The App layer can be written, but not rendered or started here. |
| Windows + real hardware test (rule 89) | **NOT AVAILABLE** | All Windows-specific behaviour is **UNVERIFIED BY EXECUTION**. |
| Syntax check (tree-sitter C# grammar) | AVAILABLE | All 77 C# files parse without syntax errors (2026-09-19). **Syntax only — not a compile, not a type check.** |
| Contract check (`tools/check-contracts.py`) | AVAILABLE | Heuristic check of the API surface: object initialisers, enum/static member access, interface implementations. Currently **0 findings**. Not a compiler. |

Therefore, for the current revision:

- Build status: **NOT BUILT**
- Test status: **NOT TESTED**
- Type correctness: **NOT VERIFIED** (no compiler available)
- Runtime behaviour on Windows: **NOT VERIFIED** (no Windows, no hardware)

---

## 2. Repository layout (current)

| Project | Files | Lines | Purpose | State |
| --- | --- | --- | --- | --- |
| `HardwareGuardian.Core` | 42 | 7 341 | Domain + contracts + services, no Windows APIs, embedded `Resources/en.json` + `de.json` | Written; contract-checked; localisation 470/470 keys |
| `HardwareGuardian.Infrastructure` | 23 | 4 010 | Paths, registry, processes, PowerShell, persistence, logging, HTTP, security, backup, rollback, localisation | Written; contract-checked |
| `HardwareGuardian.Hardware` | 2 | 1 095 | WMI provider for real hardware | Written; contract-checked |
| `HardwareGuardian.Sensors` | 2 | 524 | ACPI / performance / storage / vendor sensor providers | Written; contract-checked |
| `HardwareGuardian.Drivers` | 1 | 277 | Driver inventory + PnP problem-code analysis | Written; contract-checked |
| `HardwareGuardian.Bios` | 1 | 307 | Firmware assessment (never flashes), firmware file inspection | Written; contract-checked |
| `HardwareGuardian.Security` | 1 | 95 | Security report aggregation | Written; contract-checked |
| `HardwareGuardian.Manufacturer` | 3 | 1 023 | Official-source adapters, resolver, update centre (`IUpdateCenter`) | Written; contract-checked |
| `HardwareGuardian.Maintenance` | 5 | 1 480 | Scan / plan / dry run / execute, software + process inventory, workload detection | Written; contract-checked |
| `HardwareGuardian.Windows` | 1 | 931 | `IWindowsHealthService`: DISM, event log, Defender (read only), Windows Update, startup/services | Written; contract-checked |
| `HardwareGuardian.Simulation` | 1 | 419 | `MockHardwareProvider` fixture, clearly labelled as simulation | Written; contract-checked |
| `HardwareGuardian.Reporting` | 1 | 620 | `IReportGenerator`: JSON / TXT / HTML; PDF deliberately blocked | Written; contract-checked |

Total: **83 C# files, 18 122 lines** in 12 projects, all listed in `HardwareGuardian.sln`.

Not present yet: `HardwareGuardian.App` (WPF/MVVM/DI shell and composition root),
`HardwareGuardian.Tests`, `scripts/`, CI workflow, installer definition (Inno Setup or WiX), and the
real build/test/hardware evidence. PDF export is intentionally not implemented (see section 4).

---

## 3. Defects found and fixed in this session (continued and extended)

A contract checker (`tools/check-contracts.py`) was written and used to compare every module
against the real Core contracts. Findings that were fixed:

| Defect | Location | Fix |
| --- | --- | --- |
| `new Core.Values.Measured<long>.Known(...)` — invalid C# (object creation on a static factory) | `Infrastructure/Security/HashService.cs` | Uses `Measured<long>.Known(info.Length, origin)` with a real `ValueOrigin.LocalFile` |
| Shared static registry base keys were disposed via `using var baseKey = GetBaseKey(scope)` | `Infrastructure/Platform/WindowsRegistryAccess.cs` | Base keys are no longer disposed (6 occurrences); only explicitly opened keys are |
| `Freshness.Expired` does not exist (that member belongs to `CacheDisposition`) | `Core/Services/UpdateDecisionEngine.cs` | Uses `Freshness.Stale` with a documented meaning (must be refreshed before any decision) |
| Simulation fixture used the invented member `DriverRecord.DeviceClass_` | `Simulation/MockHardwareProvider.cs` | Removed; the real field is `DriverInfo.DeviceClass` |
| WMI queries used the class-name shorthand, which some providers reject | `Hardware/Wmi/WmiReader.cs` | Explicit `SELECT * FROM <class> [WHERE ...]` |
| `WindowsSignatureVerifier` and `Measured<T>` tri-state flagged as null dereference | (analysis) | False positives of the heuristic scan, verified against the real declarations |
| Checker reported `SomeEnum.Value.ToString()` as a typo | `tools/check-contracts.py` | Inherited members (`ToString`, `Equals`, ...) are exempt; documented in the tool |
| Rollback restored files without proving the result | `Infrastructure/Security/RollbackService.cs` | Compares SHA-256 of backup copy and restored file; ambiguous targets are reported as manual instead of guessed |
| Report generator used `JsonSerialization.Indented` (does not exist) and read sensor values as if `Measured<T>` had no `HasValue` semantics | `Reporting/ReportGenerator.cs` | Uses `JsonOptions.Default`; sensor values go through `Measured<T>.HasValue` |
| Localisation keys were referenced but no resource file existed | `Core/Resources` | `en.json` + `de.json` with 470 keys each, enforced by `tools/check-localization.py` |
| No project file for `Hardware` / `Maintenance`, no solution | `src/**`, root | Added; `tools/generate-solution.py` writes a deterministic `HardwareGuardian.sln` (12 projects), `tools/check-projects.py` verifies references |
| Dead conditional expression at the end of `PathGuard.Evaluate` | `Core/Services/PathGuard.cs` | Replaced by an explicit statement + comment: the intent never turns a refusal into an allowance |
| Duplicated `LocalMachine` arm in `IsWriteAllowed`, leftover `Now()` helper | `Infrastructure/Platform/WindowsRegistryAccess.cs` | Removed |
| Module layer written against **assumed** contracts (whole maintenance module, update centre, workload detector) | `HardwareGuardian.Maintenance`, `HardwareGuardian.Manufacturer` | Rewritten against the real contracts; three files with invented types deleted |
| Missing Core contracts for software/process inventory | `Core/Models/InventoryModels.cs`, `Core/Abstractions/InventoryAbstractions.cs` | Added `InstalledSoftwareRecord`, `ProcessRecord`, `ProcessSnapshotEntry`, `ProcessCategory`, `ISoftwareInventoryService`, `IProcessInventoryService`, `IProcessSnapshotProvider` |
| `IMaintenanceService` would have been declared twice | `Core/Abstractions/InventoryAbstractions.cs` | Duplicate removed; the contract lives in `FeatureAbstractions.cs` only |
| `LocalizedText.FromExternal(...)` invented | `Maintenance/WorkloadDetector.cs` | Replaced by a localisation key with the evidence as argument |
| `IRegistryAccess` reads were called without the required `retrievedAt` argument | `Maintenance/InventoryServices.cs` | Corrected |
| `DriverRecord.ServiceOrDriver` does not exist (it is on `PnpDeviceInfo`) | driver analysis | Corrected to `InfName`/`DriverFileName` |
| CS8602 risk: `.Value.…` chains without guaranteed narrowing | `Hardware/Wmi/WindowsHardwareProvider.cs` | Values are pulled into local variables first (3 places) |

Verified as **already correct** against the real contracts (no change needed): `BackupService`
(`IBackupService` signature and `BackupRequest` usage), all hardware/sensor/BIOS/manufacturer model
usage, `ApprovalRequestDraft`, `ApprovalRecord`, `ProblemDraft`.

### What the checker cannot prove

`tools/check-contracts.py` is a heuristic. It does **not** check method argument types or arity,
generic constraints, tuple element names, nullability of arguments, overload resolution, or any
behaviour. A compiler and the test suite are still mandatory.

---

## 4. Safety decisions encoded in the current code

- No numeric health score anywhere (`HealthStatus` is a classification, not a number).
- Missing data is rendered `UNKNOWN` with a machine-readable reason; nothing is substituted.
- ACPI thermal zones are always `SensorQuality.Limited` and never labelled a CPU core temperature.
- Firmware is never flashed. Without a verified board revision and a configured official source the
  assessment is `BLOCKED` (`BOARD_REVISION_UNKNOWN`, `SOURCE_UNKNOWN`).
- Update sources: manufacturer / Microsoft / verified OEM only. `UpdateDecisionEngine` requires
  trust ≥ `VerifiedOem`, verification ≥ `MetadataMatch`, a known URL, a proven hardware match and a
  positive compatibility statement — otherwise `BLOCKED`.
- Maintenance is a four-stage pipeline: **scan (measure only) → plan → dry run → execute**, and the
  execution additionally requires an `ApprovalRecord` whose `OperationId` equals the plan id.
  Protected categories (`PrefetchedData`, `WindowsInstallerCache`, `RecycleBin`) are measured and
  explained but never cleaned. Every individual file must pass `IPathGuard` for its own root, plus a
  second containment check, before deletion; files younger than 24 h and reparse points are skipped.
  After a run, the same location is re-measured and the result is recorded as evidence.
- Categories that cannot be separated safely (`InstallerLeftovers`, `OldLogs`) are **not offered at
  all** instead of being guessed at.
- Registry access has no "registry cleaning" capability by design; writes are restricted to an
  allow list (own key, startup key, service keys).

---

- **PDF export is blocked, not faked.** `IReportGenerator.SupportedFormats` returns HTML, TXT and
  JSON. A request for PDF raises `OperationBlockedException` with `UNSUPPORTED_PLATFORM` and the list
  of supported formats. HTML is print ready and covers the documented use case; a "PDF" that is
  really a renamed text file would violate rule 61.
- **Rollback automates only what it can verify.** Registry exports (`reg.exe import`) and copied
  files (with SHA-256 comparison before success is claimed) are automated. A system restore point and
  a driver state are reported as explicit manual steps - they need interaction and a restart.
- **The localizer never hides a gap.** A missing key renders as `[[Key]]` and is added to
  `JsonLocalizer.MissingKeys` for the diagnostics view instead of showing an empty string.

## 5. What has to happen before this can be called production ready

1. ~~Implement the remaining services~~ **done**: `IWindowsHealthService`, `IReportGenerator`,
   `IRollbackService`, `MockHardwareProvider`, `ILocalizer` + resources. Still missing: the WPF shell.
2. Implement `src/HardwareGuardian.App` (WPF, MVVM, DI, Dark default, runtime DE/EN switch) and the
   DI composition root that wires all 12 projects.
3. Implement `tests/HardwareGuardian.Tests` covering the safety invariants (fail-closed paths,
   version comparison, decision engine, path guard, maintenance dry run vs. execute, localizer key
   parity, report generator blocked-format path).
4. Build on a Windows machine with the pinned .NET 10 SDK, fix what the compiler reports, run the
   tests, and record the raw output as evidence.
5. Run the application on the reference machine (Ryzen 5 5600G / GIGABYTE B450M S2H / BIOS F67 /
   Windows 11 Pro / RTX 3060 / 24 GB) and record which values are detected correctly — without
   modifying that machine.
6. Produce the release artefacts (portable exe, installer, checksums, release notes) **by building
   them**, never by hand.

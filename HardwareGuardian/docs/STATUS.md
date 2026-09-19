# Hardware Guardian — Build Status (honest working document)

This file is deliberately blunt. It exists so that no reader can mistake the current
state for a finished product. It is updated after every work session.

Last updated: 2026-09-19 (third session)

---

## 1. What this environment can and cannot do

| Capability | State | Consequence |
| --- | --- | --- |
| .NET SDK / `dotnet build` | **NOT AVAILABLE** (no SDK, no install path: `dot.net` unreachable, no apt package) | **No project in this repository has ever been compiled.** |
| NuGet restore (`dotnet restore`) | **NOT AVAILABLE** (api.nuget.org unreachable from the shell) | Package pins were verified against the NuGet flat-container index, but no package has been downloaded. |
| WPF / WPF designer | **NOT AVAILABLE** (Linux) | The App layer can be written, but not rendered or started here. |
| Windows + real hardware test (rule 89) | **NOT AVAILABLE** | All Windows-specific behaviour is **UNVERIFIED BY EXECUTION**. |
| Syntax check (tree-sitter C# grammar) | AVAILABLE | All 118 C# files parse without syntax errors (2026-09-19). **Syntax only — not a compile, not a type check.** |
| Contract check (`tools/check-contracts.py`) | AVAILABLE | Heuristic check of the API surface: object initialisers, enum/static member access, `local.Member` against the declared type of the local, interface implementations. Covers `src/` **and** `tests/`. Currently **0 findings**. Not a compiler. |
| Unit tests | **WRITTEN, NOT EXECUTED** | `tests/HardwareGuardian.Tests` exists (11 files, xUnit v3). Running them needs the .NET SDK, which this environment does not have. |

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
| `HardwareGuardian.Infrastructure` | 24 | 4 138 | Paths, registry, processes, PowerShell, persistence, logging, HTTP, security, backup, rollback, localisation | Written; contract-checked |
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
| `HardwareGuardian.Diagnostics` | 6 | 811 | Diagnostic modules: driver health, storage health, sensors, Windows health, workloads, firmware assessment | Written; contract-checked |
| `HardwareGuardian.App` | 17 | 2 510 | WPF shell: DI root, MVVM, Dark/Light theme, DE/EN at runtime, 5 pages | Written; XAML-checked |
| `tests/HardwareGuardian.Tests` | 11 | 1 333 | xUnit v3 test project: version comparison, path guard, problem registry, state machine, overall status, update decision engine, maintenance safety, localisation parity, report generator, simulation fixture | Written; contract-checked; **NOT EXECUTED** |

Total: **118 C# files, 22 904 lines + 10 XAML files** in 15 projects, all listed in
`HardwareGuardian.sln`.

Not present yet: `scripts/`, the CI workflow, the installer definition (Inno Setup or WiX), the
release artefacts and the real build/test/hardware evidence. PDF export is intentionally not
implemented (see section 4).

### The application shell

`HardwareGuardian.App` is a WPF application with a real composition root and no service locator in
the views:

* `App.xaml.cs` builds the container, creates the logger, loads the settings, applies the theme and
  the language, and selects **exactly one** hardware provider - `WindowsHardwareProvider`, or
  `MockHardwareProvider` when the application is started with `--simulation`. Simulation is never
  chosen automatically, and a banner stays visible while it is active.
* Five pages exist and work against the real services: Overview (status, findings, sensors, modules,
  report export), Hardware (components, details, origin of every value), Windows (checks, DISM, SFC,
  Defender, updates), Maintenance (scan, selection, **mandatory dry run**, approval, execute),
  Settings (language, theme, offline mode, privacy of reports, locations, audit log).
* Theme: Dark is the default, Light and System are selectable; colours live only in
  `Themes/Dark.xaml` and `Themes/Light.xaml`, so switching a theme changes open windows immediately.
* Language: every visible string is resolved through `ILocalizer`; a change raises one notification
  that refreshes all bound texts. Missing keys are shown as `[[Key]]` and listed in
  `JsonLocalizer.MissingKeys`.
* Unhandled errors (dispatcher, domain, task) are logged and surfaced as a notification instead of
  closing the window silently.

---

## 3. Defects found and fixed in this session (continued and extended)

Checks that run without a .NET SDK (`bash tools/verify-all.sh`): contracts, localisation, project
references, XAML and syntax. Last result: 118 files / 370 types, 577 localisation keys in both
languages, 15 projects, 10 XAML files - all clean (0 findings, and every key used in code is
defined, with no unused key left behind). **This is not a build.**

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
| **Real bug:** `SystemSnapshot.StorageDevices` does not exist (the member is `Storage`) | `Manufacturer/UpdateCenter.cs` (2 places) | Corrected; found by the new typed-receiver check after the member map had been repaired |
| **Real gap:** 6 vendor keys (`Vendor_AMD`, `Vendor_ASUS`, `Vendor_GIGABYTE`, `Vendor_HP`, `Vendor_MSI`, `Vendor_NVIDIA`) were used but not defined — they are the ones the reference machine needs | `Core/Resources/*.json` | Added in both languages; the checker now recognises `…Key = "…"` positions, which the old "every segment must contain a lower-case letter" rule had skipped |
| Test project used four members that do not exist (`MaintenanceResult.Outcome`, `ModuleResult.SkipReasonKey`, `SystemIdentity.Model`, `IClock` without `UtcNow`) | `tests/HardwareGuardian.Tests` | Corrected against the real contracts; the checker now scans `tests/` as well |
| Contract checker had five blind spots: interfaces were not parsed (no access modifier ⇒ no members collected), `record struct` produced a bogus type named `struct` and lost `Measured<T>`/`TextInfo`/`ValueOrigin` completely, extension methods were misread as their parameter names, nested types merged the scopes of outer and inner methods, and single-parameter methods never bound their parameter | `tools/check-contracts.py` | All five fixed and documented in the tool; the member map grew from 352 to 370 types, which is why the real defects above became visible |
| Localisation checker reported deliberately missing test keys as findings and could not see vendor keys | `tools/check-localization.py` | Test-only keys are built at runtime; `…Key` positions are recognised as keys |

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
   `IRollbackService`, `MockHardwareProvider`, `ILocalizer` + resources, diagnostic modules.
2. ~~Implement the WPF shell and the composition root~~ **done**: `src/HardwareGuardian.App` with
   DI, MVVM, Dark/Light/System theme, runtime DE/EN, five working pages.
3. ~~Implement `tests/HardwareGuardian.Tests`~~ **written, not executed.** The project covers the
   safety invariants (fail-closed paths, version comparison, decision engine, path guard, maintenance
   dry run vs. execute, localizer key parity, report generator blocked-format path, simulation
   fixture). It must be run on a machine with the SDK; until then the test result is **unknown**.
4. Build on a Windows machine with the pinned .NET 10 SDK, fix what the compiler reports, run the
   tests, and record the raw output as evidence.
5. Run the application on the reference machine (Ryzen 5 5600G / GIGABYTE B450M S2H / BIOS F67 /
   Windows 11 Pro / RTX 3060 / 24 GB) and record which values are detected correctly — without
   modifying that machine.
6. Produce the release artefacts (portable exe, installer, checksums, release notes) **by building
   them**, never by hand.

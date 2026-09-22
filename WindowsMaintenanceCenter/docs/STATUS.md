# Windows Maintenance Center — Build Status (honest working document)

This file is deliberately blunt. It exists so that no reader can mistake the current
state for a finished product. It is updated after every work session.

Last updated: 2026-09-20 (seventh session)

---

## 1. What this environment can and cannot do

| Capability | State | Consequence |
| --- | --- | --- |
| .NET SDK / `dotnet build` | **NOT AVAILABLE IN THIS CONTAINER** (no SDK, no install path) — but a GitHub hosted Windows runner builds the whole solution: run `35505778032`, 15 projects including WPF, 0 errors | The build is proven **on the CI machine**, never here. What that machine cannot prove (sensors, battery, UAC prompt, reboot) is listed in `docs/VM-CI.md` |
| NuGet restore (`dotnet restore`) | **NOT AVAILABLE IN THIS CONTAINER**; the CI runner restores every package and the restore log is filed with each run | The pins are exercised for real now; a package that cannot be resolved fails the CI run instead of being discovered later |
| WPF / WPF designer | **NOT AVAILABLE** (Linux) | The App layer can be written, but not rendered or started here. |
| Windows + real hardware test (rule 89) | **NOT AVAILABLE** | All Windows-specific behaviour is **UNVERIFIED BY EXECUTION**. |
| Syntax check (tree-sitter C# grammar) | AVAILABLE | All 131 C# files parse without syntax errors (2026-09-20). **Syntax only — not a compile, not a type check.** |
| Contract check (`tools/check-contracts.py`) | AVAILABLE | Heuristic check of the API surface: object initialisers, enum/static member access, `local.Member` against the declared type of the local, interface implementations. Covers `src/` **and** `tests/`. Currently **0 findings**. Not a compiler. |
| Unit tests | **EXECUTED AND PASSING ON THE CI MACHINE**: 359 cases, 0 failed, run `35694300454`; the TRX file and a `summary.txt` (timestamp, version, build, environment, result per chapter 71) sit in `test-results/unit/20260922T062120Z-359-of-359/` | This is a real test run, and it is still not a substitute for the target machine (chapter 93). Nothing here claims that a Windows-only behaviour was verified. |

Therefore, for the current revision:

- Build status: **BUILT ON THE WINDOWS CI MACHINE** (run `35505778032`, 0 errors, 50 warnings); **never built in this container**
- Test status: **359 cases executed, 359 passed on the Windows CI machine**; nothing verified on the target machine
- Type correctness: **NOT VERIFIED** (no compiler available)
- Runtime behaviour on Windows: **NOT VERIFIED** (no Windows, no hardware)

---

## 2. Repository layout (current)

| Project | Files | Lines | Purpose | State |
| --- | --- | --- | --- | --- |
| `WindowsMaintenanceCenter.Core` | 45 | 7 900 | Domain + contracts + services, no Windows APIs, embedded `Resources/en.json` + `de.json` | Written; contract-checked; localisation 726/726 keys |
| `WindowsMaintenanceCenter.Infrastructure` | 24 | 4 173 | Paths, registry, processes, PowerShell, persistence, logging, HTTP, security, backup, rollback, localisation | Written; contract-checked |
| `WindowsMaintenanceCenter.Hardware` | 5 | 1 542 | WMI provider for real hardware, Secure Boot variable, TPM state, firewall profiles | Written; contract-checked |
| `WindowsMaintenanceCenter.Sensors` | 2 | 524 | ACPI / performance / storage / vendor sensor providers | Written; contract-checked |
| `WindowsMaintenanceCenter.Drivers` | 1 | 277 | Driver inventory + PnP problem-code analysis | Written; contract-checked |
| `WindowsMaintenanceCenter.Bios` | 1 | 307 | Firmware assessment (never flashes), firmware file inspection | Written; contract-checked |
| `WindowsMaintenanceCenter.Security` | 1 | 95 | Security report aggregation | Written; contract-checked |
| `WindowsMaintenanceCenter.Manufacturer` | 3 | 1 048 | Official-source adapters, resolver, update centre (`IUpdateCenter`) | Written; contract-checked |
| `WindowsMaintenanceCenter.Maintenance` | 5 | 1 726 | Scan / plan / dry run / execute, software + process inventory, workload detection | Written; contract-checked |
| `WindowsMaintenanceCenter.Windows` | 1 | 1 271 | `IWindowsHealthService`: DISM, event log, Defender (read only), Windows Update with KB/category/severity/restart, TPM, firewall, startup/services | Written; contract-checked |
| `WindowsMaintenanceCenter.Simulation` | 1 | 419 | `MockHardwareProvider` fixture, clearly labelled as simulation | Written; contract-checked |
| `WindowsMaintenanceCenter.Reporting` | 1 | 939 | `IReportGenerator`: JSON / TXT / HTML; PDF deliberately blocked | Written; contract-checked |
| `WindowsMaintenanceCenter.Diagnostics` | 6 | 811 | Diagnostic modules: driver health, storage health, sensors, Windows health, workloads, firmware assessment | Written; contract-checked |
| `WindowsMaintenanceCenter.App` | 17 | 2 701 | WPF shell: DI root, MVVM, Dark/Light theme, DE/EN at runtime, 5 pages | Written; XAML-checked |
| `tests/WindowsMaintenanceCenter.Tests` | 19 | 3 035 | xUnit v3 test project: version comparison, path guard, problem registry, state machine, overall status, update decision engine, maintenance safety, localisation parity, report generator, simulation fixture, inventory failure handling, SMBIOS code tables, Secure Boot interpretation, TPM/firewall verdicts, size formatting, Windows update result codes and command-line safety | Written; contract-checked; **NOT EXECUTED** |

Total: **131 C# files, 26 647 lines (18 of them test files) + 10 XAML files** in 15 projects, all
listed in `WindowsMaintenanceCenter.sln`.

Delivery layer:

| Component | State |
| --- | --- |
| `scripts/build.ps1` | Written: restore, build (with commit and build date injected), publish the portable single-file exe. **NOT EXECUTED.** |
| `scripts/test.ps1` | Written: `dotnet test` with TRX + console logger, fails on the first failing test. **NOT EXECUTED.** |
| `scripts/release.ps1` | Written: portable exe, Inno Setup call, `WindowsMaintenanceCenter-Checksums.txt` (SHA-256) and `WindowsMaintenanceCenter-ReleaseNotes.txt`. **NOT EXECUTED.** |
| `installer/WindowsMaintenanceCenter.iss` | Written: Inno Setup 6, per-user or per-machine, Start Menu entry + optional desktop shortcut, clean uninstall that asks before deleting settings and audit data. **NOT COMPILED.** |
| `.github/workflows/windowsmaintenancecenter.yml` | Written: a Linux job running the SDK-free checks, and a Windows job for build, tests, packaging, checksum verification and artefact upload. **NOT EXECUTED** (never ran on GitHub). |
| `docs/BUILD.md`, `docs/RELEASE.md`, `docs/SECURITY.md`, `README.md` | Written. |

Not present: the release artefacts themselves, the build/test evidence and the verification on real
hardware. PDF export is **not implemented**; the owner decided on 2026-09-20 that it is to be built
(see `docs/ABNAHME.md`, section 4).

**Die verbindliche Spezifikation liegt jetzt im Repository.** Der Auftraggeber hat am 2026-09-20 die
Abnahme-, Test-, Sicherheits- und Release-Spezifikation V1.0 des "Windows Maintenance Center"
vorgegeben. Sie steht wortgleich in `docs/SPEC-WMC-V1.md` (Kapitel 1-103, Module M00-M48, zehn
Release-Gates, Prioritäten P0-P3). Drei Dokumente gehören dazu:

* `docs/ABNAHME-WMC.md` - Abgleich jedes Moduls M00-M48 gegen die MUSS-Kriterien, mit dem, was
  fehlt, und den drei Abweichungen, die eine Entscheidung brauchen (Produktname
  `WindowsMaintenanceCenter` gegen `WindowsMaintenanceCenter`, SQLite gegen die heutigen JSON-Dateien,
  Fehler-ID-Schema `WMC-<MODUL>-<Nr>`).
* `docs/RELEASE_STATUS.md` - der Release-Entscheid, heute `RELEASE_BLOCKED`, mit den blockierenden
  Tests im Format des Kapitels 94 (BLOCKING TEST, MODULE, REASON, EVIDENCE, REQUIRED FIX).
* `test-results/` - das Nachweisverzeichnis aus Kapitel 71 mit den zehn Unterordnern. Alle sind
  leer, weil kein Test ausgeführt wurde; Kapitel 5 macht daraus `NOT VERIFIED`.

**`docs/ABNAHME.md`** wurde am 2026-09-20 angelegt (ältere Zählung der Regeln 83-133, weiterhin
gültig für die dort genannten Einzelbefunde): the module-by-module gap list against the acceptance
rules 83-133 (status per module, the missing MUSS criteria with file names, the four MUSS conflicts
that need an owner decision - PDF report, AI module, self-update, winget - and what can only be
proven on a Windows machine). It supersedes any optimistic reading of this document: no module is
`PASSED`, every acceptance is `BLOCKED`, and a release is therefore blocked under rule 129.

### Fifth session - logic review of the core and the check runner

The review of `ScanOrchestrator`, `ProblemRegistry` and the report writer found one defect of the
kind the specification forbids outright (missing data presented as a clean result) and three
structural weaknesses. All of them are fixed and each fix has a check or a test:

| Finding | Location | Fix |
| --- | --- | --- |
| **Failed hardware reads disappeared.** The inventory reader turned every failed read into a `ProblemDraft`, but the orchestrator never registered them: the problem registry stayed empty, no problem ID was assigned, the dashboard, the problem centre and both reports never mentioned it. A scan in which a whole WMI class was unreadable could therefore end as **Healthy**. | `Core/Diagnostics/ScanOrchestrator.cs`, `Core/Models/ReportModels.cs`, `Reporting/ReportGenerator.cs` | The orchestrator registers the inventory problems (warning level, category from the failed class, evidence names the API call), writes a warning into the live protocol, and carries `InventoryFailedReads` plus the notes into the snapshot. JSON and TXT reports list them under "Hardware reads that failed". Covered by `InventoryFailureTests`. |
| `RunModuleAsync` and `ReadInventoryAsync` did not clear the problem registry, so a single-module run reported the findings of the previous full scan as its own result. | `Core/Diagnostics/ScanOrchestrator.cs` | Every pass clears the registry and reads the hardware again: a snapshot contains what this pass found. The dead `_lastInventory` cache that would have served stale data was removed. |
| The progress total of a full scan was the magic number `_modules.Count + 18` while the inventory reports 17 steps. Over time that number drifts and the progress bar (and with it the ETA) becomes wrong. | `Core/Diagnostics/InventoryReader.cs`, `Core/Diagnostics/ScanOrchestrator.cs` | `InventoryReader.StepCount` is the single source; a test compares it against the read methods of `IHardwareProvider`, so a new read method fails the suite instead of silently skewing the progress. |
| **The portable build was not portable.** `PathProvider` selects portable mode only when the marker file `WindowsMaintenanceCenter.portable` sits next to the executable (or `--portable` was passed), and no script ever created that file. `WindowsMaintenanceCenter-Portable-x64.exe`, handed out exactly as `release.ps1` produces it, would have written its configuration, reports, backups and audit log to `%ProgramData%\WindowsMaintenanceCenter` - a portable program writing into the machine data folder. | `scripts/release.ps1`, `installer/WindowsMaintenanceCenter.iss`, new `docs/PORTABLE_README.txt` | The release writes the marker beside the executable, and the installer refuses a source folder that contains it (an installed copy must use the installed layout). The installer also installs a `README.txt` that never existed - `docs/PORTABLE_README.txt` now says what the tool does, what it never does, where the data stays and which order is enforced |
| **Two visible strings in the report were hard-coded English.** The hardware section printed the network role as `[virtual]`, `[wi-fi]`, `[bluetooth]`, `[wired]` and the empty-processor case as a literal "UNKNOWN: no processor object was reported", regardless of the selected language - in a document whose whole point is that it can be read in German or English (rule 41). The localisation checker cannot see either, because the text never passes through a key. | `Reporting/ReportGenerator.cs`, `Core/Resources/{de,en}.json` | The network role is one of four localisation keys, the empty case names a message key; five new keys in both languages (702). The rule for this file is now: a text that goes into a report leaves the method through `_localizer[...]` or through `Show(...)`, which appends the recorded reason of an unmeasured value |
| **The build broke twice for a reason no check could explain.** `Directory.Build.props` imports a version file that lived in `build/`. That file disappeared from the working tree twice, each time leaving all 15 projects unloadable (MSB4019) with no trace of who removed it - and the earlier entry in this document blames an "unclear cause". The cause is now known: directories named `build` are excluded from the working snapshots used while developing, so the file was deleted by the environment, not by a person. | `eng/Version.props` (moved), `Directory.Build.props`, `tools/check-projects.py`, `tools/check-mutation.py`, `scripts/*.ps1`, `docs/BUILD.md`, `docs/RELEASE.md` | The version properties live in `eng/` - the usual place for build engineering files in .NET repositories and a name that survives. The import, both scripts, both documents, the checker and the mutation case follow the new path; `check-projects` resolves the import and requires the file, so a third disappearance would be reported instead of being discovered through a failed build |
| **The step between analysis and approval was missing.** `IBackupService` and `IRollbackService` were written, registered in the composition root and never called by anything: `MaintenanceService.Execute` went from the dry run straight to the approval, so the BACKUP stage of the documented order (spec section 44) did not exist in the running application. The restore point, the registry export and the rollback path were unreachable code. | `Core/Models/MaintenanceModels.cs`, `Core/Enums.cs` (`BACKUP_REQUIRED`), `Core/Abstractions/FeatureAbstractions.cs` (`RecordBackupAsync`), `Maintenance/MaintenanceService.cs`, `App/ViewModels/MaintenanceViewModel.cs`, `App/App.xaml.cs` | A plan states whether it needs a backup (everything that is not purely re-creatable: optional, protected and unknown categories). `RecordBackupAsync` checks availability for the plan's risk, creates the backup and records it for exactly these locations - and fails closed when no backup service is configured, when the availability is not sufficient or when the plan needs none. `ExecuteAsync` refuses a plan whose locations have no backup on record, with `BACKUP_REQUIRED` in the audit log and nothing deleted. The view model now performs BACKUP before it even asks for the approval, and the composition root injects the backup service. Five new tests cover the gate, the other-locations case and the fail-closed paths |
| **The mandatory dry run was enforced only in the user interface.** `MaintenanceService.ExecuteAsync` accepted any plan with `Mode = Execute` and a matching approval - it never checked that a dry run had happened. The gate lived in `MaintenanceViewModel` (a private flag), so any other caller - a future page, an automation, a test - could delete files without the user ever having seen what would be removed. On top of that the dry run and the executed plan were two different objects with different ids and no link between them, so the audit trail could not show which dry run preceded an execution. | `Core/Models/MaintenanceModels.cs` (`DryRunPlanId`), `Core/Enums.cs` (`DRY_RUN_REQUIRED`), `Maintenance/MaintenanceService.cs` | The service keeps a register of performed dry runs, keyed by the locations they covered (category + root + safety class). A plan is only executable when it names a dry run that is still on record for exactly those locations; otherwise the run is BLOCKED with `DRY_RUN_REQUIRED`, nothing is deleted and the refusal is audited. `BuildPlanAsync` links the execute plan to the recorded dry run, so the normal flow is unaffected. New tests: execution without a dry run is blocked, a dry run for other locations does not authorise the execution, and a recorded dry run makes the execution possible |
| **A fabricated value in the hardware data path.** `Win32_SystemEnclosure.ChassisTypes` is an array of SMBIOS codes (`uint16[]`), but the provider read it through the string helper. The helper's fallback is `raw.ToString()`, so the snapshot contained the text **"System.UInt16[]"** as a *known, measured* value - the exact "looks like data but is none" case the specification forbids. The correctly read array next to it was dead code. | `Hardware/Wmi/WindowsHardwareProvider.cs`, `Hardware/Wmi/WmiReader.cs`, new `Core/Values/SmbiosCodes.cs` | New `WmiObject.GetUIntArray` reads numeric arrays without parsing text; the codes are decoded with the documented CIM/DMTF tables; the code stays inside the text ("Desktop (SMBIOS code 3)") so every statement is verifiable; an undocumented code is reported as a code, never guessed. `SmbiosCodesTests` pins the anchors |
| **A wrong memory type was one line away.** `SMBIOSMemoryType` and `FormFactor` are bare codes (26, 8). Read as text they would have been shown as "26" and "8"; the widespread PowerShell lookup table that many implementations copy labels 20/21/22 as DDR/DDR2/DDR2 FB-DIMM, while the specification has those at 0x12/0x13/0x14 (18/19/20). Following it would have printed a wrong memory type on every machine from DDR2 onwards. | `Core/Values/SmbiosCodes.cs`, `Hardware/Wmi/WindowsHardwareProvider.cs`, `App/ViewModels/HardwareViewModel.cs` | The tables are taken from DMTF DSP0134 §7.18.2 (memory device type) and the CIM enumerations (`CIM_PhysicalMemory.FormFactor`, `ChassisTypes`); a test asserts 18/19/20/24/26/34 and would fail on the popular wrong table. The hardware page now shows "24 GB @ 3200 MHz · DDR4 (SMBIOS code 26) · DIMM (SMBIOS code 8)" per module |
| **The source list claimed a verification that had not happened.** `ManufacturerSources.cs` declares fourteen vendor pages with `BaselineVerification = VerificationLevel.SourceReachable` and justifies the file with "entries were verified as reachable over HTTPS". No check existed, and in the environment used for this work not a single one of those pages was ever requested - the claim was a statement about the tool without an action behind it (rule 61). | `Manufacturer/ManufacturerSources.cs` (header, notes, markers), new `tools/check-source-urls.py`, `tools/check-mutation.py`, `tools/verify-all.sh` | Four sources that were actually fetched and identified (AMD, NVIDIA, GIGABYTE, Microsoft Update Catalog) carry `SourceReachable` and a dated note naming the page; the other fourteen carry `NotVerified` with the reason. The header now says what was checked and when, and that `BaselineVerification`/`RequiresManualVerification` are operator documentation - no code reads them, `SupportsAutomatedCheck` plus the runtime `ISourceVerifier` decide. The new checker requests every URL over HTTPS and compares the answer with the claim, and it enforces the policy rules offline (HTTPS only, no credentials, no IP, no localhost, no third-party driver portal). It exits 3 and says "this is NOT a pass" when the network is blocked, and it stops after three dead hosts instead of blaming the vendors for a blocked egress. |
| **Secure Boot was reported as "on" when the read had failed.** `ReadSecureBootState` treated `ERROR_INSUFFICIENT_BUFFER` (122) as "Secure Boot is enabled" and otherwise returned `null`, dropping the error code. 122 only says that the caller's buffer did not fit; a machine without an EFI variable store, a denied read or an unlisted Windows error all ended in the same silent `null`, and one of them ended in **Enabled**. A security statement that is guessed is exactly what rule 91 forbids. | new `Hardware/Wmi/SecureBootReader.cs`, `Hardware/Wmi/WindowsHardwareProvider.cs`, new `tests/WindowsMaintenanceCenter.Tests/SecureBootReadingTests.cs` | The P/Invoke and the interpretation are separated: `SecureBootReader.Read()` requests the documented single byte (UEFI spec 3.3, `SecureBoot` is a UINT8) and retries once with a wider buffer only when the firmware reports 122, then keeps the deviation inside the evidence text. `Interpret(bytesReturned, lastError, buffer)` is a pure function and every failure returns an unknown state **with a named reason** (122 "says nothing about the state", no EFI variable store, missing variable, denied read, not supported). The three call sites now show that reason instead of a bare "could not be read", and the motherboard/BIOS/Windows pages print "Enabled" only for a byte the firmware really returned. Eight tests pin it, including "An_insufficient_buffer_is_never_reported_as_enabled" |
| **The reports described problems but not the machine.** Both the JSON and the TXT report listed problems, components, sensors, updates, maintenance, history and audit - and no hardware inventory at all. The processor, the memory modules with their type and form factor, board revision, BIOS, drive health and wear, adapters, monitors and the battery were readable on screen and missing from the file a user hands to somebody else. A report that cannot answer "which firmware is on this board" is not complete. | `Reporting/ReportGenerator.cs`, `Core/Resources/de.json`, `Core/Resources/en.json` | A `hardware` section (JSON) and an inventory section (TXT) are built from the snapshot: Windows identity, system, board (including whether the revision is verified), BIOS/UEFI, processors, memory with per-module type and form factor, graphics, storage with SMART state and volumes, network, monitors, audio, printers and the battery. Every leaf is either the measured value or `UNKNOWN: <reason>` - never `0`, never empty, so "not measured" stays distinguishable from "measured as zero". Serial numbers, MAC and IP addresses are masked when the options ask for it. Five new tests cover the two representations, the masking of MAC/IP and the "reason instead of a value" rule |
| **Unlisted PnP classes were reported as PCI devices.** The classification table ended with `_ => ComponentCategory.Pci`, so a device whose setup class is not in the table (HID, camera, smart card reader, anything new) was filed under a bus type that was never measured. `MapCategoryFromClass` was an alias of the same table, kept for a distinction it did not make. | `Core/Values/PnpClassMap.cs` (new), `Hardware/Wmi/WindowsHardwareProvider.cs`, new `PnpClassMapTests` | The table is a documented dictionary (processor, computer, firmware, display, monitor, the whole storage stack, net/modem/bluetooth, media, USB, printer, battery, thermal) and returns `Unknown` for anything else, with `IsKnown` so the caller can say "class not listed". The alias is gone; both call sites use the same table. Six tests include "An_unlisted_class_is_never_reported_as_a_bus" |
| **The redirect check was documented but not implemented.** The class comment of `SourceVerifier` promised "redirects are checked hop by hop against the host allow list", and the allow list was in fact only applied to the URL the caller handed in. A vendor page that answers with a redirect to any other host was accepted and reported as `SourceReachable` with the adapter's trust class - so a third-party portal could be smuggled in through an official page, which rule 12 forbids outright. | `Infrastructure/Security/SourceVerifier.cs`, new `tests/WindowsMaintenanceCenter.Tests/SourceVerifierTests.cs` | The check is a named method (`IsHostAllowed`), used for the start URL and for **every** hop; a redirect that leaves the allow list is BLOCKED with `SOURCE_NOT_VERIFIABLE` and the offending host in the evidence. An empty allow list still means "no allow list configured" and keeps the structural checks. Four new tests drive the real decision logic through a stub `HttpMessageHandler`: a redirect out of the allow list is blocked, a redirect inside it is followed, a foreign start host costs no request at all, and the structural rules (`http`, credentials, localhost, private addresses, non-URL) stay rejected |
| **Two download rules were permissive by default.** `DownloadRequest.RequireHashVerification` and `RequireSignatureVerification` defaulted to `false` and no caller in the application set them, so the pump delivered artefacts without a compared hash and without a verified signature with `IsReadyForApproval = true` - the two checks the specification names as mandatory (rule 30) were effectively off. | `Core/Abstractions/SecurityAbstractions.cs`, new `tests/WindowsMaintenanceCenter.Tests/DownloadServiceTests.cs` | Both default to `true` (fail closed); a source that publishes no hash or an unsigned artefact now has to be released explicitly and visibly instead of by a permissive default. Five tests run the real pipeline through a stub transport: without a published hash, without a verified signature and with a wrong hash nothing is released, a matching hash plus a verified signature is released, and a redirect is refused instead of followed |
| **The delete guard checked the text of a path, not the place it points to.** `PathGuard` used `Path.GetFullPath`, which only cleans up the string. A junction or symlink inside an allowed root (a cache folder, a downloaded helper) pointing at `C:\Windows\System32` passed both the containment and the protection check, because the text looked as if it were inside the cache - and the delete would have followed the link. Trailing dots and spaces were accepted too, although Windows strips them on access, so the checked string and the deleted file could differ. | `Core/Services/PathGuard.cs`, `tests/WindowsMaintenanceCenter.Tests/PathGuardTests.cs`, `Core/Resources/{de,en}.json` | A segment that ends with a dot or a space is refused (`PathGuard_Reason_AmbiguousSegment`). `TryResolveRealPath` follows links and junctions segment by segment; the resolved location must be inside an allowed root, must not be protected, and must not be the root itself - both the written path and the resolved one are checked. The resolution is injectable (one optional constructor argument) so the decision logic is testable without the privileges to create a link; three new tests cover "resolves outside", "resolves back inside" and the ambiguous segments |
| **The build was broken at the root.** `Directory.Build.props` imports `eng/Version.props`, but that file was missing from the working tree - MSBuild fails to load **every** project with MSB4019 before a single line is compiled. No check noticed, because nothing looked above the project files. | `eng/Version.props` (moved out of `build/`), `tools/check-projects.py` | The file is back (byte-identical to the branch history) and the checker now resolves every `<Import Project="…">` in the MSBuild root files, requires `eng/Version.props`, `Directory.Packages.props` and `global.json` to exist, and fails if any project sets `VersionPrefix` itself. Verified by removing the file: three findings. |
| The SDK-free runner printed "all available checks passed" even when a check had been skipped for a missing dependency (`verify-syntax.py` without tree-sitter returned 0). A partial run looked like a full one. | `tools/verify-all.sh`, `tools/verify-syntax.py` | A skipped check exits with code 3; the runner counts those, prints "checks passed, but N check(s) did not run" and exits with code 2 instead of 0. Green now means: every check ran and every check passed. |

Tool numbers after this session: 127 files / 392 declared types, 702 localisation keys per language,
10 XAML files with 143 bindings, 15 projects, and **13/13** deliberate defects reported by the
mutation self-test (new cases: a test double that loses an interface method, a deleted MSBuild
import target, and a third-party driver portal in the source list).

### Sixth session - acceptance rules 83-133, the two missing Windows checks and the update list

The acceptance rules 83-133 were applied as what they are: a checklist that decides whether anything
may be called finished. `docs/ABNAHME.md` records the result per module - ten modules do not exist
yet, several MUSS criteria inside existing modules are missing, and **no module can be `PASSED`**
while not a single test has been executed here. The owner then answered the four open conflicts: the
PDF report is to be built, the AI module is to be written as a self-contained "Windows Stalker" that
may research online but must never send personal data, `winget` programme updates **and** the self
update are both wanted (winget first), and the work order is diagnostics first. This session closed
the two criteria that were missing inside rule 87 and completed the update metadata of rule 90.

| Finding | Location | Fix |
| --- | --- | --- |
| **DIAG-F-009 was never implemented.** No line of code read the firewall state. The Windows assessment listed seven checks, and the firewall was not one of them - a MUSS criterion of rule 87 that no module covered. | new `Hardware/Wmi/FirewallReader.cs`, `Windows/WindowsHealthService.cs`, `Core/Models/WindowsModels.cs` | The reader asks the same class the operating system cmdlet reads (`MSFT_NetFirewallProfile` in `Root\StandardCimv2`), accepts the numeric and the word form of `Enabled`, and reports a state that was not reported as "not reported". The verdict is a pure function (`FirewallState`), the check appears as `WindowsCheck_Firewall` with one evidence line per profile. Read only: no firewall setting is touched |
| **DIAG-F-011 was never implemented.** There was no TPM check at all - neither the state nor the absence of a TPM was read or shown. | new `Hardware/Wmi/TpmReader.cs`, `Windows/WindowsHealthService.cs` | `Win32_Tpm` in `Root\CIMV2\Security\MicrosoftTpm` is read through the documented properties (`IsEnabled_InitialValue`, `IsActivated_InitialValue`, `IsOwned_InitialValue`, `SpecVersion`, `ManufacturerIdTxt`, `ManufacturerVersion`). The verdict is a pure function over nullable values, because the firmware values are a snapshot of the instantiation: a provider that does not answer stays `Unknown`, no TPM instance becomes `NotPresent`, and the TPM is never cleared, prepared or changed |
| **UPDATE-F-003 was satisfied in name only.** The update search printed the title of each offered update and nothing else, so the user could not see which knowledge base number, which classification, which severity, how large or whether a restart is required - despite rule 90 naming exactly those fields. | `Infrastructure/Platform/PowerShellRunner.cs`, `Core/Models/WindowsModels.cs`, `Windows/WindowsHealthService.cs` | The template now emits every field the agent has, tagged with the index of its update (`UPDATE=<i>|<title>`, `KB=`, `CAT=`, `SEV=`, `SIZE=`, `REBOOT=`, `MAND=`, `DL=`) plus `SYSTEM_REBOOT=` from `Microsoft.Update.SystemInfo`; the parser ignores unknown fields so a future agent version cannot inject a value that is shown as measured. `WindowsUpdateInfo` carries the new fields as `TextInfo`/`Measured<ulong>`/`bool?`, and the restart statement of the agent is added to the registry based pending restart detection instead of replacing it |
| **UPDATE-F-002 was not reachable for the user.** The offered updates existed in the model (`Available`) but no page displayed them; the Windows page showed a single summary line. | `App/ViewModels/WindowsHealthViewModel.cs`, `App/Views/WindowsHealthView.xaml`, `Core/Resources/{de,en}.json` | A new card lists what the agent offers: title, KB number, category, severity, restart and download size, each cell "not reported" when the agent stayed silent; an explicit hint appears when the agent offers nothing. Display only - nothing is downloaded or installed from this page |
| **Visible values were not localized and not distinguishable from measured ones.** The maintenance table printed the safety class as the raw enum name (`Safe`), the directory size with a hard-coded `MB` suffix, an unmeasured size as the English literal `UNKNOWN`, and the Windows checks printed `True`/`False` for "performed" and "admin" in a German user interface. | new `Core/Values/SizeText.cs`, `App/ViewModels/{MaintenanceViewModel,WindowsHealthViewModel}.cs` | One size formatter renders bytes in the fitting unit with the culture's number format and lets the caller word the "not measured" case; the safety class maps to four new keys (`Safety_*`), yes/no to `Value_Yes`/`Value_No`, the update fields to `Value_NotReported`; 24 new keys in both languages (726 per language) |
| **Four enum values claimed checks that do not exist.** `WindowsCheckId` declared `ServiceHealth`, `TimeSynchronisation` and `DriverSignatures`, and nothing ever produced them - a reader would assume those checks exist. | `Core/Models/WindowsModels.cs`, new `tests/WindowsMaintenanceCenter.Tests/PlatformReaderTests.cs` | The three unused values are removed (driver signatures are assessed in the driver module, services in the startup assessment), and a test now walks the enum and requires a localized name for every remaining check, so a new check without a name fails the suite instead of appearing as "unknown check" in the list |
| **The localisation checker reported a false alarm and could not see built keys.** The new WMI property names (`IsEnabled_InitialValue`) were read as missing translation keys, and a key assembled at runtime (`"Safety_" + item.SafetyClass`) counted as a dead string - the first would have pushed someone into adding dummy keys to both language files. | `tools/check-localization.py` | Arguments of the WMI accessors are blanked before the key search, and concatenated prefixes are collected and reported as "reachable through a built prefix" instead of dead |
| **The window between "recorded" and "reported" was untested for both readers.** The verdict logic is the part a user reads as a statement about their machine's security, and there was no test for it. | new `tests/WindowsMaintenanceCenter.Tests/PlatformReaderTests.cs` | Sixteen new cases: unreadable versus absent TPM, all four verdicts, a partially reported state staying unknown, the specification generation parsing, unreadable versus empty firewall profile list, a disabled profile winning over an unreported one, the pinned WMI class and namespace names, the size formatter (units, culture, not measured, negative), and the enum/localisation coupling |

The update chain was continued in the same session (UPDATE-F-005 ... F-008): the service downloads
and installs one offered update, addressed **only by its position in the offer list**, so no title,
URL or knowledge base number ever becomes part of a command. An installation without the approval
record of the confirmed operation, without an elevated process or with an impossible position is
BLOCKED before the update agent is contacted. The numeric result code of the agent is mapped by a
pure function in Core (0-5 documented; anything else is unknown and never a success), and the state
after the action is measured again by a new search instead of being assumed (UPDATE-R-001). The user
interface for selection and installation is **not built yet**.

Tool numbers after this session: 133 C# files (19 of them test files) / 413 declared types,
**742 localisation keys per language**, 10 XAML files with 151 bindings, 15 projects, and **13/13**
deliberate defects reported by the mutation self-test. What did **not** change: nothing here was compiled, no test was executed, and
no machine was measured - the new readers have never seen a real TPM or a real firewall.

### Fourth session - safety review of the delivery level

The pass over the shell and the delivery level found more than names this time: the Windows repair
path did not ask for approval, one button promised SFC and ran DISM, and the contract checker had
blind spots that made deliberate typos invisible (see the findings table). What is verified now:

* every check run (`check-contracts`, `check-localization`, `check-xaml`, `check-bindings`,
  `check-projects`, `verify-syntax`, `generate-solution --check`, `check-mutation`) is clean,
* the mutation self-test proves that thirteen deliberate defects are still reported,
* the binding checker resolves all 143 bindings, the XAML checker reports no hard-coded UI text and
  the localisation checker reports no missing and no unused key in either language.

Still not verified: compilation, the unit tests, real hardware, the PowerShell scripts, the
installer and the CI runs. That needs a Windows machine with the .NET 10 SDK.

### Checks that run without an SDK

| Tool | What it proves | Current result |
| --- | --- | --- |
| `tools/verify-syntax.py` | every C# file parses with the tree-sitter C# grammar | 131 files, no syntax error (exit code 3 and an explicit note when tree-sitter is missing) |
| `tools/check-contracts.py` | object initialisers, enum/static members, members on fields, parameters, `foreach` variables and LINQ lambda parameters, interface implementations (src **and** tests) | 131 files / 403 types, 0 findings |
| `tools/check-localization.py` | every key used in C# **or XAML** exists in both languages; no dead key; both files symmetric; WMI property names and keys built from a prefix are handled | 726 keys, 0 missing, 0 dead, 4 keys reachable through the prefix `Safety_` |
| `tools/check-xaml.py` | XAML is well formed, resource keys exist, `DataType` names a known type, every `{services:Loc Key}` is defined, **no visible attribute carries a hard-coded literal**, every root element with `x:Class` has code-behind | 10 files, 0 findings |
| `tools/check-bindings.py` | every `{Binding}` path resolves against its data scope (view model or item type) | 10 files, 151 bindings, 0 findings |
| `tools/check-projects.py` | project references provide the used namespaces, every directory has a project, versions are centrally declared, every MSBuild `<Import>` resolves (`eng/Version.props`, `Directory.Packages.props`, `global.json` present, no project sets its own version) | 15 projects / 131 sources, 0 findings |
| `tools/generate-solution.py --check` | `WindowsMaintenanceCenter.sln` matches the projects on disk | up to date |
| `tools/check-mutation.py` | the checkers above actually fail: thirteen deliberate defects (property, enum member, field/parameter/lambda/`foreach` member, lost interface member in a test double, unknown localisation key, hard-coded UI text, wrong binding, a missing MSBuild import target, a third-party driver portal in the source list) are injected into a temporary copy one at a time | 13/13 reported, exit code 0 |
| `tools/check-source-urls.py` | every manufacturer landing page answers over HTTPS and matches its claim in the source file; policy rules (HTTPS only, no credentials, no IP, no localhost, no third-party portal) hold | **did not run** in this environment: no direct outbound network (exit 3, "this is NOT a pass"). The four sources marked `SourceReachable` were confirmed by fetching them through the sandbox's page fetcher on 2026-09-20; `verify-all.sh` reports the skipped network check without calling the offline run incomplete |

### The application shell

`WindowsMaintenanceCenter.App` is a WPF application with a real composition root and no service locator in
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

Checks that run without a .NET SDK (`bash tools/verify-all.sh`): syntax, contracts, localisation,
XAML, bindings, project references, solution freshness and a mutation self-test of the checkers
themselves. Last result: 118 files / 370 types,
659 localisation keys in both languages, 15 projects, 10 XAML files with 139 resolved bindings -
all clean (0 findings, every key used in code is defined, no unused key left behind, every binding
path resolves against its data scope). **This is not a build.**

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
| No project file for `Hardware` / `Maintenance`, no solution | `src/**`, root | Added; `tools/generate-solution.py` writes a deterministic `WindowsMaintenanceCenter.sln` (12 projects), `tools/check-projects.py` verifies references |
| Dead conditional expression at the end of `PathGuard.Evaluate` | `Core/Services/PathGuard.cs` | Replaced by an explicit statement + comment: the intent never turns a refusal into an allowance |
| Duplicated `LocalMachine` arm in `IsWriteAllowed`, leftover `Now()` helper | `Infrastructure/Platform/WindowsRegistryAccess.cs` | Removed |
| Module layer written against **assumed** contracts (whole maintenance module, update centre, workload detector) | `WindowsMaintenanceCenter.Maintenance`, `WindowsMaintenanceCenter.Manufacturer` | Rewritten against the real contracts; three files with invented types deleted |
| Missing Core contracts for software/process inventory | `Core/Models/InventoryModels.cs`, `Core/Abstractions/InventoryAbstractions.cs` | Added `InstalledSoftwareRecord`, `ProcessRecord`, `ProcessSnapshotEntry`, `ProcessCategory`, `ISoftwareInventoryService`, `IProcessInventoryService`, `IProcessSnapshotProvider` |
| `IMaintenanceService` would have been declared twice | `Core/Abstractions/InventoryAbstractions.cs` | Duplicate removed; the contract lives in `FeatureAbstractions.cs` only |
| `LocalizedText.FromExternal(...)` invented | `Maintenance/WorkloadDetector.cs` | Replaced by a localisation key with the evidence as argument |
| `IRegistryAccess` reads were called without the required `retrievedAt` argument | `Maintenance/InventoryServices.cs` | Corrected |
| `DriverRecord.ServiceOrDriver` does not exist (it is on `PnpDeviceInfo`) | driver analysis | Corrected to `InfName`/`DriverFileName` |
| CS8602 risk: `.Value.…` chains without guaranteed narrowing | `Hardware/Wmi/WindowsHardwareProvider.cs` | Values are pulled into local variables first (3 places) |
| **Real bug:** `SystemSnapshot.StorageDevices` does not exist (the member is `Storage`) | `Manufacturer/UpdateCenter.cs` (2 places) | Corrected; found by the new typed-receiver check after the member map had been repaired |
| **Real gap:** 6 vendor keys (`Vendor_AMD`, `Vendor_ASUS`, `Vendor_GIGABYTE`, `Vendor_HP`, `Vendor_MSI`, `Vendor_NVIDIA`) were used but not defined — they are the ones the reference machine needs | `Core/Resources/*.json` | Added in both languages; the checker now recognises `…Key = "…"` positions, which the old "every segment must contain a lower-case letter" rule had skipped |
| Test project used four members that do not exist (`MaintenanceResult.Outcome`, `ModuleResult.SkipReasonKey`, `SystemIdentity.Model`, `IClock` without `UtcNow`) | `tests/WindowsMaintenanceCenter.Tests` | Corrected against the real contracts; the checker now scans `tests/` as well |
| Contract checker had five blind spots: interfaces were not parsed (no access modifier ⇒ no members collected), `record struct` produced a bogus type named `struct` and lost `Measured<T>`/`TextInfo`/`ValueOrigin` completely, extension methods were misread as their parameter names, nested types merged the scopes of outer and inner methods, and single-parameter methods never bound their parameter | `tools/check-contracts.py` | All five fixed and documented in the tool; the member map grew from 352 to 370 types, which is why the real defects above became visible |
| Localisation checker reported deliberately missing test keys as findings and could not see vendor keys | `tools/check-localization.py` | Test-only keys are built at runtime; `…Key` positions are recognised as keys |
| **Real UI bug:** `MainWindow.xaml` bound the simulation banner to `MainViewModel.SimulationNotice`, which did not exist — WPF fails such a binding silently, so the banner would have stayed empty | `App/ViewModels/MainViewModel.cs` | Property added (uses `Report_SimulationWarning`); found by the new binding checker |
| **Compile error:** `MainViewModel.ProtocolEntries` was an `ObservableCollection<T>` but called `Reset(...)`, which only exists on `BulkObservableCollection<T>` | `App/ViewModels/MainViewModel.cs` | Type corrected |
| No check existed for WPF bindings at all (a wrong path does not throw, it produces an empty control) | `tools/check-bindings.py` (new) | Resolves every `{Binding}` root against the view model of the file or the item type of the surrounding templates; 139 bindings checked, 0 findings |
| **Safety rule broken in the Windows page:** the "DISM repair" button started `DISM /RestoreHealth` after a single click, without an approval record - every other system-changing path in the app asks first | `IWindowsHealthService` (Core), `WindowsHealthService`, `WindowsHealthViewModel`, `WindowsHealthView.xaml` | The contract now carries the `ApprovalRecord`; the service refuses a repair without it (BLOCKED, `APPROVAL_REQUIRED`) and refuses `sfc.exe /scannow` in general (`REPAIR_NOT_AUTOMATED`); the page asks for approval and only then calls the service. Both refusals are written to the audit log |
| **The "SFC" button was mislabelled:** it ran `DISM /CheckHealth`, so neither the label nor the evidence matched the action | `WindowsHealthService`, `WindowsHealthView.xaml` | System file verification really runs `sfc.exe /verifyonly` (read-only) and is interpreted from its own output; a repair of system files stays unautomated with a documented reason. The button says what it does |
| **The contract checker skipped the most common code shapes:** a lambda parameter (`problems.Select(p => …)`), a `foreach` variable whose name was reused in a second loop, and any expression inside an interpolated string (`$"{entry.Kind}"`). A deliberate typo in each of those places stayed invisible | `tools/check-contracts.py` | Scopes are positional now (local, loop body, lambda body - the innermost binding wins), interpolation holes stay visible while the literal around them is blanked, findings carry the line number, and a type name that is itself a member (`device.HealthStatus.Display`) is no longer mistaken for a static access. All six shapes are covered by the mutation self-test |
| **Localisation gap in the UI:** 99 visible strings in the ten XAML files were hard-coded English (`"Cancel"`, `"Findings"`, every column header), so switching to German would have left the shell in English | `App/Views/*.xaml` + `Core/Resources/{en,de}.json` | All 99 replaced by `{services:Loc Key}` (82 distinct keys, `Shell_*`, `Section_*`, `Column_*`, `Action_*`, `Option_*`, `Field_*`, `Notice_*`); `check-xaml.py` now fails on any new hard-coded visible literal, and `check-localization.py` reads XAML keys as well |

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
2. ~~Implement the WPF shell and the composition root~~ **done**: `src/WindowsMaintenanceCenter.App` with
   DI, MVVM, Dark/Light/System theme, runtime DE/EN, five working pages.
3. ~~Implement `tests/WindowsMaintenanceCenter.Tests`~~ **written, not executed.** The project covers the
   safety invariants (fail-closed paths, version comparison, decision engine, path guard, maintenance
   dry run vs. execute, localizer key parity, report generator blocked-format path, simulation
   fixture). It must be run on a machine with the SDK; until then the test result is **unknown**.
4. Build on a Windows machine with the pinned .NET 10 SDK, fix what the compiler reports, run the
   tests, and record the raw output as evidence.
5. Run the application on the reference machine (Ryzen 5 5600G / GIGABYTE B450M S2H / BIOS F67 /
   Windows 11 Pro / RTX 3060 / 24 GB) and record which values are detected correctly — without
   modifying that machine.
6. Produce the release artefacts (portable exe, installer, checksums, release notes) **by building
   them**, never by hand. The scripts and the installer definition exist; they have never been run,
   because that needs Windows + SDK + Inno Setup.

### Delivery layer: what the pipeline itself now refuses to do

The Windows job is written but has never run (no Windows machine, no .NET SDK in the development
container). What can be checked without running it is that it cannot report a success it did not
earn - three holes were closed:

* `scripts/test.ps1` accepted a return code of 0 as "tests passed". A test run that discovers no
  tests exits with 0. The script now requires the TRX result file to exist and to contain at least
  120 executed tests (the suite has 16 classes with 125 cases), otherwise it fails.
* The workflow uploaded `artifacts/release/*` without checking the four required artefacts. It now
  lists `WindowsMaintenanceCenter-Portable-x64.exe`, `WindowsMaintenanceCenterSetup-x64.exe`,
  `WindowsMaintenanceCenter-Checksums.txt` and `WindowsMaintenanceCenter-ReleaseNotes.txt`, requires each to exist
  with a non-zero size, and verifies the checksums against the files.
* The upload step ran only after a green job, so a failed build produced no diagnostics at all.
  It now runs with `if: always()` and keeps the test results and the download diagnostics.
* The job sets `DOTNET_CLI_TELEMETRY_OPTOUT=1` (the application ships no telemetry, the build sends
  none either) together with `DOTNET_NOLOGO` and `NUGET_PACKAGES`.

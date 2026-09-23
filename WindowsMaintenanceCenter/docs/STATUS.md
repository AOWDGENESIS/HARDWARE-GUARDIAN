# Windows Maintenance Center — Build Status (honest working document)

This file is deliberately blunt. It exists so that no reader can mistake the current
state for a finished product. It is updated after every work session.

Last updated: 2026-09-22 (twenty-third session)

---

## 1. What this environment can and cannot do

| Capability | State | Consequence |
| --- | --- | --- |
| .NET SDK / `dotnet build` | **NOT AVAILABLE IN THIS CONTAINER** (no SDK, no install path) — but a GitHub hosted Windows runner builds the whole solution: run `35505778032`, 15 projects including WPF, 0 errors | The build is proven **on the CI machine**, never here. What that machine cannot prove (sensors, battery, UAC prompt, reboot) is listed in `docs/VM-CI.md` |
| NuGet restore (`dotnet restore`) | **NOT AVAILABLE IN THIS CONTAINER**; the CI runner restores every package and the restore log is filed with each run | The pins are exercised for real now; a package that cannot be resolved fails the CI run instead of being discovered later |
| WPF / WPF designer | **NOT AVAILABLE** (Linux) | The App layer can be written, but not rendered or started here. |
| Windows + real hardware test (rule 89) | **NOT AVAILABLE** | All Windows-specific behaviour is **UNVERIFIED BY EXECUTION**. |
| Syntax check (tree-sitter C# grammar) | AVAILABLE after `pip install tree-sitter tree-sitter-c-sharp tree-sitter-powershell` (a fresh container has none, and the runner then reports the skipped check instead of passing quietly) | All 167 C# files parse without syntax errors (2026-09-23). **Syntax only — not a compile, not a type check.** |
| Contract check (`tools/check-contracts.py`) | AVAILABLE | Heuristic check of the API surface: object initialisers, enum/static member access, `local.Member` against the declared type of the local, interface implementations. Covers `src/` **and** `tests/`. Currently **0 findings** (167 files, 488 declared types). Not a compiler. |
| Unit tests | **EXECUTED AND PASSING ON THE CI MACHINE**: 370 cases, 0 failed, run `35704157557` (the run before, `35694300454`, had 359); the TRX file and a `summary.txt` (timestamp, version, build, environment, result per chapter 71) sit in `test-results/unit/20260922T062120Z-359-of-359/` | This is a real test run, and it is still not a substitute for the target machine (chapter 93). Nothing here claims that a Windows-only behaviour was verified. |

Therefore, for the current revision:

- Build status: **BUILT ON THE WINDOWS CI MACHINE** (runs `35505778032` and `35704157557`, 0 errors); **never built in this container**
- Test status: **370 cases executed, 370 passed on the Windows CI machine** (run `35704157557`); the suite has since grown by 22 cases (3 localisation, 19 one-click maintenance) that **have never been executed** - they are written, contract-checked and reviewed, nothing more
- Type correctness: **NOT VERIFIED** (no compiler available)
- Runtime behaviour on Windows: **NOT VERIFIED** (no Windows, no hardware)

---

## 2. Repository layout (current)

| Project | Files | Lines | Purpose | State |
| --- | --- | --- | --- | --- |
| `WindowsMaintenanceCenter.Core` | 58 | 11 536 | Domain + contracts + services, no Windows APIs, embedded `Resources/{de,en,ja,ru}.json` (961 keys each) | Written; contract-checked; localisation 961/961 keys in four languages |
| `WindowsMaintenanceCenter.Infrastructure` | 27 | 5 021 | Paths, registry, processes, PowerShell, persistence, logging, HTTP, security, backup, rollback, localisation | Written; contract-checked |
| `WindowsMaintenanceCenter.Hardware` | 6 | 1 787 | WMI provider for real hardware, Secure Boot variable, TPM state, firewall profiles, BitLocker | Written; contract-checked |
| `WindowsMaintenanceCenter.Sensors` | 2 | 525 | ACPI / performance / storage / vendor sensor providers | Written; contract-checked |
| `WindowsMaintenanceCenter.Drivers` | 1 | 277 | Driver inventory + PnP problem-code analysis | Written; contract-checked |
| `WindowsMaintenanceCenter.Bios` | 1 | 307 | Firmware assessment (never flashes), firmware file inspection | Written; contract-checked |
| `WindowsMaintenanceCenter.Security` | 1 | 95 | Security report aggregation | Written; contract-checked |
| `WindowsMaintenanceCenter.Manufacturer` | 3 | 1 090 | Official-source adapters, resolver, update centre (`IUpdateCenter`) | Written; contract-checked |
| `WindowsMaintenanceCenter.Maintenance` | 5 | 1 782 | Scan / plan / dry run / execute, software + process inventory, workload detection, free space remeasurement | Written; contract-checked |
| `WindowsMaintenanceCenter.Windows` | 1 | 1 798 | `IWindowsHealthService`: DISM, SFC, CHKDSK, event log, Defender (read + Quick/Full Scan), Windows Update, TPM, firewall, BitLocker | Written; contract-checked |
| `WindowsMaintenanceCenter.Simulation` | 1 | 419 | `MockHardwareProvider` fixture, clearly labelled as simulation | Written; contract-checked |
| `WindowsMaintenanceCenter.Reporting` | 1 | 969 | `IReportGenerator`: JSON / TXT / HTML; PDF deliberately blocked | Written; contract-checked |
| `WindowsMaintenanceCenter.Diagnostics` | 6 | 862 | Diagnostic modules: driver health, storage health, sensors, Windows health, workloads, firmware assessment | Written; contract-checked |
| `WindowsMaintenanceCenter.App` | 21 | 3 709 | WPF shell: DI root, MVVM, Dark/Light theme, four languages at runtime, 7 pages | Written; XAML-checked; **NOT EXECUTED** |
| `tests/WindowsMaintenanceCenter.Tests` | 33 | 6 855 | xUnit v3 test project: version comparison, path guard, problem registry, state machine, overall status, update decision engine, maintenance safety, localisation parity, report generator, simulation fixture, inventory failure handling, SMBIOS code tables, Secure Boot interpretation, TPM/firewall verdicts, size formatting, Windows update result codes and command-line safety, one-click maintenance, BitLocker, sensitive data redaction, CHKDSK, Defender scans, free space remeasurement | Written; contract-checked; **NOT EXECUTED** |

Total: **167 C# files, 37 032 lines (33 of them test files) + 12 XAML files** in 15 projects, all
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
| **The portable build was not portable.** `PathProvider` selects portable mode only when the marker file `WindowsMaintenanceCenter.portable` sits next to the executable (or `--portable` was passed), and no script ever created that file. `WindowsMaintenanceCenter-Portable-x64.exe`, handed out exactly as `release.ps1` produces it, would have written its configuration, reports, backups and audit log to `%ProgramData%\WindowsMaintenanceCenter` - a portable program writing into the machine data folder. | `scripts/release.ps1`, `installer/WindowsMaintenanceCenter.iss`, new `docs/PORTABLE_README.txt` | The release writes the marker beside the executable, and the installer refuses a source folder that contains it (an installed copy must use the installed layout). **This fix was incomplete and is corrected below (finding of 2026-09-22): the marker file never reached the delivered artefact.** The installer also installs a `README.txt` that never existed - `docs/PORTABLE_README.txt` now says what the tool does, what it never does, where the data stays and which order is enforced |
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
**939 localisation keys per language**, 12 XAML files with 202 bindings, 15 projects, and **20/20**
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
| `tools/verify-syntax.py` | every C# file parses with the tree-sitter C# grammar | 160 files, no syntax error (exit code 3 and an explicit note when tree-sitter is missing) |
| `tools/check-contracts.py` | object initialisers, enum/static members, members on fields, parameters, `foreach` variables and LINQ lambda parameters, interface implementations (src **and** tests) | 154 files / 455 types, 0 findings |
| `tools/check-localization.py` | every key used in C# **or XAML** exists in both languages; no dead key; both files symmetric; WMI property names and keys built from a prefix are handled | 939 keys per language (de, en, ja, ru), 0 missing, 0 dead, placeholders equal across languages, single unescaped braces reported, `--self-test` for both, every catalogue must be embedded by its project | 4 languages, 0 findings |
| `tools/check-xaml.py` | XAML is well formed, resource keys exist, `DataType` names a known type, every `{services:Loc Key}` is defined, **no visible attribute carries a hard-coded literal**, every root element with `x:Class` has code-behind | 11 files, 0 findings |
| `tools/check-bindings.py` | every `{Binding}` path resolves against its data scope (view model or item type) | 11 files, 167 bindings, 0 findings |
| `tools/check-projects.py` | project references provide the used namespaces, every directory has a project, versions are centrally declared, every MSBuild `<Import>` resolves (`eng/Version.props`, `Directory.Packages.props`, `global.json` present, no project sets its own version) | 15 projects / 154 sources, 0 findings |
| `tools/generate-solution.py --check` | `WindowsMaintenanceCenter.sln` matches the projects on disk | up to date |
| `tools/check-powershell.py` | the scripts of the acceptance kit parse: tree-sitter over `scripts/`, `installer/` and `eng/`; the known blind spots of the grammar (unit suffixes like `1GB`, bare comma argument lists, `2>$null` in a command expression, `switch` cases spread over lines) are **filtered and counted**, never hidden silently; `--self-test` proves an unclosed call is reported | 10 files, 0 findings outside the known blind spots, 15 suppressed and listed on request - a pre-filter, the real parser on Windows is the authority (exit 3 and an explicit "not a pass" when the grammar is missing) |
| `tools/check-mutation.py` | the checkers above actually work, in both directions: every deliberate defect (property, enum member, field/parameter/lambda/`foreach` member, lost interface member in a test double, unknown localisation key, hard-coded UI text, wrong binding, a binding without a mode to a read-only property on a TwoWay-by-default target, an unclosed call in a kit script, a translation that drops a placeholder, a catalogue the project does not embed, an area of the evidence directory that disappears, a .gitignore rule that hides a whole area, a missing MSBuild import target, a third-party driver portal in the source list) has to be **reported**, and one valid construct (`new X(...).Y()`) has to stay **silent** - a checker that cries wolf is as broken as a silent one | 21/21, exit code 0 |
| `tools/check-evidence-layout.py` | the evidence directory follows chapter 71: the **ten areas** exist, no folder invents a name besides the documented `ci/`, no run folder is empty, and `test-results/README.md` names every area - an area that disappears from the repository must not look like "nothing to record" | ten areas present, 0 findings; `--self-test` proves a missing area, an invented folder, an empty run folder, a README that forgets an area and an area hidden by a .gitignore rule are all reported. The .gitignore check follows git's own rule that a file below an excluded directory stays excluded - the rule that cost this project the `release/` area |
| `tools/check-repo-size.py` | the repository stays reviewable: no file over 4 MB, no more than 64 MB tracked in total, no more than 4000 files, `test-results/` no more than 48 MB; `--self-test` proves an oversized file is reported | 740 files / 9.5 MB, evidence 7.2 MB of 48 MB, 0 findings - the evidence was thinned to the runs the gates actually cite (11.8 MB of intermediate CI logs and 2.6 MB of repeated test folders removed) |
| `tools/check-source-urls.py` | every manufacturer landing page answers over HTTPS and matches its claim in the source file; policy rules (HTTPS only, no credentials, no IP, no localhost, no third-party portal) hold | **did not run** in this environment: no direct outbound network (exit 3, "this is NOT a pass"). The four sources marked `SourceReachable` were confirmed by fetching them through the sandbox's page fetcher on 2026-09-20; `verify-all.sh` reports the skipped network check without calling the offline run incomplete |

### The application shell

`WindowsMaintenanceCenter.App` is a WPF application with a real composition root and no service locator in
the views:

* `App.xaml.cs` builds the container, creates the logger, loads the settings, applies the theme and
  the language, and selects **exactly one** hardware provider - `WindowsHardwareProvider`, or
  `MockHardwareProvider` when the application is started with `--simulation`. Simulation is never
  chosen automatically, and a banner stays visible while it is active.
* Seven pages exist and work against the real services: Overview (status, findings, sensors, modules,
  report export), Hardware (components, details, origin of every value), Windows (checks, DISM, SFC,
  Defender, updates), Maintenance (scan, selection, **mandatory dry run**, approval, execute),
  One-click maintenance (the eight phases of chapter 33 with category deselection, risk display,
  waiting approval, cancel button, before/after measurement and report), Recovery (the interrupted run
  and its restoration) and Settings (language, theme, offline mode, privacy of reports, locations,
  audit log).
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
XAML, bindings, project references, solution freshness, a size budget and a mutation self-test of the
checkers themselves. Last result: 154 files / 455 types, 839 localisation keys per language, 15
projects, 11 XAML files with 167 resolved bindings, 579 tracked files / 19.8 MB - all clean (0
findings, every key used in code is defined, no unused key left behind, every binding path resolves
against its data scope, every deliberate defect is still reported by its checker). **This is not a
build.**

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
| **The report was cleaned in one format only, and only for one kind of character.** `Safe()` escaped C0/C1 control characters - but not the Unicode *format* characters: the bidi override U+202E, the zero width space U+200B, the BOM U+FEFF, the invisible tag characters U+E0001/U+E007F. None of them is a control character, no serialiser escapes them, and every one of them reorders or hides visible text - a device name could make a report *read* like a success while the values say something else. On top of that the HTML report contained the text report inside `<pre>` (so one hole appeared in two documents) and the JSON report applied no sanitation at all, although its own test comment claimed it did. | new `Core/Values/SafeText.cs`, `Infrastructure/Serialization/JsonSerialization.cs` (`JsonOptions.Report`), `Reporting/ReportGenerator.cs`, `tests/.../ReportGeneratorTests.cs` | One rule for all delivered documents: control, format, surrogate and line separators are shown as code points; letters, digits and non-BMP characters - including emoji from a device name - stay. The text, HTML and JSON reports all use it (the JSON report gets its own options, so settings, journal and audit log still read back unchanged), five text paths that carried external values without cleaning were closed, and the SEC-14 test checks all three formats. |
| **A test defect that looked like a code defect.** The new SEC-14 test failed in run 35696805257 with "a raw U+202E was found at position 0" - and the code was right. `Assert.DoesNotContain("\u202E", document)` compares with the **current culture** by default, and a search text that consists only of an ignorable character matches at position 0 of *every* text. The failure message said exactly that: `(pos 0)` with the beginning of the document as the "found" context. | `tests/.../ReportGeneratorTests.cs` | The document check uses ordinal comparison (`IndexOf(char)`, `StringComparison.Ordinal`) and names the position, the total length and the surroundings of any raw character, so the next failure needs no guessing. The reason is written into the test, so nobody "fixes" the report for it. |
| **A hanging test step without an upper limit.** The first run of the installation cycle (35695298315) never finished: the step stood still for more than ten minutes - the environment record waits on WMI providers without a limit. The job then held a runner until it was cancelled by hand, which this access cannot do (`HTTP 403`). | `scripts/vm/Evidence.ps1`, `scripts/vm/Test-InstallerCycle.ps1`, `.github/workflows/windowsmaintenancecenter.yml` | Every WMI/CIM query has a 20 s limit and becomes a line in the environment record instead of a hang; setup and uninstaller run under a 300 s budget and a timeout turns into a named finding; the step has a 15 minute limit and the job 45 minutes, so a hang ends the run and the record step still writes the log into the branch. |
| **The portable fix did not reach the delivered file - the artefact that is handed out was never portable.** The entry above says the release writes `WindowsMaintenanceCenter.portable` beside the executable. That is true for the *build* folder `artifacts/portable/`; the artefact that is handed out is the **single file** `artifacts/release/WindowsMaintenanceCenter-Portable-x64.exe`. Nobody unpacks a folder, the marker has no folder to sit in, `PathProvider.IsPortable` was false - and the "portable" program wrote configuration, reports, backups and audit log into `%ProgramData%\WindowsMaintenanceCenter`. No build, no test and no check could see it: the release log showed the marker being written, the checksums matched, and only running the delivered file would have shown the truth. On top of that the installer guarded against exactly this case with a runtime check that could never fire: `InitializeSetup` compared `{#SourceDirectory}` - a path on the build machine - with the working directory of the machine running the setup, and installed the marker file into `{tmp}` with `dontcopy`, where nothing ever read it. | new `Core/Services/PortableMode.cs`, `Infrastructure/Platform/PathProvider.cs`, `src/WindowsMaintenanceCenter.App/WindowsMaintenanceCenter.App.csproj`, `scripts/build.ps1` (`-Portable`), `scripts/release.ps1`, `installer/WindowsMaintenanceCenter.iss`, `scripts/vm/Test-InstallerCycle.ps1`, new `tests/.../PortableModeTests.cs` | The decision has one home and three checkable facts: `--portable` on the command line, the marker file beside the executable (folder form), or the marker **inside** the executable - `[AssemblyMetadata("WmcPortableDefault", "true")]`, which the publish switch `-Portable` puts into the published assembly. The release publishes **twice, deliberately**: `artifacts/portable` with `-Portable` becomes the portable artefact, `artifacts/install` without it becomes the program folder the installer installs (an installed copy must not write next to itself, chapter 82). The installer's dead runtime check became a compile-time `#error` that reads its own source path, the `dontcopy` placeholder is gone, and `release.ps1` refuses an installer source folder that contains the marker *before* Inno Setup starts. `check-projects.py` keeps the name `WmcPortableDefault` in step between the project that writes it and `PortableMode` that reads it; seven unit tests pin the decision; and `Test-InstallerCycle.ps1` - which now runs in CI - copies the portable artefact into an empty folder and fails when no `data` folder appears next to it, which is the only check that could have caught this |
| **Localisation gap in the UI:** 99 visible strings in the ten XAML files were hard-coded English (`"Cancel"`, `"Findings"`, every column header), so switching to German would have left the shell in English | `App/Views/*.xaml` + `Core/Resources/{en,de}.json` | All 99 replaced by `{services:Loc Key}` (82 distinct keys, `Shell_*`, `Section_*`, `Column_*`, `Action_*`, `Option_*`, `Field_*`, `Notice_*`); `check-xaml.py` now fails on any new hard-coded visible literal, and `check-localization.py` reads XAML keys as well |

Verified as **already correct** against the real contracts (no change needed): `BackupService`
(`IBackupService` signature and `BackupRequest` usage), all hardware/sensor/BIOS/manufacturer model
usage, `ApprovalRequestDraft`, `ApprovalRecord`, `ProblemDraft`.

### The first start, and the proof that could not be written (runs 35697744225 and 35701860625)

| Defect | Location | Fix |
| --- | --- | --- |
| **The delivered program could not start at all.** The installation cycle of run 35697255686 started the installed program for the first time in this project's history and it ended with exit code 1. The log that run 35697744225 filed as evidence held exactly one line: `Critical · App · A TwoWay or OneWayToSource binding cannot work on the read-only property 'ProgressPercent'`. `MainWindow.xaml` bound `ProgressBar.Value` without a mode, and `ProgressBar.Value` inherits `RangeBase.Value`, whose metadata carries `BindsTwoWayByDefault`; WPF therefore demanded a settable property, `MainViewModel.ProgressPercent` has a private setter, and the exception came up while the window was loading. Every start of the program on every machine failed - the build was green, the XAML compiler was happy, the binding checker was happy (it checks paths, not modes), and nothing had ever started the program. | `App/Views/MainWindow.xaml`, `tools/check-bindings.py`, `tools/check-mutation.py` | The binding carries `Mode=OneWay` with the reason next to it. `check-bindings.py` knows the targets that are TwoWay by default (`RangeBase.Value`, `TextBox.Text`, `Selector.SelectedItem/Index/Value`, `ToggleButton.IsChecked`, `DatePicker.SelectedDate`, `ComboBox.Text`) and reports a binding without a mode to a property without a public setter; the message names control, property and fix. Mutation case 15 produces exactly this defect and the checker reports it. **Proof that it is fixed:** run 35701860625, `test-results/ci/run-35701860625-1/installer-cycle.log` line 32 - `PASS program closes exit code 0 after the window was closed`. |
| **The report writer of the evidence kit never worked.** `Complete-WmcEvidenceRun` ended with `Argument types do not match` in three runs; every step of the installation cycle had passed, and the run folder still held no `report.json` and no `report.txt` - a complete cycle without a report. Run 35697744225 named only a line number, run 35701860625 the statement (`$report = [ordered]@{`), and neither said which frame threw. | `scripts/vm/Evidence.ps1`, new `scripts/vm/Test-EvidenceLibrary.ps1`, workflow step "Evidence library self-test" | The error text carries the stack, and the library has a self-test that runs before the cycle: it walks the real path, checks the five fields of chapter 71 in `report.json`, refuses a `PASSED` report without a measurement (chapter 86, negative on purpose) and, when the real path fails, probes the constructs one at a time. That is how the cause was found - **not the literal was broken, the value was**: an array that PowerShell builds from a `System.Collections.Generic.List[object]` (`@($Run.Evidence)`) cannot be handed to a hashtable or pscustomobject literal on that PowerShell, while `[ordered]@{ e = $Run.Environment }`, `[pscustomobject]@{ l = 'a','b' }` and every other form are fine. The report is therefore filled through `OrderedDictionary.Add()` and the lists are passed as the .NET type that `ConvertTo-Json` writes as an array. **Proven** by run 35704157557: self-test green, and `test-results/installer/20260922T082333Z-INS-CI/report.json` plus `report.txt` exist with `result: PASSED`. |
| **A checker that reports a correct construct.** The contract checker read `var build = new BuildInfoProvider(paths, paths).Get();` as binding `build` to `BuildInfoProvider`, ignored `.Get()` and reported three "has no member 'Version'" findings for `App.xaml.cs` - a false alarm that would have blocked every commit. | `tools/check-contracts.py`, `tools/check-mutation.py` | Constructor chains resolve through the return type of every link (methods now carry their return type, interfaces carried theirs already); an unresolved link ends the resolution instead of guessing, and an unknown receiver is never reported. The mutation self-test got a case for the *other* direction (`expect_clean`: a valid construct must stay silent), so a checker that cries wolf fails the same gate as one that stays silent - 16/16. |
| **The checkers could not see what a binding engine does at run time.** `check-bindings.py` resolved paths only, `check-mutation.py` knew thirteen defects, and nothing measured the repository itself. | `tools/check-bindings.py`, `tools/check-mutation.py`, new `tools/check-repo-size.py`, `tools/verify-all.sh` | The binding checker now knows the TwoWay defaults of the WPF targets; the mutation file has 16 cases in both directions; a size budget (4 MB per file, 64 MB in total, 4000 files, 48 MB of evidence) with its own self-test keeps the repository reviewable, which is what a diff that never fills up depends on. |

### Four languages were required, two were shipped (2026-09-22)

| Defect | Location | Fix |
| --- | --- | --- |
| **Chapter 63 asks for de-DE, en-US, ja-JP and ru-RU; the product shipped two.** The loader had `new[] { "en", "de" }`, the settings list had its own copy of the same two values, and `LanguagePreference` had only `German` and `English` - so there was no way to even *name* a third language. | new `Core/Resources/ja.json` and `Core/Resources/ru.json`, `Core/WindowsMaintenanceCenter.Core.csproj`, `tests/.../LocalizationTests.cs` | Both catalogues exist with **939 keys each** (847 at the time of that fix, plus the keys the one-click page added later), the same keys as English and German, and the placeholders of every key are identical across all four languages (checked by `tools/check-localization.py` and by new unit tests that take the language list from the assembly instead of naming it). The project embeds `Resources/*.json` by glob, so a catalogue cannot sit in the folder without shipping. **Not claimed:** that the translations are linguistically reviewed - they are machine written and a native speaker review is an open point, named in `docs/RELEASE_STATUS.md`. Nor is the chapter 63 proof on a machine (`test-results/localization/` is empty): the interface cannot be started here. |

### The evidence area that could never be committed (2026-09-23)

| Defect | Location | Fix |
| --- | --- | --- |
| **The tenth evidence area of chapter 71 was excluded by the project's own .gitignore.** The pattern `release/` (meant for build output) also matched `test-results/release/`, and git cannot re-include a file below an excluded directory - so the folder existed on disk, was invisible to git, and every clone showed nine areas. The reason nobody noticed: the checker that was supposed to look at the layout did not exist, and the other areas carry files that were committed before the rule mattered. | `WindowsMaintenanceCenter/.gitignore`, new `tools/check-evidence-layout.py` | The rule is re-included for the evidence path (`!test-results/release/`). The new checker verifies that all **ten** areas exist, that no folder invents a name besides the documented `ci/`, that no run folder is empty, that the README names every area - and now also that a **fresh file in every area would be committable**: it reads the .gitignore rules and applies git's own rule that a file below an excluded directory stays excluded. 21 deliberate defects are reported by `tools/check-mutation.py`. |
| **A new test result file would have been ignored as well.** `*.trx` is in the .gitignore (build output), so the TRX file that Gate 2 rests on could only ever exist because it was committed before the rule. Every future run would have written evidence that no clone receives. | `WindowsMaintenanceCenter/.gitignore` | `!test-results/**/*.trx` (and the same for `.xml`/`.json`/`.jsonl`) keeps the evidence committable while build output stays ignored. Proven with a probe file: `git status` lists it instead of hiding it. |

### BitLocker was missing from the P0 discovery (2026-09-23)

| Defect | Location | Fix |
| --- | --- | --- |
| **Chapter 8 lists BitLocker among the must-have discoveries; nothing in the code read it** - and `docs/ABNAHME-WMC.md` said so. A P0 module criterion that no code path can satisfy. | new `Hardware/Wmi/BitLockerReader.cs`, `Core/Models/WindowsModels.cs`, `Windows/WindowsHealthService.cs`, `tests/.../BitLockerReadingTests.cs` | The reader queries the documented `Win32_EncryptableVolume` class (read-only properties only - the provider's methods require administrator rights and would change the system) and judges with three honest outcomes side by side: protected, unprotected, and **not reported**. A protected system volume plus a volume that answered nothing is `UNKNOWN PROTECTION`, never a green check, and `PROTECTION ON` with a fully decrypted volume is reported as unprotected because the two answers contradict each other. 11 unit tests pin the value tables and the falsification cases. **Not claimed:** that this ever read a real BitLocker volume - the class needs Windows, and the CI is stopped by a billing problem. |

### The log could have contained a password (2026-09-23)

| Defect | Location | Fix |
| --- | --- | --- |
| **M38-S-001 ("no passwords in the log") had no mechanism behind it.** The technical log and the audit file wrote whatever the caller passed: a recovery command line, an approval note or an exception message could carry a credential into a file that is handed over to support or to a customer. | new `Core/Security/SensitiveDataGuard.cs`, `Infrastructure/Logging/TechnicalFileLogger.cs`, `Infrastructure/Persistence/FileAuditSink.cs`, `tests/.../SensitiveDataGuardTests.cs` | Redaction happens **before** the line is built, in both the technical log and the audit file, and the log records `redacted: true` so a quiet log can be told apart from one that had a secret in it. Recognised shapes: keyword values (`password=`, `token:`, `apiKey`), command line passwords (`--password`, `-Passphrase`, `/password:`), BitLocker recovery passwords (48 digits, grouped or not) and private key blocks. 12 tests cover both directions - a secret **must** go, ordinary evidence text **must** stay readable, because a filter that mangles the log gets switched off. **What it cannot do and does not claim:** recognise a document's contents or a secret written as a normal sentence. M38-S-002 rests on the code paths never writing file contents into a message, which no filter can compensate for. |

### M12 File System Integrity (CHKDSK), M18 Defender Scans, M04 Remeasurement and M22 Restore Point Validation (2026-09-23)

| Defect | Location | Fix |
| --- | --- | --- |
| **M12 CHKDSK was missing from the Repair Engine.** DISM and SFC were wired up, but file system integrity verification was not. Chapter 18 demands file system integrity assessment and repair without blind modifications. | `Windows/WindowsHealthService.cs`, `Core/Models/WindowsModels.cs`, `tests/.../IntegrityCheckTests.cs` | Added `WindowsCheckId.FileSystemIntegrity`, `RunFileSystemCheckAsync`, and `InterpretChkdskOutput`. Read-only online scan runs as `chkdsk.exe {drive} /scan` (Windows 8+ online self-healing scan, no volume unmount); repair `/f` requires an explicit approval record. Exit codes and tool output are parsed, reboot requirements (`M12-E-002`) are detected, and critical repair actions document the WinRE recovery path (`M12-R-001`). 5 unit tests. |
| **M18 Quick Scan and Full Scan had no trigger.** Defender status was read via `Get-MpComputerStatus`, but M18-F-001 and M18-F-002 require initiating Quick and Full scans, and M18-F-003 requires verifying the status after the scan. | `Windows/WindowsHealthService.cs`, `Infrastructure/Platform/PowerShellRunner.cs`, `tests/.../DefenderScanTests.cs` | Added `DefenderScanType`, `DefenderScanResult`, `RunDefenderScanAsync`, and PowerShell templates for `Start-MpScan -ScanType QuickScan/FullScan`. Re-queries `GetDefenderStatusAsync` immediately after execution to record updated state. Tests pin that no template disables Defender (`M18-S-001`) or Firewall (`M18-S-002`). |
| **M04-F-004 required remeasuring free space after cleanup on disk.** The engine calculated freed bytes by summing deleted files, but did not measure actual filesystem free space before and after execution. | `Maintenance/MaintenanceService.cs`, `Core/Models/MaintenanceModels.cs`, `tests/.../MaintenanceFreeSpaceTests.cs` | Added `SystemFreeSpaceBefore` and `SystemFreeSpaceAfter` to `MaintenanceResult`. The execution phase queries actual available free space on the system drive before and after deletions and records the delta in audit evidence. |
| **M22 Restore Points had static naming and lacked validation.** `Checkpoint-Computer` was called with a hardcoded string `'WindowsMaintenanceCenter'` and creation was not verified against the requested operation. | `Infrastructure/Security/BackupService.cs` | Operation-specific naming `WMC-{Kind}-{OpId}` is generated, and the created restore point is queried and validated (`RP_VALIDATED=true`) to satisfy `M22-F-002`. |

### An M27 that only existed in the acceptance table (2026-09-23)

| Defect | Location | Fix |
| --- | --- | --- |
| **Chapter 33 was listed as a P0 module and did not exist.** `docs/ABNAHME-WMC.md` named M27 under "nothing implemented", and nothing in the product offered the specified sequence. The maintenance page did the individual steps, but the run as a whole - discovery, diagnostic, plan, approval, backup, execution, validation, report - had no conductor, so no run could ever be checked against the specification. | new `Core/Services/OneClickMaintenanceService.cs`, `Core/Models/OneClickModels.cs`, `App/ViewModels/OneClickViewModel.cs`, `App/Views/OneClickView.xaml`, `tests/.../OneClickMaintenanceTests.cs` | The conductor runs exactly the eight phases in the specified order and refuses to skip a gate: without an approval of exactly this plan nothing is executed, a plan that needs a backup is secured first, and the final state is `SUCCESS` only when execution **and** validation happened. It writes every phase with what/why/risk/result to the live protocol, the audit log and the report, and it measures free space before and after. **Measured, not assumed:** a missing reading stays `UNKNOWN` (the delta is computed only when both readings exist, and the sum refuses to compare two different sets of volumes), and 19 unit tests pin the order, the approval gate, the deselect rule, the backup order, the state journal and the cancellation path. **Not claimed:** that this ran on Windows - it cannot here, because the CI is stopped by a billing problem, so `test-results/` stays empty for M27 and `docs/RELEASE_STATUS.md` keeps the gate open. |

### The language selector was not translated, and there was no second chance to notice (2026-09-22)

| Defect | Location | Fix |
| --- | --- | --- |
| **The language and theme selectors showed raw identifiers.** The settings page bound `ComboBox.ItemsSource` straight to `LanguagePreference` values, so a user who had just switched the interface to German was offered a list reading "System", "German", "English" - in every language. The theme list showed "System", "Dark", "Light" the same way. Chapter 63 asks for four languages and chapter 41 for a catalogue, and these two lists were the only place in the product where the *identifier* was shown instead of a translation - because a plain `{Binding}` to an enum needs a converter and nobody had added one. | new `EnumLocalizedNameConverter`, `App.xaml`, `App/Views/SettingsView.xaml`, `Core/Resources/{de,en}.json` | One converter turns the value into a catalogue key (`Language_` + value, `Theme_` + value) and the localizer resolves it; a missing catalogue entry shows the localizer's marker instead of a wrong name. Both selectors use it, and the keys (`Language_System/German/English/Japanese/Russian`, `Theme_System/Dark/Light`) exist in both catalogues. |
| **A language could be offered without a catalogue, or shipped without being offered.** The loader had `new[] { "en", "de" }` written into it and the settings list had its own copy of the same two values - two places to keep in step, and neither would notice a third catalogue file. | new `Core/Services/LanguageCatalog.cs`, `Infrastructure/Localization/JsonLocalizer.cs`, `App/ViewModels/SettingsViewModel.cs` | Which languages exist is *measured* from the assembly: every embedded `Resources/<code>.json` is a shipped language. The loader loads exactly those, `LanguagePreference` gained `Japanese`/`Russian` so the interface can name them, and the settings list offers a language only when its catalogue is there. Adding `ja.json` and `ru.json` is now the whole remaining work for chapter 63 - no list has to be found and updated. |
| **Nothing checked whether a translation keeps its placeholders.** `tools/check-localization.py` compared key sets, not the `{0}`/`{1}` inside the texts. A translation that renumbers or drops a placeholder is a `string.Format` failure at run time, and in a diff of prose it is invisible. | `tools/check-localization.py`, `tools/check-mutation.py`, `tools/verify-all.sh` | The checker compares the placeholder set of every key across all languages, reports a single unescaped brace (which `string.Format` would read as a placeholder), and has `--self-test` proving both are reported; mutation case 18 removes a placeholder from the German catalogue and the checker notices. 18/18. |

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
   DI, MVVM, Dark/Light/System theme, runtime de/en/ja/ru, seven working pages - **written and checked,
   never started**: the container has no WPF, so what the pages look like is unverified until the
   interface runs on Windows.
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

---

## 6. Stand der Ablage (was liegt wo, was ist nicht abgelesen)

Diese Zeilen stehen hier, damit ein Leser den Zustand nicht aus einem Chat rekonstruieren muss. Stand:
2026-09-23.

**Betriebshinweis (aus Schaden gelernt):** Diese Umgebung wird gelegentlich **neu geklont**. Dann
steht der Zweig auf `main`, die Arbeit liegt als unverfolgte Datei auf der Platte und die alten
Commit-Objekte fehlen. Vorgehen: `git fetch origin arena/01a0bb04-entwicklungen` → `git reset FETCH_HEAD`
(die Dateien bleiben liegen) → weiterarbeiten → committen → pushen. **Nicht** `reset --hard` und
**nicht** `clean`: beides würde genau die Arbeit löschen, die noch nicht gepusht ist. Deshalb gilt:
jede Sitzung endet mit einem Push, und die Parser-Pakete
(`pip install --break-system-packages tree-sitter tree-sitter-c-sharp tree-sitter-powershell`) müssen
in einer frischen Umgebung einmal installiert werden, sonst meldet `verify-all.sh` zu Recht drei
übersprungene Prüfungen.

**Zweig und Kopf:** `arena/01a0bb04-entwicklungen` = Remote-Stand `8073341`; Kette seit `main`
(`5a6a4cf`): `d759180` → `220c34d` → `3397fc0` → `2bd169e` → `fd11038` → `60b379b` → `4e36846` →
`63cb536` → `c02d86c` → `e6d3a47` → `87540a8` (vier Sprachen) → `27ea6ad` (M27) → `54849dd`
(Nachweislayout) → `2fa2985` → `fc2515c` → `55bd18a` → `8073341` (BitLocker, Wächter, .gitignore-Fixes).
Alle Prüfungen ohne SDK sind grün: `bash tools/verify-all.sh` (21 Mutationen, Nachweislayout, Lokalisierung 961x4,
Verträge 167 Dateien / 488 Typen, XAML, Bindings, Projekte, Größe). Für den Übertrag nach `AOWDGENESIS/HARDWARE-GUARDIAN`
liegt das Synchronisationsskript `tools/sync-to-hardware-guardian.sh` bereit.

**Gemessen und belegt (auf der CI-Maschine, nicht auf der Zielmaschine):**

* Bau und Tests: Lauf `35704157557`, Commit `fd11038` - 15 Projekte inklusive WPF, 0 Fehler,
  **370 von 370 Testfällen bestanden** (`test-results/ci/run-35704157557-1/`,
  `test-results/unit/20260922T083031Z-370-of-370/`).
* Installationszyklus: `test-results/installer/20260922T082333Z-INS-CI/` - 20 Kriterien PASS,
  Ergebnis `PASSED`, offene Punkte im Bericht genannt.

**Geschrieben, geprüft, aber nie ausgeführt (NOT VERIFIED):**

* 55 Testfälle (3 Lokalisierung, 19 Ein-Klick-Wartung, 11 BitLocker, 12 Sensibler-Daten-Wächter, 5 CHKDSK, 3 Defender-Scan, 2 Cleanup-Freispeicher). Sie sind im Baum, ihre Zahl ist nicht
  auf einer echten Maschine gemessen - deshalb steht Gate 2 weiterhin auf dem gemessenen Stand von 370.
* Die vier Sprachkataloge sind mit je 961 Schlüsseln symmetrisch und platzhaltergleich, aber nicht von Muttersprachlern
  gelesen und nie auf einem Bildschirm gesehen.
* M27 (Ein-Klick-Wartung) ist vollständig implementiert (Dienst, Seite, 19 Fälle), hat aber keinen
  Lauf auf einer Maschine; die acht VM-Fälle `M27-01`…`M27-08` in `docs/TESTMATRIX-VM.md` erheben ihn.

**Blockiert, ohne dass Code das ändern könnte:**

* **Die CI startet seit dem 2026-09-22 nicht mehr** - beide Workflows melden `steps=0` mit der
  Anmerkung „recent account payments have failed or your spending limit needs to be increased".
  Das kann nur der Kontoinhaber beheben. Bis dahin entsteht **kein** neuer Windows-Nachweis.
* Zielhardware für Sensoren, Akku, SMBIOS, UAC-Dialog und Neustart fehlt weiterhin; das VM-Testkit
  (`scripts/vm/`) und `docs/TESTMATRIX-VM.md` liegen dafür bereit.

**Nächste Schritte, in dieser Reihenfolge:**

1. Kontoproblem beheben, damit die CI wieder startet; danach den Lauf ablesen und den Nachweisordner
   nach `test-results/ci/` holen (Log-Download ist aus dieser Umgebung gesperrt).
2. Die 22 neuen Fälle mit dem nächsten Lauf wirklich ausführen und die Zahl in
   `scripts/test.ps1` (`MinimumTests`) erst dann anheben, wenn sie gemessen ist.
3. P0-Lücken nach `docs/RELEASE_STATUS.md` schließen (M26, M05, M11, M14, M20, M42; M27 ist aus
   dieser Liste heraus, siehe Tabelle in `docs/ABNAHME-WMC.md`).
4. Zielmaschine: `docs/TESTMATRIX-VM.md` Abschnitt 7 (14 Sicherheitsangriffe), 8 (Lokalisierung,
   Offline, UI) und 8a (M27) abarbeiten und die Ordnernamen aus Kapitel 71 befüllen.

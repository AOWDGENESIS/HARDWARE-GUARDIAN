# GitHub Copilot & AI Development Instructions
## Project: Windows Maintenance Center / Hardware Guardian (V1.0)

This project strictly adheres to the engineering specification **SPEC-WMC-V1.md** (Chapters 1–103, Modules M00–M48).
All AI assistants, contributors, and automated tooling must uphold the following architectural constraints:

### 1. Safety & Veracity Principles (Spec Chapter 86 & 96)
- **Zero Hallucination / No Fabricated Success**: A status of `SUCCESS` or `PASSED` is ONLY permitted when an action was BOTH executed AND explicitly validated by a measurement. Without proof, the status must remain `NOT_STARTED` or `BLOCKED`.
- **No Mock / Fake Diagnostics**: Never return fake hardware or performance numbers. If a hardware value or sensor cannot be read, report `UNKNOWN` along with a diagnostic reason code.
- **Maintenance Safety**: Maintenance actions must strictly follow: Scan -> Plan -> **Mandatory Dry Run** -> User Approval -> Execute -> Verify. Only `SAFE` and `OPTIONAL` categories may be touched. `PROTECTED` and `UNKNOWN` categories are strictly read-only.
- **No Security Downgrades**: Never disable Windows Defender, never modify firewall rules without explicit user prompt, never bypass UAC, and never touch unknown services or scheduled tasks automatically.

### 2. Architecture & Code Boundaries
- **Layering**: `UI (WPF / MVVM)` -> `Core (Domain / Models / Pure Functions)` -> `Action Registry` -> `Admin Worker` -> `Windows / APIs`.
- **Cross-Platform Testability**: `WindowsMaintenanceCenter.Tests` targets `net10.0` (Cross-Platform). Pure business logic, interpreters, calculators, and parsers MUST reside in `WindowsMaintenanceCenter.Core`. OS-dependent calls belong behind interfaces (`IPlatformReader`, `IActionExecutor`, etc.) in `WindowsMaintenanceCenter.Windows` or `WindowsMaintenanceCenter.Bios`.
- **Localization**: All user-facing strings must exist across all four official languages (`de-DE`, `en-US`, `ja-JP`, `ru-RU`) in `src/WindowsMaintenanceCenter.Core/Resources/{de,en,ja,ru}.json` with matching placeholder indices.

### 3. Verification & Evidence (Spec Chapter 71)
- Automated evidence is strictly written into `test-results/` across the 10 defined areas: `unit`, `integration`, `safety`, `security`, `recovery`, `installer`, `localization`, `offline`, `regression`, `release`.
- Code changes must pass all static repository gates (`tools/verify-all.sh`, `check-bindings.py`, `check-contracts.py`, `check-localization.py`, `check-powershell.py`, `check-projects.py`, `check-repo-size.py`, `check-xaml.py`, `verify-syntax.py`).

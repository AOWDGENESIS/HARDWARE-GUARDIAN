# Security design notes

Windows Maintenance Center reads a lot and changes very little. This document lists the rules that the code
enforces, and where they are enforced.

## Principles

* **Fail closed.** When evidence is missing or cannot be verified, the result is `BLOCKED` (with a
  machine-readable reason) or `UNKNOWN` - never a guess and never a silent success.
* **Least privilege.** Reads happen without elevation where possible. Everything that needs
  administrator rights is blocked with `ADMINISTRATOR_REQUIRED` when the process is not elevated,
  instead of triggering a UAC prompt behind the user's back.
* **No cloud dependency.** The application works offline. Network access is only used for official
  manufacturer/Microsoft sources and only when the user enabled update checks.
* **No telemetry.** `TelemetryEnabled` exists for compatibility and is fixed to `false`; nothing is
  transmitted. Machine name and serial numbers stay local and are masked in reports.

## Update sources

`UpdateDecisionEngine` only recommends an update when **all** of the following hold:

* the source is known and has a URL,
* trust is at least `VerifiedOem` (manufacturer, Microsoft, Windows Update, verified OEM),
* verification is at least `MetadataMatch`,
* the hardware match is proven (not assumed),
* compatibility is positively established (`IsCompatible == true`, not `null`),
* the facts are not stale.

Third-party driver portals are never used as a source. In simulation mode everything is blocked.

## File operations

* Every delete goes through `IPathGuard` for the concrete root of that cleanup target, plus a second
  containment check on the normalised path, and the file must be older than the minimum age.
* Protected categories are measured and explained but never cleaned.
* Automatic rollback covers file copies (verified with SHA-256 before success is claimed) and
  registry exports. A system restore point and a driver state remain explicit manual steps.

## Firmware

Firmware is never flashed. Without a verified board revision and a configured official source the
assessment is `BLOCKED` (`BOARD_REVISION_UNKNOWN`, `SOURCE_UNKNOWN`). A firmware recommendation
always names the manual step (vendor tool or BIOS Flashback).

## Reporting vulnerabilities

Report a vulnerability through a GitHub security advisory on
`https://github.com/AOWDGENESIS/Entwicklungen` (Security → Report a vulnerability) so that the report
stays private until it is fixed. Please include the version, the commit and the raw log/audit output
if possible - the application writes an audit trail precisely for that purpose.

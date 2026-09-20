# Release-Entscheid (Kapitel 92/93/94 der WMC-Spezifikation)

**Status: `RELEASE_BLOCKED`**
Stand: 2026-09-20, Branch `arena/01a0bb04-entwicklungen`.

Kapitel 92 lässt genau zwei Zustände zu. Es gibt keinen dritten. Dieser Entscheid wird ausschließlich
aus tatsächlich ausgeführten Tests gebildet - und es gibt hier keine: kein .NET SDK, kein Windows,
keine virtuelle Maschine, keine Referenzhardware.

Testreport nach Kapitel 70 (Werte nur aus ausgeführten Tests; hier sind die Felder leer, weil nichts
lief):

```text
VERSION: 1.0.0 (eng/Version.props)
COMMIT:  siehe git log -1
DATE:    2026-09-20
ENVIRONMENT: Linux-Sandbox ohne .NET SDK, ohne Windows, ohne VM

TESTS:   0 ausgeführt (245 geschrieben, 0 kompiliert)
PASSED:  0
FAILED:  0
BLOCKED: 245

SECURITY:  BLOCKED (kein Angriff ausgeführt)
SAFETY:    BLOCKED
RECOVERY:  BLOCKED
INSTALLER: BLOCKED (nie kompiliert)
OFFLINE:   BLOCKED
REGRESSION:BLOCKED

RELEASE:
BLOCKED
```

## Blockierende Tests (Format nach Kapitel 94)

| BLOCKING TEST | MODULE | REASON | EVIDENCE | REQUIRED FIX |
| --- | --- | --- | --- | --- |
| Gate 1 Build | alle | Kein .NET SDK in dieser Umgebung; kein Projekt wurde je kompiliert | `docs/STATUS.md` Abschnitt 1 | Windows-Rechner mit .NET 10 SDK: `scripts/build.ps1`, Ergebnisse nach `test-results/release/` |
| Gate 2 Unit | M00-M48 | 245 Testfälle geschrieben, nie ausgeführt | `tests/WindowsMaintenanceCenter.Tests`, TRX fehlt | `scripts/test.ps1` (verlangt TRX und mindestens 120 Fälle), Ergebnis nach `test-results/unit/` |
| Gate 4 Security | M32, M33, M44 | Keiner der 14 Pflichtangriffe (Command Injection, Path Traversal, Argument Injection, Privilege Escalation, Tampered Config, Tampered Update, Invalid Signature, Corrupt Backup, No Admin, UAC Cancel, Process Abort, Log Manipulation, Database Corruption, Report Injection) wurde ausgeführt | `test-results/security/` ist leer | Angriffe auf isolierter Windows-VM ausführen und belegen |
| Gate 5 Safety | M04, M24, M25, M27, M31, M34 | Approval-, Backup-, Rollback- und Admin-Grenze sind implementiert, aber nicht geprüft | `test-results/safety/` ist leer | Testmatrix Kapitel 76/77 durchlaufen |
| Gate 6 Offline | M29 | Offline-Betrieb aller lokalen Module nie gemessen | `test-results/offline/` ist leer | VM ohne Netz, Kapitel 61 |
| Gate 7 Installer | M40, M41 | Inno-Setup-Definition nie kompiliert, nie ausgeführt | `installer/WindowsMaintenanceCenter.iss`, `test-results/installer/` leer | Kapitel 62/74 auf sauberer VM |
| Gate 8 Localization | M37 | Japanisch und Russisch fehlen vollständig (742 Schlüssel je Sprache existieren nur für Deutsch und Englisch) | `src/WindowsMaintenanceCenter.Core/Resources/`, `test-results/localization/` leer | ja-JP und ru-RU übersetzen, dann Kapitel 63 prüfen |
| Gate 9 Recovery | M35 | Recovery-Engine nicht implementiert | `test-results/recovery/` leer | M35 umsetzen, Kapitel 64 (Test A/B/C) nachweisen |
| Gate 10 Final VM | M46 | Keine Windows-11-x64-VM verfügbar (UEFI, Secure Boot, TPM) | `test-results/release/` leer | Kapitel 52/65 auf sauberer VM |
| P0-Umfang | M31 | M31-S-001/S-002 verletzt: die Anwendung startet als Ganzes erhöht neu, statt eine registrierte Aktion privilegiert auszuführen | `src/WindowsMaintenanceCenter.Infrastructure/Platform/ElevationService.cs` | Admin Worker und Action Registry nach Kapitel 37/38 umsetzen |
| P0-Umfang | M35, M26, M27, M05, M11, M14, M20, M42 | Module fehlen ganz oder überwiegend | `docs/ABNAHME-WMC.md` Abschnitt 1 | Module umsetzen |
| P0-MUSS | M34, M35 | Zustandsmodell ist an Kapitel 40 angeglichen, aber der Zustand wird noch nicht persistiert (M34-F-001) und unterbrochene Jobs werden nicht erkannt (M34-R-001/M35) | `Core/Services/SystemStateMachine.cs` (nur Arbeitsspeicher) | Persistenz plus Recovery-Engine nach Kapitel 40/41 |
| Release-Blocker Kapitel 55 | - | „falscher SUCCESS-Status" wäre ausgelöst, sobald irgendwo `PASSED` ohne Nachweis behauptet würde | dieser Entscheid | keine falschen Erfolgsmeldungen; Status bleibt `BLOCKED` |

## Was diesen Entscheid ändern würde

Nur Nachweise, keine Einschätzung: Build-Protokoll, Testreport (TRX/JSON), Sicherheitsprotokolle,
Installer- und VM-Protokolle in `test-results/`, jeweils mit Zeitpunkt, Version, Build, Umgebung und
Ergebnis (Kapitel 71). Erst wenn Kapitel 93 vollständig erfüllt ist, darf hier `RELEASE_READY`
stehen.

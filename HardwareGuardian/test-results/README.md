# test-results/ - Nachweisverzeichnis (Kapitel 71 der WMC-Spezifikation)

Die zehn Unterordner entsprechen der Spezifikation. **Alle sind derzeit leer, weil kein Test
ausgeführt wurde.** Kapitel 5 sagt: ein Test ohne Nachweis gilt als `NOT VERIFIED`, und Kapitel 2
lässt für ein Modul ohne Nachweis nur `BLOCKED` zu.

Jeder abgelegte Report enthält: `timestamp`, `version`, `build`, `environment`, `result`.

Wer was hier ablegt:

| Ordner | Erzeuger | Aktion |
| --- | --- | --- |
| `unit/` | `scripts/test.ps1` (TRX + Konsolenausgabe) | `dotnet test` auf einem Windows-Rechner mit .NET 10 SDK |
| `integration/` | Handlauf nach Kapitel 58 | UI, Core, Discovery, Execution, Backup, Recovery, Reporting |
| `safety/` | Handlauf nach Kapitel 60, Matrizen 76/77 | Approval APPROVE/DECLINE/CANCEL/TIMEOUT, Admin-Grenze |
| `security/` | Handlauf nach Kapitel 50, 78-82 | alle vierzehn Pflichtangriffe |
| `recovery/` | Handlauf nach Kapitel 64 | Test A (Prozessabbruch), B (Anwendung beenden), C (Backup beschädigen) |
| `installer/` | Inno-Setup-Log + Kapitel 62/74 | Installation, Upgrade, Reparatur, Deinstallation |
| `localization/` | Handlauf nach Kapitel 63 | vier Sprachen, keine sichtbaren Schlüssel |
| `offline/` | Handlauf nach Kapitel 61 | VM ohne Netz, lokale Module |
| `regression/` | nach jeder Änderung, Kapitel 51 | Start, Dashboard, Discovery, Cleanup, Update, Repair, Backup, Rollback, Security, Installer, Uninstaller, Lokalisierung |
| `release/` | `scripts/release.ps1` (Hashes, Notes) + Kapitel 65 | reproduzierbarer Build, Artefakte, finaler VM-Durchlauf |

Ohne einen Report in diesen Ordnern bleibt `docs/RELEASE_STATUS.md` auf `RELEASE_BLOCKED`.

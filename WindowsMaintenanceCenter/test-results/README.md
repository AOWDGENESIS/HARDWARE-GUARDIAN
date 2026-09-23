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

### Zusatzordner `ci/` (dokumentierte Ausnahme)

`ci/` ist **keiner** der zehn Bereiche, sondern hält die Rohprotokolle der Windows-Läufe
(`run-<Laufnummer>-1/`: Restore, Build, Tests, Publish, Installer, Umgebungsrecord). Er ist der
Ort, an dem die Gates 1–4 ihren Nachweis finden, und deshalb bewusst vorhanden. Wer eine Regel
ergänzt, die nur die zehn Bereiche zulässt, muss `ci/` als Ausnahme nennen - `tools/check-evidence-layout.py`
tut genau das und prüft, dass diese README ihn erwähnt.

### Stand (ausgedünnt am 2026-09-23)

* **Belegt:** `installer/` (vollständiger Installationszyklus aus Lauf `35704157557` sowie die
  Iterationen davor), `integration/` (M00-E-001), `unit/` (315, 348, 359 und die 370/370-Läufe) und
  `ci/` (sechs Läufe).
* **Ausgedünnt am 2026-09-23:** 35 Laufordner in `ci/` (11,8 MB) und 10 weitere Testordner in
  `unit/` (2,6 MB) waren Zwischenstände auf der Suche nach dem Riss in der Reparaturmessung bzw.
  Wiederholungen desselben Ergebnisses. Sie wurden gelöscht, weil der Diff reviewbar bleiben muss
  (`tools/check-repo-size.py`); **die in den Gates zitierten Nachweise bleiben vollständig liegen**
  (CI: 35501975574, 35505778032, 35507347841, 35694300454, 35701860625, 35704157557; Testläufe:
  315/315, 348/348, 359/359 und die beiden 370/370-Läufe). Ein gelöschter Lauf ist über seine
  Laufnummer in GitHub jederzeit wieder abrufbar - was hier nicht mehr liegt, ist nicht verschwunden,
  sondern reproduzierbar. Gekürzt wurden ausschließlich Zwischenstände; kein Bereich ist dadurch
  nachträglich als „nicht gemessen" erschienen, was nicht vorher schon so war.
* **Leer und das mit Absicht** (mit `.gitkeep`, also ohne Inhalt): `safety/`, `security/`,
  `recovery/`, `localization/`, `offline/`, `regression/`, `release/` - für diese Bereiche wurde
  nichts gemessen, und genau das sagen sie aus.
* `tools/check-evidence-layout.py` prüft das: die zehn Bereiche müssen existieren, kein Ordner darf
  einen eigenen Namen erfinden, kein Laufordner darf leer sein, und diese README muss jeden Bereich
  nennen.

Ohne einen Report in diesen Ordnern bleibt `docs/RELEASE_STATUS.md` auf `RELEASE_BLOCKED`.

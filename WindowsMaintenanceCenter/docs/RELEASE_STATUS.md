# Release-Entscheid (Kapitel 92/93/94 der WMC-Spezifikation)

**Status: `RELEASE_BLOCKED`**
Stand: 2026-09-22, Branch `arena/01a0bb04-entwicklungen`, letzter vollständiger Lauf
`35704157557` auf `fd11038` (grün: Bau, 370 von 370 Testfällen, Paketierung, Selbsttest der
Nachweisbibliothek, Installationszyklus mit Bericht).

Kapitel 92 lässt genau zwei Zustände zu. Es gibt keinen dritten. Dieser Entscheid wird ausschließlich
aus tatsächlich ausgeführten Tests gebildet.

Er hat sich geändert: **Bau, Unit-Suite, Paketierung und jetzt auch Installation und Deinstallation
sind echte, abgelesene Nachweise.** Lauf `35704157557` hat alle Schritte bestanden — Bau (0 Fehler),
370 Testfälle (0 fehlgeschlagen), Portable-EXE, Inno-Setup-Installer, Prüfsummendatei samt
Gegenprüfung, Selbsttest der Nachweisbibliothek und der Installationszyklus auf der Windows-Maschine
mit vollständigem Bericht (`test-results/installer/20260922T082333Z-INS-CI/`, Ergebnis `PASSED`).
Derselbe Lauf hat den Startabsturz widerlegt: die installierte Datei startet und endet mit Exit-Code 0.

Ein Befund des 2026-09-22 gehört zum Entscheid dazu und ist jetzt **belegt behoben**: **das
ausgelieferte Portable-Artefakt war nicht portabel** — der Portabel-Modus hing an einer Datei *neben*
der ausgelieferten Einzeldatei, die es dort nie gab. Der Zyklus von Lauf `35704157557` startet das
portable Artefakt in einem leeren Ordner und belegt, dass es seine Daten **daneben** ablegt
(`portableDataFolder` und `portableDataFiles` im Bericht), und dass die *installierte* Kopie es
**nicht** tut.

Der Entscheid bleibt `RELEASE_BLOCKED`, weil Nachweise fehlen, die **nur die Zielmaschine** liefern
kann: kein einziger der vierzehn Sicherheitsangriffe ist ausgeführt, kein Offline-Betrieb, kein
Neustart-/Recovery-Fall, keine Sensorik, kein UAC-Abbruch, keine Reparatur- und Upgrade-Installation,
und zwei Sprachen fehlen ganz. Installation und Deinstallation selbst sind seit Lauf `35704157557`
belegt und stehen nicht mehr auf dieser Liste.

Testreport nach Kapitel 70 (Werte aus den abgelesenen Läufen):

```text
VERSION: 1.0.0 (eng/Version.props)
COMMIT:  fd11038 (Lauf 35704157557)
DATE:    2026-09-22
ENVIRONMENT: GitHub-Runner "windows-latest" (Windows Server 2025 Datacenter 10.0.26100, .NET SDK
             10.0.401), virtuelle Maschine ohne Sensorik, ohne Akku, ohne SMART echter Laufwerke,
             ohne UAC-Dialog

BUILD:    0 Fehler (MSBuild; die Hinweise der Analysatoren stehen im build-summary.txt)
TESTS:    370 ausgeführt, 370 bestanden, 0 fehlgeschlagen, 0 übersprungen (TRX im Nachweisordner)
PASSED:   370
FAILED:   0
BLOCKED:  die Fälle, die Zielhardware oder eine interaktive Sitzung brauchen (siehe Matrizen)

SECURITY:  BLOCKED (kein Angriff ausgeführt; 14 Pflichtangriffe offen)
SAFETY:    BLOCKED (Zielmaschine)
RECOVERY:  BLOCKED (Zielmaschine)
INSTALLER: TEILWEISE - Installation, Start, Nutzungssonde, Deinstallation und der portable
           Datenspeicherort sind auf der CI-Maschine belegt (Ergebnis PASSED, 20 Kriterien);
           Reparatur, Upgrade, Neustart, weitere Laufwerke und "USE" bleiben offen
OFFLINE:   BLOCKED
LOCALIZATION: BLOCKED (ja-JP und ru-RU fehlen)
REGRESSION: TEILWEISE - jeder CI-Lauf ist Regressionsevidenz, aber ohne UI und ohne Zielmaschine

RELEASE:
BLOCKED
```

## Erfüllte Gates (mit Nachweis, nicht mit Einschätzung)

| Gate | Nachweis | Zustand |
| --- | --- | --- |
| Gate 1 Build | `test-results/ci/run-35505778032-1/build.log` und `build-summary.txt`: 15 Projekte inklusive WPF, 0 Fehler; `dotnet build` im Release-Schritt erneut | **bestanden (CI-Umgebung)** |
| Gate 2 Unit | `test-results/ci/run-35701860625-1/windowsmaintenancecenter.trx` (und `test.log`): **370 ausgeführt, 370 bestanden, 0 fehlgeschlagen, 0 übersprungen** auf der CI-Maschine, Commit `d759180`; `scripts/test.ps1` verlangt TRX und mindestens 370 Fälle | **bestanden (CI-Umgebung)** |
| Gate 3 Integration (Teil) | `scripts/test.ps1` startet die Anwendung unmittelbar; `-result-trx` und Konsolenprotokoll liegen bei | teilweise: die Oberfläche selbst wurde nicht bedient |
| Gate 7 Installer (Installation und Deinstallation) | `test-results/installer/20260922T082333Z-INS-CI/` aus Lauf `35704157557`, Commit `fd11038` - **vollständiger Nachweisordner**: `report.json` und `report.txt` mit Ergebnis `PASSED`, dazu `steps.txt` (20 Kriterien PASS, 0 FAIL), `logs/install.log`, `logs/uninstall.log`, `logs/technical-*.jsonl`, `startup-log-tail.txt` und `environment.json`, alle Nachweisdateien mit SHA-256. Gemessen: stille Installation Exit-Code 0; installierte Datei SHA-256-identisch mit dem Bau (`5190c229…fcca`); Startmenüeintrag; installierte Kopie **nicht** portabel; Programm startet und läuft (Datenordner mit 2 Dateien); **Exit-Code 0 beim Schließen**; Deinstallation entfernt Eintrag, Programmordner und Verknüpfung und behält die Daten bewusst; portables Artefakt startet und legt seine Daten neben sich ab. Der Bericht nennt die offenen Punkte (Reparatur, Upgrade, Neustart, weitere Laufwerke, „USE") selbst | **bestanden (CI-Umgebung)**; die im Bericht genannten Punkte bleiben der Zielmaschine vorbehalten |

## Blockierende Tests (Format nach Kapitel 94)

| BLOCKING TEST | MODULE | REASON | EVIDENCE | REQUIRED FIX |
| --- | --- | --- | --- | --- |
| Gate 4 Security | M32, M33, M44 | Keiner der 14 Pflichtangriffe (Command Injection, Path Traversal, Argument Injection, Privilege Escalation, Tampered Config, Tampered Update, Invalid Signature, Corrupt Backup, No Admin, UAC Cancel, Process Abort, Log Manipulation, Database Corruption, Report Injection) ist auf einer Maschine ausgeführt; die Unit-Suite deckt mehrere davon auf Codeebene ab (SEC-01 bis SEC-03, SEC-12, SEC-14), aber ein Testfall ist kein ausgeführter Angriff. Gemessen ist inzwischen ein Teilstück von M32: die Exit-Codes der Reparatur- und Prüfwerkzeuge mit den **nur lesenden** Schaltern (`DISM /ScanHealth`, `fsutil`, `ipconfig`, `systeminfo`; SFC und CHKDSK bewusst ausgelassen und als offene Punkte benannt) | `test-results/security/<Lauf>-M32-SEC-EXIT-001/` (Bericht, Messwerte mit Quelle, Ausgabe jeder Werkzeugausführung als Nachweis) | Die vierzehn Angriffe auf isolierter Windows-VM ausführen (`docs/TESTMATRIX-VM.md`, Abschnitt 7) und je Angriff belegen |
| Gate 5 Safety | M04, M24, M25, M27, M31, M34 | Approval-, Backup-, Rollback- und Admin-Grenze sind implementiert und unit-getestet, aber nicht auf einer Maschine durchlaufen. Für **M27** ist der Ablauf jetzt zusätzlich auf Codeebene gepinnt: 19 Testfälle in `OneClickMaintenanceTests` fordern die acht Phasen in der vorgeschriebenen Reihenfolge, dass ohne Freigabe nichts ausgeführt wird, dass eine abgewählte Kategorie nicht im Plan landet, dass Sicherung vor Ausführung läuft, dass eine fehlende Messung UNKNOWN bleibt (nie 0), dass ein fehlgeschlagenes Element nie als Erfolg endet und dass ein Abbruch als `CANCELLED` mit genannter Operation endet - **ein Testfall ist kein ausgeführter Lauf**, deshalb bleibt das Gate offen | `test-results/safety/` ist leer | Matrizen Kapitel 76/77 auf Standardnutzer- und Adminkonto (`scripts/vm/Test-UacMatrix.ps1`), dazu ein Ein-Klick-Lauf auf der Ziel-VM |
| Gate 6 Offline | M29 | Offline-Betrieb aller lokalen Module nie gemessen | `test-results/offline/` ist leer | VM ohne Netz, Kapitel 61 |
| Gate 7 Installer (Kapitel 74) | M40, M41 | Der Zyklus belegt Installation, Start, Nutzung, Deinstallation und den portablen Datenspeicherort. Nicht belegt und im Bericht des Zyklus als offene Punkte geführt: **Reparaturinstallation, Upgrade über eine Vorgängerversion, der Neustart nach der Installation, Installation auf D: / E: / F: und „USE" im Sinne von Kapitel 62** (ein Mensch, der die Oberfläche bedient). Diese Punkte brauchen eine Maschine mit diesen Laufwerken, einen Vorgänger-Installer und einen Menschen - die CI-Maschine kann sie nicht liefern | im Bericht genannt: `test-results/installer/20260922T082333Z-INS-CI/report.txt`, Abschnitt `open` | Kapitel 62/74 auf sauberer VM und auf Zielhardware |
| Gate 8 Localization | M37 | **Vier Sprachkataloge liegen bei: de, en, ja, ru - je 939 Schlüssel, 0 fehlend, 0 tot, Platzhalter je Schlüssel über alle Sprachen gleich** (`tools/check-localization.py`, vier neue Testfälle in `LocalizationTests` fordern Symmetrie, Platzhaltergleichheit und dass nur Sprachen mit Katalog angeboten werden). Was **fehlt**: (a) die Kataloge sind maschinell erstellt und **nicht von Muttersprachlern geprüft** - das ist ein eigener offener Punkt und darf nicht als erledigt gelesen werden; (b) der Nachweis nach Kapitel 63 auf einer Maschine (Start in ja-JP und ru-RU, Screenshots je Sprache, LOC-01/LOC-02, Kapitel 63) ist nicht geführt: die Oberfläche lässt sich hier nicht starten | `src/WindowsMaintenanceCenter.Core/Resources/{de,en,ja,ru}.json`, `tests/WindowsMaintenanceCenter.Tests/LocalizationTests.cs`, `test-results/localization/` (leer) | Nachweise auf der Zielmaschine: vier Sprachen starten, Screenshots und Einstellungsdatei je Sprache ablegen (LOC-01/LOC-02); zusätzlich eine muttersprachliche Durchsicht der ja- und ru-Dateien |
| Gate 9 Recovery | M35, M34 | Die Recovery-Engine ist umgesetzt und unit-getestet (11 Testfälle, Zustandsbuchhaltung im Journal), aber Kapitel 64 verlangt den Lauf auf der Maschine: keine Unterbrechung, kein Rollback über einen Neustart, kein Nachweis mit echten Dateien | `test-results/recovery/` leer | Recovery-Fälle A/B/C auf der Ziel-VM fahren (`scripts/vm/`, `docs/TESTMATRIX-VM.md`) und belegen |
| Gate 10 Final VM | M46 | Keine Windows-11-x64-VM mit UEFI, Secure Boot und TPM verfügbar | `test-results/release/` leer | Kapitel 52/65 auf sauberer VM |
| P0-MUSS | M34, M35 | Zustandsmodell ist an Kapitel 40 angeglichen, aber der Zustand wird noch nicht persistiert (M34-F-001) und unterbrochene Jobs werden nicht erkannt (M34-R-001/M35) | `Core/Services/SystemStateMachine.cs` (nur Arbeitsspeicher) | Persistenz plus Recovery-Engine nach Kapitel 40/41 |
| P0-Umfang | M26, M05, M11, M14, M20, M42 | Module fehlen ganz oder überwiegend. **M27 (One-Click Maintenance) ist aus dieser Liste heraus**: Dienst, Seite und 19 Testfälle liegen vor - was fehlt, ist der Lauf auf einer Maschine (siehe Gate 5) | `docs/ABNAHME-WMC.md` Abschnitt 1 | Module umsetzen |
| P1-Umfang | M37, M40, M43, M47, M48 | Sprachdateien, Installer-Details, Berichtsvarianten und Barrierefreiheit sind unvollständig | `docs/ABNAHME-WMC.md` Abschnitt 1 | nach den P0-Punkten |
| Release-Blocker Kapitel 55 | - | „falscher SUCCESS-Status" wäre ausgelöst, sobald irgendwo `PASSED` ohne Nachweis behauptet würde | dieser Entscheid | keine falschen Erfolgsmeldungen; Status bleibt `BLOCKED` |

## Was diesen Entscheid ändern würde

Nur Nachweise, keine Einschätzung: Sicherheitsprotokolle, Installer- und VM-Protokolle,
Offline-, Recovery- und Lokalisierungsnachweise in `test-results/`, jeweils mit Zeitpunkt, Version,
Build, Umgebung und Ergebnis (Kapitel 71). Erst wenn Kapitel 93 vollständig erfüllt ist, darf hier
`RELEASE_READY` stehen.

Der Weg dahin steht als Arbeitsliste in `docs/TESTMATRIX-VM.md`; das Messwerkzeug für die
Zielmaschine in `docs/VM-TESTKIT.md`.

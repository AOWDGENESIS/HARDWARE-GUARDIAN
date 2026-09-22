# Release-Entscheid (Kapitel 92/93/94 der WMC-Spezifikation)

**Status: `RELEASE_BLOCKED`**
Stand: 2026-09-20, Branch `arena/01a0bb04-entwicklungen`, letzter vollständiger Lauf
`35505778032` auf `ef5163d`.

Kapitel 92 lässt genau zwei Zustände zu. Es gibt keinen dritten. Dieser Entscheid wird ausschließlich
aus tatsächlich ausgeführten Tests gebildet.

Er hat sich geändert: **Bau und Unit-Suite sind jetzt echte, abgelesene Nachweise.** Der
Windows-Lauf `35694300454` hat alle Schritte bestanden — Bau (0 Fehler), 359 Testfälle (0
fehlgeschlagen), Portable-EXE, Inno-Setup-Installer, Prüfsummendatei und die Gegenprüfung der
Prüfsummen. Die Nachweise liegen im Zweig (`test-results/unit/20260922T062120Z-359-of-359/`,
`test-results/ci/run-35505778032-1/`).

Ein Befund des 2026-09-22 gehört zum Entscheid dazu: **das ausgelieferte Portable-Artefakt war nicht
portabel** — der Portabel-Modus hing an einer Datei *neben* der ausgelieferten Einzeldatei, die es dort
nie gab. Der Fehler ist behoben (Marker in der Assembly, `-Portable` beim Publish, Kompilierzeitprüfung
im Installer, Nachweis im Installationszyklus), aber die Auslieferungsform ist damit **von neuem zu
prüfen**: bis der Zyklus gelaufen ist, trägt keine Zeile dieses Dokuments einen Nachweis dafür.

Der Entscheid bleibt trotzdem `RELEASE_BLOCKED`, weil die Nachweise fehlen, die **nur die
Zielmaschine** liefern kann: kein einziger der vierzehn Sicherheitsangriffe ist ausgeführt, keine
Installation, kein Offline-Betrieb, kein Neustart-/Recovery-Fall, keine Sensorik, kein UAC-Abbruch,
und zwei Sprachen fehlen ganz.

Testreport nach Kapitel 70 (Werte aus den abgelesenen Läufen):

```text
VERSION: 1.0.0 (eng/Version.props)
COMMIT:  ef5163d (Lauf 35505778032; der Lauf selbst lief auf 8a5c0b9-Vorgaenger, siehe run.txt)
DATE:    2026-09-20
ENVIRONMENT: GitHub-Runner "windows-latest" (Windows Server 2025, .NET SDK 10.0.401), virtuelle
             Maschine ohne Sensorik, ohne Akku, ohne SMART echter Laufwerke, ohne UAC-Dialog

BUILD:    0 Fehler / 50 Hinweise (CA1826 28, CA1861 22)
TESTS:    359 ausgeführt, 359 bestanden, 0 fehlgeschlagen, 0 übersprungen (TRX im Nachweisordner)
PASSED:   359
FAILED:   0
BLOCKED:  die Fälle, die Zielhardware oder eine interaktive Sitzung brauchen (siehe Matrizen)

SECURITY:  BLOCKED (kein Angriff ausgeführt; 14 Pflichtangriffe offen)
SAFETY:    BLOCKED (Zielmaschine)
RECOVERY:  BLOCKED (Zielmaschine)
INSTALLER: TEILWEISE - Artefakte gebaut, Installer-Log vorhanden, aber keine Installation,
           kein Upgrade, keine Reparatur, keine Deinstallation auf einer Maschine ausgeführt
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
| Gate 7 Installer (Installation und Deinstallation) | `test-results/ci/run-35701860625-1/installer-cycle.log` und `test-results/installer/20260922T075623Z-INS-CI/` (`steps.txt`, `logs/install.log`, `logs/uninstall.log`, `startup-log-tail.txt`, `environment.json`) aus Lauf `35701860625`, Commit `d759180`: stille Installation Exit-Code 0, Deinstallations-Eintrag und Version geprüft, installierte Datei SHA-256-identisch mit dem Bau (`60a5c3bd…7806`), Startmenüeintrag vorhanden, installierte Kopie **nicht** portabel, Programm startet und läuft (Datenordner und Logzeile entstehen), **Exit-Code 0 beim Schließen**, Deinstallation entfernt Eintrag, Programmordner und Verknüpfung und behält die Daten nach Rückfrage, portables Artefakt startet und legt seine Daten **neben sich** ab. Der Setup-Bau selbst: `run-35505778032-1/release.log` | **bestanden (CI-Umgebung), Nachweisordner noch unvollständig** (s. blockierender Punkt unten) |

## Blockierende Tests (Format nach Kapitel 94)

| BLOCKING TEST | MODULE | REASON | EVIDENCE | REQUIRED FIX |
| --- | --- | --- | --- | --- |
| Gate 4 Security | M32, M33, M44 | Keiner der 14 Pflichtangriffe (Command Injection, Path Traversal, Argument Injection, Privilege Escalation, Tampered Config, Tampered Update, Invalid Signature, Corrupt Backup, No Admin, UAC Cancel, Process Abort, Log Manipulation, Database Corruption, Report Injection) ist ausgeführt | `test-results/security/` ist leer | Angriffe auf isolierter Windows-VM ausführen (`docs/TESTMATRIX-VM.md`, Abschnitt 7) und belegen |
| Gate 5 Safety | M04, M24, M25, M27, M31, M34 | Approval-, Backup-, Rollback- und Admin-Grenze sind implementiert und unit-getestet, aber nicht auf einer Maschine durchlaufen | `test-results/safety/` ist leer | Matrizen Kapitel 76/77 auf Standardnutzer- und Adminkonto (`scripts/vm/Test-UacMatrix.ps1`) |
| Gate 6 Offline | M29 | Offline-Betrieb aller lokalen Module nie gemessen | `test-results/offline/` ist leer | VM ohne Netz, Kapitel 61 |
| Gate 7 Installer (Rest) | M40, M41 | (a) Der Zyklus meldet an jedem seiner Punkte PASS, sein Nachweisordner enthält aber **kein** `report.json` und kein `report.txt`: der Report-Schreiber des Nachweiskits scheitert mit `Argument types do not match`, und ohne Bericht ist der Lauf als Nachweis unvollständig. (b) Es fehlen weiterhin Reparaturinstallation, Upgrade über eine Vorgängerversion, Neustart, Installation auf weiteren Laufwerken und „USE" im Sinne von Kapitel 62 (ein Mensch, der die Oberfläche bedient) | `test-results/installer/20260922T075623Z-INS-CI/` (ohne `report.json`), `test-results/ci/run-35701860625-1/installer-cycle.log` | Fix des Report-Schreibers, bewiesen durch `scripts/vm/Test-EvidenceLibrary.ps1` in einem Lauf; danach Kapitel 62/74 auf sauberer VM |
| Gate 8 Localization | M37 | Japanisch und Russisch fehlen vollständig | `src/WindowsMaintenanceCenter.Core/Resources/` | ja-JP und ru-RU übersetzen, dann Kapitel 63 prüfen |
| Gate 9 Recovery | M35, M34 | Die Recovery-Engine ist umgesetzt und unit-getestet (11 Testfälle, Zustandsbuchhaltung im Journal), aber Kapitel 64 verlangt den Lauf auf der Maschine: keine Unterbrechung, kein Rollback über einen Neustart, kein Nachweis mit echten Dateien | `test-results/recovery/` leer | Recovery-Fälle A/B/C auf der Ziel-VM fahren (`scripts/vm/`, `docs/TESTMATRIX-VM.md`) und belegen |
| Gate 10 Final VM | M46 | Keine Windows-11-x64-VM mit UEFI, Secure Boot und TPM verfügbar | `test-results/release/` leer | Kapitel 52/65 auf sauberer VM |
| P0-MUSS | M34, M35 | Zustandsmodell ist an Kapitel 40 angeglichen, aber der Zustand wird noch nicht persistiert (M34-F-001) und unterbrochene Jobs werden nicht erkannt (M34-R-001/M35) | `Core/Services/SystemStateMachine.cs` (nur Arbeitsspeicher) | Persistenz plus Recovery-Engine nach Kapitel 40/41 |
| P0-Umfang | M35, M26, M27, M05, M11, M14, M20, M42 | Module fehlen ganz oder überwiegend | `docs/ABNAHME-WMC.md` Abschnitt 1 | Module umsetzen |
| P1-Umfang | M37, M40, M43, M47, M48 | Sprachdateien, Installer-Details, Berichtsvarianten und Barrierefreiheit sind unvollständig | `docs/ABNAHME-WMC.md` Abschnitt 1 | nach den P0-Punkten |
| Release-Blocker Kapitel 55 | - | „falscher SUCCESS-Status" wäre ausgelöst, sobald irgendwo `PASSED` ohne Nachweis behauptet würde | dieser Entscheid | keine falschen Erfolgsmeldungen; Status bleibt `BLOCKED` |

## Was diesen Entscheid ändern würde

Nur Nachweise, keine Einschätzung: Sicherheitsprotokolle, Installer- und VM-Protokolle,
Offline-, Recovery- und Lokalisierungsnachweise in `test-results/`, jeweils mit Zeitpunkt, Version,
Build, Umgebung und Ergebnis (Kapitel 71). Erst wenn Kapitel 93 vollständig erfüllt ist, darf hier
`RELEASE_READY` stehen.

Der Weg dahin steht als Arbeitsliste in `docs/TESTMATRIX-VM.md`; das Messwerkzeug für die
Zielmaschine in `docs/VM-TESTKIT.md`.

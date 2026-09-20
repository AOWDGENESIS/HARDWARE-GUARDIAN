# Abnahmestatus nach den Regeln 83–133 (Arbeitsstand, ehrlich)

Dieses Dokument beantwortet genau eine Frage: **Was fehlt noch, bis ein Release zulässig wäre?**

Es ist kein Fortschrittsbericht. Es ist eine Mängelliste gegen die Abnahmekriterien der Regeln
83–133, erstellt am 2026-09-20 aus dem tatsächlichen Inhalt des Repositorys (nicht aus der
Erinnerung an die Entwicklung). Jede Zeile lässt sich im Code nachprüfen; die Datei- und
Funktionsnamen stehen dabei.

Nachgeführt am 2026-09-20 (zweite Fassung): Testkatalog mit den MUSS-IDs der Regeln 86–124 (§1a),
Release-Abnahme der Regeln 125–133, und die vom Eigentümer beantworteten Entscheidungen (§4). Die
Umsetzung der Regeln 87 (Firewall, TPM) und 90 (Update-Metadaten und Update-Liste) ist in dieser
Fassung berücksichtigt - sie sind damit **implementiert, aber weiterhin nicht ausgeführt und damit
nicht abgenommen**.

---

## 0. Der Satz, der alles andere bestimmt

> **83.1** Ein Test darf niemals als bestanden markiert werden, wenn er übersprungen, nur simuliert,
> nicht manuell überprüft, wegen fehlender Testumgebung nicht durchgeführt, nur teilweise oder nur
> im Erfolgsfall funktionsfähig ist. Nicht ausgeführte Tests erhalten **BLOCKIERT**.

In dieser Entwicklungsumgebung gibt es **kein .NET SDK, kein Windows und keine Referenzhardware**.
Damit gilt:

| Ebene | Zustand |
| --- | --- |
| Kompilierung | nie ausgeführt → **BLOCKED** |
| 125 Testfälle in 16 Klassen | geschrieben, nie ausgeführt → **BLOCKED** |
| `scripts/build.ps1`, `scripts/test.ps1`, `scripts/release.ps1` | nie ausgeführt → **BLOCKED** |
| `installer/WindowsMaintenanceCenter.iss` | nie kompiliert → **BLOCKED** |
| CI-Workflow | nie gelaufen → **BLOCKED** |
| Referenzhardware (Ryzen 5 5600G / B450M S2H / F67 / Win11) | nicht vorhanden → **BLOCKED** |

**Folge nach Regel 84:** Es gibt **kein einziges Modul mit Status `PASSED`**. Jedes Modul steht,
sobald seine Implementierung fertig ist, auf `READY_FOR_TEST`; die Abnahme ist für **alle** Module
`BLOCKED`. Nach Regel 128 und 129 ist ein Release damit gesperrt – nicht durch einen Fehler,
sondern durch fehlenden Nachweis.

Die folgende Tabelle unterscheidet deshalb zwei Dinge:

* **Stand** = Zustand der Implementierung (mein Urteil aus dem Code),
* **Abnahme** = Zustand nach Regel 84 (immer `BLOCKED`, solange nichts ausgeführt wurde).

---

## 1. Module aus den Regeln 86–124

| Regel | Modul | Stand | Konkret fehlt für MUSS |
| --- | --- | --- | --- |
| 86 | Dashboard | `READY_FOR_TEST` | nichts Strukturelles im Code; DASH-F-004/006/007 nur durch Lauf auf echter Maschine belegbar |
| 87 | Systemanalyse | `IN_DEVELOPMENT` | **DIAG-F-009 Firewallstatus fehlt komplett** (kein Fund im Code), **DIAG-F-011 TPM-Status fehlt komplett**, DIAG-F-007 Windows-Update-Status nur als Suche ohne Installationsweg |
| 88 | Cleanup | `READY_FOR_TEST` | CLEAN-F-004 Abwahl je Kategorie in der Oberfläche prüfen; **CLEAN-R-001**: Löschen ist nicht wiederherstellbar (Definitionsfrage, siehe §4) |
| 89 | Storage Analyzer | **`NOT_STARTED`** | gesamtes Modul: Ordnergrößen, größte Dateien, Sortierung nach Größe/Alter, Kennzeichnung System-/Benutzerdateien. Vorhanden ist nur die Größenmessung der Cleanup-Ordner (`Maintenance/DirectoryMeasurement.cs`) |
| 90 | Windows Update | `IN_DEVELOPMENT` | UPDATE-F-003 unvollständig (nur Titel, **keine KB-Nummer, keine Kategorie, kein Neustart-Flag**), **UPDATE-F-004…F-008 fehlen** (keine Einzelauswahl, keine Installation, kein Installationsstatus, keine Fehler-/Neustartanzeige), UPDATE-R-001 Nachprüfung nach Installation fehlt |
| 91 | Programm-Updates / winget | **`NOT_STARTED`** | kein Fund von `winget` oder `Get-Package` im gesamten Code; kein Exit-Code-Auswerter, keine Versionsnachprüfung |
| 92 | Softwaremanager | `IN_DEVELOPMENT` | SOFTWARE-F-001/002 vorhanden (`Maintenance/InventoryServices.cs`, Registry-Inventar), **F-003 Suche, F-004 Sortierung, F-005 Deinstallation, F-006 Nachprüfung und die gesamte Oberfläche fehlen** |
| 93 | Autostartmanager | `IN_DEVELOPMENT` | **STARTUP-F-001 Registry-Autostarts und STARTUP-F-002 Startup-Ordner fehlen**; vorhanden ist nur eine Liste von Diensten (`Win32_Service` in `Windows/WindowsHealthService.cs`). F-005 Signaturprüfung, F-006/F-007 Deaktivieren/Reaktivieren fehlen |
| 94 | Dienste-Manager | `IN_DEVELOPMENT` | F-001…F-003 vorhanden (Name, Status, Starttyp lesend), **F-004 Abhängigkeiten, F-005 Start, F-006 Stopp, F-007 Starttyp ändern und SERVICE-S-002 Kennzeichnung kritischer Dienste fehlen** |
| 95 | Aufgabenplaner | **`NOT_STARTED`** | kein Fund von `Get-ScheduledTask`, `schtasks` oder einer Aufgabenquelle; TASK-F-001…F-006 alle offen |
| 96 | Reparaturcenter | `IN_DEVELOPMENT` | DISM CheckHealth/ScanHealth/RestoreHealth und SFC `/verifyonly` vorhanden (mit Freigabe, `Windows/WindowsHealthService.cs`), **REPAIR-F-005 CHKDSK fehlt**, **REPAIR-R-001 Wiederherstellungspunkt vor kritischer Reparatur nicht verdrahtet** (der Reparaturpfad ruft den Sicherungsdienst nicht auf) |
| 97 | Laufwerksoptimierung | **`NOT_STARTED`** | kein `Optimize-Volume`, kein `defrag`. Es existiert eine nicht aufgerufene Vorlage `storage.trim.status` in `Infrastructure/Platform/PowerShellRunner.cs` |
| 98 | Performance Center | `IN_DEVELOPMENT` | CPU-Auslastung vorhanden (`Sensors/SensorProviders.cs`, `Win32_PerfFormattedData_PerfOS_Processor`), RAM-Auslastung vorhanden, **PERF-F-003 Datenträgeraktivität und PERF-F-004 Top-Prozesse fehlen**, PERF-F-005 Bootanalyse fehlt (nur Laufzeit). Keine eigene Seite |
| 99 | Prozessmanager | `IN_DEVELOPMENT` | Prozessliste mit PID, Pfad, Arbeitsspeicher vorhanden (`Maintenance/InventoryServices.cs`), **PERF-F-003 CPU je Prozess, PROCESS-F-005 Publisher, F-006 Signaturprüfung, F-007 Detailansicht, F-002 PID-Anzeige in einer Oberfläche und PROCESS-S-002/F-001 Beenden fehlen**. Es gibt keine Seite |
| 100 | Netzwerkdiagnose | `IN_DEVELOPMENT` | Adapter, IP, Gateway und DNS werden gelesen (`Hardware/Wmi/WindowsHardwareProvider.cs`), **NETWORK-F-005 Internetprüfung, F-006 DNS-Auflösung, F-007 aktive TCP-Verbindungen fehlen** |
| 101 | Security Center | `IN_DEVELOPMENT` | Defender-Status und Echtzeitschutz lesend vorhanden (`Infrastructure/Platform/PowerShellRunner.cs`, Vorlage `defender.status`), **SECURITY-F-003 Firewallstatus, F-005 Quick Scan, F-006 Full Scan, F-007 Scanergebnis fehlen** |
| 102 | Event Log Analyzer | `IN_DEVELOPMENT` | Es wird **nur** das Systemprotokoll der letzten 7 Tage mit Level 1–2 gezählt (`Windows/WindowsHealthService.cs`, `ReadSystemEventLogAsync`). **EVENT-F-002 Application-Log, F-003 Update-Events, F-004 WHEA, F-005 Disk-Events, F-006…F-009 Filter und Gruppierung fehlen** |
| 103 | Crash Analyzer | **`NOT_STARTED`** | kein `BugCheck`, kein Dump-Leser, keine WHEA-Auswertung. Vorhanden ist lediglich eine Cleanup-Kategorie für Dump-Dateien |
| 104 | Hardware Health | `READY_FOR_TEST` | Sensoren mit Herkunft und Qualitätsstufe, ACPI-Thermalzonen nicht als Kerntemperatur verkauft, SMART nur wenn vorhanden; Beleg fehlt (Lauf) |
| 105 | Restore Points | `IN_DEVELOPMENT` | Erstellung und Nachweis vorhanden (`Infrastructure/Security/BackupService.cs`, `checkpoint-Computer`), **RESTORE-F-003 Bezeichnung ist fest (`WindowsMaintenanceCenter`) statt vorgangsbezogen**, **RESTORE-E-001 fehlt**: bei Fehlschlag wird protokolliert und weitergearbeitet, statt risikogebunden zu stoppen |
| 106 | Backup Engine | `IN_DEVELOPMENT` | Erzeugung, ID, Manifest, Verknüpfung im Audit vorhanden (seit Commit `e967f42` im Ablauf erzwungen), **BACKUP-F-003 Validierung und BACKUP-E-001 Erkennung beschädigter Sicherungen sind nicht nachgewiesen** (Manifest ohne Prüfsummen) |
| 107 | Rollback Engine | `IN_DEVELOPMENT` | `Infrastructure/Security/RollbackService.cs` existiert, ist registriert und **aus der Oberfläche nicht erreichbar**; ROLLBACK-F-003 Statusanzeige, F-004 Validierung im Ablauf, E-001/S-001 Meldungen sind ungeprüft |
| 108 | Change Journal | `IN_DEVELOPMENT` | Audit-Protokoll (JSON + TXT) vorhanden; **JOURNAL-F-007 Rollbackstatus fehlt als Feld**, F-006 Sicherungs-ID wird nur mittelbar über `newState` geführt, keine eigene Journal-Sicht |
| 109 | Wartungsplan | **`NOT_STARTED`** | kein Scheduler, kein Job, keine Zeitsteuerung; PLAN-F-001…F-006 alle offen |
| 110 | One-Click-Maintenance | **`NOT_STARTED`** | kein Fund von `OneClick` im Code |
| 111 | Reporting | `READY_FOR_TEST` mit **Konflikt** | HTML, TXT, JSON vorhanden (Hardwareinventar seit `67f0341` enthalten), **REPORT-F-002 PDF ist bewusst gesperrt** → dieses MUSS gilt als fehlgeschlagen, solange die Sperre bestehen bleibt (Entscheidung in §4) |
| 112 | Offline-Modus | `READY_FOR_TEST` | Offline-Erkennung und Sperren für Online-Funktionen vorhanden; OFFLINE-F-001…F-006 nur auf echter Maschine belegbar |
| 113 | KI-Modul | **`NOT_STARTED`** | kein KI-Client, keine Empfehlungs-Engine. Alle AI-F sind MUSS → Konflikt in §4 |
| 114 | Admin Worker | `IN_DEVELOPMENT` | Start ohne Adminrechte vorhanden; **ADMIN-F-003 ist so nicht erfüllt**: `Infrastructure/Platform/ElevationService.cs` startet die *gesamte* Anwendung neu mit erhöhten Rechten, statt nur die freigegebene Aktion zu privilegieren. ADMIN-E-001 (UAC-Abbruch) ungeprüft |
| 115 | Command Execution | `READY_FOR_TEST` | Ausführbare Aktionen als Vorlagenkatalog, Parametermuster, Timeouts, Exit-Codes, Abbruch, Prozessläufer vorhanden; EXEC-S-001/S-002 architektonisch adressiert, Beleg fehlt |
| 116 | State Machine | `READY_FOR_TEST` | Zustandsautomat mit allen geforderten Zuständen vorhanden; STATE-F-002 Protokollierung der Wechsel vorhanden; STATE-F-004 Recovery aus unterbrochenem Zustand ist **nicht** implementiert (siehe 117) |
| 117 | Recovery | **`NOT_STARTED`** | keine Erkennung unterbrochener Jobs, keine Optionsanzeige |
| 118 | Configuration | `IN_DEVELOPMENT` | Speichern, Lesen, Erkennung ungültiger Konfiguration und `SchemaVersion = 1` vorhanden; **CONFIG-R-001 Sicherung der Konfiguration vor Migration fehlt**, Migration selbst existiert nicht |
| 119 | Lokalisierung | `READY_FOR_TEST` | 702 Schlüssel je Sprache, Prüfwerkzeug sauber; LANG-F-004/F-005 Formatierung nur stellenweise über `CultureInfo`; LANG-E-001 Fallback über Marker – alles unbelegt |
| 120 | Installer | **`BLOCKED`** | Datei geschrieben, nie kompiliert, nie auf einer sauberen VM ausgeführt |
| 121 | Deinstallation | **`BLOCKED`** | im Skript enthalten (Startmenü, Desktop, optionale Daten, Nachfrage vor Löschen der Daten), nie ausgeführt |
| 122 | Auto Update | **`NOT_STARTED`** | kein Selbst-Update: weder Versionserkennung noch Signatur-/Hashprüfung noch Rückfallebene |
| 123 | Logging | `READY_FOR_TEST` | technisches Dateilog und Audit-Protokoll vorhanden; LOG-F-003 Exit-Codes protokolliert, LOG-F-004 eindeutige Fehler-IDs über Problem-IDs; LOG-S-001/S-002 nicht nachgewiesen |
| 124 | Performance | `IN_DEVELOPMENT` | asynchron und abbrechbar gebaut, **PERFAPP-F-001 Startzeit, F-004 Speicherverbrauch und jede Messung fehlen** |

Zusätzlich fehlen **Oberflächen für fast alle Module ab Regel 89**. Navigierbar sind heute genau
fünf Bereiche: Dashboard, Hardware, Windows-Zustand, Wartung, Einstellungen
(`App/ViewModels/MainViewModel.cs`, Zeilen 89–93). Storage Analyzer, Softwaremanager, Autostart,
Dienste, Aufgaben, Reparatur, Laufwerksoptimierung, Performance, Prozesse, Netzwerk, Security,
Event-Log, Crash, Wiederherstellung, Wartungsplan und One-Click haben **keine Seite** – damit ist
für sie in der Regel 131 Punkt 2 („UI vorhanden“) unerfüllt.

---

## 1a. Testkatalog nach Regel 85 und Modulstatus nach Regel 84

Testklassen: **F** Functional, **S** Safety, **R** Recovery, **E** Error Handling, **P** Performance,
**U** UI, **I** Integration, **A** Accessibility, **L** Localization, **O** Offline.

Zulässige Modulstatus: `NOT_STARTED`, `IN_DEVELOPMENT`, `READY_FOR_TEST`, `TESTING`, `PASSED`,
`FAILED`, `BLOCKED` (Regel 84). Es gibt noch **keinen Testkatalog als Datei** - die ID-Spalte unten
ist der Inhalt, den ein Generator füllen muss (Regel 130 verlangt daraus die Tabelle
`Modul | Tests | Bestanden | Fehlgeschlagen | Blockiert | Status` je Version).

**Keine Zeile dieser Tabelle ist ausgeführt.** Nach Regel 83.1 ist der Status jedes Moduls damit
`BLOCKED`; die Spalte „Stand der Funktion“ sagt nur, wie weit die Implementierung ist.

| Regel | Modul | MUSS-Test-IDs | Stand der Funktion | Was bis `READY_FOR_TEST` fehlt |
| --- | --- | --- | --- | --- |
| 86 | Dashboard | DASH-F-001…F-007, E-001, U-001, L-001 | Seite, Live-Protokoll, Fortschritt, Problem-Zentrum vorhanden | DASH-F-007 „tatsächlicher Analysezeitpunkt“ in der Oberfläche prüfen; alle Läufe fehlen |
| 87 | Systemanalyse | DIAG-F-001…F-011, E-001, E-002, O-001, S-001, R-001 | Inventur, Windows/Build, CPU, RAM, Laufwerke, freier Speicher, Update-Status, Defender; **F-009 Firewall und F-011 TPM in dieser Sitzung gebaut** | keine Funktion mehr offen; Beleg (Lauf) fehlt für alle elf |
| 88 | Cleanup | CLEAN-F-001…F-009, S-001…S-004, E-001, E-002, R-001 | Analyse, Kandidaten, Trockenlauf, Freigabe, Sicherung, Größenmessung vorhanden | CLEAN-F-004 Abwahl je Kategorie in der Oberfläche prüfen; CLEAN-R-001 Definition „reversibel“ offen (§4) |
| 89 | Storage Analyzer | STORAGE-F-001…F-008, S-001, S-002, E-001 | nur Ordnergrößenmessung für Cleanup | gesamtes Modul: Ordnergrößen, größte/älteste Dateien, Sortierung, System-/Benutzerkennzeichnung, Zugriffsfehler, Seite |
| 90 | Windows Update | UPDATE-F-001…F-008, S-001, E-001, R-001 | Suche + **F-002 Liste und F-003 Felder (Titel, KB, Kategorie, Status/Severity, Neustart, Größe) in dieser Sitzung gebaut** | F-004 Einzelauswahl, F-005 Installation, F-006 Installationsstatus, F-007 Fehlerbehandlung, F-008 Neustartanzeige, R-001 Nachprüfung; UPDATE-S-001 Automatikplan fehlt |
| 91 | Programm-Updates (winget) | APPUPDATE-F-001…F-006, S-001, E-001, O-001 | keine Zeile | gesamtes Modul (Entscheidung §4: bauen, zuerst) |
| 92 | Softwaremanager | SOFTWARE-F-001…F-006, S-001, E-001 | Installationsinventar (Name, Hersteller, Version) lesend vorhanden | F-003 Suche, F-004 Sortierung, F-005 Deinstallation, F-006 Nachprüfung, Seite |
| 93 | Autostartmanager | STARTUP-F-001…F-007, S-001, R-001, E-001 | nur eine Dienstliste (Win32_Service) | F-001 Registry-Autostarts, F-002 Startup-Ordner, F-003 geplante Einträge, F-005 Signatur, F-006/F-007 Deaktivieren/Reaktivieren mit Rücknahme, Seite |
| 94 | Dienste-Manager | SERVICE-F-001…F-007, S-001, S-002, R-001 | Name, Status, Starttyp lesend | F-004 Abhängigkeiten, F-005 Start, F-006 Stopp, F-007 Starttyp ändern, S-002 Kennzeichnung kritischer Dienste, Seite |
| 95 | Aufgabenplaner | TASK-F-001…F-006, S-001, R-001 | keine Zeile | gesamtes Modul (Anzeige, Trigger, Programm, Hersteller, Deaktivieren/Reaktivieren) |
| 96 | Reparaturcenter | REPAIR-F-001…F-008, S-001, S-002, E-001, R-001 | DISM CheckHealth/ScanHealth/RestoreHealth, SFC /verifyonly, Exit-Codes, Ausgabe, Freigabe, Neustartankündigung | F-005 CHKDSK; R-001 Wiederherstellungspunkt vor kritischer Reparatur ist nicht verdrahtet; eigene Seite |
| 97 | Laufwerksoptimierung | DRIVE-F-001…F-005, S-001, E-001 | nur eine ungenutzte PowerShell-Vorlage (trim status) | gesamtes Modul: Laufwerkstyp, SSD/HDD-Unterscheidung, passende Optimierung, Protokoll, Validierung |
| 98 | Performance Center | PERF-F-001…F-005, S-001…S-003 | CPU- und RAM-Auslastung als Sensoren | F-003 Datenträgeraktivität, F-004 Top-Prozesse, F-005 Bootanalyse, Seite |
| 99 | Prozessmanager | PROCESS-F-001…F-007, S-001, S-002, E-001 | Prozessliste mit PID, Pfad, Arbeitsspeicher | F-003 CPU je Prozess, F-005 Publisher, F-006 Signaturprüfung, F-007 Details, PROCESS-S-001 Systemkennzeichnung, PROCESS-S-002/E-001 Beenden, Seite |
| 100 | Netzwerkdiagnose | NETWORK-F-001…F-007, E-001, O-001 | Adapter, IP-Konfiguration, Gateway, DNS lesend | F-005 Internetprüfung, F-006 DNS-Auflösung, F-007 aktive TCP-Verbindungen, Seite |
| 101 | Security Center | SECURITY-F-001…F-007, S-001, E-001 | Defender-Status, Echtzeitschutz, Signaturstand; Firewallzustand seit dieser Sitzung als Prüfung | F-003 Firewall in einer eigenen Seite, F-005 Quick Scan, F-006 Full Scan, F-007 Scanergebnis |
| 102 | Event Log Analyzer | EVENT-F-001…F-009, E-001, S-001 | Zählung im Systemprotokoll, 7 Tage, Level 1–2 | F-002 Application-Log, F-003 Update-Events, F-004 WHEA, F-005 Disk-Events, F-006…F-009 Filter und Gruppierung, Seite |
| 103 | Crash Analyzer | CRASH-F-001…F-005, S-001, E-001 | keine Zeile | gesamtes Modul: BugCheck-Ereignisse, Zeitpunkt, Code, Dumps, WHEA |
| 104 | Hardware Health | HARDWARE-F-001…F-004, S-001 | Sensoren mit Quelle und Qualitätsstufe, SMART nur wenn gemessen, ACPI nie als Kerntemperatur | keine Funktion offen; Beleg fehlt (Lauf auf echter Hardware) |
| 105 | Restore Points | RESTORE-F-001…F-003, E-001 | Erstellung und Nachweis vorhanden | F-003 vorgangsbezogene Bezeichnung (heute fest „WindowsMaintenanceCenter“), E-001 risikogebundener Stopp bei Fehlschlag |
| 106 | Backup Engine | BACKUP-F-001…F-004, R-001, E-001 | Sicherung, eindeutige ID, Manifest, Verknüpfung im Audit, Gate im Wartungsablauf | F-003 Validierung und E-001 Erkennung beschädigter Sicherungen (Manifest ohne Prüfsummen), R-001 Nachweis der Wiederherstellung |
| 107 | Rollback Engine | ROLLBACK-F-001…F-004, E-001, S-001 | Dienst vorhanden und registriert | aus der Oberfläche nicht erreichbar; F-003 Statusanzeige, F-004 Validierung nach Rollback, E-001/S-001 Meldungen ungeprüft |
| 108 | Change Journal | JOURNAL-F-001…F-007, S-001 | Audit-Protokoll (JSON + TXT) vorhanden | F-006 Sicherungs-ID nur mittelbar, F-007 Rollbackstatus als Feld fehlt, eigene Journal-Sicht |
| 109 | Wartungsplan | PLAN-F-001…F-006, S-001, E-001 | keine Zeile | gesamtes Modul (Scheduler, Jobs, Zeitplan, aktiv/inaktiv, ändern, löschen) |
| 110 | One-Click-Maintenance | ONECLICK-F-001…F-008, S-001 | keine Zeile | gesamtes Modul |
| 111 | Reporting | REPORT-F-001…F-006, S-001 | TXT, JSON, HTML mit Hardwareinventar und ausgeführten Aktionen | REPORT-F-002 PDF (Entscheidung §4: bauen) |
| 112 | Offline-Modus | OFFLINE-F-001…F-006, E-001, S-001 | Offline-Erkennung, Sperren für Online-Funktionen, lokale Diagnose | keine Funktion offen; Beleg fehlt |
| 113 | KI-Modul | AI-F-001…F-003, S-001, S-002, E-001, O-001 | keine Zeile | gesamtes Modul (Entscheidung §4: „Windows Stalker“, online recherchieren, keine privaten Daten senden) |
| 114 | Admin Worker | ADMIN-F-001…F-003, S-001, S-002, E-001 | Start ohne Adminrechte, Hinweis auf Erfordernis | **F-003 verletzt:** `ElevationService` startet die ganze Anwendung erhöht neu statt nur die freigegebene Aktion; E-001 UAC-Abbruch ungeprüft |
| 115 | Command Execution | EXEC-F-001…F-005, S-001, S-002 | Vorlagenkatalog, Parametermuster, Timeouts, Exit-Codes, Abbruch | keine Funktion offen; Beleg fehlt |
| 116 | State Machine | STATE-F-001…F-004, S-001 | Zustandsautomat mit allen geforderten Zuständen | STATE-F-004 Recovery aus unterbrochenem Zustand (siehe 117) |
| 117 | Recovery | RECOVERY-F-001…F-005, S-001 | keine Zeile | gesamtes Modul |
| 118 | Configuration | CONFIG-F-001…F-004, E-001, R-001 | Speichern, Lesen, SchemaVersion, Erkennung ungültiger Konfiguration | CONFIG-R-001 Sicherung vor Migration (Migration existiert nicht) |
| 119 | Lokalisierung | LANG-F-001…F-005, E-001 | 726/726 Schlüssel je Sprache, Prüfwerkzeug sauber, Größen- und Zahlenformate seit dieser Sitzung über die aktive Kultur | LANG-F-004/F-005 im Lauf belegen; Anzeige der neuen Update-Spalten und der Nicht-gemeldet-Texte |
| 120 | Installer | INSTALL-F-001…F-007, S-001, S-002, E-001 | Definition geschrieben (Startmenü, Desktop, saubere Deinstallation) | nie kompiliert, nie ausgeführt |
| 121 | Deinstallation | UNINSTALL-F-001…F-005, S-001, E-001 | im Skript enthalten (Dateien, Startmenü, Desktop, Nachfrage vor Datenlöschung) | nie ausgeführt |
| 122 | Auto Update | SELFUPDATE-F-001…F-007, R-001, S-001 | keine Zeile | gesamtes Modul (Entscheidung §4: bauen) |
| 123 | Logging | LOG-F-001…F-004, S-001, S-002 | Dateilog, Audit-Protokoll, Exit-Codes, Problem-IDs | Beleg; Prüfung, dass keine Passwörter und keine persönlichen Inhalte protokolliert werden |
| 124 | Performance | PERFAPP-F-001…F-004, E-001 | asynchron und abbrechbar gebaut | Startzeitmessung auf Referenzhardware, Speichermessung, Beleg der Responsivität |

### Release-Abnahme (Regeln 125-133)

| Regel | Was verlangt ist | Stand |
| --- | --- | --- |
| 125 Stabilität | Referenzinstallation ohne reproduzierbare Abstürze, UI-Freezes, Endlosschleifen, unkontrollierte Hintergrundprozesse; ein reproduzierbarer Crash in einem MUSS-Szenario ist `RELEASE BLOCKED` | nicht prüfbar ohne Windows-Maschine |
| 126 Sicherheitstests | dreizehn Angriffe: Command Injection, Path Traversal, unvalidierte Argumente, manipulierte Konfiguration, manipulierte Updatepakete, ungültige Signaturen, beschädigte Backups, fehlende Administratorrechte, UAC-Abbruch, Prozessabbruch, manipulierte Logs, ungültige Datenbank, beschädigte Reports | Vorkehrungen vorhanden (Parameter-Whitelist, Pfadprüfer mit Linkauflösung, fail-closed-Download, Quellen-Allowlist), **kein Angriff ausgeführt** |
| 127 Installer-Abnahme | saubere VM → Setup → Installation → Start → Systemanalyse → Wartung → Update → Neustart → Start → Deinstallation | nie durchlaufen (kein Inno Setup verfügbar) |
| 128 Release-Abnahme | alle kritischen Module PASSED, Sicherheits-MUSS-Tests PASSED, Installer/Uninstaller/Upgrade/Rollback/Recovery/Offline PASSED, keine offenen kritischen Fehler | kein Punkt erfüllt |
| 129 Release-Blocker | zwölf Blocker, unter anderem Datenverlust, ungewollte Systemänderung, fehlendes Rollback, Command Injection, falsche Erfolgsmeldungen | formal ausgelöst, sobald ein Status `PASSED` ohne Nachweis behauptet würde |
| 130 Abnahmeprotokoll | Tabelle `Modul \| Tests \| Bestanden \| Fehlgeschlagen \| Blockiert \| Status` je Release-Version | Generator fehlt; Zahlen können erst nach echten Läufen entstehen |
| 131 Definition of Done | zwölf Punkte je Modul, ab Punkt 9 („Tests tatsächlich ausgeführt“) | für jedes Modul unerfüllt |
| 132 Release Candidate | RC-Version, Testinstallation auf sauberem Windows 11, feste Kette bis Deinstallation | nicht vorhanden |
| 133 Abschlussregel | „fertig“ heißt: existiert, funktioniert tatsächlich, unter realistischen Bedingungen getestet, verhält sich bei Fehlern sicher, dokumentiert, erfüllt alle Abnahmekriterien. Nicht ausgeführter Test = `BLOCKED`, fehlgeschlagener MUSS-Test = `FAILED` | es wird an keiner Stelle etwas anderes behauptet |

## 2. Querschnittsanforderungen, die noch gar nicht existieren

| Regel | Was fehlt |
| --- | --- |
| 85 Testklassen | Es gibt **keinen Testkatalog** mit IDs wie `CLEANUP-S-002`. Die 125 vorhandenen Testfälle sind nach Klasse benannt, nicht nach Kriterium zugeordnet |
| 126 Sicherheitstests | **Keiner der dreizehn geforderten Angriffe wurde ausgeführt** (Command Injection, Path Traversal, ungültige Argumente, manipulierte Konfiguration, manipulierte Updatepakete, ungültige Signaturen, beschädigte Backups, fehlende Rechte, UAC-Abbruch, Prozessabbruch, manipulierte Logs, ungültige Datenbank, beschädigte Reports). Einzelne Vorkehrungen existieren (Parameter-Whitelist, Pfadprüfer mit Linkauflösung, fail-closed-Download, Quellen-Allowlist), aber ohne Lauf | 
| 127 Installer-Abnahme | Die Kette *saubere VM → Setup → Start → Analyse → Wartung → Update → Neustart → Start → Deinstallation* wurde nie durchlaufen |
| 128 Release-Abnahme | erfüllt **keinen** Punkt: keine kritischen Module PASSED, Sicherheitstests nicht gelaufen, Installer/Uninstaller/Upgrade/Rollback/Recovery/Offline unbelegt |
| 129 Release-Blocker | formal ausgelöst durch „falsche Erfolgsmeldungen“, sobald wir einen Status `PASSED` behaupten würden, ohne geprüft zu haben |
| 130 Abnahmeprotokoll | Protokolltabelle existiert nicht – sie kann erst nach echten Läufen Zahlen enthalten |
| 131 Definition of Done | für jedes Modul unerfüllt ab Punkt 3 (Fehlerbehandlung belegt), in jedem Fall ab Punkt 9 (Tests tatsächlich ausgeführt) |
| 132 Release Candidate | kein RC, keine RC-Testinstallation |
| 133 Abschlussregel | Es wird an keiner Stelle `fertig` behauptet; die Zählung implementierter Funktionen wird nicht als Nachweis verwendet |

---

## 3. Was ohne Windows-Rechner sofort umsetzbar ist

Alle Punkte sind reine Implementierung mit vorhandenen Mitteln; sie machen Module von
`NOT_STARTED`/`IN_DEVELOPMENT` zu `READY_FOR_TEST`, **nicht** zu `PASSED`.

1. **TPM- und Firewallstatus lesen** (DIAG-F-009, DIAG-F-011) – beides sind MUSS-Kriterien der
   Systemanalyse und fehlen vollständig.
2. **Windows-Update-Metadaten** (UPDATE-F-003): KB-Nummer, Kategorie und Neustartbedarf aus dem
   COM-Objekt; danach UPDATE-F-004 Auswahl.
3. **Event Log Analyzer** (EVENT-F-002…F-009) – System- und Anwendungsprotokoll, Filter nach
   Level/Quelle/ID, Gruppierung wiederkehrender Fehler, ohne Ursachenbehauptung.
4. **Storage Analyzer** (STORAGE-F-001…F-008) – Ordnergrößen, größte Dateien, Sortierung,
   Kennzeichnung System/Benutzer, Zugriffsfehler als UNKNOWN.
5. **Prozessmanager** (PROCESS-F-003…F-007) – CPU-Anteil, Publisher, Signaturprüfung, Details.
6. **Netzwerkdiagnose** (NETWORK-F-005…F-007) – Erreichbarkeits-, DNS- und TCP-Prüfung lokal.
7. **Dienste-Manager** (SERVICE-F-004…F-007) – Abhängigkeiten, kritische Kennzeichnung,
   Start/Stopp/Starttyp mit Freigabe, Journal und Rücknahme.
8. **Autostartmanager** (STARTUP-F-001…F-007) – Registry, Startup-Ordner, Signatur, Deaktivieren
   statt Löschen mit Wiederherstellung.
9. **Security Center** (SECURITY-F-003, F-005…F-007) – Firewallstatus, Quick/Full Scan mit
   Freigabe und Ergebnisdarstellung.
10. **Testkatalog nach Regel 85** – ID-Schema je Modul, Zuordnung der vorhandenen 125 Fälle,
    Generator für das Abnahmeprotokoll nach Regel 130.
11. **Recovery und Change Journal** (RECOVERY-*, JOURNAL-F-007) – Erkennung unterbrochener
    Vorgänge aus dem Journal, Rollbackstatus als Feld.

---

## 4. Entscheidungen des Eigentümers (beantwortet am 2026-09-20)

Der Eigentümer hat die vier offenen MUSS-Konflikte entschieden. Damit sind sie keine Blocker mehr,
sondern **offene Arbeit**:

| Punkt | Entscheidung | Bedeutung für die Umsetzung |
| --- | --- | --- |
| REPORT-F-002 PDF | **PDF wird echt umgesetzt** | Ein PDF-Writer muss ohne Cloud und ohne Telemetrie auskommen; der Bericht erscheint dann in allen vier Formaten |
| 113 KI-Modul | **Selbst bauen, Name „Windows Stalker“** | Analysiert die strukturierten Diagnosedaten, gibt Empfehlungen mit Begründung, darf online recherchieren, **darf aber niemals private Daten versenden** und niemals selbstständig Systemänderungen ausführen (AI-S-001/AI-S-002). Der Versand muss auf technische Angaben begrenzt und im Code erzwungen werden |
| 122 Auto Update | **Wird umgesetzt** | Versionserkennung, Download, Signatur- und Hashprüfung, Installation, Rückfallebene |
| 91 winget | **Wird umgesetzt, zuerst** | Erkennung installierter Programme, verfügbare Updates, Einzelauswahl, Exit-Code-Auswertung, Versionsnachprüfung; weiterhin keine Drittanbieter-Portale |
| Reihenfolge | **Diagnose zuerst** | Regeln 87 (Firewall/TPM) und 90 (Update-Metadaten) sind in der Sitzung vom 2026-09-20 umgesetzt; als Nächstes folgen die Analyzer- und Verwaltungsmodule |

### Weiterhin offen (keine reine Umsetzungsfrage)

Die verbleibenden Punkte der früheren Fassung dieses Abschnitts:

| Punkt | Widerspruch | Optionen |
| --- | --- | --- |
| REPORT-F-002 PDF | Der Berichtsteil sperrt PDF mit `UNSUPPORTED_PLATFORM` und `SupportedFormats` enthält es nicht – laut Regel 83.1 ein fehlgeschlagenes MUSS | (a) PDF-Bibliothek aufnehmen und echt erzeugen, (b) Kriterium streichen lassen, (c) dauerhaft `BLOCKED` ausweisen |
| 113 KI-Modul | Es gibt kein KI-Modul; alle AI-*-Tests sind MUSS und können nicht bestanden werden | (a) Modul umsetzen (lokal, ohne Cloud-Zwang, nur Vorschläge), (b) als nicht im Lieferumfang erklären |
| 122 Auto Update | Kein Selbst-Update vorhanden, SELFUPDATE-F-001…F-007 sind MUSS | (a) umsetzen (Signatur-/Hashprüfung, Rückfallebene), (b) für Version 1.0 streichen |
| 91 winget | Kein winget-Pfad vorhanden, APPUPDATE-F-001…F-006 sind MUSS | (a) umsetzen, (b) ganz aus dem Lieferumfang nehmen. Nie über Drittanbieter-Portale |
| CLEAN-R-001 | Löschen in Cache-Ordnern ist endgültig; nur als reversibel bezeichnete Aktionen müssen zurücknehmbar sein | **offen**: gilt „reversibel“ für Papierkorb-Option und Sicherung, oder muss jeder Löschvorgang in den Papierkorb |

---

## 5. Was nur auf einer Windows-Maschine passieren kann

Nicht durch mehr Code zu ersetzen, sondern nur durch Ausführung:

* `dotnet build` und `dotnet test` – 125 Fälle, 16 Klassen,
* die drei PowerShell-Skripte und der Inno-Setup-Lauf,
* der CI-Workflow (beide Jobs),
* die Referenzhardware-Messung (Ryzen 5 5600G, GIGABYTE B450M S2H, BIOS F67, Windows 11 Pro,
  RTX 3060, 24 GB) inklusive Nachweis, dass nichts verändert wurde,
* Installer-/Deinstaller-Abnahme auf einer sauberen VM (Regel 127),
* Release-Candidate-Durchlauf (Regel 132),
* Stabilitäts- und Sicherheitstests (Regel 125, 126).

**Ergebnis dieser Bestandsaufnahme:** Der Release ist nach Regel 129 gesperrt. Der Sperrgrund ist
nicht ein einzelner Fehler, sondern die Summe aus fehlenden Modulen (Regel 89, 91, 95, 97, 103,
109, 110, 113, 117, 122), fehlenden MUSS-Kriterien in vorhandenen Modulen (Regel 87, 90, 92–94,
96, 98–102, 105–108, 114, 118) und dem Umstand, dass kein einziger Test ausgeführt werden konnte.

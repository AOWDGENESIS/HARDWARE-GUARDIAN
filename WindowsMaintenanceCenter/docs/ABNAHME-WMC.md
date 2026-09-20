# Abnahme gegen die WMC-Spezifikation V1.0 (Modul für Modul, ehrlich)

Grundlage: `docs/SPEC-WMC-V1.md` (vom Auftraggeber vorgegeben, am 2026-09-20 übernommen).
Dieses Dokument beantwortet je Modul M00-M48 genau drei Fragen: **Was existiert? Was fehlt für die
MUSS-Kriterien? Was ist der Status nach Kapitel 2 der Spezifikation?**

## 0. Statusregel und der eine Satz, der alles bestimmt

Kapitel 2 lässt genau sieben Werte zu, und Kapitel 68 sagt, wann `BLOCKED` gilt: wenn eine
notwendige Windows-Komponente fehlt, die Testumgebung nicht verfügbar ist oder ein sicherer Test
nicht möglich ist.

In dieser Entwicklungsumgebung gibt es **kein .NET SDK, kein Windows und keine Windows-11-VM**.
Damit gilt:

| Ebene | Zustand |
| --- | --- |
| Build (Gate 1) | nie ausgeführt → `BLOCKED` |
| Unit-Tests (Gate 2) | 206 Testfälle geschrieben, nie ausgeführt → `BLOCKED` |
| Integration/Safety/Security/Recovery/Offline/Regression (Gates 3-6, 9) | nie ausgeführt → `BLOCKED` |
| Installer/Uninstaller (Gate 7) | Definition geschrieben, nie kompiliert → `BLOCKED` |
| Lokalisierung (Gate 8) | 2 von 4 Sprachen vorhanden; keine Sprachprüfung gelaufen → `BLOCKED` |
| VM-Abnahme (Gate 10, Modul M46) | keine VM verfügbar → `BLOCKED` |

**Folge nach Kapitel 2 und 68: kein Modul ist `PASSED`.** Der Projektstatus ist
`RELEASE_BLOCKED` (siehe `docs/RELEASE_STATUS.md`). Die Spalte „Funktionsstand" unten sagt nur, wie
weit die Umsetzung ist - sie ist **kein** Abnahmenachweis.

---

## 1. Modulzustand M00-M48

| Modul | Prio | Funktionsstand | Was für die MUSS-Kriterien fehlt |
| --- | --- | --- | --- |
| M00 Core / Foundation | P0 | Shell, Navigation, Konfiguration lesen/schreiben, Absturzschutz für kaputte Konfiguration | `M00-L-003` Japanisch und `M00-L-004` Russisch fehlen vollständig; `M00-F-003` sauberes Beenden und `M00-O-001` Start ohne Internet nicht belegt |
| M01 Dashboard | P0 | Version, Build, CPU, RAM, GPU, Laufwerke, freier Speicher, Defender, Firewall, Updatezustand werden real gelesen | `M01-U-001/U-002` Statusfarben mit Erklärung prüfen; `M01-U-003` Gesamtbewertung nur aus echten Prüfungen belegen; kein Lauf |
| M02 System Discovery | P0 | CPU, RAM, GPU, Laufwerke, Mainboard, BIOS/UEFI, TPM, Secure Boot, Windows, Netzwerk, Defender, Firewall, Restore Points | **BitLocker fehlt** (keine Abfrage im Code); `M02-E-001` Protokollierung nicht erreichbarer Quellen im Lauf belegen |
| M03 Diagnostic Engine | P0 | Problemregister mit ID, Schweregrad, Evidenz | Finding-Felder `CATEGORY`/`SOURCE`/`RECOMMENDATION`/`RISK` je Eintrag prüfen und ergänzen; `M03-S-002` UNKNOWN-Pfad belegen |
| M04 Cleanup Engine | P0 | Ablauf SCAN→CLASSIFY→CALCULATE→DISPLAY→APPROVAL→BACKUP→EXECUTE→VALIDATE vorhanden, Kategorien mit Sicherheitsklasse, Trockenlauf erzwungen | `M04-F-004` freier Speicher **nach** dem Lauf erneut messen; `M04-S-001` persönliche Ordner standardmäßig aus; `M04-S-008` Systemschutzpfade prüfen; `M04-R-001` Wiederherstellbarkeit klären |
| M05 Storage Analyzer | P1 | nichts | gesamtes Modul (`M05-F-001`…`F-005`, Doppelte Dateien, Alter/Größe-Filter) |
| M06 Windows Update | P0 | Suche, Liste mit KB/Kategorie/Severity/Neustart/Größe, Einzelinstallation mit Freigabe, Ergebniscode-Auswertung, Nachprüfung | `M06-F-006` Mehrfachauswahl; Auswahl- und Installationsoberfläche; `M06-F-007` Nachprüfung im Lauf belegen |
| M07 Software / winget | P1 | nichts | gesamtes Modul; `M07-S-001` kein `upgrade --all`, `M07-E-001` UNAVAILABLE statt SUCCESS |
| M08 Softwaremanager | P1 | Installationsinventar (Name, Hersteller, Version) lesend | Suche, Sortierung, Details, Deinstallation mit Freigabe, Nachprüfung, Seite |
| M09 Autostart | P1 | nichts (nur Diensteliste lesend) | Run/RunOnce, Startup-Ordner, geplante Tasks, Signatur, Deaktivieren statt Löschen, Rücknahme |
| M10 Services | P1 | Name, Status, Starttyp lesend | Abhängigkeiten, Start, Stopp, Starttypänderung, Zustand vorher speichern |
| M11 Task Scheduler | P1 | nichts | gesamtes Modul |
| M12 Repair Engine | P0 | DISM CheckHealth/ScanHealth/RestoreHealth, SFC /verifyonly, Exit-Codes, Ausgabe, Freigabe | **CHKDSK fehlt**; `M12-F-003` Validierung nach jeder Aktion; `M12-R-001` dokumentierter Recovery-Pfad je kritischer Reparatur |
| M13 WinSxS | P1 | Komponentenspeicher-Prüfung teilweise | Größe ermitteln, Einsparung anzeigen, `/ResetBase`-Wirkung erklären (nicht ausführen) |
| M14 Drive Optimization | P1 | nichts | gesamtes Modul, inklusive Verbot der HDD-Defragmentierung auf SSD |
| M15 Performance | P1 | CPU- und RAM-Auslastung, Prozessliste | Disk-, GPU-, Boot-Metriken, Autostartbezug, Seite |
| M16 Process Manager | P1 | Name, PID, Pfad, Arbeitsspeicher | CPU je Prozess, Disk/GPU/Netzwerk, Publisher, Signatur, Parent, Beenden mit Freigabe und Nachprüfung |
| M17 Network Diagnostics | P1 | Adapter, IP, Gateway, DNS, IPv4/IPv6 lesend | Internet, DNS-Auflösung, Ping, TCP, Listening Ports |
| M18 Security Center | P0 | Defender-Status, Echtzeitschutz, Firewallprofile, Signaturstand | `M18-F-001` Quick Scan, `M18-F-002` Full Scan, `M18-F-003` Status nach Scan |
| M19 Event Log Analyzer | P1 | Systemprotokoll, 7 Tage, Level 1-2 | Application, Update, Defender, Disk, WHEA, Kernel; Filter, Gruppierung |
| M20 Crash Analyzer | P1 | nichts | BugChecks, Minidumps, WHEA, Application Crashes; „Ursache nicht eindeutig bestimmbar" als Standard |
| M21 Hardware Health | P1 | Sensoren mit Quelle und Qualitätsstufe, SMART nur wenn gemessen | `M21-F-001` Anzeige nicht verfügbarer Sensoren im Lauf belegen |
| M22 Restore Points | P0 | Erstellung und Nachweis | `M22-F-002` Validierung der Erstellung im Lauf; Bezeichnung je Vorgang statt fester Text |
| M23 Backup Engine | P0 | Erzeugung, eindeutige ID, Manifest, Gate im Wartungsablauf | Lesbarkeitsprüfung, `M23-S-001` ungültiges Backup nie als gültig, `M23-R-001` Recovery-Verwendung |
| M24 Rollback Engine | P0 | Dienst vorhanden und registriert | Klassifizierung FULLY/PARTIALLY/NOT_REVERSIBLE, Ausführung, Nachprüfung, Kennzeichnung vor der Aktion, erreichbar aus der Oberfläche |
| M25 Change Journal | P0 | Audit-Protokoll (JSON + TXT) mit Zeit, Aktion, Ziel, Ergebnis | Felder BEFORE, BACKUP, APPROVAL, ERROR, ROLLBACK je Eintrag; eigene Journal-Sicht |
| M26 Maintenance Plans | P1 | nichts | erstellen, bearbeiten, aktivieren, deaktivieren, löschen; automatischer Neustart nur mit expliziter Aktivierung |
| M27 One-Click Maintenance | P0 | nichts | gesamter Ablauf DISCOVERY→…→REPORT inklusive Vorher/Nachher-Messung |
| M28 Reporting | P1 | HTML, TXT, JSON mit echten Systemdaten und Aktionen | **PDF fehlt** (Auftrag: umsetzen); `M28-F-005` übersprungene Aktionen im Bericht |
| M29 Offline Engine | P0 | Offline-Erkennung, Sperren für Online-Funktionen | Offline-Lauf über alle lokalen Module als Nachweis |
| M30 AI | P2 | nichts | Modul „Windows Stalker": strukturierte Daten lesen, Empfehlungen mit Begründung, kein Shell-Zugriff, kein Versand privater Daten |
| M31 Admin Worker | P0 | Start ohne Adminrechte, Hinweis auf Erfordernis | **`M31-S-001`/`M31-S-002` verletzt**: heute startet die ganze Anwendung erhöht neu, statt eine registrierte Aktion privilegiert auszuführen; UAC-Abbruch behandeln |
| M32 Action Registry | P0 | Vorlagenkatalog mit Parametermuster, Timeouts | Aktionen mit ID, Risiko, Adminbedarf, Argumenten, Validierung, Rollback, Timeout, Freigabe |
| M33 Command Execution | P0 | Prozessläufer mit Whitelist, Timeout, Exit-Code, Abbruch | Nachweis der Timeout- und Abbrucherkennung |
| M34 State Machine | P0 | Zustandsautomat vorhanden | **Zustandsnamen weichen ab**: Spezifikation verlangt INITIALIZING, DISCOVERY, DIAGNOSTIC, PLAN_GENERATED, AWAITING_APPROVAL, BACKUP, EXECUTING, VALIDATING, SUCCESS, ERROR, ROLLBACK, RECOVERING, BLOCKED, CANCELLED; RECOVERING fehlt ganz |
| M35 Recovery Engine | P0 | nichts | unterbrochene Jobs erkennen, Backup-/Rollbackstatus, Optionen anzeigen, ohne Freigabe nichts Riskantes tun |
| M36 Configuration | P0 | Laden, Speichern, Schema-Version, Schutz vor ungültiger Konfiguration | `M36-F-003` Migration inklusive Sicherung vor der Migration |
| M37 Localization | P1 | Deutsch und Englisch vollständig (742 Schlüssel je Sprache), Prüfwerkzeug sauber | **Japanisch und Russisch fehlen**; Datums-/Zahlenformate belegen; Fallback definieren und prüfen |
| M38 Logging | P0 | Dateilog, Audit-Protokoll, Exit-Codes | Level TRACE…CRITICAL als steuerbare Stufen; Nachweis, dass keine Passwörter und keine Dokumentinhalte im Log stehen |
| M39 Privacy | P0 | Telemetrie standardmäßig aus, keine Cloudpflicht | `M39-S-005` Prüfung des Diagnoseexports auf sensible Daten |
| M40 Installer | P0 | Inno-Definition geschrieben (Startmenü, Desktop, saubere Deinstallation) | **nie kompiliert, nie ausgeführt**; Artefaktname auf `WindowsMaintenanceCenter-Setup-x64.exe` umstellen |
| M41 Uninstaller | P0 | in der Definition enthalten | nie ausgeführt; Hintergrundkomponenten, Backups nur nach Nachfrage |
| M42 Auto Update | P1 | nichts | Ablauf CHECK→DOWNLOAD→SIGNATURE→HASH→BACKUP→INSTALL→STARTTEST, Rückfallebene |
| M43 Release Build | P0 | Skripte geschrieben | nie ausgeführt; Artefaktnamen umstellen; Hashes, Signaturstatus, Reproduzierbarkeit |
| M44 Security Testing | P0 | Vorkehrungen vorhanden (Parameter-Whitelist, Pfadprüfer mit Linkauflösung, fail-closed-Download, Quellen-Allowlist) | **kein einziger der 14 Angriffe ausgeführt** → `BLOCKED` |
| M45 Regression | P0 | nichts | Ablauf BUILD→UNIT→INTEGRATION→SAFETY→REGRESSION existiert nur als Absicht |
| M46 VM Acceptance | P0 | nichts | keine Windows-11-VM verfügbar → `BLOCKED` |
| M47 Accessibility | P1 | Fokusreihenfolge und Kontraste teilweise gesetzt | Tastaturnavigation, Fokus, Kontrast, Skalierung, keine reine Farbkennzeichnung prüfen |
| M48 Documentation | P1 | README, STATUS, BUILD, RELEASE, SECURITY, ABNAHME, PORTABLE_README | **ARCHITECTURE, INSTALLATION, USER_GUIDE, DEVELOPER_GUIDE, TESTING, CHANGELOG, PRIVACY, RECOVERY fehlen** |

---

## 2. Abweichungen zwischen Spezifikation und bisherigem Produkt

**Entschieden am 2026-09-20 durch den Auftraggeber:**

* **Produktname: umbenannt.** Das Projekt heißt jetzt `WindowsMaintenanceCenter`
  (Verzeichnis, Lösung, 15 Projekte, Namensräume, Ressourcen, Installer, Skripte, CI-Workflow,
  Dokumente). Die Artefakte heißen `WindowsMaintenanceCenter-Setup-x64.exe`,
  `WindowsMaintenanceCenter-Portable-x64.exe`, `WindowsMaintenanceCenter-Checksums.txt`,
  `WindowsMaintenanceCenter-ReleaseNotes.txt`. Die Markerdatei für den portablen Modus heißt
  `WindowsMaintenanceCenter.portable`.
* **Datenhaltung: beides.** JSON bleibt die Standardablage für Konfiguration und Einstellungen;
  zusätzlich wird SQLite für Journal und Historie aufgenommen und ist umschaltbar. Kapitel 82
  (Schema-Version, Migration, Integritätsprüfung, Backup, Recovery) wird damit auf die
  SQLite-Ablage bezogen prüfbar; Kapitel 100 nennt SQLite als Kernbestandteil.

Die folgenden Punkte waren vorher offen und sind damit beantwortet:

| Punkt | Spezifikation | Bestand | Wirkung |
| --- | --- | --- | --- |
| Produktname | „Windows Maintenance Center", Artefakte `WindowsMaintenanceCenter-*` | Projekt, Namespaces, Ressourcen, Installer und Skripte heißen `WindowsMaintenanceCenter` | Umbenennung betrifft Lösung, 15 Projekte, Namensräume, Ressourcen, Installer, Skripte, Dokumente. **Entscheidung des Auftraggebers erforderlich** (jetzt umbenennen oder erst nach Funktionsfertigkeit) |
| Datenhaltung | SQLite als Kernbestandteil (Kapitel 100) und eigenes Modul für Datenbanksicherheit (Kapitel 82) | Konfiguration und Audit heute als JSON-Dateien, kein SQLite | **entschieden: beides** - JSON bleibt Standard, SQLite kommt für Journal und Historie hinzu und ist umschaltbar. Arbeitsschritt: Paket aufnehmen, Journal in SQLite spiegeln, Schema-Version/Migration/Integritätsprüfung/Backup/Recovery umsetzen |
| Fehler-IDs | `WMC-UPDATE-0042` (Kapitel 85) | Problemregister vergibt `{Präfix}-{n:D3}` | Präfixschema auf `WMC-<MODUL>-<Nr>` angleichen |

Sprachen: Die Spezifikation verlangt vier Sprachen (Kapitel 37, 43, 63). Deutsch und Englisch sind
vollständig; Japanisch und Russisch sind **nicht vorhanden**. Die Übersetzung von 742 Schlüsseln je
Sprache ist ein eigener Arbeitsschritt und muss für Gate 8 vollständig sein.

Struktur der Nachweise ist angelegt: `test-results/` mit den zehn Unterordnern aus Kapitel 71. Alle
Ordner sind leer, weil kein Test ausgeführt wurde - genau das schreibt die Spezifikation vor
(Kapitel 5: ein Test ohne Nachweis gilt als `NOT VERIFIED`).

---

## 3. Reihenfolge bis zur ersten belastbaren Abnahme

Ohne Windows-Maschine ist kein Gate zu bestehen. Was hier trotzdem sinnvoll ist, in dieser
Reihenfolge:

1. **Zustandsmaschine und Fehler-IDs an die Spezifikation angleichen** (M34, Kapitel 85) - sie
   tragen alle anderen Module.
2. **M31 Admin Worker**: Aktion für Aktion privilegieren statt die ganze Anwendung neu zu starten.
3. **M32 Action Registry**: registrierte Aktionen mit Risiko, Freigabe, Rollback, Timeout.
4. **M06 Oberfläche** für Auswahl und Installation der Windows-Updates, dann M07 winget.
5. **M05, M09, M10, M11, M14, M16-M20**: Analyzer und Verwaltungsmodule mit Seiten und Empty States.
6. **M35 Recovery + M24 Rollback + M25 Journal** schließen.
7. **M37** Japanisch und Russisch; **M48** fehlende Dokumente.
8. Erst danach Gates 1-10 auf einer Windows-11-VM; vorher ist jeder Status `BLOCKED`.

**Kein Modul ist heute `PASSED`, und keines darf als `PASSED` bezeichnet werden, bevor ein Gate auf
echter Umgebung nachweisbar bestanden ist.**

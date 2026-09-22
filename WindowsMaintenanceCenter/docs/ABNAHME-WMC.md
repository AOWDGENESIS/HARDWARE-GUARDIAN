# Abnahme gegen die WMC-Spezifikation V1.0 (Modul für Modul, ehrlich)

Grundlage: `docs/SPEC-WMC-V1.md` (vom Auftraggeber vorgegeben, am 2026-09-20 übernommen).
Dieses Dokument beantwortet je Modul M00-M48 genau drei Fragen: **Was existiert? Was fehlt für die
MUSS-Kriterien? Was ist der Status nach Kapitel 2 der Spezifikation?**

## 0. Statusregel und der eine Satz, der alles bestimmt

Kapitel 2 lässt genau sieben Werte zu, und Kapitel 68 sagt, wann `BLOCKED` gilt: wenn eine
notwendige Windows-Komponente fehlt, die Testumgebung nicht verfügbar ist oder ein sicherer Test
nicht möglich ist.

In dieser Entwicklungsumgebung gibt es **kein .NET SDK, kein Windows und keine Windows-11-VM**. Seit
dem 2026-09-20 läuft deshalb eine **Windows-VM als CI-Runner** (`docs/VM-CI.md`). Sie hat Bau, Tests
und die Paketierung wirklich ausgeführt; was sie nicht kann (Sensorik, Akku, UAC-Dialog, Neustart),
bleibt der Zielmaschine vorbehalten.

| Ebene | Zustand |
| --- | --- |
| Build (Gate 1) | **bestanden auf der CI-VM** (Lauf `35694300454`: 15 Projekte inklusive WPF, 0 Fehler) |
| Unit-Tests (Gate 2) | **bestanden auf der CI-VM**: 359 Fälle ausgeführt, 0 fehlgeschlagen (Lauf `35694300454`); TRX und `summary.txt` in `test-results/unit/20260922T062120Z-359-of-359/` |
| Integration/Safety/Security/Recovery/Offline/Regression (Gates 3-6, 9) | auf der Zielmaschine nicht ausgeführt → `BLOCKED` (Arbeitsliste `docs/TESTMATRIX-VM.md`, Messwerkzeug `docs/VM-TESTKIT.md`) |
| Installer/Uninstaller (Gate 7) | Installer und Portable-EXE gebaut und mit SHA-256 gegengeprüft; **Installationszyklus auf der CI-Maschine gelaufen** (Lauf `35704157557`, Bericht `test-results/installer/20260922T082333Z-INS-CI/report.json`, Ergebnis `PASSED`): stille Installation, Layout, Versions- und SHA-256-Gleichheit, Start mit Exit-Code 0, Nutzungssonde, Deinstallation und portables Artefakt. Offen und im Bericht als offene Punkte genannt: Reparaturinstallation, Upgrade, Neustart, weitere Laufwerke, „USE" im Sinne von Kapitel 62 → **teilweise** (Kern belegt, Kapitel-74-Punkte offen) |
| Lokalisierung (Gate 8) | 2 von 4 Sprachen vorhanden; keine Sprachprüfung gelaufen → `BLOCKED` |
| VM-Abnahme (Gate 10, Modul M46) | Ziel-VM nicht verfügbar → `BLOCKED` |

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
| M25 Change Journal | P0 | Audit-Protokoll (JSON + TXT) mit Zeit, Aktion, Ziel, Ergebnis; Fehler tragen jetzt WHY (`Cause`) und LOG (`LogReference`) nach Kapitel 85 | Felder BEFORE, BACKUP, APPROVAL, ERROR, ROLLBACK je Eintrag; eigene Journal-Sicht; SQLite-Spiegel (Entscheid: JSON bleibt Standard, SQLite zusätzlich) |
| M26 Maintenance Plans | P1 | nichts | erstellen, bearbeiten, aktivieren, deaktivieren, löschen; automatischer Neustart nur mit expliziter Aktivierung |
| M27 One-Click Maintenance | P0 | nichts | gesamter Ablauf DISCOVERY→…→REPORT inklusive Vorher/Nachher-Messung |
| M28 Reporting | P1 | HTML, TXT, JSON mit echten Systemdaten und Aktionen | **PDF fehlt** (Auftrag: umsetzen); `M28-F-005` übersprungene Aktionen im Bericht |
| M29 Offline Engine | P0 | Offline-Erkennung, Sperren für Online-Funktionen | Offline-Lauf über alle lokalen Module als Nachweis |
| M30 AI | P2 | nichts | Modul „Windows Stalker": strukturierte Daten lesen, Empfehlungen mit Begründung, kein Shell-Zugriff, kein Versand privater Daten |
| M31 Admin Worker | P0 | **umgesetzt, nicht ausgeführt**; führt keine Aktion ohne Sicherung und Freigabe aus, wenn die Registrierung sie verlangt: `AdminWorker` führt genau eine registrierte Aktion aus, hebt pro Aktion über `runas` anstatt die Anwendung neu zu starten, wertet Win32 1223 als UAC-Abbruch (`UAC_CANCELLED`, nichts geändert) und gibt nie einen Erfolg ohne Nachprüfung aus. `ElevationService` (Neustart der ganzen Anwendung) existiert weiter, ist aber nicht mehr der Weg einer Aktion | Negativnachweis: keiner der 14 Sicherheitsangriffe ist gelaufen (Gate 4), Abnahme auf VM offen |
| M32 Action Registry | P0 | **umgesetzt, nicht ausgeführt**, inklusive Freigabe- und Sicherungstor (Kapitel 30/44: BACKUP → APPROVAL → EXECUTE, gebunden an genau diese Aktion): `ActionRegistry` mit ID, Beschreibung, Risiko, Adminbedarf, Argumentformen (Text/Number/Path/Choice/Switch), Timeout, Rollback-Verweis und Freigabeflag; Argumente werden vor dem Start geprüft (Metazeichen Kapitel 78, Pfadpolitik Kapitel 79), der Kommandostring entsteht nur aus der Registrierung. Katalog: `SystemActionCatalog` (3 freigegebene lesende Abfragen, 5 registrierte aber **nicht** freigegebene Prüf-/Reparaturwerkzeuge). Fehlerkennungen nach Kapitel 85 | Freigabe der Reparaturwerkzeuge erst nach Freigabe-/Backup-Tor und gemessenen Exit-Codes; Nachweis auf VM offen |
| M33 Command Execution | P0 | **umgesetzt, nicht ausgeführt**: Start nur über die Registry, Timeout mit Abbruch des Prozessbaums und Ergebnis `BLOCKED` (nie Erfolg, `Action_Blocked_Timeout`), Exit-Code != 0 → `Failed`, dokumentierte Befundcodes → `ReportedFindings` (Lauf vollständig, Zustand nicht behoben). Fehlerkennungen nach Kapitel 85 | Nachweis der Timeout- und Abbrucherkennung: Testfälle geschrieben, aber nie ausgeführt (Gate 2 `BLOCKED`) |
| M34 State Machine | P0 | **umgesetzt**: genau die vierzehn Zustände des Kapitels 40; aus DISCOVERY führt kein Weg in EXECUTING, aus EXECUTING keiner direkt nach SUCCESS. `M34-F-001` jeder Zustandswechsel **wird gespeichert**: das Zustandsjournal (`StateJournal`, `FileStateJournal`) schreibt jede Transition unter demselben Lock auf die Platte (flush-to-disk), jeder Eintrag trägt Vorgang, Aktion, Position und Hashkette; RECOVERING ist aus jedem unterbrechbaren Zustand erreichbar und führt nie direkt in eine Ausführung | `M34-R-001` „unterbrochene Jobs werden erkannt" ist in der Software belegt (Unit-Tests, CI-Lauf), aber **auf keiner Zielmaschine mit echtem Absturz** geprüft → bis dahin `BLOCKED` (Gate 9/10, `docs/TESTMATRIX-VM.md` REC-01/02) |
| M35 Recovery Engine | P0 | **umgesetzt und bedienbar**: `RecoveryEngine` beantwortet M35-F-001…F-003 aus Aufzeichnungen (letzter Vorgang aus dem Journal, Backup über die OperationId gematcht, Rollbackfähigkeit über `IRollbackService`), `RecoveryCoordinator` macht daraus einen Befund `WMC-M35-###` im Problem Center mit Freigabeentwurf `Recovery:<backupId>` (Risiko hoch), und die Seite „Wiederherstellung" zeigt RECOVERY AVAILABLE, Werkzeug und Ergebnis. Ohne Freigabe für genau dieses Backup wird nichts ausgeführt (M35-S-001); verweigerte Anfragen sind kein Zustandswechsel, eine ausgeführt Wiederherstellung steht als RECOVERING→ROLLBACK→VALIDATING→SUCCESS/BLOCKED im Journal; Erfolg nur bei Ausführung **und** Validierung (Kapitel 86) | `M35-R-001` „Recovery wird getestet" verlangt einen echten Abbruch-/Wiederanlauf auf der Zielmaschine → `BLOCKED`, bis REC-01/02 im Kit gelaufen sind |
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

## 2a. Umgesetzt nach der Spezifikation (Stand 2026-09-20)

| Punkt | Was geändert wurde | Fundstelle |
| --- | --- | --- |
| M34 Zustandsmodell | Genau die vierzehn Zustände des Kapitels 40; die Übergangstabelle erlaubt aus DISCOVERY keinen Sprung in EXECUTING und aus EXECUTING keinen Sprung nach SUCCESS (Nachweis: 9 Testfälle) | `Core/Enums.cs`, `Core/Services/SystemStateMachine.cs` |
| Laufausgang | `StateFromHealth` endet bei kritischen oder warnenden Befunden in ERROR und ohne eindeutiges Ergebnis in BLOCKED - nie in SUCCESS (Kapitel 86/101) | `Core/Diagnostics/ScanOrchestrator.cs` |
| Kapitel 85 Fehlerkennung | Format `WMC-<Thema>-<Nummer>`, z. B. `WMC-UPDATE-0042`, `WMC-DRIVER-NVIDIA-001`; das Thema kommt aus der Kategorie, ein leerer Präfix wird daraus gefüllt statt „GEN" | `Core/Services/ProblemRegistry.cs` |
| Kapitel 85 Fehlerdarstellung | Jeder Fehler hat WHAT (Titel), WHY (`Cause`, Standard „Ursache nicht eindeutig feststellbar"), IMPACT, ACTION und LOG (`LogReference`, leer wenn nichts geschrieben wurde); sichtbar im Dashboard (neue Spalten) und im TXT-/JSON-Bericht | `Core/Models/ProblemModels.cs`, `App/Views/DashboardView.xaml`, `Reporting/ReportGenerator.cs` |
| Texte | 8 neue Schlüssel je Sprache (746 → 750 nach der Dashboard-Erweiterung) | `Core/Resources/{de,en}.json` |

| M32 Action Registry | Neue Modelle (`RegisteredAction`, `ActionArgumentSpec`, `ActionRequest`, `ActionValidationResult`, `ActionExecutionResult`) und die Registry selbst; Ablehnungen kommen als Schlüssel zurück (die Oberfläche entscheidet die Sprache) und unterscheiden „nicht registriert", „nicht freigegeben", „Argument ungültig", „Pfad nicht erlaubt" (Nachweis: 36 Testfälle in `ActionRegistryTests.cs`) | `Core/Models/ActionModels.cs`, `Core/Services/ActionRegistry.cs` |
| Kapitel 78/79 Angriffsfälle | `" ' ; & \| > < $ ` ( ) { } % ^` in jedem Text- und Pfadargument, `..`, UNC- und Gerätepfade werden abgewiesen; Längenlimit je Argument; Platzhalter `{Name}` werden nur mit geprüften Werten ersetzt | `Core/Services/ActionRegistry.cs` |
| M31 Admin Worker | Registry zuerst, dann Admin-Tor; Anhebung je Aktion, UAC-Abbruch = `UAC_CANCELLED` ohne Änderung; Ausgabe eines erhöhten Laufs ist ehrlich als nicht erfassbar gekennzeichnet; Audit-Eintrag und Protokollzeile je Lauf und je Ablehnung (Nachweis: 12 Testfälle in `AdminWorkerTests.cs`) | `Infrastructure/Platform/AdminWorker.cs` |
| M33 Timeout/Befundcode | Timeout → Prozessbaum beenden → `BLOCKED`; Befundcode eines Werkzeugs → Lauf vollständig, `ReportedFindings`, Text „nicht behoben" statt Erfolg; fehlgeschlagene Nachprüfung → `Failed`/`VERIFICATION_FAILED` | `Infrastructure/Platform/AdminWorker.cs` |
| Aktionskatalog | `SystemActionCatalog`: freigegeben sind nur drei lesende Abfragen ohne Adminrechte (`System.FsutilDeleteNotifyQuery`, `Network.IpConfigAll`, `System.SystemInfoSnapshot`); DISM-, SFC- und CHKDSK-Aufrufe sind registriert, aber `Allowed = false` und damit sichtbar und abgelehnt statt heimlich vorhanden. Der Test hält diese Liste fest, damit kein Reparaturwerkzeug unbemerkt freigegeben wird (10 Testfälle) | `Infrastructure/Platform/SystemActionCatalog.cs`, `tests/.../SystemActionCatalogTests.cs` |
| Kapitel 30/44 Freigabe- und Sicherungstor | `RegisteredAction.RequiresApproval`/`RequiresBackup`; die Registry prüft in der Reihenfolge BACKUP (Sicherung muss **diese** Aktion nennen und einen Ablageort haben) → APPROVAL (Entscheidung `Approved` und `OperationId` = Aktions-ID) → Argumente. Eine Ablehnung nennt den fehlenden Teil beim Namen (`Action_Blocked_BackupRequired`, `Action_Blocked_ApprovalMissing`, `Action_Blocked_ApprovalMismatch`); Freigabe und Sicherung landen im Audit-Eintrag. Nachweis: 7 Testfälle in `ActionRegistryTests.cs` und 3 in `AdminWorkerTests.cs` | `Core/Services/ActionRegistry.cs`, `Infrastructure/Platform/AdminWorker.cs` |
| Reparaturwerkzeuge | `System.DismRestoreHealth` und `System.SfcScanNow` verlangen Sicherung **und** Freigabe, sind aber weiterhin `Allowed = false`; ein Test hält fest, dass beide Tore deklariert bleiben | `Infrastructure/Platform/SystemActionCatalog.cs` |
| Verdrahtung | `IActionRegistry`/`IAdminWorker` werden im Kompositionswurzel erzeugt und über DI verteilt; `App.xaml.cs` baut den Katalog einmal beim Start | `App/App.xaml.cs` |

Nicht umgesetzt und weiterhin offen: die Persistenz des Zustands über einen Neustart (`M34-F-001`
verlangt „jeder Zustand wird gespeichert") und das Wiedererkennen unterbrochener Jobs
(`M34-R-001`/M35). Beides braucht die Recovery-Engine und, nach dem Entscheid zur Datenhaltung, den
SQLite-Spiegel. Ebenso offen: das Freigabe- und Backup-Tor vor den Reparaturwerkzeugen (Kapitel 30/44)
und die Messung ihrer Exit-Codes auf einer echten Maschine - deshalb bleiben sie `Allowed = false`.

## 2b. Windows-Bauumgebung und der erste echte Kompilierungslauf (2026-09-20)

Es gibt jetzt einen Windows-Rechner, auf dem gebaut, getestet und paketiert wird: die gehostete
Windows-VM der CI (`.github/workflows/windowsmaintenancecenter.yml`, `windows-latest`, Windows Server
2025, .NET 10.0.401, Inno Setup). Die Einzelheiten und die Grenzen stehen in `docs/VM-CI.md`.

Ergebnis der ersten zwölf Läufe: **der Bau ist noch nicht bestanden.** Die Fehlerzahl je Lauf ging von
386 auf zuletzt 13 zurück; die dreizehn verbliebenen Fehler (fehlende Namensräume in Oberflächen- und
Testprojekt, `ValueOrigin.Manufacturer` statt `OfficialManufacturer`) sind für Commit `8315c29` behoben,
aber **das Ergebnis dieses Laufs ist nicht abrufbar** - der GitHub-Zugang dieses Arbeitsplatzes wurde
während des Laufs ungültig. Kein Gate ist damit bestanden; Gate 1 (Build) bleibt `BLOCKED`.

Was in diesen Läufen gefunden wurde, ist im Abnahmedokument in `docs/VM-CI.md` Abschnitt 3a/3b
verzeichnet, darunter mehrere echte Anzeigefehler (`Display` als Methodengruppe, `Info()` mit vier
Argumenten, Vergleich von `UpdateStatus` mit `StageOutcome`, `InMemoryStores` mit null-Zugriff).

## 3. Reihenfolge bis zur ersten belastbaren Abnahme

Ohne Windows-Maschine ist kein Gate zu bestehen. Was hier trotzdem sinnvoll ist, in dieser
Reihenfolge:

1. **Zustandsmaschine und Fehler-IDs an die Spezifikation angleichen** (M34, Kapitel 85) - sie
   tragen alle anderen Module.
2. **M31 Admin Worker / M32 Action Registry / M33 Command Execution**: umgesetzt (siehe §2a);
   offen sind das Freigabe-/Backup-Tor vor den Reparaturwerkzeugen, die Messung ihrer Exit-Codes und
   der Nachweis auf einer echten Maschine.
4. **M06 Oberfläche** für Auswahl und Installation der Windows-Updates, dann M07 winget.
5. **M05, M09, M10, M11, M14, M16-M20**: Analyzer und Verwaltungsmodule mit Seiten und Empty States.
6. **M35 Recovery + M24 Rollback + M25 Journal** schließen.
7. **M37** Japanisch und Russisch; **M48** fehlende Dokumente.
8. Erst danach Gates 1-10 auf einer Windows-11-VM; vorher ist jeder Status `BLOCKED`.

**Kein Modul ist heute `PASSED`, und keines darf als `PASSED` bezeichnet werden, bevor ein Gate auf
echter Umgebung nachweisbar bestanden ist.**

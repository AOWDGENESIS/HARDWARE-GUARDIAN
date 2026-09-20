# WINDOWS MAINTENANCE CENTER

## V1.0 – Verbindliche Abnahme-, Test-, Sicherheits- und Release-Spezifikation

**Status:** Verbindliche Entwicklungs- und Abnahmespezifikation
**Produkt:** Windows Maintenance Center
**Ziel:** Professionelle, sichere, nachvollziehbare Windows-Wartungsanwendung
**Abnahmemodell:** Fail-Closed
**Primärplattform:** Windows 11 x64
**Standardsprache:** Deutsch

> Dieses Dokument ist die vom Auftraggeber vorgegebene Spezifikation, am 2026-09-20 wortgleich in
> das Repository übernommen. Es ersetzt die frühere Zählung der Regeln 83-133 (Hardware-Guardian-
> Fassung) und ist ab jetzt der verbindliche Maßstab für Abnahme und Release. Der Abgleich des
> aktuellen Codes gegen diesen Katalog steht in `docs/ABNAHME-WMC.md`, der aktuelle
> Release-Entscheid in `docs/RELEASE_STATUS.md`.

---

# 1. VERBINDLICHE REGEL

Diese Spezifikation definiert nicht nur gewünschte Funktionen.

Sie definiert:

* was implementiert werden muss
* wie es getestet werden muss
* wann ein Modul als fertig gilt
* welche Fehler eine Abnahme verhindern
* welche Nachweise erforderlich sind
* wann ein Release erlaubt ist
* wann ein Release zwingend blockiert wird

Eine Funktion gilt niemals als fertig, nur weil Code dafür vorhanden ist.

## Fertig bedeutet:

```text
IMPLEMENTIERT
+
BUILD ERFOLGREICH
+
FUNKTIONSTEST BESTANDEN
+
SICHERHEITSTEST BESTANDEN
+
FEHLERBEHANDLUNG BESTANDEN
+
ROLLBACK/RECOVERY GEPRÜFT
+
LOGGING GEPRÜFT
+
UI GEPRÜFT
+
LOKALISIERUNG GEPRÜFT
+
DOKUMENTATION VORHANDEN
+
NACHWEIS VORHANDEN
```

Fehlt ein verpflichtender Punkt:

```text
NOT READY
```

---

# 2. STATUSMODELL

Jedes Modul verwendet ausschließlich diese Statuswerte:

```text
NOT_STARTED
IN_DEVELOPMENT
READY_FOR_TEST
TESTING
PASSED
FAILED
BLOCKED
```

## PASSED

Alle MUSS-Kriterien bestanden.

## FAILED

Mindestens ein MUSS-Kriterium wurde getestet und nicht bestanden.

## BLOCKED

Ein notwendiger Test kann nicht sicher oder reproduzierbar durchgeführt werden.

BLOCKED darf nicht stillschweigend in PASSED umgewandelt werden.

---

# 3. PRIORITÄTEN

## P0 – RELEASE-KRITISCH

Ohne diese Funktionen kein V1.0 Release.

Beispiele:

* App startet
* Discovery
* Logging
* State Machine
* Execution Security
* Backup/Recovery
* Cleanup Safety
* Repair Safety
* Installer
* Uninstaller
* Security
* Regression
* Datenschutz

## P1 – V1.0 WICHTIG

Muss Bestandteil der ersten vollständigen Produktversion sein.

Beispiele:

* Storage Analyzer
* Windows Update
* Software Management
* Autostart
* Services
* Task Scheduler
* Event Analyzer
* Network Diagnostics
* Reports

## P2 – ERWEITERUNG

Kann nach V1.0 folgen, darf aber nicht als V1.0 fertig dargestellt werden.

Beispiele:

* umfangreiche Performance-Historie
* zusätzliche Hardware-Sensoren
* erweiterte Automatisierung
* erweiterte AI-Funktionen

## P3 – OPTIONAL

Experimentell oder langfristig.

Beispiele:

* zusätzliche externe Informationsquellen
* optionale Cloud-Integrationen
* zusätzliche Diagnosemodule

---

# 4. TESTKLASSEN

Jeder Test erhält mindestens eine Klasse.

```text
F = Functional
S = Safety
R = Recovery
E = Error Handling
P = Performance
U = UI
I = Integration
A = Accessibility
L = Localization
O = Offline
SEC = Security
INS = Installer
REG = Regression
```

---

# 5. TESTNACHWEIS

Ein bestandener Test muss nachvollziehbar sein.

Akzeptierte Nachweise:

* Testprotokoll
* Screenshot
* Logdatei
* JSON-Testreport
* Exit Code
* Vorher/Nachher-Wert
* Hash
* Installer-Log
* Datenbankeintrag
* VM-Testprotokoll
* automatisierter Testreport

Ein Test ohne Nachweis gilt als:

```text
NOT VERIFIED
```

---

# 6. MODUL M00 – CORE / APPLICATION FOUNDATION

**Priorität:** P0

## Zweck

Grundlage der gesamten Anwendung.

## Muss-Kriterien

### M00-F-001

Die Anwendung startet ohne unbehandelten Ausnahmefehler.

### M00-F-002

Die Hauptnavigation wird korrekt geladen.

### M00-F-003

Die Anwendung kann sauber beendet werden.

### M00-F-004

Konfiguration wird geladen.

### M00-F-005

Konfiguration wird sicher gespeichert.

### M00-E-001

Beschädigte Konfiguration darf keinen unkontrollierten Absturz verursachen.

### M00-E-002

Fehlende Konfiguration muss mit Standardwerten behandelt werden.

### M00-L-001

Deutsch funktioniert vollständig.

### M00-L-002

Englisch funktioniert vollständig.

### M00-L-003

Japanisch funktioniert vollständig.

### M00-L-004

Russisch funktioniert vollständig.

### M00-O-001

Grundfunktionen starten ohne Internetverbindung.

## Abnahme

```text
ALLE M00 MUST TESTS = PASS
```

---

# 7. MODUL M01 – DASHBOARD

**Priorität:** P0

## Muss-Kriterien

### M01-F-001

Windows-Version wird tatsächlich aus dem System gelesen.

### M01-F-002

Buildnummer wird tatsächlich aus dem System gelesen.

### M01-F-003

CPU wird korrekt erkannt.

### M01-F-004

RAM wird korrekt erkannt.

### M01-F-005

GPU wird korrekt erkannt, sofern Windows sie bereitstellt.

### M01-F-006

Laufwerke werden korrekt erkannt.

### M01-F-007

Freier Speicher wird tatsächlich gemessen.

### M01-F-008

Defenderstatus wird tatsächlich abgefragt.

### M01-F-009

Firewallstatus wird tatsächlich abgefragt.

### M01-F-010

Updatezustand wird tatsächlich abgefragt.

### M01-S-001

Keine Dashboardwerte dürfen erfunden oder simuliert werden.

### M01-E-001

Nicht verfügbare Werte werden als:

```text
Nicht verfügbar
```

angezeigt.

### M01-U-001

Statusfarben sind verständlich.

### M01-U-002

Statusanzeigen enthalten eine Erklärung.

### M01-U-003

Keine künstliche Gesamtbewertung ohne technische Grundlage.

---

# 8. MODUL M02 – SYSTEM DISCOVERY

**Priorität:** P0

## Muss-Kriterien

Erkennung:

* CPU
* RAM
* GPU
* Laufwerke
* Mainboard
* BIOS/UEFI
* TPM
* Secure Boot
* Windows
* Netzwerkadapter
* Defender
* Firewall
* BitLocker
* Restore Points

### M02-S-001

Discovery verändert keinerlei Systemzustand.

### M02-S-002

Discovery führt keine Reparatur aus.

### M02-S-003

Discovery führt keine Löschung aus.

### M02-E-001

Nicht erreichbare Datenquellen werden sauber protokolliert.

### M02-O-001

Grundinventur funktioniert offline.

---

# 9. MODUL M03 – DIAGNOSTIC ENGINE

**Priorität:** P0

## Muss-Kriterien

Diagnosen müssen auf tatsächlichen Daten beruhen.

Jeder Finding-Eintrag enthält:

```text
ID
CATEGORY
SEVERITY
SOURCE
EVIDENCE
DESCRIPTION
RECOMMENDATION
RISK
```

Keine Diagnose ohne Evidenz.

### M03-S-001

Diagnostic Engine führt standardmäßig keine Änderungen durch.

### M03-S-002

Unsichere Diagnose:

```text
UNKNOWN
```

statt erfundener Ursache.

### M03-E-001

Fehler einzelner Diagnosequellen dürfen die Gesamtanalyse nicht zerstören.

---

# 10. MODUL M04 – CLEANUP ENGINE

**Priorität:** P0

## Sicherheitsregel

Cleanup darf niemals direkt aus einer Analyse heraus löschen.

Verbindlicher Ablauf:

```text
SCAN
↓
CLASSIFY
↓
CALCULATE
↓
DISPLAY
↓
APPROVAL
↓
BACKUP
↓
EXECUTE
↓
VALIDATE
↓
REPORT
```

## Muss-Kriterien

### M04-F-001

Temporäre Dateien werden erkannt.

### M04-F-002

Cleanup-Kandidaten enthalten Pfad und Größe.

### M04-F-003

Kategorien sind einzeln auswählbar.

### M04-S-001

Persönliche Ordner sind standardmäßig ausgeschlossen.

### M04-S-002

Desktop wird nicht automatisch gelöscht.

### M04-S-003

Dokumente werden nicht automatisch gelöscht.

### M04-S-004

Bilder werden nicht automatisch gelöscht.

### M04-S-005

Videos werden nicht automatisch gelöscht.

### M04-S-006

Downloads benötigen explizite Freigabe.

### M04-S-007

Unbekannte Dateien werden nicht automatisch gelöscht.

### M04-S-008

Systemkritische Dateien werden geschützt.

### M04-R-001

Reversible Cleanup-Aktionen müssen wiederherstellbar sein, sofern technisch möglich.

### M04-E-001

Gesperrte Dateien erzeugen keinen Gesamtfehler.

### M04-E-002

Nicht löschbare Dateien werden protokolliert.

### M04-F-004

Nach Cleanup wird tatsächlich freier Speicher neu gemessen.

### M04-F-005

Der Bericht enthält tatsächlich entfernte Daten.

---

# 11. MODUL M05 – STORAGE ANALYZER

**Priorität:** P1

## Muss-Kriterien

Anzeige:

* Laufwerk
* Ordner
* Datei
* Größe
* Alter
* Besitzer
* System/User
* Pfad

### M05-F-001

Größte Ordner werden korrekt ermittelt.

### M05-F-002

Größte Dateien werden korrekt ermittelt.

### M05-F-003

Dateien können nach Alter gefiltert werden.

### M05-F-004

Dateien können nach Größe gefiltert werden.

### M05-F-005

Doppelte Dateien können erkannt werden.

### M05-S-001

Analyse löscht keine Dateien.

### M05-S-002

Große Dateien werden nicht automatisch als unnötig klassifiziert.

---

# 12. MODUL M06 – WINDOWS UPDATE

**Priorität:** P0

## Muss-Kriterien

### M06-F-001

Update-Suche funktioniert.

### M06-F-002

Updates werden mit Name, KB und Kategorie angezeigt, soweit verfügbar.

### M06-F-003

Installationsstatus wird angezeigt.

### M06-F-004

Neustartbedarf wird angezeigt.

### M06-S-001

Updates werden nicht ohne Freigabe installiert.

### M06-F-005

Ein einzelnes Update kann ausgewählt werden.

### M06-F-006

Mehrere ausgewählte Updates können installiert werden.

### M06-E-001

Updatefehler werden mit Fehlerstatus protokolliert.

### M06-F-007

Nach Installation wird der Status erneut abgefragt.

### M06-F-008

Ein fehlgeschlagenes Update wird nicht als erfolgreich angezeigt.

---

# 13. MODUL M07 – SOFTWARE / WINGET

**Priorität:** P1

## Muss-Kriterien

### M07-F-001

Installierte Anwendungen werden erkannt.

### M07-F-002

Versionen werden angezeigt.

### M07-F-003

Verfügbare Updates werden erkannt.

### M07-F-004

Einzelupdates sind möglich.

### M07-F-005

Mehrfachauswahl ist möglich.

### M07-F-006

Exit Codes werden ausgewertet.

### M07-F-007

Version wird nach Update erneut geprüft.

### M07-S-001

Kein blindes `upgrade --all`.

### M07-E-001

WinGet fehlt:

```text
UNAVAILABLE
```

nicht:

```text
SUCCESS
```

---

# 14. MODUL M08 – SOFTWAREMANAGER

**Priorität:** P1

Muss:

* Programme anzeigen
* suchen
* sortieren
* Details anzeigen
* deinstallieren
* Status validieren

### M08-S-001

Keine Deinstallation ohne Benutzerfreigabe.

### M08-F-001

Nach Deinstallation wird geprüft, ob die Anwendung tatsächlich entfernt wurde.

---

# 15. MODUL M09 – AUTOSTART

**Priorität:** P1

Erkennen:

* Run
* RunOnce
* Startup Folder
* relevante Scheduled Tasks
* relevante Services

Anzeige:

```text
Name
Pfad
Publisher
Signatur
Quelle
Status
Risiko
```

### M09-S-001

Standardaktion ist Deaktivierung.

### M09-S-002

Keine automatische Löschung.

### M09-R-001

Deaktivierung muss rückgängig gemacht werden können.

### M09-F-001

Nach Deaktivierung wird Status erneut geprüft.

---

# 16. MODUL M10 – SERVICES

**Priorität:** P1

Anzeige:

* Service Name
* Display Name
* Status
* Start Type
* Publisher
* Path
* Dependencies

### M10-S-001

Unbekannte Dienste werden nicht automatisch deaktiviert.

### M10-F-001

Start funktioniert.

### M10-F-002

Stop funktioniert, sofern zulässig.

### M10-F-003

Starttypänderung funktioniert, sofern zulässig.

### M10-R-001

Vorheriger Zustand wird gespeichert.

---

# 17. MODUL M11 – TASK SCHEDULER

**Priorität:** P1

### M11-F-001

Tasks werden angezeigt.

### M11-F-002

Trigger werden angezeigt.

### M11-F-003

Programm/Pfad wird angezeigt.

### M11-F-004

Status wird angezeigt.

### M11-S-001

Keine unbekannten Tasks automatisch löschen.

### M11-R-001

Deaktivierung ist rückgängig machbar.

---

# 18. MODUL M12 – REPAIR ENGINE

**Priorität:** P0

Werkzeuge:

* DISM
* SFC
* CHKDSK
* Windows-Komponentenprüfung

Ablauf:

```text
CHECK
↓
ANALYSE
↓
REPAIR ONLY IF REQUIRED
↓
VALIDATE
```

### M12-S-001

Keine blinde Reparatur.

### M12-F-001

Exit Codes werden erfasst.

### M12-F-002

Toolausgabe wird protokolliert.

### M12-F-003

Ergebnis wird nach Abschluss validiert.

### M12-E-001

Abbruch wird erkannt.

### M12-E-002

Neustartbedarf wird angezeigt.

### M12-R-001

Kritische Reparaturaktionen besitzen einen dokumentierten Recovery-Pfad.

---

# 19. MODUL M13 – WINSXS

**Priorität:** P1

### Muss

* Komponentenstore analysieren
* Größe ermitteln
* mögliche Bereinigung anzeigen
* Einsparung anzeigen
* Aktion erklären

### M13-S-001

`/ResetBase` ist keine Standardaktion.

### M13-S-002

Rollback-Auswirkungen müssen erklärt werden.

---

# 20. MODUL M14 – DRIVE OPTIMIZATION

**Priorität:** P1

### M14-F-001

SSD/HDD/NVMe wird erkannt, soweit verfügbar.

### M14-F-002

Geeignete Windows-Optimierung wird ausgewählt.

### M14-S-001

Keine pauschale HDD-Defragmentierung auf SSD.

### M14-F-003

TRIM/ReTrim wird nur verwendet, wenn passend.

### M14-F-004

Ergebnis wird validiert.

---

# 21. MODUL M15 – PERFORMANCE

**Priorität:** P1

Metriken:

* CPU
* RAM
* Disk
* GPU
* Boot
* Prozesse
* Autostart

### M15-S-001

Keine Fake-Optimierung.

### M15-S-002

Keine Fake-RAM-Reinigung.

### M15-S-003

Keine zufälligen Registry-Tweaks.

### M15-F-001

Messwerte stammen aus realen Systemdaten.

---

# 22. MODUL M16 – PROCESS MANAGER

**Priorität:** P1

Anzeige:

* Prozess
* PID
* CPU
* RAM
* Disk
* GPU
* Netzwerk
* Pfad
* Publisher
* Signatur
* Parent Process

### M16-S-001

Kritische Prozesse werden geschützt.

### M16-S-002

Prozessbeendigung benötigt Benutzerfreigabe.

### M16-F-001

Nach Beenden wird geprüft, ob der Prozess beendet wurde.

---

# 23. MODUL M17 – NETWORK DIAGNOSTICS

**Priorität:** P1

Prüfen:

* Adapter
* IP
* Gateway
* DNS
* IPv4
* IPv6
* Internet
* DNS-Auflösung
* Ping
* TCP
* Listening Ports

### M17-O-001

Lokale Netzwerkdiagnose funktioniert offline.

### M17-E-001

Keine Internetverbindung wird nicht als Programmfehler dargestellt.

---

# 24. MODUL M18 – SECURITY CENTER

**Priorität:** P0

### Muss

* Defender Status
* Echtzeitschutz
* Firewall
* Signaturen
* Scanstatus

### M18-S-001

Keine automatische Deaktivierung von Defender.

### M18-S-002

Keine automatische Deaktivierung der Firewall.

### M18-F-001

Quick Scan kann gestartet werden.

### M18-F-002

Full Scan kann gestartet werden, sofern verfügbar.

### M18-F-003

Status nach Scan wird geprüft.

---

# 25. MODUL M19 – EVENT LOG ANALYZER

**Priorität:** P1

Logs:

* System
* Application
* Windows Update
* Defender
* Disk
* WHEA
* Kernel

### M19-F-001

Events können gefiltert werden.

### M19-F-002

Events können gruppiert werden.

### M19-F-003

Event ID wird korrekt angezeigt.

### M19-F-004

Zeitpunkt wird korrekt angezeigt.

### M19-S-001

Keine Ursache ohne ausreichende Evidenz behaupten.

---

# 26. MODUL M20 – CRASH ANALYZER

**Priorität:** P1

Erkennen:

* BugChecks
* Minidumps
* WHEA
* Application Crashes

### M20-S-001

Keine Ursache behaupten, wenn die Datenlage nicht ausreicht.

Standard:

```text
Ursache nicht eindeutig bestimmbar.
```

### M20-F-001

Vorhandene Dumps werden erkannt.

### M20-F-002

Crashzeitpunkt wird angezeigt.

---

# 27. MODUL M21 – HARDWARE HEALTH

**Priorität:** P1

Nur echte Messwerte.

### M21-S-001

Keine erfundenen Temperaturwerte.

### M21-S-002

Keine erfundenen SMART-Werte.

### M21-F-001

Nicht verfügbare Sensoren werden als nicht verfügbar angezeigt.

---

# 28. MODUL M22 – RESTORE POINTS

**Priorität:** P0

### M22-F-001

Restore Point kann erstellt werden, sofern Windows dies zulässt.

### M22-F-002

Erstellung wird validiert.

### M22-E-001

Fehler wird verständlich angezeigt.

### M22-S-001

Vorhandene Restore Points werden nicht ungefragt gelöscht.

---

# 29. MODUL M23 – BACKUP ENGINE

**Priorität:** P0

Jedes Backup:

```text
UNIQUE ID
TIMESTAMP
TYPE
TARGET
SOURCE
SIZE
HASH, sofern sinnvoll
STATUS
```

### M23-F-001

Backup wird tatsächlich erstellt.

### M23-F-002

Backup wird auf Lesbarkeit geprüft.

### M23-R-001

Backup kann für Recovery verwendet werden.

### M23-S-001

Ungültige Backups dürfen nicht als gültig markiert werden.

---

# 30. MODUL M24 – ROLLBACK ENGINE

**Priorität:** P0

Jede Aktion klassifiziert:

```text
FULLY_REVERSIBLE
PARTIALLY_REVERSIBLE
NOT_REVERSIBLE
```

### M24-F-001

Rollbackstatus wird gespeichert.

### M24-R-001

Rollback wird tatsächlich ausgeführt.

### M24-R-002

Nach Rollback wird Zustand erneut geprüft.

### M24-S-001

Nicht reversible Aktionen werden vor Ausführung gekennzeichnet.

---

# 31. MODUL M25 – CHANGE JOURNAL

**Priorität:** P0

Jede Systemänderung benötigt:

```text
ID
TIME
ACTION
TARGET
BEFORE
AFTER
BACKUP
APPROVAL
RESULT
ERROR
ROLLBACK
```

### M25-F-001

Änderung erscheint im Journal.

### M25-F-002

Zeitpunkt ist vorhanden.

### M25-F-003

Ergebnis ist vorhanden.

### M25-S-001

Keine Passwörter.

### M25-S-002

Keine unnötigen privaten Inhalte.

---

# 32. MODUL M26 – MAINTENANCE PLANS

**Priorität:** P1

Muss:

* erstellen
* bearbeiten
* aktivieren
* deaktivieren
* löschen
* anzeigen

### M26-S-001

Automatische Änderungen benötigen explizite Aktivierung.

### M26-S-002

Automatischer Neustart benötigt explizite Aktivierung.

### M26-F-001

Zeitplan wird tatsächlich gespeichert.

---

# 33. MODUL M27 – ONE-CLICK MAINTENANCE

**Priorität:** P0

Verbindlicher Ablauf:

```text
DISCOVERY
↓
DIAGNOSTIC
↓
PLAN
↓
APPROVAL
↓
BACKUP
↓
EXECUTION
↓
VALIDATION
↓
REPORT
```

### M27-S-001

Keine versteckten Aktionen.

### M27-S-002

Benutzer kann Aktionen abwählen.

### M27-S-003

Risiken werden angezeigt.

### M27-R-001

Unterbrechung erzeugt Recovery-Zustand.

### M27-F-001

Vorher/Nachher wird gemessen.

---

# 34. MODUL M28 – REPORTING

**Priorität:** P1

Formate:

* HTML
* PDF
* TXT
* JSON

### M28-F-001

Bericht wird erzeugt.

### M28-F-002

Bericht enthält reale Systemdaten.

### M28-F-003

Bericht enthält ausgeführte Aktionen.

### M28-F-004

Bericht enthält Fehler.

### M28-F-005

Bericht enthält übersprungene Aktionen.

### M28-S-001

Keine unnötigen privaten Daten.

---

# 35. MODUL M29 – OFFLINE ENGINE

**Priorität:** P0

Offline funktionieren müssen:

* Dashboard-Grunddaten
* Discovery
* Diagnostics
* Storage
* Cleanup
* Event Logs
* Autostart
* Services
* Tasks
* Reports
* lokale Security-Diagnose

### M29-O-001

Internet wird vollständig getrennt.

### M29-O-002

Anwendung startet.

### M29-O-003

Lokale Module funktionieren.

### M29-O-004

Onlinefunktionen werden korrekt als nicht verfügbar angezeigt.

Keine Fake-Ergebnisse.

---

# 36. MODUL M30 – AI

**Priorität:** P2

KI ist niemals Voraussetzung für die Kernanwendung.

### M30-F-001

Strukturierte Diagnoseinformationen können analysiert werden.

### M30-F-002

Empfehlungen enthalten Begründungen.

### M30-S-001

KI kann keine direkte Systemaktion ausführen.

### M30-S-002

KI erhält keine unnötigen persönlichen Dateien.

### M30-S-003

KI kann keine beliebigen Shell-Befehle ausführen.

### M30-O-001

App funktioniert vollständig ohne KI.

---

# 37. MODUL M31 – ADMIN WORKER

**Priorität:** P0

Architektur:

```text
UI
↓
CORE
↓
ACTION REGISTRY
↓
ADMIN WORKER
↓
WINDOWS
```

### M31-S-001

UI besitzt keine freie privilegierte Shell.

### M31-S-002

Worker akzeptiert nur registrierte Aktionen.

### M31-S-003

UAC wird nur bei Bedarf verwendet.

### M31-S-004

UAC-Abbruch wird korrekt behandelt.

### M31-E-001

Worker-Absturz führt nicht zu einem falschen SUCCESS.

---

# 38. MODUL M32 – ACTION REGISTRY

**Priorität:** P0

Jede Aktion benötigt:

```text
Action ID
Description
Risk
RequiresAdmin
Arguments
Validation
Rollback
Timeout
Allowed
```

Beispiel:

```text
Cleanup.WindowsUpdateCache
```

### M32-SEC-001

Unregistrierte Aktion wird abgelehnt.

### M32-SEC-002

Ungültige Argumente werden abgelehnt.

### M32-SEC-003

Path Traversal wird verhindert.

### M32-SEC-004

Command Injection wird verhindert.

---

# 39. MODUL M33 – COMMAND EXECUTION

**Priorität:** P0

### M33-SEC-001

Keine unkontrollierten Benutzerstrings als Shell-Befehl.

### M33-SEC-002

Argumente werden validiert.

### M33-SEC-003

Exit Code wird geprüft.

### M33-E-001

Timeout wird erkannt.

### M33-E-002

Prozessabbruch wird erkannt.

### M33-F-001

Ausgabe wird gespeichert.

### M33-S-001

Falscher Exit Code darf nicht als SUCCESS erscheinen.

---

# 40. MODUL M34 – STATE MACHINE

**Priorität:** P0

Erlaubte Zustände:

```text
INITIALIZING
DISCOVERY
DIAGNOSTIC
PLAN_GENERATED
AWAITING_APPROVAL
BACKUP
EXECUTING
VALIDATING
SUCCESS
ERROR
ROLLBACK
RECOVERING
BLOCKED
CANCELLED
```

### M34-S-001

Keine Aktion darf die State Machine umgehen.

### M34-F-001

Jeder Zustand wird gespeichert.

### M34-F-002

Transitionen werden protokolliert.

### M34-R-001

Unterbrochene Jobs können wiedererkannt werden.

---

# 41. MODUL M35 – RECOVERY ENGINE

**Priorität:** P0

Nach Programmabsturz:

```text
RECOVERY AVAILABLE
```

anzeigen.

### M35-F-001

Letzte Aktion wird erkannt.

### M35-F-002

Backupstatus wird erkannt.

### M35-F-003

Rollbackmöglichkeit wird erkannt.

### M35-R-001

Recovery wird getestet.

### M35-S-001

Keine riskante automatische Recovery ohne Freigabe.

---

# 42. MODUL M36 – CONFIGURATION

**Priorität:** P0

### M36-F-001

Konfiguration wird geladen.

### M36-F-002

Schema-Version wird geprüft.

### M36-F-003

Migration funktioniert.

### M36-E-001

Fehlgeschlagene Migration erzeugt keinen Datenverlust.

### M36-S-001

Ungültige Konfiguration wird nicht blind übernommen.

---

# 43. MODUL M37 – LOCALIZATION

**Priorität:** P1

Sprachen:

```text
de-DE
en-US
ja-JP
ru-RU
```

### M37-L-001

Keine fehlenden Ressourcen in Deutsch.

### M37-L-002

Keine fehlenden Ressourcen in Englisch.

### M37-L-003

Keine fehlenden Ressourcen in Japanisch.

### M37-L-004

Keine fehlenden Ressourcen in Russisch.

### M37-L-005

Datum/Zeit/Zahlen werden lokalisiert.

### M37-L-006

Fallback ist definiert.

---

# 44. MODUL M38 – LOGGING

**Priorität:** P0

Level:

```text
TRACE
DEBUG
INFO
WARNING
ERROR
CRITICAL
```

### M38-F-001

Fehler werden protokolliert.

### M38-F-002

Systemänderungen werden protokolliert.

### M38-F-003

Exit Codes werden protokolliert.

### M38-S-001

Keine Passwörter.

### M38-S-002

Keine Dokumentinhalte.

### M38-S-003

Keine unnötigen personenbezogenen Daten.

---

# 45. MODUL M39 – PRIVACY

**Priorität:** P0

### M39-S-001

Keine Telemetrie standardmäßig.

### M39-S-002

Keine Cloudpflicht.

### M39-S-003

Keine persönlichen Dateien übertragen.

### M39-S-004

Keine Dokumentinhalte übertragen.

### M39-S-005

Diagnoseexport wird auf sensible Daten geprüft.

---

# 46. MODUL M40 – INSTALLER

**Priorität:** P0

Installer:

```text
WindowsMaintenanceCenter-Setup-x64.exe
```

### M40-INS-001

Installer startet.

### M40-INS-002

Installationspfad kann ausgewählt werden.

### M40-INS-003

Installation auf Nicht-C-Laufwerk funktioniert.

### M40-INS-004

Startmenüeintrag wird erstellt.

### M40-INS-005

Programm startet nach Installation.

### M40-INS-006

Version wird korrekt angezeigt.

### M40-INS-007

Uninstaller wird registriert.

### M40-SEC-001

Installer enthält keine unerwünschte Drittsoftware.

### M40-SEC-002

Installer verändert keine nicht dokumentierten Systembereiche.

---

# 47. MODUL M41 – UNINSTALLER

**Priorität:** P0

### M41-INS-001

Deinstallation über Windows Settings funktioniert.

### M41-INS-002

Programmdateien werden entfernt.

### M41-INS-003

Verknüpfungen werden entfernt.

### M41-INS-004

Hintergrundkomponenten werden entfernt.

### M41-S-001

Backups werden nicht ungefragt gelöscht.

### M41-S-002

Benutzerdaten werden nur gemäß Auswahl behandelt.

---

# 48. MODUL M42 – AUTO UPDATE

**Priorität:** P1

Ablauf:

```text
CHECK
↓
DOWNLOAD
↓
SIGNATURE VALIDATION
↓
HASH VALIDATION
↓
BACKUP
↓
INSTALL
↓
START TEST
↓
SUCCESS
```

### M42-SEC-001

Ungültige Signatur wird abgelehnt.

### M42-SEC-002

Falscher Hash wird abgelehnt.

### M42-SEC-003

Beschädigtes Paket wird abgelehnt.

### M42-R-001

Fehlgeschlagenes Update erzeugt Recovery.

### M42-F-001

Neue Version wird korrekt erkannt.

---

# 49. MODUL M43 – RELEASE BUILD

**Priorität:** P0

Release erzeugt:

```text
WindowsMaintenanceCenter-Setup-x64.exe
WindowsMaintenanceCenter-Checksums.txt
WindowsMaintenanceCenter-ReleaseNotes.txt
```

Optional:

```text
WindowsMaintenanceCenter.zip
```

### M43-F-001

Release-Build ist reproduzierbar innerhalb der definierten Buildumgebung.

### M43-SEC-001

Binaries werden geprüft.

### M43-SEC-002

Hashes werden erzeugt.

### M43-SEC-003

Signaturstatus wird geprüft.

---

# 50. MODUL M44 – SECURITY TESTING

**Priorität:** P0

Pflichtprüfungen:

```text
Command Injection
Path Traversal
Argument Injection
Privilege Escalation
Tampered Config
Tampered Update
Invalid Signature
Corrupt Backup
No Admin
UAC Cancel
Process Abort
Log Manipulation
Database Corruption
Report Injection
```

## Release-Regel

Ein kritischer Security-Test:

```text
FAIL
```

bedeutet:

```text
RELEASE BLOCKED
```

---

# 51. MODUL M45 – REGRESSION

**Priorität:** P0

Nach jeder relevanten Änderung:

```text
BUILD
↓
UNIT
↓
INTEGRATION
↓
SAFETY
↓
REGRESSION
```

Mindestens prüfen:

* Start
* Dashboard
* Discovery
* Cleanup
* Update
* Repair
* Backup
* Rollback
* Security
* Installer
* Uninstaller
* Localization

---

# 52. MODUL M46 – VM ACCEPTANCE

**Priorität:** P0

Testumgebung:

```text
Windows 11 x64
Clean VM
UEFI
Secure Boot
TPM
```

Test:

```text
CLEAN INSTALL
↓
START
↓
DISCOVERY
↓
DIAGNOSTIC
↓
MAINTENANCE
↓
UPDATE
↓
RESTART
↓
START
↓
RECOVERY TEST
↓
UNINSTALL
```

Host darf erst nach bestandenem VM-Test verwendet werden.

---

# 53. MODUL M47 – ACCESSIBILITY

**Priorität:** P1

Prüfen:

* Tastaturnavigation
* Fokus
* Kontrast
* Skalierung
* verständliche Warnungen
* Statusinformationen
* keine ausschließlich farbliche Kennzeichnung

---

# 54. MODUL M48 – DOCUMENTATION

**Priorität:** P1

Pflichtdateien:

```text
README.md
ARCHITECTURE.md
SECURITY.md
INSTALLATION.md
USER_GUIDE.md
DEVELOPER_GUIDE.md
TESTING.md
RELEASE.md
CHANGELOG.md
PRIVACY.md
RECOVERY.md
```

Dokumentation darf keine Funktionen als vorhanden beschreiben, die nicht implementiert sind.

---

# 55. RELEASE-BLOCKER

Folgende Fehler blockieren V1.0 zwingend:

## Kritische Datenverluste

* persönliche Dateien ungefragt gelöscht
* Backup unbrauchbar
* Rollback funktioniert nicht
* falsche Dateien verändert

## Security

* Command Injection
* beliebige privilegierte Shell
* unkontrollierte Admin-Aktion
* manipuliertes Update wird installiert
* Signaturprüfung kann umgangen werden
* Path Traversal

## Functional

* Anwendung startet nicht
* Kernmodule stürzen reproduzierbar ab
* Installer funktioniert nicht
* Uninstaller funktioniert nicht
* State Machine kann umgangen werden

## Safety

* Aktion wird ohne Freigabe ausgeführt
* falscher SUCCESS-Status
* Security Features werden ungefragt deaktiviert
* unbekannte Dienste werden automatisch deaktiviert
* unbekannte Tasks werden gelöscht

## Privacy

* Telemetrie ohne Freigabe
* private Inhalte werden ungefragt übertragen
* Logs enthalten Passwörter oder sensible Inhalte

---

# 56. RELEASE-GATE 1 – BUILD

MUSS:

```text
Build = PASS
Warnings = reviewed
Errors = 0
```

Keine Release-Erstellung bei Buildfehlern.

---

# 57. RELEASE-GATE 2 – UNIT

Alle P0-Kernkomponenten:

```text
PASS
```

---

# 58. RELEASE-GATE 3 – INTEGRATION

Geprüft:

```text
UI
CORE
DISCOVERY
EXECUTION
BACKUP
RECOVERY
REPORTING
```

Alle P0:

```text
PASS
```

---

# 59. RELEASE-GATE 4 – SECURITY

Alle SEC-MUST-Tests:

```text
PASS
```

Ein einziger kritischer FAIL:

```text
RELEASE BLOCKED
```

---

# 60. RELEASE-GATE 5 – SAFETY

Geprüft:

```text
Approval
Backup
Rollback
State Machine
Fail Closed
Admin Boundary
```

Alles:

```text
PASS
```

---

# 61. RELEASE-GATE 6 – OFFLINE

Internet vollständig deaktivieren.

Prüfen:

```text
START
DISCOVERY
DIAGNOSTICS
STORAGE
CLEANUP
EVENTS
REPORTING
SETTINGS
```

Keine Fake-Ergebnisse.

---

# 62. RELEASE-GATE 7 – INSTALLER

Test:

```text
CLEAN VM
↓
INSTALL
↓
START
↓
USE
↓
RESTART
↓
UPDATE
↓
USE
↓
UNINSTALL
```

Alles:

```text
PASS
```

---

# 63. RELEASE-GATE 8 – LOCALIZATION

Alle vier Sprachen:

```text
Deutsch
English
日本語
Русский
```

Pflichtbereiche:

* Dashboard
* Navigation
* Dialoge
* Fehler
* Warnungen
* Einstellungen
* Installer
* wichtige Systemmeldungen

Keine sichtbaren Resource Keys.

---

# 64. RELEASE-GATE 9 – RECOVERY

Simulation:

### Test A

Prozess während Wartung abbrechen.

Erwartet:

```text
RECOVERY AVAILABLE
```

### Test B

Anwendung während Wartung beenden.

Erwartet:

```text
INCOMPLETE JOB DETECTED
```

### Test C

Backup beschädigen.

Erwartet:

```text
BACKUP INVALID
```

Keine Recovery mit ungültigem Backup.

---

# 65. RELEASE-GATE 10 – FINAL VM

Die finale Version wird in einer sauberen VM installiert.

Keine Entwicklerwerkzeuge.

Keine Entwicklungsdateien.

Keine lokalen Testartefakte.

Keine Debug-Konfiguration.

Danach:

```text
INSTALL
↓
DISCOVERY
↓
DIAGNOSTICS
↓
SAFE MAINTENANCE
↓
UPDATE
↓
RESTART
↓
REPORT
↓
UNINSTALL
```

---

# 66. DEFINITION OF DONE

Ein Modul gilt nur dann als:

```text
DONE
```

wenn:

```text
CODE
PASS
BUILD
PASS
FUNCTION
PASS
SAFETY
PASS
ERROR HANDLING
PASS
LOGGING
PASS
SECURITY
PASS
RECOVERY
PASS
UI
PASS
LOCALIZATION
PASS
DOCUMENTATION
PASS
```

erfüllt ist.

---

# 67. DEFINITION OF RELEASE READY

Die Anwendung ist:

```text
RELEASE_READY
```

wenn:

1. alle P0-Module PASSED sind
2. alle P0-MUST-Tests PASSED sind
3. alle kritischen Security-Tests PASSED sind
4. Installer PASSED ist
5. Uninstaller PASSED ist
6. Recovery PASSED ist
7. Offline-Test PASSED ist
8. Regression PASSED ist
9. Datenschutzprüfung PASSED ist
10. keine kritischen offenen Fehler vorhanden sind

---

# 68. DEFINITION OF BLOCKED

Ein Modul wird BLOCKED, wenn:

* notwendige Windows-Komponente fehlt
* benötigte Testumgebung nicht verfügbar ist
* sicherer Test nicht möglich ist
* Ergebnis nicht zuverlässig validierbar ist
* Abhängigkeit nicht reproduzierbar funktioniert
* Sicherheitszustand unklar ist

BLOCKED bedeutet:

```text
NICHT RELEASE-FERTIG
```

---

# 69. FEHLERKLASSEN

## CRITICAL

Datenverlust, Security Break, Privilege Escalation, falsche Systemänderung.

Release:

```text
BLOCKED
```

## HIGH

Wichtige Kernfunktion funktioniert nicht.

Release normalerweise:

```text
BLOCKED
```

## MEDIUM

Funktion eingeschränkt, Workaround vorhanden.

Kann nach dokumentierter Bewertung offen bleiben.

## LOW

Kosmetischer oder nicht kritischer Fehler.

Kann dokumentiert werden.

---

# 70. TESTREPORT

Jeder Build soll einen Report erzeugen:

```text
Build ID
Version
Commit
Date
Environment
OS
Architecture
Test Count
Passed
Failed
Blocked
Skipped
Critical Findings
Release Status
```

Beispiel:

```text
VERSION: 1.0.0
BUILD: 1842

TESTS: 428
PASSED: 428
FAILED: 0
BLOCKED: 0

SECURITY: PASS
SAFETY: PASS
RECOVERY: PASS
INSTALLER: PASS
OFFLINE: PASS
REGRESSION: PASS

RELEASE:
READY
```

Solche Werte dürfen nur aus tatsächlich ausgeführten Tests stammen.

---

# 71. EVIDENCE DIRECTORY

Im Entwicklungsprojekt:

```text
test-results/
├── unit/
├── integration/
├── safety/
├── security/
├── recovery/
├── installer/
├── localization/
├── offline/
├── regression/
└── release/
```

Jeder Testreport erhält:

```text
timestamp
version
build
environment
result
```

---

# 72. TESTDATEN

Tests dürfen niemals echte persönliche Daten benötigen.

Verwende:

```text
synthetic files
synthetic registry entries
test services
test tasks
test applications
test configurations
```

Wo Windows-Systemtests zwingend echte Komponenten benötigen:

```text
read-only first
```

und nur kontrollierte Änderungen.

---

# 73. VM-TESTMATRIX

Mindestens:

| Umgebung                         | Zweck             |
| -------------------------------- | ----------------- |
| Windows 11 x64 Clean             | Baseline          |
| Windows 11 x64 Updated           | aktuelle Umgebung |
| Windows 11 x64 Offline           | Offline           |
| Windows 11 x64 Standard User     | Berechtigungen    |
| Windows 11 x64 Administrator     | Elevated Tests    |
| Windows 11 x64 Recovery Scenario | Recovery          |

---

# 74. INSTALLATIONSMATRIX

Testinstallation auf:

```text
C:
D:
E:
F:
```

soweit Testsystem vorhanden.

Zusätzlich prüfen:

* bestehende Version
* Neuinstallation
* Upgrade
* Reparatur
* Deinstallation
* erneute Installation

---

# 75. BACKUP-TESTMATRIX

Test:

```text
Backup erstellen
↓
Backup lesen
↓
Backup validieren
↓
Original ändern
↓
Restore
↓
Zustand vergleichen
```

Erwartung:

```text
RESTORE VERIFIED
```

---

# 76. APPROVAL-TESTMATRIX

Jede kritische Aktion wird in vier Situationen getestet:

```text
APPROVE
DECLINE
CANCEL
TIMEOUT
```

Erwartung:

### APPROVE

Aktion darf ausgeführt werden.

### DECLINE

Keine Änderung.

### CANCEL

Keine neue Aktion.

### TIMEOUT

Sicherer Abbruch.

---

# 77. ADMIN-TESTMATRIX

Test:

```text
Standard User
↓
Aktion ohne Admin
```

Erwartet:

```text
läuft
```

Bei Admin-Aktion:

```text
UAC
↓
APPROVE
↓
ACTION
```

Bei:

```text
UAC CANCEL
```

Erwartet:

```text
NO CHANGE
```

---

# 78. COMMAND-SECURITY-TEST

Folgende Eingaben müssen sicher behandelt werden:

```text
"
'
;
&
|
>
<
$
()
{}
..
..\ 
%PATH%
```

Keine Eingabe darf zu einer nicht vorgesehenen zusätzlichen Aktion führen.

---

# 79. PATH-SECURITY

Pfadprüfungen müssen verhindern:

```text
..\..\Windows
..\..\Users
UNC paths
unexpected network shares
device paths
```

sofern diese nicht ausdrücklich Bestandteil der jeweiligen Aktion sind.

---

# 80. UPDATE-SECURITY

Testfälle:

```text
correct package
wrong hash
wrong signature
corrupt package
partial download
interrupted download
wrong version
downgrade attempt
```

Nur gültige Pakete dürfen installiert werden.

---

# 81. REPORT-SECURITY

Berichte dürfen keine:

* Passwörter
* Tokens
* API Keys
* privaten Dokumentinhalte
* unnötigen persönlichen Daten

enthalten.

---

# 82. DATABASE-SECURITY

SQLite:

* Schema-Version
* Migration
* Integritätsprüfung
* Backup
* Recovery

Test:

```text
normal DB
corrupt DB
missing DB
locked DB
old DB schema
```

---

# 83. UI-TESTMATRIX

Jede Hauptseite:

```text
START
LOAD
NAVIGATE
REFRESH
ERROR
EMPTY STATE
OFFLINE
CANCEL
CLOSE
```

prüfen.

---

# 84. LEERE ZUSTÄNDE

Jedes Modul muss einen definierten Empty State besitzen.

Beispiele:

```text
Keine Updates gefunden.

Keine kritischen Ereignisse gefunden.

Keine bereinigbaren Dateien gefunden.

Keine unterstützten Sensoren verfügbar.
```

Keine leeren weißen/kaputten Flächen ohne Erklärung.

---

# 85. FEHLERDARSTELLUNG

Jeder Fehler:

```text
ERROR ID
WHAT
WHY
IMPACT
ACTION
LOG
```

Beispiel:

```text
WMC-UPDATE-0042

Was:
Windows Update konnte nicht abgefragt werden.

Warum:
Der benötigte Dienst war nicht erreichbar.

Auswirkung:
Es wurde kein Update installiert.

Nächster Schritt:
Windows Update Dienst prüfen.

Status:
NO CHANGE
```

---

# 86. KEINE FALSCHEN ERFOLGSMELDUNGEN

Verboten:

```text
Reparatur erfolgreich
```

wenn lediglich:

```text
Command started
```

wurde.

SUCCESS darf nur nach:

```text
EXECUTION
+
VALIDATION
```

gesetzt werden.

---

# 87. KEINE FALSCHEN DIAGNOSEN

Verboten:

```text
Treiber X verursacht den Absturz.
```

wenn nur ein Zusammenhang beobachtet wurde.

Zulässig:

```text
Treiber X wurde im Zusammenhang mit dem Ereignis protokolliert.
Die vorliegenden Daten reichen nicht aus, um die Ursache eindeutig festzustellen.
```

---

# 88. KEINE FALSCHEN LEISTUNGSVERSPRECHEN

Verboten:

```text
PC jetzt 300 % schneller
```

oder vergleichbare unbelegte Aussagen.

Stattdessen:

```text
Bootzeit vor Wartung
Bootzeit nach Wartung
```

wenn tatsächlich gemessen.

---

# 89. AI SAFETY GATE

Die KI bekommt niemals:

```text
direct shell access
admin token
unrestricted command execution
```

Die KI kann ausschließlich:

```text
READ STRUCTURED DATA
↓
ANALYZE
↓
RECOMMEND
```

Danach:

```text
USER APPROVAL
↓
ACTION REGISTRY
```

---

# 90. AUTONOMIEGRENZE

Die Anwendung darf nicht eigenständig entscheiden:

```text
Ich lösche diese Datei.
Ich deaktiviere diesen Dienst.
Ich installiere dieses Update.
Ich ändere diese Registry.
```

ohne eine zuvor definierte und freigegebene Policy.

Automatisierung bedeutet:

```text
USER DEFINED POLICY
↓
VALIDATED PLAN
↓
EXECUTION
```

nicht:

```text
AI DECIDES
```

---

# 91. RELEASE CHECKLIST

Vor Release muss folgende Checkliste vollständig abgearbeitet werden:

```text
[ ] Build
[ ] Unit Tests
[ ] Integration Tests
[ ] Safety Tests
[ ] Security Tests
[ ] Recovery Tests
[ ] Offline Tests
[ ] Localization Tests
[ ] Accessibility Tests
[ ] Installer Tests
[ ] Upgrade Tests
[ ] Uninstaller Tests
[ ] Regression
[ ] Privacy Review
[ ] Documentation Review
[ ] VM Test
[ ] Hash
[ ] Signature
[ ] Release Notes
```

---

# 92. FINAL RELEASE DECISION

Die Release Engine darf nur einen der folgenden Zustände erzeugen:

```text
RELEASE_READY
RELEASE_BLOCKED
```

Nicht:

```text
probably ready
looks good
should work
almost done
```

---

# 93. RELEASE_READY BEDINGUNG

```text
ALL P0 = PASS
AND
ALL SECURITY MUST = PASS
AND
ALL SAFETY MUST = PASS
AND
INSTALLER = PASS
AND
UNINSTALLER = PASS
AND
RECOVERY = PASS
AND
OFFLINE = PASS
AND
REGRESSION = PASS
AND
NO CRITICAL OPEN BUG
```

Dann:

```text
RELEASE_READY
```

---

# 94. RELEASE_BLOCKED BEDINGUNG

Wenn irgendeine Bedingung nicht erfüllt ist:

```text
RELEASE_BLOCKED
```

mit:

```text
BLOCKING TEST
MODULE
REASON
EVIDENCE
REQUIRED FIX
```

---

# 95. V1.0 MINDESTUMFANG

V1.0 muss mindestens enthalten:

```text
Application Foundation
Dashboard
System Discovery
Diagnostics
Cleanup
Storage
Windows Update
Software Management
Autostart
Services
Task Scheduler
Repair
WinSxS
Drive Optimization
Performance
Process Manager
Network Diagnostics
Security
Event Logs
Crash Analysis
Hardware Health
Restore Points
Backup
Rollback
Change Journal
Maintenance Plans
One-Click Maintenance
Reporting
Offline Mode
Admin Worker
Action Registry
Command Security
State Machine
Recovery
Configuration
Localization
Logging
Privacy
Installer
Uninstaller
Release Pipeline
Security Testing
Regression
VM Acceptance
```

KI bleibt optional.

---

# 96. NICHT IMPLEMENTIERTE FUNKTIONEN

Eine nicht fertige Funktion muss sichtbar als:

```text
NOT IMPLEMENTED
```

gekennzeichnet werden.

Alternativ:

```text
PLANNED
```

Sie darf nicht als:

```text
AVAILABLE
```

erscheinen.

---

# 97. VERSIONIERUNG

Versionierung:

```text
MAJOR.MINOR.PATCH
```

Beispiel:

```text
1.0.0
```

Bugfix:

```text
1.0.1
```

Feature:

```text
1.1.0
```

Breaking Architecture:

```text
2.0.0
```

---

# 98. CHANGE CONTROL

Jede Änderung an einem P0-Modul muss prüfen:

```text
Was wurde geändert?
Welche Module sind betroffen?
Welche Tests müssen erneut laufen?
Welche Sicherheitsrisiken entstehen?
Ist Recovery betroffen?
Ist Installer betroffen?
Ist Datenbankmigration betroffen?
```

---

# 99. TRACEABILITY

Jede Anforderung erhält:

```text
Requirement ID
↓
Module ID
↓
Implementation
↓
Test ID
↓
Evidence
↓
Release Gate
```

Beispiel:

```text
REQ-CLEANUP-001
↓
M04
↓
Cleanup Engine
↓
M04-S-001
↓
Safety Report
↓
RELEASE GATE 5
```

Damit bleibt jede Anforderung bis zum Release nachvollziehbar.

---

# 100. ABSCHLUSSARCHITEKTUR

Das endgültige System folgt:

```text
                    WINDOWS
                       │
              ┌────────▼────────┐
              │ ADMIN WORKER    │
              └────────▲────────┘
                       │
                EXECUTION ENGINE
                       ▲
                       │
                ACTION REGISTRY
                       ▲
                       │
                  APPROVAL
                       ▲
                       │
                    PLAN
                       ▲
                       │
                  DIAGNOSTIC
                       ▲
                       │
                  DISCOVERY
                       ▲
                       │
                    CORE
                       ▲
                       │
                      UI
```

Parallel:

```text
CORE
 ├── State Machine
 ├── Logging
 ├── Configuration
 ├── SQLite
 ├── Recovery
 ├── Reporting
 └── Security Policy
```

---

# 101. ENDGÜLTIGE PRODUKTREGEL

Das Windows Maintenance Center soll nicht möglichst viele Systemänderungen durchführen.

Es soll möglichst viele **begründete, kontrollierte und überprüfbare Wartungsaufgaben** sicher durchführen.

Jede Aktion folgt:

```text
ERKENNEN
↓
ANALYSIEREN
↓
BEGRÜNDEN
↓
PLANEN
↓
RISIKO PRÜFEN
↓
BACKUP
↓
FREIGABE
↓
AUSFÜHREN
↓
VALIDIEREN
↓
PROTOKOLLIEREN
```

Bei Unsicherheit:

```text
BLOCKED
```

Bei fehlender Implementierung:

```text
NOT IMPLEMENTED
```

Bei Fehler:

```text
ERROR
```

Bei erfolgreicher und validierter Ausführung:

```text
SUCCESS
```

---

# 102. ABSCHLIESSENDE ABNAHME

Das Projekt ist erst dann offiziell fertig, wenn folgende Aussage technisch nachweisbar erfüllt ist:

> Das Windows Maintenance Center kann auf einer sauberen Windows-11-x64-Testumgebung installiert, gestartet, zur Systemanalyse verwendet, für kontrollierte Wartungsaktionen eingesetzt, sicher beendet, aktualisiert, wiederhergestellt und vollständig deinstalliert werden, ohne unbeabsichtigte Systemänderungen oder nicht dokumentierte Datenübertragungen zu verursachen.

Die Aussage muss durch reale Testnachweise belegt sein.

Keine Simulation.

Keine Annahme.

Keine Platzhalter.

Keine erfundenen Ergebnisse.

Keine Release-Freigabe aufgrund einer manuellen Einschätzung.

**Nur nachweisbare Ergebnisse zählen.**

---

# 103. ENDSTATUS

```text
PROJECT STATUS

NOT_STARTED
    ↓
IN_DEVELOPMENT
    ↓
READY_FOR_TEST
    ↓
TESTING
    ↓
PASSED
    ↓
RELEASE_READY
```

Fehlerpfad:

```text
TESTING
    ↓
FAILED
    ↓
FIX
    ↓
REGRESSION
    ↓
TESTING
```

Sicherheitspfad:

```text
UNCERTAIN
    ↓
BLOCKED
    ↓
ANALYSIS
    ↓
FIX
    ↓
RETEST
```

Recovery:

```text
INTERRUPTED
    ↓
RECOVERING
    ↓
VALIDATING
    ↓
SUCCESS
```

Final:

```text
RELEASE_READY
```

oder:

```text
RELEASE_BLOCKED
```

**Es gibt keinen dritten Zustand.**

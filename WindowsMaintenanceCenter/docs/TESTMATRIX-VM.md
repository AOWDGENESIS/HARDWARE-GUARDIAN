# Testmatrix der Zielumgebung (Kapitel 73–83)

Diese Matrix ist die **Arbeitsliste für die Zielmaschine**. Sie steht hier, weil die Nachweise auf
diesem Entwicklungsrechner nicht erzeugt werden können (siehe `docs/VM-CI.md` und
`docs/VM-TESTKIT.md`).

Spalte **Zustand** benutzt ausschließlich die Werte der Spezifikation:

* `NOT_STARTED` – der Test wurde auf der Zielmaschine nicht ausgeführt.
* `BLOCKED` – der Test kann auf der vorhandenen Umgebung nicht ausgeführt werden (Grund steht dabei).
* `FAILED` / `PASSED` – nur mit Nachweis; ohne Nachweis gilt `NOT VERIFIED` (Kapitel 5).

**Alle Zeilen stehen auf `NOT_STARTED`, solange keine Zielmaschine benutzt wurde.** Es gibt derzeit
keine Zeile mit `PASSED`; die Angaben aus dem CI-Lauf sind Bau- und Testnachweise für die
Kompilierung und die Unit-Suite, nicht für die Matrix hier.

## 1. Umgebungen (Kapitel 73)

| ID | Umgebung (Windows 11 x64) | Zweck | Nachweis | Zustand |
| --- | --- | --- | --- | --- |
| VM-01 | Clean (frisch installiert, Snapshot vor jedem Test) | Baseline: Installation, Erststart, Discovery | `test-results/installer/`, `test-results/integration/` | NOT_STARTED |
| VM-02 | Updated (alle Windows-Updates eingespielt) | Aktuelle Umgebung, Update-Erkennung | `test-results/integration/` | NOT_STARTED |
| VM-03 | Offline (Netzkabel gezogen, kein WLAN, kein Hotspot) | Gate 6, lokale Module | `test-results/offline/` | NOT_STARTED |
| VM-04 | Standard User (Konto ohne Administratorenrechte) | Berechtigungsgrenze, Admin-Matrix | `test-results/safety/` | NOT_STARTED |
| VM-05 | Administrator (Konto mit erhöhten Rechten) | Elevated Tests, UAC-Matrix | `test-results/safety/` | NOT_STARTED |
| VM-06 | Recovery-Szenario (Snapshot vorhanden, beschädigbar) | Backup, Rollback, Prozessabbruch | `test-results/recovery/` | NOT_STARTED |

Voraussetzungen für alle Umgebungen: UEFI, Secure Boot, TPM 2.0 (Kapitel 52). Die Skripte halten
diese Angaben im Umgebungsrecord fest; ein abweichendes System wird nicht als erfüllt gemeldet.

## 2. Installationsmatrix (Kapitel 74)

| ID | Fall | Erwartung | Nachweis | Zustand |
| --- | --- | --- | --- | --- |
| INS-01 | Neuinstallation auf `C:\` | Programm startet, Startmenü-Eintrag, Deinstallationseintrag | Inno-Setup-Log + Startprotokoll | NOT_STARTED |
| INS-02 | Installation auf weiteren Laufwerken (`D:\`, `E:\`, `F:\`), soweit vorhanden | gleiche Funktion, Programm nutzt den gewählten Pfad | Installer-Log je Laufwerk | NOT_STARTED |
| INS-03 | Upgrade über eine bestehende Version | Einstellungen und Verlauf bleiben erhalten | Vorher-/Nachher-Vergleich | NOT_STARTED |
| INS-04 | Reparaturinstallation | fehlende Dateien werden wiederhergestellt | Installer-Log | NOT_STARTED |
| INS-05 | Deinstallation | keine Reste: keine Dienst-, Task- oder Autostarteinträge, kein Datenverzeichnis ohne Hinweis | Registry-/Dateisystem-Vergleich vorher/nachher | NOT_STARTED |
| INS-06 | Erneute Installation nach der Deinstallation | vollständige Funktion wie bei INS-01 | Installer-Log | NOT_STARTED |
| INS-CI | **Installationszyklus auf der CI-Maschine** (Gate 7, Kapitel 62/71): Silent-Installation → Layout, Version und SHA-256 gegen den Bau → Start → Nutzungssonde (Datenordner + Logdatei) → Silent-Deinstallation → Restprüfung (Eintrag, Dateien, Verknüpfung weg; Daten bewusst behalten) | der Zyklus läuft durch und findet nichts; offen bleiben Reparatur, Upgrade, Neustart, weitere Laufwerke und „USE" (Kapitel 62) — der Bericht nennt sie als offene Punkte | `test-results/installer/` | NOT_STARTED |
| INS-07 | **Portables Artefakt in einem leeren Ordner**, ohne jede Beigabedatei | legt `data\` neben der Datei an; die Portabilität darf nicht von einer zweiten Datei abhängen (Befund vom 2026-09-22) | `test-results/installer/` (Schritt „portable artefact keeps its data next to itself") | NOT_STARTED |

## 3. Backup-Testmatrix (Kapitel 75)

| ID | Schritt | Erwartung | Nachweis | Zustand |
| --- | --- | --- | --- | --- |
| BAK-01 | Backup erstellen | Manifest mit Pfaden, Größen und SHA-256 entsteht | Manifest + Log | NOT_STARTED |
| BAK-02 | Backup lesen | jede Datei lesbar, Hashes stimmen | Prüfprotokoll | NOT_STARTED |
| BAK-03 | Backup validieren | `VALID` nur bei vollständiger Hashkette | Validierungsergebnis | NOT_STARTED |
| BAK-04 | Original ändern (nach dem Backup) | Änderung ist erkannt, wenn validiert wird | Vorher-/Nachher-Hash | NOT_STARTED |
| BAK-05 | Restore | geänderte Datei kehrt zum Stand des Backups zurück | Hash vorher/nachher | NOT_STARTED |
| BAK-06 | Zustand vergleichen | `RESTORE VERIFIED` nur bei Gleichheit aller Hashes | Vergleichsprotokoll | NOT_STARTED |
| BAK-07 | Beschädigtes Backup (Testfall, eine Datei verfälscht) | Restore wird **abgelehnt**, nichts wird überschrieben | Fehlerprotokoll + Original unverändert | NOT_STARTED |

## 4. Approval-Testmatrix (Kapitel 76)

Jede kritische Aktion (mindestens: Reparatur, Bereinigung, Update, Neustart, Wiederherstellung) in
vier Situationen:

| ID | Situation | Erwartung | Nachweis | Zustand |
| --- | --- | --- | --- | --- |
| APR-01 | APPROVE | Aktion läuft genau einmal, Ergebnis wird validiert | Protokoll + Vorher/Nachher | NOT_STARTED |
| APR-02 | DECLINE | keine Änderung am System | Vorher-/Nachher-Vergleich | NOT_STARTED |
| APR-03 | CANCEL | keine neue Aktion, Abbruch wird protokolliert | Protokoll | NOT_STARTED |
| APR-04 | TIMEOUT | sicherer Abbruch, kein Teilzustand | Protokoll + Systemvergleich | NOT_STARTED |

## 5. Admin-Testmatrix (Kapitel 77)

| ID | Fall | Erwartung | Nachweis | Zustand |
| --- | --- | --- | --- | --- |
| ADM-01 | Standardnutzer, Aktion ohne Adminrechte | läuft ohne Erhöhung | Protokoll + Prozessliste | NOT_STARTED |
| ADM-02 | Standardnutzer, Aktion mit Adminrechten, UAC bestätigt | genau eine Abfrage, dann Ausführung | UAC-Protokoll + Ereignisprotokoll | NOT_STARTED |
| ADM-03 | Standardnutzer, Aktion mit Adminrechten, UAC abgebrochen | Ergebnis `BLOCKED` mit `UAC_CANCELLED` (Win32 1223), keine Änderung | Protokoll + Systemvergleich | NOT_STARTED |
| ADM-04 | Erhöht gestartete Anwendung | keine zweite Abfrage, Aktion läuft | Protokoll | NOT_STARTED |

## 6. Wiederherstellung (Kapitel 64)

| ID | Fall | Erwartung | Nachweis | Zustand |
| --- | --- | --- | --- | --- |
| REC-01 | A: Prozess während einer Aktion abgebrochen | beim nächsten Start ist der Zustand konsistent, nichts halb angewandt | Journal + Dateivergleich | NOT_STARTED |
| REC-02 | B: Anwendung während einer Aktion beendet | wie REC-01, zusätzlich kein verwaister Kindprozess | Prozessliste + Journal | NOT_STARTED |
| REC-03 | C: Datenbestand beschädigt | Start läuft weiter, Schaden wird gemeldet, keine stillen Korrekturen | Startprotokoll + Reparaturprotokoll | NOT_STARTED |

## 7. Sicherheitsmatrix (Kapitel 78–82, Modul M44) – vierzehn Pflichtangriffe

| ID | Angriff | Erwartung | Nachweis | Zustand |
| --- | --- | --- | --- | --- |
| SEC-01 | Command Injection über einen Aktionsparameter | Aktionsregistry lehnt ab, kein Prozess entsteht | Protokoll + Prozessliste | NOT_STARTED |
| SEC-02 | Path Traversal (`..\..\Windows\System32\…`) | Pfadprüfer mit Linkauflösung lehnt ab | Protokoll | NOT_STARTED |
| SEC-03 | Argument Injection (zusätzliche Schalter) | Argument wird nicht durchgereicht | Protokoll | NOT_STARTED |
| SEC-04 | Privilege Escalation (Aktion ohne Adminrechte) | Ausführung wird verweigert oder über genau eine bestätigte Erhöhung geführt | Protokoll | NOT_STARTED |
| SEC-05 | Manipulierte Konfiguration (Aktion nachträglich freigegeben) | Freigabe wird verweigert, Änderung wird erkannt | Prüfsumme + Protokoll | NOT_STARTED |
| SEC-06 | Manipuliertes Update (fremde Quelle) | Download wird abgelehnt (Quellen-Allowlist) | Protokoll | NOT_STARTED |
| SEC-07 | Ungültige Signatur | Installation wird abgelehnt | Protokoll + Dateihash | NOT_STARTED |
| SEC-08 | Beschädigtes Backup | Restore wird abgelehnt (siehe BAK-07) | Protokoll | NOT_STARTED |
| SEC-09 | Aktion ohne Administratorrechte, die sie braucht | `BLOCKED`, kein stiller Teilerfolg | Protokoll | NOT_STARTED |
| SEC-10 | UAC-Abbruch | `UAC_CANCELLED`, nichts geändert | Protokoll | NOT_STARTED |
| SEC-11 | Prozessabbruch während der Aktion | siehe REC-01, zusätzlich Audit-Eintrag unvollständig erkennbar | Journal | NOT_STARTED |
| SEC-12 | Manipulation der Protokolldateien | Erkennung einer gekürzten/veränderten Datei (Hashkette) | Prüfprotokoll | NOT_STARTED |
| SEC-13 | Datenbankbeschädigung | Start läuft weiter, Schaden gemeldet (siehe REC-03) | Protokoll | NOT_STARTED |
| SEC-14 | Report Injection (Steuerzeichen/HTML in einem Befund) | Bericht bleibt lesbar, keine Ausführung, keine Umleitung | Bericht + Prüfung des Rohformats | NOT_STARTED |

## 8. Offline, Lokalisierung, Benutzeroberfläche

| ID | Fall | Erwartung | Nachweis | Zustand |
| --- | --- | --- | --- | --- |
| OFF-01 | Start ohne Netz (Kabel gezogen) | lokale Module laufen, Online-Module melden „nicht verfügbar" | Protokoll + Netzstatus im Umgebungsrecord | NOT_STARTED |
| OFF-02 | Update-Prüfung ohne Netz | keine erfundene Verfügbarkeit, klare Meldung | Protokoll | NOT_STARTED |
| LOC-01 | `de-DE`, `en-US` | keine sichtbaren Schlüssel, vollständige Oberfläche | Screenshot je Sprache | NOT_STARTED |
| LOC-02 | `ja-JP`, `ru-RU` | wie LOC-01 (Übersetzungen vorhanden) | Screenshot je Sprache | NOT_STARTED |
| UI-01 | Dark Mode als Vorgabe | Oberfläche startet dunkel, Umschaltung bleibt erhalten | Screenshot + Einstellungsdatei | NOT_STARTED |
| UI-02 | Fehlerbild WHAT/WHY/IMPACT/ACTION/LOG mit ID `WMC-<MODUL>-<Nr>` | vollständig sichtbar und kopierbar | Screenshot + Logauszug | NOT_STARTED |
| UI-03 | Skalierung 100 % / 150 % / 200 %, Hochkontrast | keine abgeschnittenen Elemente | Screenshots | NOT_STARTED |

## 9. Was diese Matrix nicht ersetzt

* Den Nachweis auf **physischer** Hardware für Temperatur, Lüfter, SMART, Akku und Gehäuse
  (`scripts/vm/Invoke-VmHardwareEvidence.ps1` sammelt die Werte, die die Zielmaschine hergibt; eine
  VM gibt dazu `BLOCKED` aus).
* Den **Neustart**-Nachweis über einen Reboot (Gate 10, Kapitel 65).
* Den **Offline**-Nachweis mit gezogenem Kabel (Gate 6).
* Die **Reproduzierbarkeit** des Builds (Gate 1) – dafür zählt der CI-Lauf mit festgehaltenen Hashes.

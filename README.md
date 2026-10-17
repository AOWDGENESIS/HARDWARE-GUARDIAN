# HARDWARE GUARDIAN / Windows Maintenance Center

**Windows-Hardwarediagnose und Wartungsplanung mit nachvollziehbaren Befunden, ausdruecklicher Freigabe und Sicherheitspruefungen.**

HARDWARE GUARDIAN ist ein Windows-Projekt zur Erfassung von Systeminformationen, Diagnose von Geraeten und Windows-Zustaenden sowie zur kontrollierten Planung von Wartungsaktionen. Ergebnisse und Grenzen muessen anhand der jeweiligen Datenquelle bewertet werden. Nicht verfuegbare Werte duerfen nicht als bestaetigte Fehler dargestellt werden.

Sprachen: **Deutsch** | [English](WindowsMaintenanceCenter/README.md) | [日本語](WindowsMaintenanceCenter/README.ja.md) | [Русский](WindowsMaintenanceCenter/README.ru.md)

## Funktionen

- Hardware- und Geraeteinventar, soweit Windows und die jeweiligen Treiber Daten bereitstellen.
- Diagnose von PnP-/Treiberstatus, Windows-Ereignissen und Systemintegritaet.
- Wartungsplanung mit Trockenlauf und expliziter Bestaetigung vor vorgesehenen Aenderungen.
- Protokolle und maschinenlesbare Berichte fuer nachvollziehbare Ergebnisse.
- Bewertung von Treiber-/Firmwareinformationen; Firmware wird nicht ohne ausdrueckliche separate Aktion geflasht.

## Sicherheitsgrundsaetze

- Keine stillen Systemaenderungen oder UAC-Umgehung.
- Unbekannte oder nicht lesbare Werte werden als unbekannt behandelt.
- Trockenlauf und Bestaetigung sind kein Ersatz fuer ein Backup oder eine Wiederherstellungsstrategie.
- Ein Wiederherstellungspunkt kann je nach Windows-Konfiguration fehlschlagen oder nicht verfuegbar sein. Vor jeder Wartung wichtige Daten sichern.
- Netzwerkzugriff kann fuer Update-/Herstellerinformationen, Downloads oder andere optionale Funktionen erforderlich sein. Das Projekt wird daher nicht pauschal als vollstaendig offline beschrieben.
- Pruefe jede Empfehlung, bevor du sie auf einem produktiven System ausfuehrst.

## Schnellstart

1. Repository herunterladen oder klonen:
   ```powershell
   git clone https://github.com/AOWDGENESIS/HARDWARE-GUARDIAN.git
   Set-Location HARDWARE-GUARDIAN
   ```
2. Lies `ANLEITUNG.txt` und die Hinweise in `WindowsMaintenanceCenter/`.
3. Verwende den vorgesehenen Starter oder Installer nur nach Pruefung der Herkunft und Integritaet der heruntergeladenen Dateien.
4. Fuer Entwicklung und Tests nutze die Solution und die Testanweisungen in `WindowsMaintenanceCenter/`.

## Projektstruktur

- `WindowsMaintenanceCenter/`: .NET-Anwendung, Tests, Dokumentation und Build-/Installerskripte.
- `docs/`: uebergeordnete Architektur- und Projektdokumentation.
- `scripts/` und `tools/`: Diagnose- und Validierungswerkzeuge.
- `ANLEITUNG.txt`: deutsche Benutzeranleitung.

## Status und Qualitaet

Dieses Repository enthaelt Windows-spezifische Funktionen. Ein erfolgreicher Build oder ein Unit-Test allein beweist nicht, dass jede Wartungsaktion auf allen Windows-Systemen sicher oder verfuegbar ist. Siehe Testberichte und CI-Ergebnisse fuer den tatsaechlich geprueften Umfang. Veraltete oder nicht reproduzierbare Zahlen zur Testanzahl werden hier bewusst nicht als aktuelle Garantie wiederholt.

## Mitwirken

Fehlerberichte sollten Windows-Version, relevante Protokollauszuege und reproduzierbare Schritte enthalten. Entferne vorher Benutzernamen, Seriennummern, Tokens und andere private Daten. Aenderungen an Systemwartung benoetigen Tests fuer Erfolgs-, Fehler-, Abbruch- und Rollback-Pfade.

## Lizenz

Der Projektquellcode ist unter der MIT-Lizenz lizenziert. Siehe [LICENSE](LICENSE). Drittanbieterkomponenten und mitgelieferte Binaerdateien koennen eigenen Lizenz- und Herkunftsbedingungen unterliegen; pruefe deren jeweilige Hinweise separat.

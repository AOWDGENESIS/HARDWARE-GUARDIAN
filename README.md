# HARDWARE GUARDIAN / Windows Maintenance Center

**Lokale Windows Hardware-Diagnose, Wartungs- und Update-Zentrale mit lückenlosem Protokoll (100 % Offline).**

HARDWARE GUARDIAN diagnostiziert Hardware- und Windows-Zustände bis ins Detail, erklärt alle Befunde verständlich und führt Änderungen ausschließlich nach Ihrer expliziten Freigabe durch – stets mit automatischer Sicherung und Rückrollweg.

Kein Driver-Booster, kein Registry-Cleaner und keine Pseudo-Optimierung ohne Nachweis. Es ist ein transparentes Diagnose- und Wartungswerkzeug nach Industriestandard.

Sprachen / Languages:
**Deutsch** | [English](WindowsMaintenanceCenter/README.md) | [日本語](WindowsMaintenanceCenter/README.ja.md) | [Русский](WindowsMaintenanceCenter/README.ru.md)

---

## Schnellstart unter Windows

1. **Repository herunterladen / klonen:**
   * Entweder oben auf den grünen Button **Code → Download ZIP** klicken und entpacken.
   * Oder per Befehlszeile:
     ```cmd
     git clone https://github.com/AOWDGENESIS/HARDWARE-GUARDIAN.git
     cd HARDWARE-GUARDIAN
     ```

2. **Zentrale Start-Routine ausführen:**
   * **Doppelklick auf `START.bat`**
   * Es öffnet sich das interaktive Hauptmenü:
     * `[1]` Installations-Assistent starten (Standard Windows Setup GUI)
     * `[2]` Anwendung direkt aufrufen (Hardware Guardian Launcher)
     * `[3]` VM-Testumgebung & Evidenzprüfungen durchführen (scripts\vm)
     * `[4]` Deinstallation aufrufen (Sauberes Entfernen mit Datenaufbewahrungs-Abfrage)
     * `[5]` Stand zu GitHub synchronisieren
     * `[6]` Ausführliche Anleitung anzeigen (`ANLEITUNG.txt`)

3. **Alternativ direkt als Installer:**
   * Doppelklick auf `Setup.exe` oder `HardwareGuardian.bat`

---

## Kernfunktionen

* **Vollständige Hardware-Erkennung**:
  Prozessor (CPU), Mainboard (inkl. Board-Revision), BIOS/UEFI, Arbeitsspeicher (RAM), Grafikkarte (GPU), Massenspeicher (SMART-Werte, NVMe-Verschleiß), Netzwerkadapter, WLAN, Bluetooth, Audio, USB-Geräte, PCI/PCIe-Busse, Chipsatz, Monitore, Drucker, Akku und Sensoren. Jeder Wert deklariert seine Herkunft (`WMI`, `Windows API`, `Registry`, `Herstellerquelle`). Nicht lesbare Werte werden ehrlich als `UNKNOWN` mit Ursache ausgewiesen.
* **Tiefgehende Windows-Diagnose**:
  Treiber- und PnP-Status, Windows-Systemintegrität (DISM / SFC), Ereignisprotokoll (Event Log), Windows Defender Status und Windows Update Zustand mit eindeutigen Befund-IDs (z. B. `HW-CPU-001`).
* **Sichere Wartung mit Pflicht-Trockenlauf**:
  Fester 6-Schritte-Ablauf: Scan → Plan → **Pflicht-Trockenlauf (Dry Run)** → Freigabe → Ausführung → Validierung. Kategorien: `SAFE`, `OPTIONAL`, `PROTECTED` oder `UNKNOWN`. Geschützte und unbekannte Bereiche werden gemessen, aber niemals automatisiert verändert.
* **Verifizierte Updates**:
  Prüfung ausschließlich gegen offizielle Hersteller- und Microsoft-Quellen. Firmware-Stände werden bewertet, aber niemals unaufgefordert geflasht.
* **Vollständiges Audit- und Protokollsystem**:
  Echtzeitprotokoll sowie lückenlose Berichte als maschinenlesbares JSON, TXT und druckbares HTML.

---

## Sicherheitsmerkmale

| Eigenschaft | Durchsetzung |
| :--- | :--- |
| **Keine Telemetrie / Offline** | 100 % lokal, keine Cloud-Pflicht, keine Datenübertragung nach außen. |
| **Kein UAC-Bypass** | Aktionen mit Admin-Rechten erfordern den regulären Windows UAC-Dialog. |
| **Keine Defender-Abschaltung** | Viren- und Firewall-Schutz werden weder deaktiviert noch herabgestuft. |
| **Rollback-Schutz** | Pflicht-Wiederherstellungspunkt vor jeder ändernden Systemoperation. |
| **Echte Validierung** | `PASSED` und `SUCCESS` existieren nur nach messbarem Erfolgsnachweis (Spec Kap. 86). |

---

## Projektstruktur

```text
HARDWARE-GUARDIAN/
├── START.bat                               <-- Zentrale interaktive Start-Routine
├── ANLEITUNG.txt                           <-- Ausführliche deutsche Benutzeranleitung
├── HardwareGuardian.bat                    <-- Schneller Programm-Starter
├── Setup.exe                               <-- Nativer Windows x64 GUI-Installationsassistent
├── Setup.cmd                               <-- Erweiterter Setup-Starter (mit /SILENT & /BUILD)
├── WindowsMaintenanceCenter/               <-- Gesamte C#/.NET 10 Solution (15 Projekte, 168 Quelldateien)
│   ├── src/                                <-- Quellcode (App, Core, Windows, Bios, Diagnostics etc.)
│   ├── tests/                              <-- Testsuite (370 Unit-, Integration- & Security-Tests)
│   ├── test-results/                       <-- Evidenzverzeichnis (10 Prüfbereiche gem. Kap. 71)
│   ├── docs/                               <-- Spezifikation V1.0, Abnahmeberichte, Release-Status
│   ├── scripts/                            <-- Build-, Release- und VM-Prüfskripte
│   └── installer/                          <-- GUI-Installer, Uninstaller & Inno-Setup-Skripte
├── docs/                                   <-- Plattform- & Architektur-Dokumente
└── tools/                                  <-- Prüf- und Validierungswerkzeuge
```

---

## Lizenz & Qualität
* **Qualitätsstandard:** Entwickelt nach der verbindlichen Spezifikation V1.0 (Kapitel 1–103).
* **Testabdeckung:** 370 automatisierte Tests, 10 nachgewiesene Evidenzbereiche, 0 gefälschte Erfolgsmeldungen.

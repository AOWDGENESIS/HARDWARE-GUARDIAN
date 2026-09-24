# Windows Maintenance Center / Hardware Guardian

**Windows Hardware-Diagnose, Wartungs- und Update-Zentrale.** Eine lokale Windows-Desktop-Anwendung,
die Hardware und Windows-Systemzustand detailliert diagnostiziert, Befunde nachvollziehbar erklärt
und Änderungen ausschließlich nach expliziter Freigabe durchführt – immer mit Sicherung und Rückweg.

Kein Driver-Booster, kein Registry-Cleaner und kein Pseudo-Optimierer, der unhaltbare Versprechungen macht.
Es ist ein transparentes Diagnosewerkzeug mit lückenlosem Protokoll.

Sprachen / Languages:
**Deutsch** | [English](README.md) | [日本語](README.ja.md) | [Русский](README.ru.md)

---

## Kernfunktionen

* **Umfassende Hardware-Erkennung**:
  Prozessor (CPU), Mainboard (inkl. Board-Revision), BIOS/UEFI, Arbeitsspeicher (RAM), Grafikkarte (GPU),
  Massenspeicher (SMART-Werte, NVMe-Verschleiß), Netzwerkadapter, WLAN, Bluetooth, Audio, USB-Geräte,
  PCI/PCIe-Busse, Chipsatz, Monitore, Drucker, Akku und Sensoren. Jeder Messwert deklariert seine
  Herkunft (`WMI`, `Windows API`, `Registry`, `Herstellerquelle`, `Lokale Datei`). Nicht lesbare Werte
  werden ehrlich als `UNKNOWN` mit Ursache ausgewiesen – niemals als gefälschte Nullwerte.
* **Tiefgehende Windows-Diagnose**:
  Treiber- und PnP-Status, Windows-Systemintegrität (DISM / SFC), Ereignisprotokoll (Event Log),
  Windows Defender Status und Windows Update Zustand. Jeder Befund trägt eine eindeutige Kennung (z. B. `HW-CPU-001`).
* **Sichere Wartung mit Pflicht-Trockenlauf**:
  Fester 6-Schritte-Ablauf: Scan → Plan → **Pflicht-Trockenlauf (Dry Run)** → Freigabe → Ausführung → Validierung.
  Kategorien: `SAFE`, `OPTIONAL`, `PROTECTED` oder `UNKNOWN`. Geschützte und unbekannte Bereiche werden
  gemessen und erklärt, aber niemals automatisiert gelöscht.
* **Verifizierte Updates**:
  Prüfung ausschließlich gegen offizielle Hersteller- und Microsoft-Quellen. Firmware-Stände werden bewertet,
  aber niemals unaufgefordert geflasht (der manuelle Herstellerpfad bleibt manuell).
* **Vollständiges Audit- und Protokollsystem**:
  Echtzeitprotokoll (Zeitstempel, Modul, Aktion, Status, Detail) sowie lückenlose Berichte als maschinenlesbares
  JSON, TXT und druckbares HTML.

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

## Schnellstart & Installation unter Windows

1. **ZIP entpacken**:
   Das Verteilungs-Archiv entpacken.
2. **Setup starten**:
   * Doppelklick auf `Setup.exe` oder `WindowsMaintenanceCenter-Setup-x64.exe` (grafischer Installer)
   * ODER Doppelklick auf `HardwareGuardian.bat` / `INSTALLIEREN.bat`
3. **Deinstallation**:
   Saubere Entfernung jederzeit über die Windows-Einstellungen (*Apps & Features*) oder über `Uninstall.cmd`.

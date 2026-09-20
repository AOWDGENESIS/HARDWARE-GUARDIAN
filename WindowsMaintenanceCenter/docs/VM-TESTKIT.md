# VM-Testkit (Nachweise auf der Zielmaschine)

Dieses Dokument beschreibt, **wie die Nachweise entstehen, die auf dem Entwicklungsrechner und auf
dem CI-Runner nicht entstehen können**. Es ersetzt keine Arbeit: es ist die Anleitung für den
Menschen (oder eine Fernstrecke) an der Zielmaschine.

## 1. Warum es dieses Kit gibt

Auf dem Entwicklungsrechner gibt es kein Windows, keine Virtualisierung und kein .NET-SDK
(`docs/VM-CI.md`, Abschnitt 1). Der CI-Runner ist eine Windows-VM mit echtem Kernel und echtem .NET,
aber **ohne** physische Sensorik, ohne Akku, ohne SMART-Werte echter Laufwerke, ohne UAC-Abfrage und
ohne Zustand über einen Neustart hinweg (Abschnitt 3 desselben Dokuments).

Damit bleiben folgende Nachweise offen, bis sie auf einer echten Maschine entstehen:

| Nachweis | Betroffene Module / Gates |
| --- | --- |
| Temperatur, Lüfter, SMART, Akku, Gehäuse-/Board-/BIOS-Daten, Monitore | M08–M11, M18, M20 (Gate 3, Gate 5) |
| UAC bestätigt / abgebrochen, Grenze zwischen Standardnutzer und Admin | M31, Safety- und Admin-Matrix (Gate 3) |
| Neustartverhalten, Rollback über einen Reboot, Wiederherstellungspunkt | M34/M35 (Gate 10) |
| Offline-Betrieb mit gezogenem Kabel | Gate 6 |
| Vier Sprachen auf der Oberfläche, Dark Mode, Fehlerbild | M37, M47, Gate 8 |
| Installation, Upgrade, Reparatur, Deinstallation auf der Zielmaschine | Gate 7 |

Die Arbeitsliste dazu ist `docs/TESTMATRIX-VM.md`; jeder Fall nennt dort seinen Nachweisort.

## 2. Die Zielmaschine

**Empfohlen:** echte Hardware (Desktop oder Notebook) mit Windows 11 x64.
**Ausreichend für alles außer Sensorik:** eine Windows-11-VM mit UEFI, Secure Boot und TPM 2.0.

| Anforderung | Warum |
| --- | --- |
| Windows 11 x64, UEFI, Secure Boot, TPM 2.0 | Kapitel 52 (M46) verlangt genau diese Testumgebung |
| Zwei Konten: Standardnutzer und Administrator | Admin- und UAC-Matrix (Kapitel 77) |
| Snapshot-/Wiederherstellungsmöglichkeit vor jedem Test | Wiederherstellungsszenarien (Kapitel 64) und „read-only first" (Kapitel 72) |
| Für die Sensorik: Thermalzone, Lüfter, SMART-fähiges Laufwerk, ggf. Akku | sonst meldet das Kit `BLOCKED`, und das ist dort auch richtig |
| Zugriff auf `test-results/` (Kopie des Repositorys oder Netzlaufwerk) | Nachweise müssen abgelegt werden (Kapitel 71) |

Die sechs Umgebungen aus Kapitel 73 (clean, updated, offline, Standardnutzer, Administrator,
Recovery-Szenario) werden als sechs Snapshots derselben VM gefahren. Die Zuordnung steht in
`docs/TESTMATRIX-VM.md`, Abschnitt 1.

## 3. Vorbereitung im Gast

```powershell
# Ausführen der Skripte für diese Sitzung erlauben (keine dauerhafte Änderung am System)
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force

# Prüfen, wer man ist: das Kit schreibt Adminstatus und Maschinenmodell in jeden Nachweis
whoami; [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrator')
```

Es werden **keine** zusätzlichen Programme, Pakete oder Netzverbindungen gebraucht: das Kit benutzt
ausschließlich PowerShell, CIM/WMI und das mitgelieferte Windows. Es sendet nichts nach außen und
schreibt nur in `test-results/` im Repository.

## 4. Die Skripte

| Skript | Rechte | Wirkung | Schreibt nach |
| --- | --- | --- | --- |
| `scripts/vm/Evidence.ps1` | – | Bibliothek: Nachweisordner, Umgebungsrecord, Hashes, `report.json`/`report.txt`. Wird von den anderen Skripten geladen, nicht direkt aufgerufen. | – |
| `scripts/vm/Invoke-VmHardwareEvidence.ps1` | keine | **nur lesend**: CPU, RAM (Typ/Geschwindigkeit), Mainboard, BIOS/UEFI, Gehäusetyp, GPUs, Monitore, Laufwerke, SMART-Vorhersage, Volumes, Thermalzonen, Lüfter, Akku inkl. Verschleiß, TPM, Secure Boot, Defender, Neustartzustand, Wiederherstellungspunkte, Firewallprofile. Vergleicht die Werte mit einem von der Anwendung erzeugten Bericht, wenn `-WmcReport` übergeben wird. | `test-results/integration/` |

```powershell
# Hardware-, Sensor- und Firmware-Nachweise (read-only):
pwsh ./scripts/vm/Invoke-VmHardwareEvidence.ps1

# mit Abgleich gegen einen Bericht der Anwendung (erst damit ist der Lesepfad bewiesen):
pwsh ./scripts/vm/Invoke-VmHardwareEvidence.ps1 -WmcReport C:\temp\wmc-report.json
```

**Noch nicht enthalten** (geplant, siehe `docs/TESTMATRIX-VM.md`):
UAC-/Approval-Matrix, Backup-/Restore-Kette, Wiederherstellungsszenarien, Offline, Lokalisierung,
Installer-Lebenszyklus, Sicherheitsangriffe SEC-01…SEC-14. Bis diese Skripte existieren, werden die
Fälle von Hand nach der Matrix gefahren und die Protokolle ebenfalls unter `test-results/<Bereich>/`
abgelegt. Es wird dabei **nichts** als bestanden eingetragen, was nicht ausgeführt wurde.

## 5. Was jeder Nachweis enthält

Jeder Lauf legt einen eigenen Ordner an:

```text
test-results/<bereich>/<JJJJMMTTHHMMSSZ>-<TestID>/
├── report.json        timestamp, version, build, environment, result, evidence[], measurements[] (Kapitel 71)
├── report.txt         dieselben Angaben lesbar, für das Abnahmeprotokoll
├── environment.json   der Rechner: OS/Build, Hersteller/Modell, CPU, RAM, Adminstatus, Secure Boot, TPM …
└── logs/              Kopien der Beweise (Protokolle, Screenshots, Installer-Logs) mit SHA-256 im Report
```

Statuswerte: `PASSED`, `FAILED`, `BLOCKED`, `NOT VERIFIED`. Die Bibliothek verweigert einen
`PASSED`-Nachweis ohne Messwert oder Beweisdatei — eine Erfolgsmeldung ohne Grundlage ist genau das,
was Kapitel 86 verbietet.

## 6. Nachweise zurück ins Repository

```powershell
# im Repository (nach dem Lauf auf der Zielmaschine):
git add WindowsMaintenanceCenter/test-results
git commit -m "Nachweise der Zielmaschine: <Bereich> <TestID>"
git push
```

Die Nachweise werden **nicht** bearbeitet: keine Zusammenfassung ohne die Dateien, auf die sie sich
beruft. `docs/ABNAHME-WMC.md` und `docs/RELEASE_STATUS.md` werden erst danach gezogen — und nur für
die Module, deren MUSS-Kriterien damit wirklich belegt sind.

## 7. Grundsätze des Kits

1. **Read-only zuerst** (Kapitel 72). Änderungen nur in Tests, die sie brauchen, nur nach einem
   Snapshot und nur mit ausdrücklichem Schalter.
2. **Keine erfundenen Werte.** Was nicht messbar ist, steht als „not readable (<Grund>)" oder der
   Lauf endet `BLOCKED`.
3. **Keine persönlichen Daten.** Seriennummern und Rechnernamen bleiben lokal im Nachweis; es wird
   nichts übertragen, es gibt keine Telemetrie (Kapitel 55).
4. **Ein Nachweis pro Lauf**, mit Version und Commit, damit später erkennbar ist, welcher Build
   gemessen wurde.

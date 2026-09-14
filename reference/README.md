# reference — Dauerreferenzen mit gepinntem Hash

Dieser Ordner ist der **einzige** Ort, an dem Dauerreferenzen (Regelwerke,
Leitlinien, Lastenhefte, Prompt-Bausteine) liegen, die die Runtime oder ein
Mensch laden soll.

## Warum hier und nicht im Chat

Zwei Ausfälle in kurzer Folge hatten dieselbe Ursache: Code und Texte wurden
über Chat/Markdown kopiert. Dabei passieren zwei Dinge, die man nicht sieht:

1. **Typografische Anführungszeichen** — die Darstellungsschicht ersetzt
   `"` durch `„ " "`. PowerShell erkennt danach nicht einmal mehr die
   Zeilenstruktur (Befund **F-01**).
2. **`-replace`-Ersetzungen** — beim Einfügen über Skripte werden `$1`, `$&`,
   `` $` ``, `$_` als Ersetzungsanweisungen interpretiert und zerstören den
   Text (Befund **F-36** und der zerstörte DISCOVER-Patch).

Dazu kommt: Ein Text, dessen Integrität niemand prüft, ist als Dauerreferenz
wertlos — man kann später nicht belegen, welche Fassung geladen wurde.

## Ablauf

**1. Datei hier ablegen** (GitHub-Web-Upload oder lokal kopieren).

**2. Registrieren** — trägt Größe, Zeichenzahl und SHA256 ins Manifest ein:

```powershell
& .\tools\Register-ReferenceDocument.ps1 -Path .\reference\KI_Dauerreferenz_Programmierung_und_Softwarequalitaet_DE.md -Note "Regelwerk Softwarequalitaet v1"
```

**3. Prüfen** — vergleicht jede registrierte Datei gegen ihren gepinnten Hash:

```powershell
& .\tools\Register-ReferenceDocument.ps1 -Verify
```

**4. Bei Änderung:** `-Force` verlangt eine bewusste Entscheidung. Ohne `-Force`
verweigert das Werkzeug stilles Überschreiben und zeigt alt → neu.

## Dateien

| Datei | Zweck |
|---|---|
| `manifest.json` | gepinnte Hashes aller registrierten Referenzen (Quelle der Wahrheit) |
| `<Dokument>.<md|txt>` | die Referenztexte selbst |

`manifest.json` wird **ordinal sortiert** und mit `\n`-Zeilenenden geschrieben,
damit der Hashes-Vergleich zwischen Rechnern und Kulturen stabil bleibt
(Begründung: Befund **F-08** — `Sort-Object` sortiert kulturabhängig).

## Regeln

- Referenzen werden **nie** aus einem Chat, einer E-Mail oder einer
  Zwischenablage übernommen. Immer als Datei.
- Jede Referenz bekommt einen Hash. Was keinen Hash hat, ist keine Referenz,
  sondern eine Behauptung.
- Wenn eine Runtime eine Referenz lädt, gehört der Hash in den Laufbericht
  (`reference_name`, `reference_sha256`) — sonst ist später nicht belegbar,
  womit gearbeitet wurde (Befund **F-18**).
- Widersprüche zwischen Referenzen und Masterprompt werden **ausgeschrieben**,
  nicht implizit aufgelöst: Welche Regel gewinnt? Diese Entscheidung gehört in
  die Referenz, nicht in den Kopf des Lesers.

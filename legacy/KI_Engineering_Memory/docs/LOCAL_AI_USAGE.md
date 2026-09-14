# Verwendung mit beliebiger KI

Die KI soll `reference/LOAD_INSTRUCTION.txt` als verbindliche Engineering-Regeln laden.

Fuer aktuelle Maschineninformationen soll sie `runtime/KI_Maschinenprofil.md` beziehungsweise den ueber `Export-KI-Context.ps1` erzeugten Kontext verwenden.

Empfohlenes Muster:

1. Stable rules laden.
2. Aktuelles Runtime-Profil laden.
3. Aufgabenanforderungen bestimmen.
4. Nur relevante Regeln in den aktiven Kontext uebernehmen.
5. Vor Ausgabe Code pruefen, testen und Fehlerpfade bewerten.
6. Keine Maschinenfakten aus dem Langzeitgedaechtnis als aktuell annehmen.

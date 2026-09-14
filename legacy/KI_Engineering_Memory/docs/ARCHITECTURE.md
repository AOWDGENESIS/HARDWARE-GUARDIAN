# Architektur

KI Engineering Memory besteht aus vier Schichten:

1. Policy Layer: stabile Programmier- und Qualitaetsregeln.
2. Runtime Layer: aktuelle Maschinen- und Toolfakten.
3. Context Builder: kombiniert nur die fuer eine Aufgabe benoetigten Informationen.
4. Integration Layer: stellt den Kontext fuer beliebige KI-Frontends, lokale Modelle, Agenten und Projekte bereit.

Keine Schicht darf ein konkretes Projekt voraussetzen.

Runtime-Fakten haben ein Ablauf-/Aktualisierungsprinzip: Eine KI darf sie nicht als dauerhaft wahr behandeln, sondern soll Erhebungszeitpunkt und Quelle beruecksichtigen.

Der Updater schreibt ausschliesslich Runtime-Dateien. Die dauerhafte Referenz wird nicht von Inventurdaten ueberschrieben.

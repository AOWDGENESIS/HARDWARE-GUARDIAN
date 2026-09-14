# JavaScript - Programmierreferenz

PROGRAMMING_LANGUAGE: JavaScript
DOCUMENT_TYPE: Language Reference
STATUS: Consolidated 2026-09-14

## Rolle
JavaScript ist zentral fuer Browser-UIs, lokale Web-Dashboards, Node.js-Backends und interaktive Agentenoberflaechen.

## Engineering-Regeln
- Untrusted Input validieren und kontextgerecht escapen.
- Async-Code mit explizitem Fehlerpfad behandeln.
- API-Antworten validieren, bevor sie verwendet werden.
- DOM-Manipulation gegen XSS absichern.
- Abhaengigkeiten und Lockfiles reproduzierbar halten.

## Typische Werkzeuge
- Node.js
- npm
- package-lock.json
- ESLint
- Vitest oder Jest

## Sicherheitsfokus
XSS, Prototype Pollution, unvalidierte API-Daten, Secrets im Frontend und unsichere Dependency-Ketten.

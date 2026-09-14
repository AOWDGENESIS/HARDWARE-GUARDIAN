# Rust - Programmierreferenz

PROGRAMMING_LANGUAGE: Rust
DOCUMENT_TYPE: Language Reference
STATUS: Consolidated 2026-09-14

## Rolle
Rust ist besonders interessant fuer memory-sichere Systemsoftware, native Tools und performancekritische Komponenten.

## Engineering-Regeln
- Ownership und Borrowing als Kern des Designs nutzen.
- unsafe nur begruendet und isoliert einsetzen.
- cargo fmt und cargo clippy verwenden.
- cargo test als Standardgate einsetzen.
- Dependencies ueber Cargo.lock reproduzierbar behandeln, wo passend.

## Typische Werkzeuge
- cargo
- rustc
- clippy
- rustfmt

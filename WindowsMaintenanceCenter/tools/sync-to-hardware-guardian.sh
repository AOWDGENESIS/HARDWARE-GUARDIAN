#!/usr/bin/env bash
# Synchronisation von Windows Maintenance Center in das Repository HARDWARE-GUARDIAN
# Verwendung:
#   1. Direkt mit GitHub CLI (sofern dort Schreibrechte vergeben sind):
#      bash tools/sync-to-hardware-guardian.sh
#   2. Oder mit einem Personal Access Token:
#      GH_TOKEN="<token>" bash tools/sync-to-hardware-guardian.sh
#   3. Oder manuell von jedem autorisierten Rechner:
#      git push https://github.com/AOWDGENESIS/HARDWARE-GUARDIAN.git arena/01a0bb04-entwicklungen:main

set -euo pipefail

TARGET_REPO="https://github.com/AOWDGENESIS/HARDWARE-GUARDIAN.git"

echo "=== Synchronisation nach HARDWARE-GUARDIAN ==="
echo "Ziel: $TARGET_REPO (main)"

if git push --dry-run "$TARGET_REPO" "HEAD:refs/heads/main" 2>/dev/null; then
    echo "Push-Berechtigung vorhanden. Übertrage..."
    git push "$TARGET_REPO" "HEAD:refs/heads/main"
    echo "Übertragung erfolgreich abgeschlossen."
else
    echo ""
    echo "[HINWEIS] Das aktive Token in dieser Umgebung hat noch keine Schreibrechte auf AOWDGENESIS/HARDWARE-GUARDIAN (HTTP 403: push=false)."
    echo "Sobald die Arena-App in GitHub für AOWDGENESIS/HARDWARE-GUARDIAN freigegeben wurde, kann der Push direkt ausgeführt werden."
    echo ""
    echo "Alternativ kann der Stand von jedem autorisierten Rechner mit folgendem Befehl übertragen werden:"
    echo "  git push https://github.com/AOWDGENESIS/HARDWARE-GUARDIAN.git HEAD:refs/heads/main"
fi

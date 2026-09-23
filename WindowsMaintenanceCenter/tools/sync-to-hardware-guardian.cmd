@echo off
setlocal
cd /d "%~dp0\.."
echo =====================================================================
echo  Synchronisation nach AOWDGENESIS/HARDWARE-GUARDIAN
echo =====================================================================
echo.
echo Dieses Skript uebertraegt alle Komponenten, Commits und Nachweise
echo in das Ziel-Repository: https://github.com/AOWDGENESIS/HARDWARE-GUARDIAN.git
echo.

git push https://github.com/AOWDGENESIS/HARDWARE-GUARDIAN.git HEAD:refs/heads/main
if %errorlevel% equ 0 (
    echo.
    echo =====================================================================
    echo  [OK] Erfolgreich nach AOWDGENESIS/HARDWARE-GUARDIAN (main) uebertragen!
    echo =====================================================================
) else (
    echo.
    echo [HINWEIS] Falls GitHub nach Anmeldedaten fragt, bitte ueber den Browser
    echo oder mit Ihrem GitHub-Konto autorisieren.
)
pause

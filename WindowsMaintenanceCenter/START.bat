@echo off
chcp 65001 >nul 2>nul
setlocal enabledelayedexpansion
cd /d "%~dp0"
title HARDWARE GUARDIAN / Windows Maintenance Center - Start-Routine

:MENU
cls
echo ================================================================================
echo   HARDWARE GUARDIAN / WINDOWS MAINTENANCE CENTER v1.0.0
echo   Diagnose, Wartung und Updates mit lückenlosem Protokoll (100%% Offline)
echo ================================================================================
echo.
echo   Wählen Sie eine gewünschte Option:
echo.
echo   [1] Installations-Assistent starten (Standard Windows Setup GUI)
echo   [2] Anwendung direkt starten (Hardware Guardian Launcher)
echo   [3] VM-Testumgebung & Evidenzprüfungen durchführen (scripts\vm)
echo   [4] Deinstallation aufrufen (Sauberes Entfernen mit Datenabfrage)
echo   [5] Stand zu GitHub synchronisieren (AOWDGENESIS/HARDWARE-GUARDIAN)
echo   [6] Anleitung & Dokumentation anzeigen (ANLEITUNG.txt)
echo   [7] Beenden
echo.
echo ================================================================================
set /p "CHOICE=Ihre Auswahl [1-7]: "

if "%CHOICE%"=="1" goto DO_SETUP
if "%CHOICE%"=="2" goto DO_START
if "%CHOICE%"=="3" goto DO_TESTS
if "%CHOICE%"=="4" goto DO_UNINSTALL
if "%CHOICE%"=="5" goto DO_SYNC
if "%CHOICE%"=="6" goto DO_DOCS
if "%CHOICE%"=="7" goto DO_EXIT

echo.
echo Ungültige Eingabe. Bitte wählen Sie eine Zahl von 1 bis 7.
timeout /t 2 >nul
goto MENU

:DO_SETUP
echo.
echo Starte Installations-Assistenten...
if exist "Setup.exe" (
    start "" "Setup.exe"
) else (
    call "Setup.cmd"
)
pause
goto MENU

:DO_START
echo.
echo Starte Hardware Guardian...
call "HardwareGuardian.bat"
pause
goto MENU

:DO_TESTS
echo.
echo Starte VM-Testumgebung und Evidenzprüfungen...
where pwsh.exe >nul 2>nul
if %errorlevel% equ 0 (
    pwsh.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\vm\Run-VmTestEnvironment.ps1"
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\vm\Run-VmTestEnvironment.ps1"
)
echo.
pause
goto MENU

:DO_UNINSTALL
echo.
echo Starte Deinstallationsroutine...
where pwsh.exe >nul 2>nul
if %errorlevel% equ 0 (
    pwsh.exe -NoProfile -ExecutionPolicy Bypass -File ".\installer\Uninstall-WMC.ps1"
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\installer\Uninstall-WMC.ps1"
)
pause
goto MENU

:DO_SYNC
echo.
call ".\tools\sync-to-hardware-guardian.cmd"
goto MENU

:DO_DOCS
if exist "ANLEITUNG.txt" (
    start "" notepad.exe "ANLEITUNG.txt"
) else if exist "INSTALL.txt" (
    start "" notepad.exe "INSTALL.txt"
) else (
    echo Dokumentation liegt unter docs\SPEC-WMC-V1.md
)
goto MENU

:DO_EXIT
echo Auf Wiedersehen!
timeout /t 1 >nul
exit /b 0

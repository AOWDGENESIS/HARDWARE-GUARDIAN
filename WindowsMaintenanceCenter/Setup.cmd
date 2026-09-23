@echo off
setlocal
cd /d "%~dp0"
title Windows Maintenance Center - Setup & Build

echo =====================================================================
echo  Windows Maintenance Center v1.0.0
echo  Windows Hardware Diagnostics, Maintenance ^& Update Center
echo =====================================================================
echo.
echo Dieses Skript prueft die Voraussetzungen und startet den Build-
echo und Release-Prozess zur Erstellung der ausfuehrbaren Dateien (.exe).
echo.

where pwsh.exe >nul 2>nul
if %errorlevel% equ 0 (
    echo [OK] PowerShell Core (pwsh.exe) gefunden.
    set "PS_CMD=pwsh.exe"
) else (
    where powershell.exe >nul 2>nul
    if %errorlevel% equ 0 (
        echo [OK] Windows PowerShell (powershell.exe) gefunden.
        set "PS_CMD=powershell.exe"
    ) else (
        echo [FEHLER] Keine PowerShell auf diesem System gefunden.
        pause
        exit /b 1
    )
)

echo.
echo Starte Release-Erstellung (scripts\release.ps1)...
echo.

%PS_CMD% -NoProfile -ExecutionPolicy Bypass -File ".\scripts\release.ps1"

if %errorlevel% equ 0 (
    echo.
    echo =====================================================================
    echo  Build erfolgreich!
    echo  Die Artefakte befinden sich in: artifacts\release\
    echo    - WindowsMaintenanceCenter-Setup-x64.exe (Installer)
    echo    - WindowsMaintenanceCenter-Portable-x64.exe (Portable)
    echo =====================================================================
) else (
    echo.
    echo [HINWEIS] Der Build erfordert das .NET 10 SDK und Inno Setup 6 (ISCC.exe).
    echo Details siehe docs\BUILD.md und docs\RELEASE.md.
)

pause

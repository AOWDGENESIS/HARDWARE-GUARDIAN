@echo off
chcp 65001 >nul 2>nul
setlocal enabledelayedexpansion
cd /d "%~dp0"
title Hardware Guardian / Windows Maintenance Center - Setup

echo =====================================================================
echo  Hardware Guardian / Windows Maintenance Center v1.0.0
echo  Standard Windows Installer
echo =====================================================================
echo.

set "SILENT_ARG="
if /i "%~1"=="/S" set "SILENT_ARG=-Silent"
if /i "%~1"=="/SILENT" set "SILENT_ARG=-Silent"
if /i "%~1"=="-Silent" set "SILENT_ARG=-Silent"

if /i "%~1"=="/BUILD" (
    echo Starte vollstaendige Release-Kompilierung (scripts\release.ps1)...
    where pwsh.exe >nul 2>nul
    if !errorlevel! equ 0 (
        pwsh.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\release.ps1"
    ) else (
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\release.ps1"
    )
    echo.
    pause
    exit /b !errorlevel!
)

where pwsh.exe >nul 2>nul
if %errorlevel% equ 0 (
    set "PS_CMD=pwsh.exe"
) else (
    where powershell.exe >nul 2>nul
    if %errorlevel% equ 0 (
        set "PS_CMD=powershell.exe"
    ) else (
        echo [FEHLER] Keine PowerShell auf diesem Windows-System gefunden.
        echo Bitte installieren Sie PowerShell oder führen Sie die Anwendung direkt aus.
        echo.
        pause
        exit /b 1
    )
)

echo Starte Installations-Assistent...
echo.

%PS_CMD% -NoProfile -ExecutionPolicy Bypass -File ".\installer\Install-WMC.ps1" %SILENT_ARG%

if %errorlevel% neq 0 (
    echo.
    echo =====================================================================
    echo [HINWEIS] Der grafische Assistent konnte nicht gestartet werden.
    echo Fehlercode: %errorlevel%
    echo.
    echo Fuehre automatische Standard-Installation im Textmodus aus...
    echo =====================================================================
    echo.
    %PS_CMD% -NoProfile -ExecutionPolicy Bypass -File ".\installer\Install-WMC.ps1" -Silent
    if !errorlevel! equ 0 (
        echo [OK] Installation im Standardverzeichnis erfolgreich abgeschlossen!
    ) else (
        echo [FEHLER] Auch die Hintergrund-Installation meldet einen Fehler.
    )
)

echo.
echo Setup-Vorgang abgeschlossen.
pause

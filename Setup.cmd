@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"
title Windows Maintenance Center - Installation

echo =====================================================================
echo  Windows Maintenance Center v1.0.0
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
        echo [FEHLER] Keine PowerShell auf diesem System gefunden.
        pause
        exit /b 1
    )
)

echo Starte Installations-Assistent...
%PS_CMD% -NoProfile -ExecutionPolicy Bypass -File ".\installer\Install-WMC.ps1" %SILENT_ARG%
if %errorlevel% neq 0 (
    echo.
    echo Ein Fehler ist beim Ausfuehren des Installations-Assistenten aufgetreten.
    pause
    exit /b %errorlevel%
)

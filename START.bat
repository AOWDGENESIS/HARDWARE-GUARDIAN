@echo off
chcp 65001 >nul 2>nul
setlocal enabledelayedexpansion
cd /d "%~dp0"
title HARDWARE GUARDIAN / Windows Maintenance Center - Start-Routine

if exist "WindowsMaintenanceCenter\START.bat" (
    cd WindowsMaintenanceCenter
    call "START.bat" %*
    exit /b !errorlevel!
)

echo [FEHLER] WindowsMaintenanceCenter\START.bat nicht gefunden.
pause
exit /b 1

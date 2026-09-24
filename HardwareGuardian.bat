@echo off
chcp 65001 >nul 2>nul
setlocal
cd /d "%~dp0"
title Hardware Guardian - Starter

echo =====================================================================
echo  Hardware Guardian v1.0.0
echo =====================================================================
echo.

if exist "%~dp0START.bat" (
    call "%~dp0START.bat" %*
) else if exist "%~dp0Setup.cmd" (
    call "%~dp0Setup.cmd" %*
) else (
    echo [FEHLER] Start-Dateien nicht gefunden.
)

if %errorlevel% neq 0 (
    pause
)

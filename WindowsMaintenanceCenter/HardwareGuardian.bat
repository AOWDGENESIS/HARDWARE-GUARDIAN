@echo off
setlocal
cd /d "%~dp0"
title Hardware Guardian / Windows Maintenance Center

where pwsh.exe >nul 2>nul
if %errorlevel% equ 0 (
    set "PS=pwsh.exe"
) else (
    set "PS=powershell.exe"
)

if "%~1"=="" (
    call "%~dp0Setup.cmd"
) else (
    call "%~dp0Setup.cmd" %*
)

@echo off
chcp 65001 >nul 2>nul
cd /d "%~dp0"
call "%~dp0Setup.cmd" %*
if %errorlevel% neq 0 (
    pause
)

@echo off
setlocal
if not exist "%~dp0scripts\Check-Videos.ps1" (
    echo Extract the entire ZIP first, then run check-videos.cmd.
    pause
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Check-Videos.ps1" %*
if errorlevel 1 (
    echo Check failed. Files and settings were not changed.
    pause
    exit /b 1
)
pause
exit /b 0

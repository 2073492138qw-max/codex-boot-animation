@echo off
setlocal
if not exist "%~dp0scripts\Diagnose.ps1" (
    echo Extract the entire ZIP first, then run check.cmd.
    pause
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Diagnose.ps1"
if errorlevel 1 (
    echo.
    echo Check failed. Read the error above; plugin and startup settings were not changed.
    pause
    exit /b 1
)
pause
exit /b 0

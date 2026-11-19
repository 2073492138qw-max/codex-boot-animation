@echo off
setlocal
if not exist "%~dp0scripts\Start-Standalone.ps1" (
    echo Extract the entire ZIP to a folder first, then run start.cmd.
    pause
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-Standalone.ps1"
if errorlevel 1 (
    echo.
    echo Could not start Codex Boot Animation. Read the error above.
    pause
    exit /b 1
)
echo.
echo Started! You can close this window now.
pause
exit /b 0

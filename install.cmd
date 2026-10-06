@echo off
setlocal
if not exist "%~dp0scripts\Install.ps1" (
    echo Extract the entire ZIP to a permanent folder first, then run install.cmd.
    pause
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Install.ps1"
if errorlevel 1 (
    echo.
    echo Installation stopped. Read the error above; no existing setup was silently replaced.
    pause
    exit /b 1
)
echo.
echo Installation finished. Review the Codex hook trust prompt if shown.
pause
exit /b 0

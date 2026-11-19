@echo off
setlocal
if not exist "%~dp0scripts\Install-Standalone.ps1" (
    echo Extract the entire ZIP to a permanent folder first, then run install.cmd.
    pause
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Install-Standalone.ps1"
if errorlevel 1 (
    echo.
    echo Installation stopped. Read the error above; no existing setup was silently replaced.
    pause
    exit /b 1
)
echo.
echo Installation finished! All 5 scenes work automatically without Codex hook setup.
echo Keep this folder in place; right-click the tray icon to customize videos or settings.
pause
exit /b 0

@echo off
setlocal
if not exist "%~dp0scripts\Uninstall-Companion.ps1" (
    echo Uninstall script not found. Run this from the extracted folder.
    pause
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Uninstall-Companion.ps1" -PluginRoot "%~dp0plugins\codex-boot-animation"
if errorlevel 1 (
    echo.
    echo Uninstall stopped. Read the error above.
    pause
    exit /b 1
)
echo.
echo Stopped and removed Windows sign-in autostart. Personal videos and settings were preserved.
pause
exit /b 0

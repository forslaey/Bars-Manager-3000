@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Git-Sync.ps1" -Action Download %*
set "syncExit=%errorlevel%"
echo.
pause
exit /b %syncExit%

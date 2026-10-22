@echo off
setlocal
if not exist "%~dp0Git-Sync.ps1" (
  echo ERROR: Missing file: "%~dp0Git-Sync.ps1"
  echo Keep Git-Download.cmd, Git-Upload.cmd and Git-Sync.ps1 together.
  echo Restore Git-Sync.ps1 from the complete project on GitHub.
  echo.
  pause
  exit /b 2
)
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Git-Sync.ps1" -Action Download %*
set "syncExit=%errorlevel%"
echo.
pause
exit /b %syncExit%

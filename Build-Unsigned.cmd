@echo off
setlocal
if not exist "%~dp0tools\Build-OneFileRust.ps1" (
  echo ERROR: tools\Build-OneFileRust.ps1 is missing. Keep this launcher in the complete project folder.
  pause
  exit /b 2
)
echo Building BARS Manager 3000...
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Build-OneFileRust.ps1" -OutputDirectory "%~dp0bin"
set "BuildResult=%ERRORLEVEL%"
echo.
if not "%BuildResult%"=="0" (
  echo BUILD FAILED. Read the error above.
) else (
  echo BUILD COMPLETE. Output folder: "%~dp0bin"
)
echo.
pause
exit /b %BuildResult%

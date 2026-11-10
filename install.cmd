@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
set "RC=%ERRORLEVEL%"
echo.
if not "%RC%"=="0" (
  echo Installation failed. Please read the message above.
) else (
  echo Installation finished.
  echo You can close this window.
)
pause
exit /b %RC%

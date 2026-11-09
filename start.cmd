@echo off
setlocal
set "STEAM=C:\Program Files (x86)\Steam"
for /f "tokens=2,*" %%A in ('reg query "HKCU\Software\Valve\Steam" /v SteamPath 2^>nul ^| find "SteamPath"') do set "STEAM=%%B"
set "STEAM=%STEAM:/=\%"
if not exist "%STEAM%\steam.exe" set "STEAM=C:\Program Files (x86)\Steam"

tasklist /FI "IMAGENAME eq steam.exe" 2>nul | find /I "steam.exe" >nul
if errorlevel 1 (
  start "" "%STEAM%\steam.exe" -cef-enable-debugging
  timeout /t 10 /nobreak >nul
)

curl -s --max-time 2 -o nul "http://[::1]:8080/json/version"
if errorlevel 1 (
  echo.
  echo [!] Steam debug port is not reachable.
  echo     Close Steam completely, then run this shortcut again.
  echo.
  timeout /t 6 >nul
)

start "" "%~dp0SteamKoreanKeyboard.exe" --full

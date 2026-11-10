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

call :cdp_open
if errorlevel 1 (
  echo.
  echo [!] Steam is running, but the debug port is closed.
  echo     Steam must be fully restarted once after installation.
  echo     Close Steam from the tray menu, then run this shortcut again.
  echo.
  timeout /t 12 /nobreak >nul
)

start "" "%~dp0SteamKoreanKeyboard.exe" --full
exit /b 0

:cdp_open
rem A plain HTTP 200 is not enough: another local service may own port 8080.
rem Steam's CDP version document always carries "Protocol-Version".
curl -s --max-time 2 "http://127.0.0.1:8080/json/version" 2>nul | findstr /C:"Protocol-Version" >nul && exit /b 0
curl -s --max-time 2 "http://[::1]:8080/json/version" 2>nul | findstr /C:"Protocol-Version" >nul && exit /b 0
exit /b 1

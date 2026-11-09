@echo off
setlocal
pushd "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

"%CSC%" /nologo /target:winexe /codepage:65001 /out:SteamKoreanKeyboard.exe ^
  /win32icon:app.ico /resource:payload.js,SteamOskKorean.payload.js ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
  Program.cs Config.cs Log.cs NativeMethods.cs CdpClient.cs InputService.cs VirtualKeyboardInjector.cs Engine.cs ControlServer.cs TrayContext.cs Startup.cs DebugSetup.cs Diagnostics.cs

if errorlevel 1 (
  echo Build failed.
  if not defined CI pause
  popd
  exit /b 1
)

echo Built SteamKoreanKeyboard.exe
popd

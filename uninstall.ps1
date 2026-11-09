param([string]$SteamPath)

$ErrorActionPreference = 'SilentlyContinue'

$steam = $SteamPath
if (-not $steam) {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
}
if (-not $steam) { $steam = 'C:\Program Files (x86)\Steam' }
$steam = $steam -replace '/', '\'
$target = Join-Path $steam 'steam-osk-korean'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -SteamPath "' + $steam + '"'
    Start-Process powershell -Verb RunAs -ArgumentList $arguments
    exit
}

Stop-Process -Name SteamKoreanKeyboard -Force
Start-Sleep -Milliseconds 400

$flag = Join-Path $steam '.cef-enable-remote-debugging'
if (Test-Path $flag) { Remove-Item $flag -Force }

if (Test-Path $target) { Remove-Item $target -Recurse -Force }

$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktop 'Steam (Korean).lnk'
if (Test-Path $shortcutPath) { Remove-Item $shortcutPath -Force }

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'SteamKoreanKeyboard' -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Uninstalled.' -ForegroundColor Yellow
Write-Host 'Steam files were not modified.'

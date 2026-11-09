param([string]$SteamPath)

$ErrorActionPreference = 'Stop'

$src = Split-Path -Parent $PSCommandPath

# Resolve as the current user first: after UAC elevation HKCU may belong to a
# different account, so the path is passed through to the elevated run.
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

if (-not (Test-Path (Join-Path $steam 'steam.exe'))) {
    Write-Host ''
    Write-Host ('Steam을 찾지 못했습니다: ' + $steam) -ForegroundColor Red
    Write-Host 'Steam을 한 번 실행한 뒤 다시 시도해 주세요.'
    exit 1
}

New-Item -ItemType Directory -Force -Path $target | Out-Null
foreach ($file in @('SteamKoreanKeyboard.exe', 'payload.js', 'start.cmd', 'uninstall.cmd', 'uninstall.ps1', 'README.md')) {
    Copy-Item (Join-Path $src $file) $target -Force
}

# Let Steam open its local debug port on every launch (same marker file that
# Steam homebrew tools use).
$flag = Join-Path $steam '.cef-enable-remote-debugging'
if (-not (Test-Path $flag)) {
    New-Item -ItemType File -Path $flag -Force | Out-Null
}

$desktop = [Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktop 'Steam (Korean).lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $target 'start.cmd'
$shortcut.WorkingDirectory = $target
$shortcut.IconLocation = (Join-Path $target 'SteamKoreanKeyboard.exe') + ',0'
$shortcut.Description = 'Steam Korean virtual keyboard'
$shortcut.Save()

# Keep an existing "run at sign-in" entry pointing at the installed copy.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty -Path $runKey -Name 'SteamKoreanKeyboard' -ErrorAction SilentlyContinue) {
    Set-ItemProperty -Path $runKey -Name 'SteamKoreanKeyboard' -Value ('"' + (Join-Path $target 'SteamKoreanKeyboard.exe') + '" --full')
}

Write-Host ''
Write-Host 'Installed to:' $target -ForegroundColor Green
Write-Host 'Shortcut    :' $shortcutPath -ForegroundColor Green
Write-Host ''
Write-Host 'Run "Steam (Korean)" from the desktop.'

param(
    [string]$SteamPath,
    [string]$SourcePath,
    [string]$DesktopPath
)

$ErrorActionPreference = 'Stop'

$src = $SourcePath
if (-not $src) { $src = Split-Path -Parent $PSCommandPath }

function Find-SteamPath {
    param([string]$Hint)

    if ($Hint) { return $Hint }

    $path = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if (-not $path) {
        $path = (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -ErrorAction SilentlyContinue).InstallPath
    }
    if (-not $path) {
        $path = (Get-ItemProperty 'HKLM:\SOFTWARE\Valve\Steam' -ErrorAction SilentlyContinue).InstallPath
    }
    if (-not $path) { $path = 'C:\Program Files (x86)\Steam' }
    return $path
}

# Resolve as the current user first: after UAC elevation HKCU and the desktop
# may belong to a different account, so both are passed through.
$steam = (Find-SteamPath $SteamPath) -replace '/', '\'
$target = Join-Path $steam 'steam-osk-korean'

$desktop = $DesktopPath
if (-not $desktop) { $desktop = [Environment]::GetFolderPath('Desktop') }

$files = @('SteamKoreanKeyboard.exe', 'payload.js', 'start.cmd', 'uninstall.cmd', 'uninstall.ps1', 'README.md')

if (-not (Test-Path (Join-Path $steam 'steam.exe'))) {
    Write-Host ''
    Write-Host ('Steam을 찾지 못했습니다: ' + $steam) -ForegroundColor Red
    Write-Host 'Steam을 한 번 실행한 뒤 다시 시도해 주세요.'
    exit 1
}

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# Can the current user write to the Steam folder? If yes, no elevation is
# needed, which also avoids the "elevated account cannot read the user's
# Downloads folder" problem.
$canWrite = $false
$probe = Join-Path $steam ('.osk-write-test-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType File -Path $probe -Force | Out-Null
    Remove-Item $probe -Force
    $canWrite = $true
}
catch {
    $canWrite = $false
}

if (-not $admin -and -not $canWrite) {
    # Stage the files somewhere every account can read, then elevate once.
    $stage = Join-Path $env:Public 'steam-osk-korean-stage'
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    foreach ($file in $files) {
        Copy-Item (Join-Path $src $file) $stage -Force
    }

    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"' +
                 ' -SteamPath "' + $steam + '"' +
                 ' -SourcePath "' + $stage + '"' +
                 ' -DesktopPath "' + $desktop + '"'
    try {
        Start-Process powershell -Verb RunAs -ArgumentList $arguments -Wait
    }
    catch {
        Write-Host ''
        Write-Host '설치가 취소되었습니다.' -ForegroundColor Yellow
    }
    exit
}

# Remember whether Steam already had the port open, so the final message can
# tell the user whether a restart is required.
$steamRunning = [bool](Get-Process steam -ErrorAction SilentlyContinue)
$portOpen = $false
try {
    Invoke-WebRequest -UseBasicParsing -TimeoutSec 2 'http://127.0.0.1:8080/json/version' | Out-Null
    $portOpen = $true
}
catch {
    try {
        Invoke-WebRequest -UseBasicParsing -TimeoutSec 2 'http://[::1]:8080/json/version' | Out-Null
        $portOpen = $true
    }
    catch {
    }
}

# The exe is locked while the tray app is running.
Get-Process SteamKoreanKeyboard -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

New-Item -ItemType Directory -Force -Path $target | Out-Null
foreach ($file in $files) {
    Copy-Item (Join-Path $src $file) $target -Force
}

# Let Steam open its local debug port on every launch (same marker file that
# Steam homebrew tools use).
$flag = Join-Path $steam '.cef-enable-remote-debugging'
if (-not (Test-Path $flag)) {
    New-Item -ItemType File -Path $flag -Force | Out-Null
}

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

if ($steamRunning -and -not $portOpen) {
    Write-Host 'Steam이 이미 실행 중입니다.' -ForegroundColor Yellow
    Write-Host 'Steam을 트레이 메뉴에서 완전히 종료한 뒤 다시 실행해야 적용됩니다.' -ForegroundColor Yellow
    Write-Host '(Steam은 켤 때만 표시 파일을 읽습니다.)' -ForegroundColor Yellow
}
elseif (-not $steamRunning) {
    Write-Host '바탕화면의 "Steam (Korean)" 바로가기로 Steam과 앱을 함께 실행하세요.' -ForegroundColor Green
}
else {
    Write-Host 'Steam 연결이 이미 열려 있습니다. 바탕화면의 "Steam (Korean)" 바로가기로 앱을 실행하세요.' -ForegroundColor Green
}

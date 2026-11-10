param(
    [string]$SteamPath,
    [string]$DesktopPath
)

$ErrorActionPreference = 'SilentlyContinue'

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

$steam = (Find-SteamPath $SteamPath) -replace '/', '\'
$target = Join-Path $steam 'steam-osk-korean'

$desktop = $DesktopPath
if (-not $desktop) { $desktop = [Environment]::GetFolderPath('Desktop') }

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

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
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"' +
                 ' -SteamPath "' + $steam + '"' +
                 ' -DesktopPath "' + $desktop + '"'
    try {
        $child = Start-Process powershell -Verb RunAs -ArgumentList $arguments -Wait -PassThru -ErrorAction Stop
    }
    catch {
        Write-Host ''
        Write-Host '삭제가 취소되었습니다. (관리자 권한 승인 필요)' -ForegroundColor Yellow
        exit 1
    }

    if ($child.ExitCode -ne 0) {
        Write-Host ''
        Write-Host '삭제에 실패했습니다.' -ForegroundColor Red
        Write-Host 'Steam을 완전히 종료한 뒤 다시 시도해 주세요.'
        exit $child.ExitCode
    }

    Write-Host ''
    Write-Host 'Uninstalled.' -ForegroundColor Yellow
    Write-Host 'Steam files were not modified.'
    exit 0
}

Stop-Process -Name SteamKoreanKeyboard -Force
Start-Sleep -Milliseconds 400

$flag = Join-Path $steam '.cef-enable-remote-debugging'
if (Test-Path $flag) { Remove-Item $flag -Force }

if (Test-Path $target) { Remove-Item $target -Recurse -Force }

$shortcutPath = Join-Path $desktop 'Steam (Korean).lnk'
if (Test-Path $shortcutPath) { Remove-Item $shortcutPath -Force }

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'SteamKoreanKeyboard' -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Uninstalled.' -ForegroundColor Yellow
Write-Host 'Steam files were not modified.'
exit 0

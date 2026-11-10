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

# A plain HTTP 200 on 8080 is not proof of Steam: another local service can
# own the port. The body must look like a CDP version document.
function Test-CdpPort {
    foreach ($url in @('http://127.0.0.1:8080/json/version', 'http://[::1]:8080/json/version')) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -TimeoutSec 2 $url
            if ($response.Content -match '"Browser"\s*:') { return $true }
        }
        catch {
        }
    }

    return $false
}

function Show-Result {
    param([string]$Target, [string]$ShortcutPath)

    Write-Host ''
    Write-Host 'Installed to:' $Target -ForegroundColor Green
    Write-Host 'Shortcut    :' $ShortcutPath -ForegroundColor Green
    Write-Host ''

    $steamRunning = [bool](Get-Process steam -ErrorAction SilentlyContinue)
    $portOpen = Test-CdpPort

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
}

# Resolve as the current user first: after UAC elevation HKCU and the desktop
# may belong to a different account, so both are passed through.
$steam = (Find-SteamPath $SteamPath) -replace '/', '\'
$target = Join-Path $steam 'steam-osk-korean'

$desktop = $DesktopPath
if (-not $desktop) { $desktop = [Environment]::GetFolderPath('Desktop') }

$shortcutPath = Join-Path $desktop 'Steam (Korean).lnk'
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
    # Stage everything the elevated copy needs, including this script: the
    # administrator account may not be able to read the user's folder.
    $stage = Join-Path $env:Public 'steam-osk-korean-stage'
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Copy-Item $PSCommandPath (Join-Path $stage 'install.ps1') -Force
    foreach ($file in $files) {
        $item = Join-Path $src $file
        if (-not (Test-Path $item)) {
            Write-Host ''
            Write-Host ('설치 파일이 없습니다: ' + $item) -ForegroundColor Red
            Write-Host 'zip 압축을 모두 푼 뒤 다시 실행해 주세요.'
            exit 1
        }

        Copy-Item $item $stage -Force
    }

    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $stage 'install.ps1') + '"' +
                 ' -SteamPath "' + $steam + '"' +
                 ' -SourcePath "' + $stage + '"' +
                 ' -DesktopPath "' + $desktop + '"'
    try {
        $child = Start-Process powershell -Verb RunAs -ArgumentList $arguments -Wait -PassThru -ErrorAction Stop
    }
    catch {
        Write-Host ''
        Write-Host '설치가 취소되었습니다. (관리자 권한 승인 필요)' -ForegroundColor Yellow
        Write-Host '같은 방법으로 다시 실행해 주세요.'
        exit 1
    }

    if ($child.ExitCode -ne 0) {
        Write-Host ''
        Write-Host '설치에 실패했습니다.' -ForegroundColor Red
        Write-Host 'Steam 폴더에 파일을 쓸 수 없거나 앱을 교체하지 못했습니다.'
        Write-Host 'Steam을 완전히 종료한 뒤 다시 시도해 주세요.'
        exit $child.ExitCode
    }

    if (-not (Test-Path (Join-Path $target 'SteamKoreanKeyboard.exe'))) {
        Write-Host ''
        Write-Host '설치 결과를 확인하지 못했습니다.' -ForegroundColor Red
        Write-Host ('경로: ' + $target)
        exit 1
    }

    Show-Result -Target $target -ShortcutPath $shortcutPath
    exit 0
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
    Set-ItemProperty -Path $runKey -Name 'SteamKoreanKeyboard' -Value ('"' + (Join-Path $target 'SteamKoreanKeyboard.exe') + '" --full --startup')
}

Show-Result -Target $target -ShortcutPath $shortcutPath
exit 0

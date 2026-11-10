# Fresh-PC simulation for SteamKoreanKeyboard.
#
# Reproduces the first-run situations of a clean Windows install without a
# second PC or a VM:
#   * Steam folder writable by Users  -> no UAC, marker created
#   * Steam folder writable only by Administrators (Program Files ACL model)
#     -> the medium-integrity process cannot write, same as a fresh PC
#   * missing steam.exe, marker-name collision, already-prepared PC
#   * the app must keep running when the marker cannot be created
#
# Safe: every Steam root lives under %TEMP%; the real Steam folder and HKCU are
# never modified. The tray app is stopped for the "app keeps running" test and
# started again afterwards unless -NoAppRestart is given.

param(
    [string]$ExePath,
    [switch]$Keep,
    [switch]$NoAppRestart,
    [switch]$ElevationTest
)

$ErrorActionPreference = 'Stop'

if (-not $ExePath) {
    $ExePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'SteamKoreanKeyboard.exe'
}

$ExePath = (Resolve-Path $ExePath).Path
$installedExe = 'C:\Program Files (x86)\Steam\steam-osk-korean\SteamKoreanKeyboard.exe'
$realSteam = 'C:\Program Files (x86)\Steam'
$root = Join-Path $env:TEMP ('osk-sim-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$logPath = Join-Path $env:LOCALAPPDATA 'SteamKoreanKeyboard\SteamKoreanKeyboard.log'

$results = New-Object System.Collections.ArrayList
$wasRunning = [bool](Get-Process SteamKoreanKeyboard -ErrorAction SilentlyContinue)

function Add-Result {
    param([string]$Name, [bool]$Pass, [string]$Detail)

    $state = if ($Pass) { 'PASS' } else { 'FAIL' }
    [void]$results.Add([pscustomobject]@{ State = $state; Test = $Name; Detail = $Detail })
    Write-Host ('  [' + $state + '] ' + $Name + '  ' + $Detail)
}

function New-SteamRoot {
    param([string]$Path)

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    Set-Content -Path (Join-Path $Path 'steam.exe') -Value 'dummy' -Encoding ASCII
}

# Program Files model: Users may read/execute, only administrators may write.
# The current process has a UAC-filtered token, so its Administrators group is
# deny-only and the ACL check behaves exactly like a fresh Windows install.
function Set-ProtectedAcl {
    param([string]$Path)

    & icacls $Path /inheritance:r /grant:r ('*S-1-5-18:(OI)(CI)F') ('*S-1-5-32-544:(OI)(CI)F') ('*S-1-5-32-545:(OI)(CI)RX') /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls failed for $Path" }
}

function Invoke-SetupChild {
    param([string]$SteamRoot, [int]$TimeoutSeconds = 20)

    $process = Start-Process -FilePath $ExePath `
        -ArgumentList '--enable-debug', '--quiet', '--steam', ('"' + $SteamRoot + '"') `
        -PassThru
    $finished = $process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $finished) {
        $process.Kill()
        return @{ Code = -1; Seconds = $TimeoutSeconds; TimedOut = $true }
    }

    return @{ Code = $process.ExitCode; Seconds = 0; TimedOut = $false }
}

function Get-MarkerTime {
    param([string]$Path)

    $marker = Join-Path $Path '.cef-enable-remote-debugging'
    if (-not (Test-Path $marker)) { return $null }
    return (Get-Item $marker).LastWriteTimeUtc.Ticks
}

function Read-NewLog {
    param([long]$Offset)

    if (-not (Test-Path $logPath)) { return '' }
    $stream = [IO.File]::Open($logPath, 'Open', 'Read', 'ReadWrite')
    try {
        $length = $stream.Length
        if ($length -le $Offset) { return '' }
        [void]$stream.Seek($Offset, 'Begin')
        $buffer = New-Object byte[] ($length - $Offset)
        [void]$stream.Read($buffer, 0, $buffer.Length)
        return [Text.Encoding]::UTF8.GetString($buffer)
    }
    finally {
        $stream.Dispose()
    }
}

Write-Host ''
Write-Host '=== Fresh-PC simulation (SteamKoreanKeyboard 1.0.7) ==='
Write-Host ('exe : ' + $ExePath)
Write-Host ('temp: ' + $root)
Write-Host ''

New-Item -ItemType Directory -Force -Path $root | Out-Null

$realMarkerBefore = Get-MarkerTime $realSteam
$logOffset = 0
if (Test-Path $logPath) { $logOffset = (Get-Item $logPath).Length }

try {
    # 1. Clean PC, Steam folder writable by the user.
    $writable = Join-Path $root 'writable-steam'
    New-SteamRoot $writable
    $run = Invoke-SetupChild $writable
    $marker = Test-Path (Join-Path $writable '.cef-enable-remote-debugging')
    Add-Result 'fresh PC / writable folder' ($run.Code -eq 0 -and $marker) ('exit=' + $run.Code + ' marker=' + $marker)

    # 2. Second launch on the same PC.
    $before = Get-MarkerTime $writable
    Start-Sleep -Milliseconds 1200
    $run = Invoke-SetupChild $writable
    $after = Get-MarkerTime $writable
    Add-Result 'second launch / already prepared' ($run.Code -eq 0 -and $before -eq $after) ('exit=' + $run.Code + ' unchanged=' + ($before -eq $after))

    # 3. Fresh PC where Steam lives elsewhere (wrong --steam must not fall back
    #    to this machine's HKCU; that fallback was the reported bug).
    $run = Invoke-SetupChild (Join-Path $root 'no-steam-here')
    $realMarkerAfter = Get-MarkerTime $realSteam
    Add-Result 'steam.exe missing' ($run.Code -eq 3) ('exit=' + $run.Code + ' (expected 3)')
    Add-Result 'real Steam folder untouched' ($realMarkerBefore -eq $realMarkerAfter) ('real marker unchanged=' + ($realMarkerBefore -eq $realMarkerAfter))

    # 4. A folder with the marker name (File.Create would say "access denied").
    $collision = Join-Path $root 'collision-steam'
    New-SteamRoot $collision
    New-Item -ItemType Directory -Force -Path (Join-Path $collision '.cef-enable-remote-debugging') | Out-Null
    $run = Invoke-SetupChild $collision
    Add-Result 'marker name taken by a folder' ($run.Code -eq 2) ('exit=' + $run.Code + ' (expected 2)')

    # 5. Fresh PC, Program Files ACL (Users read-only).
    $protected = Join-Path $root 'protected-steam'
    New-SteamRoot $protected
    Set-ProtectedAcl $protected | Out-Null
    $run = Invoke-SetupChild $protected
    $marker = Test-Path (Join-Path $protected '.cef-enable-remote-debugging')
    Add-Result 'fresh PC / protected folder' ($run.Code -eq 2 -and -not $marker) ('exit=' + $run.Code + ' marker=' + $marker)

    # 6. Same protected folder with the app itself (--startup: no UAC, must
    #    keep running and explain the problem instead of exiting).
    Get-Process SteamKoreanKeyboard -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800

    $app = Start-Process -FilePath $ExePath -ArgumentList '--startup', '--full', '--steam', ('"' + $protected + '"') -PassThru
    Start-Sleep -Seconds 6
    $alive = -not $app.HasExited
    $tail = Read-NewLog $logOffset
    $skipped = ($tail -match 'elevation skipped or cancelled')
    Add-Result 'app survives a protected folder' ($alive -and $skipped) ('alive=' + $alive + ' logged=' + $skipped)

    if ($alive) { $app.Kill() }
    Start-Sleep -Milliseconds 500

    # 7. Logs must not land in the install folder.
    $stray = Join-Path (Split-Path -Parent $ExePath) 'SteamKoreanKeyboard.log'
    $strayIsOld = (Test-Path $stray) -and ((Get-Item $stray).LastWriteTime -lt (Get-Date).AddMinutes(-2))
    Add-Result 'log outside the install folder' ((-not (Test-Path $stray)) -or $strayIsOld) ('new log=' + $logPath)

    # 8. Optional: the real UAC path. Needs one human click on the prompt.
    if ($ElevationTest) {
        $elev = Join-Path $root 'elevation-steam'
        New-SteamRoot $elev
        Set-ProtectedAcl $elev | Out-Null

        Get-Process SteamKoreanKeyboard -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Milliseconds 800
        $elevOffset = 0
        if (Test-Path $logPath) { $elevOffset = (Get-Item $logPath).Length }

        Write-Host ''
        Write-Host '*** UAC: approve the prompt once (this is the real elevation test). ***'
        Write-Host ''

        $app = Start-Process -FilePath $ExePath -ArgumentList '--full', '--steam', ('"' + $elev + '"') -PassThru
        $marker = Join-Path $elev '.cef-enable-remote-debugging'
        $deadline = (Get-Date).AddSeconds(60)
        while ((Get-Date) -lt $deadline -and -not (Test-Path $marker) -and -not $app.HasExited) {
            Start-Sleep -Milliseconds 500
        }

        $created = Test-Path $marker
        $elevLog = Read-NewLog $elevOffset
        $byChild = ($elevLog -match 'created by elevated child')
        Add-Result 'elevation creates the marker' ($created -and $byChild) ('marker=' + $created + ' byChild=' + $byChild)

        if (-not $app.HasExited) { $app.Kill() }
    }
}
finally {
    # The elevation test leaves a marker created by the elevated process: its
    # owner is Administrators and it inherits Users:(RX), so the filtered token
    # cannot change or delete it directly. Restore access, then let cmd remove
    # the tree (DELETE_CHILD on the parent is enough).
    if (Test-Path $root) {
        $me = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        & icacls $root /reset /T /C 2>$null | Out-Null
        & icacls $root /grant ($me + ':(OI)(CI)F') /T /C 2>$null | Out-Null
    }

    Get-Process SteamKoreanKeyboard -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500

    if (-not $Keep -and (Test-Path $root)) {
        & cmd /c rmdir /s /q "$root" 2>$null
        if (Test-Path $root) {
            # Harmless leftovers: %TEMP% is cleaned by Windows.
            Write-Host ('[note] could not fully remove ' + $root)
        }
    }

    if ($wasRunning -and -not $NoAppRestart -and (Test-Path $installedExe)) {
        Start-Process -FilePath $installedExe -ArgumentList '--full'
        Write-Host ''
        Write-Host 'Installed app restarted.'
    }
}

Write-Host ''
$failed = @($results | Where-Object { $_.State -eq 'FAIL' }).Count
$results | Format-Table -AutoSize
Write-Host ('Result: ' + ($results.Count - $failed) + '/' + $results.Count + ' passed')
if ($failed -gt 0) { exit 1 }
exit 0

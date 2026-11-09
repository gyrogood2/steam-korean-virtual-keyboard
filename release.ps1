param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath
$version = (Select-String -Path (Join-Path $root 'Config.cs') -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value

Push-Location $root
try {
    cmd /c build.cmd
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }

    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null

    foreach ($file in @('SteamKoreanKeyboard.exe', 'payload.js', 'start.cmd', 'install.cmd', 'install.ps1',
                        'uninstall.cmd', 'uninstall.ps1', 'README.md', 'LICENSE')) {
        Copy-Item (Join-Path $root $file) $dist -Force
    }

    $zip = Join-Path $root ('SteamKoreanKeyboard-' + $version + '.zip')
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip

    Write-Host ''
    Write-Host ('Release ' + $version + ': ' + $zip) -ForegroundColor Green
}
finally {
    Pop-Location
}

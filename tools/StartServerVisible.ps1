$ErrorActionPreference = 'Stop'
$serverRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server'
if (Get-Process -Name valheim_server -ErrorAction SilentlyContinue) { throw 'Server already running.' }
$line = Get-Content -LiteralPath (Join-Path $serverRoot 'SERVEEER.bat') | Where-Object { $_ -match '^valheim_server\.exe\s' } | Select-Object -First 1
if (-not $line) { throw 'Original server launch settings not found.' }
$tokens = [regex]::Matches($line, '"[^"]*"|\S+')
$launchArgs = @($tokens | Select-Object -Skip 1 | ForEach-Object { $_.Value.Trim('"') })
if ($launchArgs -notcontains '-crossplay') { $launchArgs += '-crossplay' }
$launchArgs += @('-logFile', (Join-Path $serverRoot 'ValheimMastery_server_1.4.112.log'))
$env:SteamAppId = '892970'
Set-Location -LiteralPath $serverRoot
$Host.UI.RawUI.WindowTitle = 'Valheim dedicated server — Mastery 1.4.112'
Write-Host 'Server console. Use Ctrl+C for a graceful save and shutdown.'
& (Join-Path $serverRoot 'valheim_server.exe') @launchArgs

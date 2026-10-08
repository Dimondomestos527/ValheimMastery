@echo off
setlocal
set "VM_ACTION=install"
set "VM_SCRIPT=%~f0"
set "VM_TARGET=%~1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$t=[IO.File]::ReadAllText($env:VM_SCRIPT); $marker='# POWERSHELL'; $script=$t.Substring($t.LastIndexOf($marker)+$marker.Length); & ([ScriptBlock]::Create($script))"
set "VM_RESULT=%ERRORLEVEL%"
pause
exit /b %VM_RESULT%
# POWERSHELL
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$repo = 'Dimondomestos527/ValheimMastery'
$headers = @{ 'User-Agent' = 'ValheimMastery-Installer/2' }
$commit = (Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/commits/Release" -Headers $headers).sha
if ($commit -notmatch '^[0-9a-f]{40}$') { throw 'Invalid Release commit' }
$base = "https://raw.githubusercontent.com/$repo/$commit"
$manifest = Invoke-RestMethod -Uri "$base/installer/bootstrap.json" -Headers $headers
if ($manifest.schema -ne 2) { throw 'Unsupported installer manifest' }
$cacheRoot = Join-Path $env:LOCALAPPDATA 'ValheimMasteryInstaller'
if (Test-Path -LiteralPath $cacheRoot) {
 if ((Get-Item -LiteralPath $cacheRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Unsafe cache directory' }
}
$session = Join-Path $cacheRoot ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $session -Force | Out-Null
function Download-Checked($url, $dest, $hash) {
 if ($hash -notmatch '^[0-9a-f]{64}$') { throw 'Invalid checksum' }
 Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $dest -Headers $headers
 if ((Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw 'Download checksum mismatch' }
}
$archive = Join-Path $session 'python.tar.gz'
Download-Checked 'https://github.com/astral-sh/python-build-standalone/releases/download/20261003/cpython-3.13.16%2B20261003-x86_64-pc-windows-msvc-install_only.tar.gz' $archive '5e100ee3d592ff500f4408a624f054d202e32d9dba8a12b2226bef81083fd778'
$tar = Join-Path $env:SystemRoot 'System32\tar.exe'
if (-not (Test-Path -LiteralPath $tar)) { throw 'Windows 10/11 tar.exe is required' }
& $tar -xzf $archive -C $session
if ($LASTEXITCODE -ne 0) { throw 'Private Python extraction failed' }
$python = Join-Path $session 'python\python.exe'
$core = Join-Path $session 'mastery_installer.py'
foreach ($item in @('mastery_installer.py','steam_launch.py','language_setting.py')) {
 Download-Checked "$base/installer/$item" (Join-Path $session $item) $manifest.runtime_files.$item
}
$coreArgs = @($core, $env:VM_ACTION, '--commit', $commit)
if ($env:VM_TARGET) { $coreArgs += @('--target', $env:VM_TARGET) }
& $python -X utf8 @coreArgs
if ($LASTEXITCODE -ne 0) { throw 'Installer stopped; see the message above' }
Write-Host 'Finished. Private installer runtime cache:' $session

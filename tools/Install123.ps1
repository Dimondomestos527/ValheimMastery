$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$serverRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server'
$clientRoot = 'C:\ValheimModDev'
$worldRoot = 'C:\Users\Domesos\AppData\LocalLow\IronGate\Valheim\worlds_local\DEEEP'
function AssertStopped {
    $running = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -in @('valheim.exe','valheim_server.exe') })
    if ($running.Count -ne 0) { throw 'Valheim client or server is running; installation refused. No process will be terminated.' }
}
function AssertCopy($source, $destination) {
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) {
        throw "Copy verification failed: $destination"
    }
}
AssertStopped
$clientBuild = Join-Path $projectRoot 'bin\Perks123Client\ValheimMastery.dll'
$serverBuild = Join-Path $projectRoot 'bin\Perks123Server\ValheimMastery.dll'
$clientTarget = Join-Path $clientRoot 'BepInEx\plugins\ValheimMastery\ValheimMastery.dll'
$serverTarget = Join-Path $serverRoot 'BepInEx\plugins\ValheimMastery\ValheimMastery.dll'
foreach ($build in @($clientBuild,$serverBuild)) {
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($build).FileVersion -ne '1.4.123.0') { throw "Unexpected build version: $build" }
}
$backup = Join-Path $projectRoot ('dist\Backup_pre_1.4.123_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
if (Test-Path -LiteralPath $backup) { throw 'Backup destination already exists.' }
New-Item -ItemType Directory -Path $backup | Out-Null
Copy-Item -LiteralPath $clientTarget -Destination (Join-Path $backup 'Client_previous.dll')
Copy-Item -LiteralPath $serverTarget -Destination (Join-Path $backup 'Server_previous.dll')
AssertCopy $clientTarget (Join-Path $backup 'Client_previous.dll')
AssertCopy $serverTarget (Join-Path $backup 'Server_previous.dll')
foreach ($entry in @(@{Root=$clientRoot;Name='ClientConfig'},@{Root=$serverRoot;Name='ServerConfig'})) {
    $configDir = Join-Path $entry.Root 'BepInEx\config'
    $destination = Join-Path $backup $entry.Name
    New-Item -ItemType Directory -Path $destination | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $configDir -File | Where-Object { $_.Name -match 'mastery' }) {
        $copy = Join-Path $destination $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination $copy
        AssertCopy $file.FullName $copy
    }
    # Gold is a separate store beside the configuration. Preserve it if this installer is rerun.
    $goldStore = Join-Path $configDir 'ValheimMasteryGold'
    if (Test-Path -LiteralPath $goldStore) { Copy-Item -LiteralPath $goldStore -Destination $destination -Recurse }
}
$worldBackup = Join-Path $backup 'World'
New-Item -ItemType Directory -Path $worldBackup | Out-Null
Copy-Item -LiteralPath $worldRoot -Destination $worldBackup -Recurse
$verifiedFiles = 0
foreach ($file in Get-ChildItem -LiteralPath $worldRoot -File -Recurse) {
    $relative = $file.FullName.Substring($worldRoot.Length).TrimStart('\')
    AssertCopy $file.FullName (Join-Path (Join-Path $worldBackup 'DEEEP') $relative)
    $verifiedFiles++
}
AssertStopped
$friendDir = Join-Path $projectRoot 'dist\ValheimMastery_1.4.123_ForFriend'
if (!(Test-Path -LiteralPath $friendDir)) { New-Item -ItemType Directory -Path $friendDir | Out-Null }
try {
    Copy-Item -LiteralPath $clientBuild -Destination $clientTarget
    Copy-Item -LiteralPath $serverBuild -Destination $serverTarget
    Copy-Item -LiteralPath $clientBuild -Destination (Join-Path $friendDir 'ValheimMastery.dll')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'PERKS123_CANDIDATE_TEST_UA.txt') -Destination $friendDir
    AssertCopy $clientBuild $clientTarget
    AssertCopy $serverBuild $serverTarget
    AssertCopy $clientBuild (Join-Path $friendDir 'ValheimMastery.dll')
} catch {
    AssertStopped
    Copy-Item -LiteralPath (Join-Path $backup 'Client_previous.dll') -Destination $clientTarget
    Copy-Item -LiteralPath (Join-Path $backup 'Server_previous.dll') -Destination $serverTarget
    throw
}
AssertStopped
[pscustomobject]@{
    Version = '1.4.123'; Backup = $backup; WorldFilesVerified = $verifiedFiles
    ClientHash = (Get-FileHash -LiteralPath $clientTarget -Algorithm SHA256).Hash
    ServerHash = (Get-FileHash -LiteralPath $serverTarget -Algorithm SHA256).Hash
    FriendHash = (Get-FileHash -LiteralPath (Join-Path $friendDir 'ValheimMastery.dll') -Algorithm SHA256).Hash
    ServerStarted = $false; ClientStarted = $false; LiveStatus = 'LIVE_TEST_REQUIRED'
} | Format-List

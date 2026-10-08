param([ValidateSet('Launch','Status','Stop')][string]$Stage, [ValidatePattern('^[a-z0-9-]+$')][string]$RunId='first')
$ErrorActionPreference='Stop'
$devRoot=Split-Path -Parent $PSScriptRoot
$evidence=Join-Path $devRoot 'validation\unity-smoke-20261004'
$scratch=Join-Path $evidence 'runtimes\server'
$exe=Join-Path $scratch 'valheim_server.exe'
$pidPath=Join-Path $evidence "server-$RunId-process.csv"
if($Stage -eq 'Launch'){
 if(Test-Path $pidPath){throw 'Launch record exists; no repeated startup'}
 if(!(Test-Path (Join-Path $evidence 'copy-verification.csv'))){throw 'Verified copied runtime required'}
 foreach($name in 'valheim_server.exe','UnityPlayer.dll','BepInEx\core\0Harmony.dll','valheim_server_Data\Managed\assembly_valheim.dll','BepInEx\plugins\ValheimMastery\ValheimMastery.dll'){
  $path=Join-Path $scratch $name
  $row=Import-Csv (Join-Path $evidence 'copy-manifest-before.csv')|Where-Object Destination -eq $path
  if(!$row -or (Get-FileHash -LiteralPath $path).Hash -ne $row.SHA256){throw "Prelaunch integrity failure: $path"}
 }
 if(Get-NetUDPEndpoint -LocalPort 27991,27992 -ErrorAction SilentlyContinue){throw 'Scratch ports already occupied'}
 $args='-batchmode -nographics -name "Mastery Unity QC disposable" -world "MasteryQCSmoke20261004" -port 27991 -password "ScratchSmokeOnly123" -public 0 -savedir "'+(Join-Path $scratch 'saves')+'" -logFile "'+(Join-Path $scratch "logs\Unity-$RunId.log")+'"'
 $priorAppId=[Environment]::GetEnvironmentVariable('SteamAppId','Process')
 try {
  $env:SteamAppId='892970'
  $process=Start-Process -FilePath $exe -WorkingDirectory $scratch -ArgumentList $args -WindowStyle Hidden -PassThru
 } finally {[Environment]::SetEnvironmentVariable('SteamAppId',$priorAppId,'Process')}
 [pscustomobject]@{Pid=$process.Id;Exe=$exe;Arguments=$args;StartedUtc=[DateTime]::UtcNow.ToString('o')}|Export-Csv $pidPath -NoTypeInformation
 "Owned scratch server launched PID=$($process.Id)";exit
}
$record=Import-Csv $pidPath
$process=Get-CimInstance Win32_Process -Filter "ProcessId=$($record.Pid)"
if($process -and $process.ExecutablePath -ne $exe){throw 'PID executable identity does not match owned scratch server'}
if($Stage -eq 'Stop'){
 if(!$process){'Owned scratch server already exited';exit}
 $helper=Join-Path $evidence 'GracefulConsoleStop.exe'
 & $helper $record.Pid
 $signalExit=$LASTEXITCODE
 "Owned-console CTRL_C helper exit=$signalExit"
 [pscustomobject]@{Pid=$record.Pid;SignalExit=$signalExit;Utc=[DateTime]::UtcNow.ToString('o')}|Export-Csv (Join-Path $evidence "server-$RunId-stop-request.csv") -NoTypeInformation
 if($signalExit){throw 'Graceful stop signal failed; no forced termination performed'}
 exit
}
 "Owned scratch server running=$([bool]$process) PID=$($record.Pid)"
 if(Test-Path (Join-Path $scratch 'QC_OBSERVER_REPORT.txt')){Get-Content (Join-Path $scratch 'QC_OBSERVER_REPORT.txt')|Where-Object {$_ -notmatch '\| (PREFIX|POSTFIX|TRANSPILER|FINALIZER) \|' }}
 if(Test-Path (Join-Path $scratch 'BepInEx\LogOutput.log')){Get-Content (Join-Path $scratch 'BepInEx\LogOutput.log') -Tail 30}

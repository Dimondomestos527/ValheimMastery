param([ValidateSet('Baseline','Deploy','Launch','Status','Stop','Collect','Integrity','Finalize')][string]$Stage,[ValidateSet('PASS','FAIL')][string]$Result='FAIL')
$ErrorActionPreference='Stop'
$dev=Split-Path -Parent $PSScriptRoot
$root='C:\ValheimModDev'
$e=Join-Path $dev 'validation\controlled-client-smoke-20261004'
$target=Join-Path $root 'BepInEx\plugins\ValheimMastery\ValheimMastery.dll'
$new=Join-Path $dev 'bin\Perks123Client\ValheimMastery.dll'
$observer=Join-Path $root 'BepInEx\plugins\MasteryUnityQcObserver.dll'
$expected='839D03955995ECBC5E3B80698D0E8BB9CE86A6C5072183F38868D74B15EFD409'
function Fingerprint($f){[pscustomobject]@{Path=$f.FullName;SHA256=(Get-FileHash -LiteralPath $f.FullName).Hash;Length=$f.Length;LastWriteUtc=$f.LastWriteTimeUtc.ToString('o')}}
function Files {
 @(Get-ChildItem (Join-Path $root 'BepInEx\config'),'C:\Users\Domesos\AppData\LocalLow\IronGate\Valheim',(Join-Path $dev 'src-modern'),(Join-Path $dev 'artifacts\reference'),(Join-Path $dev 'bin\Perks123Client'),(Join-Path $dev 'bin\Perks123Server') -Recurse -File)+
 @(Get-ChildItem 'C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server' -Recurse -File|Where-Object {!$_.FullName.StartsWith((Join-Path (Split-Path -Parent (Split-Path -Parent $dev)) 'MasteryDev')+'\',[StringComparison]::OrdinalIgnoreCase)})
}
if($Stage -eq 'Baseline'){
 if(Get-CimInstance Win32_Process|Where-Object Name -eq 'valheim.exe'){throw 'Client already running'}
 if(Test-Path $e){throw 'Evidence exists; no overwrites'}
 if(Test-Path $observer){throw 'Observer destination already exists'}
 if(Test-Path (Join-Path $root 'QC_OBSERVER_REPORT.txt')){throw 'Prior observer report exists'}
 New-Item -ItemType Directory -Path $e|Out-Null
 $rollback=Join-Path $e ('rollback-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))
 New-Item -ItemType Directory -Path $rollback|Out-Null
 $original=Fingerprint(Get-Item $target)
 $original|Export-Csv (Join-Path $e 'original-dll.csv') -NoTypeInformation
 Copy-Item -LiteralPath $target -Destination (Join-Path $rollback 'ValheimMastery.dll')
 if((Get-FileHash (Join-Path $rollback 'ValheimMastery.dll')).Hash -ne $original.SHA256){throw 'Rollback hash mismatch'}
 [pscustomobject]@{Path=(Join-Path $rollback 'ValheimMastery.dll')}|Export-Csv (Join-Path $e 'rollback-path.csv') -NoTypeInformation
 foreach($rel in 'BepInEx\LogOutput.log','BepInEx\preloader.log'){$file=Join-Path $root $rel;if(Test-Path $file){Copy-Item -LiteralPath $file -Destination (Join-Path $e ('before-'+(Split-Path -Leaf $file)))}}
 & reg.exe export 'HKCU\Software\IronGate\Valheim' (Join-Path $e 'playerprefs-before.reg') /y
 if($LASTEXITCODE){throw 'Registry baseline export failed'}
 @(foreach($file in Files){Fingerprint $file})|Export-Csv (Join-Path $e 'protected-before.csv') -NoTypeInformation
 $steam=(Get-ItemProperty 'HKCU:\Software\Valve\Steam' -Name SteamPath).SteamPath
 $steamFiles=@(Get-Item (Join-Path $steam 'config\config.vdf'),(Join-Path $steam 'config\loginusers.vdf') -ErrorAction SilentlyContinue)
 foreach($user in Get-ChildItem (Join-Path $steam 'userdata') -Directory -ErrorAction SilentlyContinue){$path=Join-Path $user.FullName '892970';if(Test-Path $path){$steamFiles+=@(Get-ChildItem $path -Recurse -File)}}
 @(foreach($file in $steamFiles){Fingerprint $file})|Export-Csv (Join-Path $e 'steam-readable-before.csv') -NoTypeInformation
 "Baseline done; rollback=$rollback";exit
}
if(!(Test-Path (Join-Path $e 'original-dll.csv'))){throw 'Verified baseline required'}
if($Stage -eq 'Deploy'){
 if(Get-Process -Name valheim -ErrorAction SilentlyContinue){throw 'Client running'}
 if((Get-FileHash $new).Hash -ne $expected){throw 'Rebuilt source hash mismatch'}
 $prior=Import-Csv (Join-Path $e 'original-dll.csv')
 if((Get-FileHash $target).Hash -ne $prior.SHA256){throw 'Current DLL drifted since baseline'}
 Copy-Item -LiteralPath $new -Destination $target -Force
 if((Get-FileHash $target).Hash -ne $expected){throw 'Deployment hash mismatch'}
 if(Test-Path $observer){throw 'Observer already present'}
 Copy-Item -LiteralPath (Join-Path $dev 'qc-unity-observer\bin\Release\netstandard2.1\MasteryUnityQcObserver.dll') -Destination $observer
 Fingerprint(Get-Item $observer)|Export-Csv (Join-Path $e 'observer-installed.csv') -NoTypeInformation
 Fingerprint(Get-Item $target)|Export-Csv (Join-Path $e 'deployed-dll.csv') -NoTypeInformation
 'Rebuilt client + inspection-only observer installed';exit
}
if($Stage -eq 'Launch'){
 if(Test-Path (Join-Path $e 'client-process.csv')){throw 'No repeated launch'}
 if(Get-Process -Name valheim -ErrorAction SilentlyContinue){throw 'Client running'}
 if((Get-FileHash $target).Hash -ne $expected){throw 'Prelaunch DLL mismatch'}
 $exe=Join-Path $root 'valheim.exe'
 $args='-logFile "'+(Join-Path $e 'Unity-client.log')+'"'
 $p=Start-Process -FilePath $exe -WorkingDirectory $root -ArgumentList $args -WindowStyle Hidden -PassThru
 [pscustomobject]@{Pid=$p.Id;Exe=$exe;Arguments=$args;Utc=[DateTime]::UtcNow.ToString('o')}|Export-Csv (Join-Path $e 'client-process.csv') -NoTypeInformation
 "Client launched PID=$($p.Id)";exit
}
if($Stage -eq 'Status' -or $Stage -eq 'Stop'){
 $record=Import-Csv (Join-Path $e 'client-process.csv');$p=Get-CimInstance Win32_Process -Filter "ProcessId=$($record.Pid)"
 if($p -and $p.ExecutablePath -ne $record.Exe){throw 'Owned client PID exe mismatch'}
 if($Stage -eq 'Stop'){
  if(!$p){'Client already exited';exit}
  $process=Get-Process -Id $record.Pid
  $accepted=$process.CloseMainWindow()
  [pscustomobject]@{Pid=$record.Pid;CloseMainWindowAccepted=$accepted;Utc=[DateTime]::UtcNow.ToString('o')}|Export-Csv (Join-Path $e 'close-request.csv') -NoTypeInformation
  "Normal client close request accepted=$accepted";exit
 }
 "Client running=$([bool]$p)"
 $report=Join-Path $root 'QC_OBSERVER_REPORT.txt';if(Test-Path $report){Get-Content $report|Where-Object {$_ -notmatch '\| (PREFIX|POSTFIX|TRANSPILER|FINALIZER) \|' }}
 if(Test-Path (Join-Path $root 'BepInEx\LogOutput.log')){Get-Content (Join-Path $root 'BepInEx\LogOutput.log') -Tail 12};exit
}
if(Get-Process -Name valheim -ErrorAction SilentlyContinue){throw 'Close client before collect/integrity/finalize'}
if($Stage -eq 'Collect'){
 foreach($rel in 'QC_OBSERVER_REPORT.txt','BepInEx\LogOutput.log'){$file=Join-Path $root $rel;if(Test-Path $file){Copy-Item -LiteralPath $file -Destination (Join-Path $e (Split-Path -Leaf $file))}}
 & reg.exe export 'HKCU\Software\IronGate\Valheim' (Join-Path $e 'playerprefs-after.reg') /y
 'Client evidence collected';exit
}
if($Stage -eq 'Integrity'){
 $changes=[Collections.Generic.List[object]]::new();$known=@{};$before=@(Import-Csv (Join-Path $e 'protected-before.csv'))
 foreach($row in $before){$known[$row.Path]=$true;if(!(Test-Path -LiteralPath $row.Path)){$changes.Add([pscustomobject]@{Path=$row.Path;Change='MISSING'});continue};$now=Fingerprint(Get-Item -LiteralPath $row.Path);if($now.SHA256 -ne $row.SHA256 -or $now.LastWriteUtc -ne $row.LastWriteUtc){$changes.Add([pscustomobject]@{Path=$row.Path;Change=if($now.SHA256 -eq $row.SHA256){'TIMESTAMP_ONLY'}else{'CONTENT_CHANGED'}})}}
 foreach($file in Files){if(!$known.ContainsKey($file.FullName)){$changes.Add([pscustomobject]@{Path=$file.FullName;Change='NEW'})}}
 $changes|Export-Csv (Join-Path $e 'protected-changes.csv') -NoTypeInformation
 [pscustomobject]@{Protected=$before.Count;Changes=$changes.Count}|Export-Csv (Join-Path $e 'integrity-result.csv') -NoTypeInformation
 $steamChanges=@(foreach($row in Import-Csv (Join-Path $e 'steam-readable-before.csv')){if(!(Test-Path -LiteralPath $row.Path) -or (Get-FileHash -LiteralPath $row.Path).Hash -ne $row.SHA256){$row}})
 $steamChanges|Export-Csv (Join-Path $e 'steam-readable-changes.csv') -NoTypeInformation
 "Protected=$($before.Count) Changes=$($changes.Count) SteamObservedChanges=$($steamChanges.Count)";$changes|Format-Table -AutoSize;exit
}
if($Stage -eq 'Finalize'){
 if($Result -eq 'FAIL'){$backup=(Import-Csv (Join-Path $e 'rollback-path.csv')).Path;$hash=(Import-Csv (Join-Path $e 'original-dll.csv')).SHA256;if((Get-FileHash $backup).Hash -ne $hash){throw 'Rollback artifact drifted'};Copy-Item -LiteralPath $backup -Destination $target -Force;if((Get-FileHash $target).Hash -ne $hash){throw 'Rollback verification failed'}}
 elseif((Get-FileHash $target).Hash -ne $expected){throw 'PASS final DLL mismatch'}
 if(Test-Path $observer){$row=Import-Csv (Join-Path $e 'observer-installed.csv');if((Get-FileHash $observer).Hash -ne $row.SHA256){throw 'Observer drift; do not delete'};if(!(Test-Path (Join-Path $e 'QC_OBSERVER_REPORT.txt'))){throw 'Preserved evidence required before observer removal'};Remove-Item -LiteralPath $observer}
 Fingerprint(Get-Item $target)|Export-Csv (Join-Path $e 'final-dll.csv') -NoTypeInformation
 "Final result=$Result; QC observer removed; DLL SHA256=$((Get-FileHash $target).Hash)"
}

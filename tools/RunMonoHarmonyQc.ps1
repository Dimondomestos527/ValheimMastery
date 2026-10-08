param([ValidatePattern('^[A-Za-z0-9_-]+$')][string]$RunId='mono-final')
$ErrorActionPreference='Stop'
$devRoot=Split-Path -Parent $PSScriptRoot
$runtimeRoot='C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server'
$evidence=Join-Path $devRoot 'validation\tooling-blockers-20261004'
$csc='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
foreach($source in @('FrameworkHarmonyHost','EmbeddedMonoLauncher')){
 & $csc /nologo /target:exe /platform:x64 /r:System.Core.dll "/out:$evidence\$source.exe" "$devRoot\qc-harmony-smoke\$source.cs" > "$evidence\$RunId.$source.build.log" 2>&1
 if($LASTEXITCODE){throw "Isolated QC host compile failed: $source"}
}
$results=[Collections.Generic.List[object]]::new()
$fingerprints=[Collections.Generic.List[object]]::new()
Push-Location $evidence
try{
 foreach($variant in @('Client','Server')){
  $managed=if($variant -eq 'Client'){'C:\ValheimModDev\valheim_Data\Managed'}else{Join-Path $runtimeRoot 'valheim_server_Data\Managed'}
  $mono=if($variant -eq 'Client'){'C:\ValheimModDev\MonoBleedingEdge'}else{Join-Path $runtimeRoot 'MonoBleedingEdge'}
  foreach($path in @((Join-Path $mono 'EmbedRuntime\mono-2.0-bdwgc.dll'),(Join-Path $managed 'mscorlib.dll'),(Join-Path $runtimeRoot 'BepInEx\core\0Harmony.dll'))){$fingerprints.Add([pscustomobject]@{Variant=$variant;Path=$path;SHA256=(Get-FileHash -LiteralPath $path).Hash;FileVersion=(Get-Item $path).VersionInfo.FileVersion})}
  foreach($mode in @('patchall','transpilers')){
   $log=Join-Path $evidence "$RunId.$variant.$mode.log";if(Test-Path $log){throw "Existing evidence $log"}
   $arguments=@($mono,$managed,"$evidence\FrameworkHarmonyHost.exe","$devRoot\bin\Perks123$variant\ValheimMastery.dll",(Join-Path $runtimeRoot 'BepInEx\core'))
   if($mode -eq 'transpilers'){$arguments+='transpilers'}
   & "$evidence\EmbeddedMonoLauncher.exe" @arguments > $log 2>&1
   $results.Add([pscustomobject]@{Variant=$variant;Mode=$mode;ExitCode=$LASTEXITCODE;Log=$log})
   Write-Output "$variant actual-runtime Mono $mode exit=$LASTEXITCODE"
  }
 }
}finally{Pop-Location}
$results|Export-Csv (Join-Path $evidence "$RunId.results.csv") -NoTypeInformation
$fingerprints|Export-Csv (Join-Path $evidence "$RunId.runtime-fingerprints.csv") -NoTypeInformation

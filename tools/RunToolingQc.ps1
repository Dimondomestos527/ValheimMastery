$ErrorActionPreference='Stop'
$devRoot=Split-Path -Parent $PSScriptRoot
$evidence=Join-Path $devRoot 'validation\tooling-blockers-20261004'
$saved=@{};$local=@{NUGET_PACKAGES=Join-Path $devRoot '.nuget\packages';NUGET_HTTP_CACHE_PATH=Join-Path $devRoot '.nuget\http-cache';NUGET_SCRATCH=Join-Path $devRoot '.nuget\scratch';NUGET_PLUGINS_CACHE_PATH=Join-Path $devRoot '.nuget\plugins-cache';DOTNET_CLI_HOME=Join-Path $devRoot '.dotnet-home';DOTNET_CLI_TELEMETRY_OPTOUT='1';TEMP=Join-Path $evidence 'temp';TMP=Join-Path $evidence 'temp'}
foreach($name in $local.Keys){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process');[Environment]::SetEnvironmentVariable($name,$local[$name],'Process')}
Push-Location $devRoot
try {
 & dotnet restore 'qc-harmony-smoke\qc-harmony-smoke.csproj' --packages $local.NUGET_PACKAGES -v minimal -nr:false > (Join-Path $evidence 'qc-restore.log') 2>&1
 if($LASTEXITCODE){throw 'QC restore failed'}
 & dotnet build 'qc-harmony-smoke\qc-harmony-smoke.csproj' -t:Rebuild -c Release --no-restore -p:UseSharedCompilation=false -nr:false -v minimal > (Join-Path $evidence 'qc-build.log') 2>&1
 if($LASTEXITCODE){throw 'QC build failed'}
 $modes=@('contracts','clean-static','current123-tooling','patch123','magic70','native-movement-presentation','native-feedback','patch113','patch114','patch115','patch116','magic-foundation','audit-static','player-feedback-followup','controlled-static')
 $results=[Collections.Generic.List[object]]::new()
 foreach($variant in @('Client','Server')){
  $plugin=Join-Path $devRoot "bin\Perks123$variant\ValheimMastery.dll"
  foreach($mode in $modes){
   $args=@('qc-harmony-smoke\bin\Release\net8.0\qc-harmony-smoke.dll',$mode,'--plugin',$plugin);if($variant -eq 'Server'){$args+='--server'}
   $log=Join-Path $evidence "$variant.$mode.log"
   & dotnet @args > $log 2>&1
   $results.Add([pscustomobject]@{Variant=$variant;Mode=$mode;ExitCode=$LASTEXITCODE;Plugin=$plugin;Log=$log})
   Write-Output "$variant $mode exit=$LASTEXITCODE"
  }
 }
 $results|Export-Csv (Join-Path $evidence 'qc-results.csv') -NoTypeInformation
}finally{Pop-Location;foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}

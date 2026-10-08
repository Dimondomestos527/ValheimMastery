param([ValidateSet('Baseline','Run','Integrity')][string]$Stage)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$serverRoot='C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server'
$evidence=Join-Path $projectRoot 'validation\clean-qc-20261004'
if(!(Test-Path $evidence)){New-Item -ItemType Directory -Path $evidence|Out-Null}
function ProtectedFiles {
    @(Get-ChildItem $serverRoot -Recurse -File|Where-Object {!$_.FullName.StartsWith((Join-Path $serverRoot 'MasteryDev')+'\',[StringComparison]::OrdinalIgnoreCase)})+
    @(Get-ChildItem 'C:\ValheimModDev\BepInEx' -File -Recurse)+
    @(Get-ChildItem 'C:\Users\Domesos\AppData\LocalLow\IronGate\Valheim\worlds_local\DEEEP' -File -Recurse)+
    @(Get-ChildItem (Join-Path $projectRoot 'src-modern'),(Join-Path $projectRoot 'bin\Perks123Client'),(Join-Path $projectRoot 'bin\Perks123Server'),(Join-Path $projectRoot 'artifacts\reference') -File -Recurse)+
    @(Get-Item (Join-Path $projectRoot 'ValheimMasteryPoC.csproj'),(Join-Path $projectRoot 'NuGet.config'))
}
function Fingerprint($path){$f=Get-Item $path;[pscustomobject]@{Path=$f.FullName;Length=$f.Length;LastWriteUtc=$f.LastWriteTimeUtc.ToString('o');SHA256=(Get-FileHash -LiteralPath $path).Hash}}
if($Stage -eq 'Baseline'){
    $path=Join-Path $evidence 'protected-before.csv';if(Test-Path $path){throw 'Baseline exists'}
    $before=@(foreach($f in ProtectedFiles){Fingerprint $f.FullName});$before|Export-Csv $path -NoTypeInformation
    Write-Output "QC baseline: $($before.Count) protected files."
    exit
}
if($Stage -eq 'Integrity'){
    $before=@(Import-Csv (Join-Path $evidence 'protected-before.csv'));$changes=[Collections.Generic.List[object]]::new();$known=@{}
    foreach($entry in $before){$known[$entry.Path]=$true;if(!(Test-Path $entry.Path)){$changes.Add([pscustomobject]@{Path=$entry.Path;Change='MISSING'});continue};$now=Fingerprint $entry.Path;if($now.SHA256 -ne $entry.SHA256 -or $now.Length -ne $entry.Length -or $now.LastWriteUtc -ne $entry.LastWriteUtc){$changes.Add([pscustomobject]@{Path=$entry.Path;Change='CHANGED'})}}
    foreach($f in ProtectedFiles){if(!$known.ContainsKey($f.FullName)){$changes.Add([pscustomobject]@{Path=$f.FullName;Change='NEW_PROTECTED_FILE'})}}
    [pscustomobject]@{ProtectedFiles=$before.Count;Changes=$changes.Count;GameplayChanged=$false;DeployRun=$false;GameStarted=$false;ServerStarted=$false}|Export-Csv (Join-Path $evidence 'integrity-result.csv') -NoTypeInformation
    if($changes.Count){$changes|Export-Csv (Join-Path $evidence 'integrity-changes.csv') -NoTypeInformation}
    Write-Output "Integrity: protected=$($before.Count), changes=$($changes.Count)";exit
}
$envBefore=@{};$envLocal=@{
    NUGET_PACKAGES=Join-Path $projectRoot '.nuget\packages'
    NUGET_HTTP_CACHE_PATH=Join-Path $projectRoot '.nuget\http-cache'
    NUGET_SCRATCH=Join-Path $projectRoot '.nuget\scratch'
    NUGET_PLUGINS_CACHE_PATH=Join-Path $projectRoot '.nuget\plugins-cache'
    DOTNET_CLI_HOME=Join-Path $projectRoot '.dotnet-home'
    DOTNET_CLI_TELEMETRY_OPTOUT='1';DOTNET_CLI_USE_MSBUILD_SERVER='0'
    DOTNET_GENERATE_ASPNET_CERTIFICATE='false';DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='0'
    TEMP=Join-Path $evidence 'temp';TMP=Join-Path $evidence 'temp'
}
if(!(Test-Path $envLocal.TEMP)){New-Item -ItemType Directory -Path $envLocal.TEMP|Out-Null}
foreach($name in $envLocal.Keys){$envBefore[$name]=[Environment]::GetEnvironmentVariable($name,'Process');[Environment]::SetEnvironmentVariable($name,$envLocal[$name],'Process')}
Push-Location $projectRoot
try{
    & dotnet restore 'qc-harmony-smoke\qc-harmony-smoke.csproj' --packages $envLocal.NUGET_PACKAGES -v minimal -nr:false 2>&1|Tee-Object -FilePath (Join-Path $evidence 'qc-restore.log');$restoreExit=$LASTEXITCODE
    if($restoreExit){throw "QC restore failed: $restoreExit"}
    & dotnet build 'qc-harmony-smoke\qc-harmony-smoke.csproj' -c Release --no-restore -v minimal -p:UseSharedCompilation=false -nr:false 2>&1|Tee-Object -FilePath (Join-Path $evidence 'qc-build.log');$buildExit=$LASTEXITCODE
    [pscustomobject]@{RestoreExit=$restoreExit;BuildExit=$buildExit}|Export-Csv (Join-Path $evidence 'qc-build-result.csv') -NoTypeInformation
    if($buildExit){throw "QC build failed: $buildExit"}
    $tool=Join-Path $projectRoot 'qc-harmony-smoke\bin\Release\net8.0\qc-harmony-smoke.dll'
    $modes=@('contracts','clean-static','patchall','patch123','patch122','patch121','patch120','carrier119','summon-raven119','magic70','native-storm-cage','native-landing','native-movement-presentation','deferred-vfx','patch113','patch114','patch115','patch116','gold100','combat-input','maul-fishing-riding','forge-transpiler','cooking-forge','network-manifest','magic-foundation','wood-stack','movement','workshop-boundaries','spear70-boundaries','feast-persistence','audit-static','approved-sword-polearm','approved-knives','approved-axe-bow','polearm-visual-polish','player-feedback-followup','native-feedback','canonical-bonus','remaining-visual-boundaries','spear35-static','bow70-static','controlled-static','workshop-atomic','workshop-planner-experiment','processing-batches')
    $results=[Collections.Generic.List[object]]::new()
    foreach($variant in @('client','server')){
        $folder=if($variant -eq 'client'){'Perks123Client'}else{'Perks123Server'};$plugin=Join-Path $projectRoot "bin\$folder\ValheimMastery.dll"
        foreach($mode in $modes){
            $arguments=@($tool,$mode,'--plugin',$plugin);if($variant -eq 'server'){$arguments+='--server'}
            $log=Join-Path $evidence "$variant.$mode.log"
            & dotnet @arguments > $log 2>&1
            $exit=$LASTEXITCODE;$results.Add([pscustomobject]@{Variant=$variant;Mode=$mode;ExitCode=$exit;Plugin=$plugin;Log=$log})
            Write-Output "$variant $mode exit=$exit"
        }
    }
    $results|Export-Csv (Join-Path $evidence 'qc-suite-results.csv') -NoTypeInformation
}finally{Pop-Location;foreach($name in $envBefore.Keys){[Environment]::SetEnvironmentVariable($name,$envBefore[$name],'Process')}}

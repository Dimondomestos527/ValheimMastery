param([ValidatePattern('^[A-Za-z0-9_-]*$')][string]$ValidationRun='')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$constants = 'MASTERY_RELEASE_SAFE%3BMASTERY_CLUB_PASSIVES%3BMASTERY_SPEAR35_EXPERIMENT%3BMASTERY_WORKSHOP_REMOTE_EXPERIMENT%3BMASTERY_SPEAR70_EXPERIMENT%3BMASTERY_MAGIC70_EXPERIMENT%3BMASTERY_CLUBS35_EXPERIMENT%3BMASTERY_CLUBS70_EXPERIMENT%3BMASTERY_SHIELD35_EXPERIMENT%3BMASTERY_SHIELD_RUSH_EXPERIMENT%3BMASTERY_CARRIER70_EXPERIMENT'
$targets = @(
    @{ Name='Perks123Client'; Managed='C:\ValheimModDev\valheim_Data\Managed'; Output='bin\Perks123Client\' },
    @{ Name='Perks123Server'; Managed='C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server\valheim_server_Data\Managed'; Output='bin\Perks123Server\' }
)
if(!$ValidationRun){$ValidationRun='build123-'+(Get-Date -Format 'yyyyMMdd-HHmmssfff')}
$validationRoot = Join-Path $projectRoot ('validation\'+$ValidationRun)
$environmentBefore = @{}
$localEnvironment = @{
    NUGET_PACKAGES = Join-Path $projectRoot '.nuget\packages'
    NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot '.nuget\http-cache'
    NUGET_SCRATCH = Join-Path $projectRoot '.nuget\scratch'
    NUGET_PLUGINS_CACHE_PATH = Join-Path $projectRoot '.nuget\plugins-cache'
    DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
    DOTNET_CLI_USE_MSBUILD_SERVER = '0'
    TEMP = Join-Path $projectRoot 'validation\temp'
    TMP = Join-Path $projectRoot 'validation\temp'
}
foreach ($name in $localEnvironment.Keys) {
    $environmentBefore[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name, $localEnvironment[$name], 'Process')
}
foreach ($directory in @($validationRoot,$localEnvironment.NUGET_PACKAGES,$localEnvironment.NUGET_HTTP_CACHE_PATH,$localEnvironment.NUGET_SCRATCH,$localEnvironment.NUGET_PLUGINS_CACHE_PATH,$localEnvironment.DOTNET_CLI_HOME,$localEnvironment.TEMP)) {
    if (!(Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
}
Push-Location -LiteralPath $projectRoot
try {
    foreach ($target in $targets) {
        $intermediate = (Join-Path $projectRoot ('obj\isolated123\' + $target.Name)).Replace('\','/') + '/'
        $compileIntermediate = $intermediate + 'Release/netstandard2.1/'
        $properties = @("-p:GameManaged=$($target.Managed)","-p:OutputPath=$($target.Output)","-p:DefineConstants=$constants","-p:BaseIntermediateOutputPath=$intermediate","-p:MSBuildProjectExtensionsPath=$intermediate","-p:IntermediateOutputPath=$compileIntermediate","-p:GeneratedIgnoresAccessChecksToFile=$($compileIntermediate)ValheimMasteryPoC.IgnoresAccessChecksTo.cs",'-p:AssemblySearchPath_UseCandidateAssemblyFiles=false','-p:EnableDefaultNoneItems=false','-p:DisableRarCache=true',"-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $PSScriptRoot 'BuildReferenceAudit.targets')","-p:MasteryReferenceAuditDirectory=$validationRoot","-p:MasteryReferenceAuditVariant=$($target.Name)",'-p:UseSharedCompilation=false','-nr:false')
        $restoreLog = Join-Path $validationRoot ($target.Name + '.restore.log')
        $buildLog = Join-Path $validationRoot ($target.Name + '.build.log')
        if ((Test-Path $restoreLog) -or (Test-Path $buildLog)) { throw "Validation logs already exist: $($target.Name)" }
        & dotnet restore ValheimMasteryPoC.csproj -v minimal @properties --packages $localEnvironment.NUGET_PACKAGES 2>&1 | Tee-Object -FilePath $restoreLog
        $restoreExit = $LASTEXITCODE
        [pscustomobject]@{Target=$target.Name;Stage='Restore';ExitCode=$restoreExit;Intermediate=$intermediate;Managed=$target.Managed} | Export-Csv -LiteralPath (Join-Path $validationRoot ($target.Name + '.restore-result.csv')) -NoTypeInformation
        if ($restoreExit -ne 0) { throw "Restore failed: $($target.Name)" }
        & dotnet build ValheimMasteryPoC.csproj -c Release -v normal @properties --no-restore 2>&1 | Tee-Object -FilePath $buildLog
        $buildExit = $LASTEXITCODE
        [pscustomobject]@{Target=$target.Name;Stage='Build';ExitCode=$buildExit;Intermediate=$intermediate;Managed=$target.Managed;Output=$target.Output} | Export-Csv -LiteralPath (Join-Path $validationRoot ($target.Name + '.build-result.csv')) -NoTypeInformation
        if ($buildExit -ne 0) { throw "Build failed: $($target.Output)" }
    }
    & (Join-Path $PSScriptRoot 'BuildDependencyProvenance.ps1') -EvidenceDirectory $validationRoot
} finally {
    Pop-Location
    foreach ($name in $environmentBefore.Keys) { [Environment]::SetEnvironmentVariable($name,$environmentBefore[$name],'Process') }
}
# Deliberately build only. No deployment, game or server launch.

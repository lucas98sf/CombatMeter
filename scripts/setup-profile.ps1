param(
    [string]$GameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\VRising',
    [string]$ReferenceProfile = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\VRising\profiles\Default"
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskPack = Join-Path $taskRoot '.tools/bepinex/BepInExPack_V_Rising'
if (!(Test-Path -LiteralPath $taskPack)) { throw 'Run scripts/bootstrap.ps1 first.' }
$taskProvenance = @{}
foreach ($taskKind in @('server', 'client')) {
    $taskProfile = Join-Path $taskRoot ('.profiles/' + $taskKind)
    New-Item -ItemType Directory -Force -Path $taskProfile | Out-Null
    $taskGameSource = if ($taskKind -eq 'server') { Join-Path $GameDirectory 'VRising_Server' } else { $GameDirectory }
    $taskDataName = if ($taskKind -eq 'server') { 'VRisingServer_Data' } else { 'VRising_Data' }
    # Game data is referenced for reading. All saves, logs, configs and mod binaries
    # live in this profile; installed game files and existing worlds are untouched.
    $taskDataLink = Join-Path $taskProfile $taskDataName
    if (!(Test-Path -LiteralPath $taskDataLink)) {
        New-Item -ItemType Junction -Path $taskDataLink -Target (Join-Path $taskGameSource $taskDataName) | Out-Null
    }
    foreach ($taskFile in @('GameAssembly.dll','UnityPlayer.dll','baselib.dll','UnityCrashHandler64.exe','steam_appid.txt','VERSION')) {
        $taskSource = Join-Path $taskGameSource $taskFile
        $taskDestination = Join-Path $taskProfile $taskFile
        if ((Test-Path -LiteralPath $taskSource) -and !(Test-Path -LiteralPath $taskDestination)) { Copy-Item -LiteralPath $taskSource -Destination $taskDestination }
    }
    $taskExe = if ($taskKind -eq 'server') { 'VRisingServer.exe' } else { 'VRising.exe' }
    Copy-Item -LiteralPath (Join-Path $taskGameSource $taskExe) -Destination (Join-Path $taskProfile $taskExe) -Force
    foreach ($taskLoaderFile in @('winhttp.dll','doorstop_config.ini')) {
        Copy-Item -LiteralPath (Join-Path $taskPack $taskLoaderFile) -Destination (Join-Path $taskProfile $taskLoaderFile) -Force
    }
    if (!(Test-Path -LiteralPath (Join-Path $taskProfile 'dotnet'))) {
        Copy-Item -LiteralPath (Join-Path $taskPack 'dotnet') -Destination $taskProfile -Recurse
    }
    $taskLoaderRoot = Join-Path $taskProfile 'BepInEx'
    New-Item -ItemType Directory -Force -Path $taskLoaderRoot | Out-Null
    if (!(Test-Path -LiteralPath (Join-Path $taskLoaderRoot 'core'))) {
        Copy-Item -LiteralPath (Join-Path $taskPack 'BepInEx/core') -Destination $taskLoaderRoot -Recurse
    }
    $taskReferenceName = if ($taskKind -eq 'server') { 'BepInEx_Server' } else { 'BepInEx' }
    $taskReferenceInterop = Join-Path $ReferenceProfile ($taskReferenceName + '/interop')
    if (!(Test-Path -LiteralPath (Join-Path $taskLoaderRoot 'interop')) -and (Test-Path -LiteralPath $taskReferenceInterop)) {
        Copy-Item -LiteralPath $taskReferenceInterop -Destination $taskLoaderRoot -Recurse
    }
    New-Item -ItemType Directory -Force -Path (Join-Path $taskProfile 'logs'),(Join-Path $taskProfile 'saves/Settings') | Out-Null
    $taskProvenance[$taskKind] = @{
        gameVersion = (Get-Content -LiteralPath (Join-Path $taskGameSource 'VERSION') -Raw).Trim()
        referenceProfile = $taskReferenceInterop
        interopHash = if (Test-Path -LiteralPath (Join-Path $taskLoaderRoot 'interop/assembly-hash.txt')) { (Get-Content -LiteralPath (Join-Path $taskLoaderRoot 'interop/assembly-hash.txt') -Raw).Trim() } else { 'Generate on first boot' }
        projectMsha256 = if (Test-Path -LiteralPath (Join-Path $taskLoaderRoot 'interop/ProjectM.dll')) { (Get-FileHash -LiteralPath (Join-Path $taskLoaderRoot 'interop/ProjectM.dll') -Algorithm SHA256).Hash } else { $null }
    }
}
$taskSettingsPath = Join-Path $taskRoot '.profiles/server/saves/Settings/ServerHostSettings.json'
if (!(Test-Path -LiteralPath $taskSettingsPath)) {
    @{
        Name='CombatMeter development'; Address='127.0.0.1'; Port=29878; QueryPort=29879
        MaxConnectedUsers=32; ListOnMasterServer=$false; ListOnSteam=$false; ListOnEOS=$false
        SaveName='CombatMeterValidation'; AutoSaveCount=2; AutoSaveInterval=600
    } | ConvertTo-Json | Set-Content -LiteralPath $taskSettingsPath -Encoding UTF8
    @{ GameModeType='PvP' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path (Split-Path $taskSettingsPath) 'ServerGameSettings.json') -Encoding UTF8
}
$taskProvenance | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskRoot '.profiles/provenance.json') -Encoding UTF8
Write-Output 'Isolated client/server profiles ready. Server: 127.0.0.1:29878.'

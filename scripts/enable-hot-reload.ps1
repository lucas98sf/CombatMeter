[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ProfilePath = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\VRising\profiles\Default",
    [switch]$WaitForGameExit,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskProfile = (Resolve-Path -LiteralPath $ProfilePath).Path
$taskNormal = Join-Path $taskProfile 'BepInEx/plugins/CombatMeter'
$taskReload = Join-Path $taskProfile 'BepInEx/BloodpebblePlugins/CombatMeter'
if (!$WaitForGameExit -and !$WhatIfPreference) {
    $taskNormalizedProfile = $taskProfile.Replace('\','/')
    $taskRunning = @(Get-CimInstance Win32_Process -Filter "Name = 'VRising.exe' OR Name = 'VRisingServer.exe'" |
        Where-Object { $_.CommandLine -and $_.CommandLine.Replace('\','/').IndexOf($taskNormalizedProfile, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
    if ($taskRunning.Count) {
        throw 'Close V Rising normally before the one-time setup, or run scripts/queue-thunderstore-update.ps1 -HotReloadSetup -Configuration Debug to install after game exit. Wait for Development profile configured in the queued log before relaunching. No setup changes were made.'
    }
}
if (!(Get-ChildItem -LiteralPath (Join-Path $taskProfile 'BepInEx/plugins') -Recurse -Filter Bloodpebble.dll)) {
    throw 'Install Bloodpebble in the selected profile first.'
}
foreach ($taskLoader in @('BepInEx','BepInEx_Server')) {
    $taskConfig = Join-Path $taskProfile ($taskLoader + '/config/Bloodpebble.cfg')
    if (!(Test-Path -LiteralPath $taskConfig)) { throw "Start the profile once with Bloodpebble so $taskConfig is generated, then retry." }
    $taskText = [IO.File]::ReadAllText($taskConfig)
    foreach ($taskSetting in @('ReloadablePluginsFolder','EnableAutoReload','AutoReloadDelaySeconds')) {
        if ($taskText -notmatch ('(?m)^' + $taskSetting + '\s*=')) { throw "Missing Bloodpebble setting $taskSetting in $taskConfig." }
    }
}
if (!$PSCmdlet.ShouldProcess($taskProfile, 'Install the permanent CombatMeter runtime and move client/server logic into Bloodpebble reload folders')) { return }
& (Join-Path $PSScriptRoot 'install-thunderstore.ps1') -ProfilePath $taskProfile -Configuration $Configuration -WaitForGameExit:$WaitForGameExit
New-Item -ItemType Directory -Force -Path $taskReload | Out-Null
$taskBackup = Join-Path $taskRoot ('artifacts/hot-reload/backups/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Force -Path $taskBackup | Out-Null
foreach ($taskLoader in @('BepInEx','BepInEx_Server')) {
    $taskConfig = Join-Path $taskProfile ($taskLoader + '/config/Bloodpebble.cfg')
    if (!(Test-Path -LiteralPath $taskConfig)) { throw "Start the profile once with Bloodpebble so $taskConfig is generated, then retry." }
    Copy-Item -LiteralPath $taskConfig -Destination (Join-Path $taskBackup ($taskLoader + '-Bloodpebble.cfg'))
    $taskText = [IO.File]::ReadAllText($taskConfig)
    foreach ($taskSetting in @('ReloadablePluginsFolder','EnableAutoReload','AutoReloadDelaySeconds')) {
        if ($taskText -notmatch ('(?m)^' + $taskSetting + '\s*=')) { throw "Missing Bloodpebble setting $taskSetting in $taskConfig." }
    }
    # Bloodpebble 1.4.2 watches only this directory, not its children.
    $taskPathValue = $taskReload.Replace('\','/')
    $taskText = [regex]::Replace($taskText, '(?m)^ReloadablePluginsFolder\s*=.*$', { param($match) 'ReloadablePluginsFolder = ' + $taskPathValue })
    $taskText = [regex]::Replace($taskText, '(?m)^EnableAutoReload\s*=.*$', 'EnableAutoReload = true')
    $taskText = [regex]::Replace($taskText, '(?m)^AutoReloadDelaySeconds\s*=.*$', 'AutoReloadDelaySeconds = 2')
    [IO.File]::WriteAllText($taskConfig, $taskText, [Text.UTF8Encoding]::new($true))
}
foreach ($taskAssembly in @('CombatMeter.Client','CombatMeter.Server')) {
    foreach ($taskExtension in @('.pdb','.dll')) {
        $taskSource = Join-Path $taskNormal ($taskAssembly + $taskExtension)
        $taskDestination = Join-Path $taskReload ($taskAssembly + $taskExtension)
        # Named leaf files only; both absolute paths must remain inside this profile.
        foreach ($taskPath in @($taskSource,$taskDestination)) {
            if (![IO.Path]::GetFullPath($taskPath).StartsWith($taskProfile + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'A deployment path escaped the selected profile.' }
        }
        if (Test-Path -LiteralPath $taskDestination) {
            Copy-Item -LiteralPath $taskDestination -Destination (Join-Path $taskBackup ($taskAssembly + $taskExtension))
        }
        Move-Item -LiteralPath $taskSource -Destination $taskDestination -Force
    }
}
$taskStable = @{}
foreach ($taskAssembly in @('CombatMeter.Runtime','CombatMeter.Core','CombatMeter.Shared')) {
    $taskStable[$taskAssembly] = (Get-FileHash -LiteralPath (Join-Path $taskNormal ($taskAssembly + '.dll')) -Algorithm SHA256).Hash
}
$taskReceiptFolder = Join-Path $taskRoot 'artifacts/hot-reload'
@{ profile=$taskProfile; reloadFolder=$taskReload; configuration=$Configuration; stable=$taskStable; backup=$taskBackup } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskReceiptFolder 'profile.json') -Encoding UTF8
Write-Output 'Development profile configured. Restart through Thunderstore once to load the persistent runtime and Bloodpebble folder settings.'
Write-Output 'Then use scripts/reload-dev.ps1 -Mode Client -Watch. Successful UI reloads preserve server encounter history; server reloads start a new meter session.'

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ProfilePath = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\VRising\profiles\Default",
    [switch]$EnableOpenWorld,
    [switch]$EnableDiagnostics,
    [switch]$WaitForGameExit,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [ValidateRange(5, 3600)][int]$WaitTimeoutSeconds = 1200
)
$ErrorActionPreference = 'Stop'
function Get-InstallHash([string]$Path) {
    $taskAlgorithm = [Security.Cryptography.SHA256]::Create()
    $taskStream = [IO.File]::OpenRead($Path)
    try { return [BitConverter]::ToString($taskAlgorithm.ComputeHash($taskStream)).Replace('-', '') }
    finally { $taskStream.Dispose(); $taskAlgorithm.Dispose() }
}
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskProfile = (Resolve-Path -LiteralPath $ProfilePath).Path
$taskPlugins = Join-Path $taskProfile 'BepInEx/plugins'
if (!(Test-Path -LiteralPath (Join-Path $taskProfile 'BepInEx/core/BepInEx.Unity.IL2CPP.dll'))) {
    throw 'This profile does not have the V Rising BepInEx IL2CPP loader installed.'
}
$taskInstalled = @(Get-ChildItem -LiteralPath $taskPlugins -Recurse -File -Filter '*.dll')
if (!($taskInstalled | Where-Object Name -eq 'ServerLaunchFix.dll')) {
    throw 'Install Mythic-ServerLaunchFix in this Thunderstore profile first, then rerun this script.'
}
$taskBloodpebble = [bool]($taskInstalled | Where-Object Name -eq 'Bloodpebble.dll')
$taskMeterFolder = Join-Path $taskPlugins 'CombatMeter'
$taskFiles = @()
foreach ($taskAssembly in @('CombatMeter.Server','CombatMeter.Client','CombatMeter.Core','CombatMeter.Shared','CombatMeter.Runtime')) {
    foreach ($taskExtension in @('.dll','.pdb')) {
        $taskSource = Join-Path $taskRoot ('src/' + $taskAssembly + '/bin/' + $Configuration + '/net6.0/' + $taskAssembly + $taskExtension)
        if (!(Test-Path -LiteralPath $taskSource)) { throw "Build CombatMeter with scripts/build.ps1 -Configuration $Configuration before installing." }
        $taskDestination = Join-Path $taskMeterFolder ($taskAssembly + $taskExtension)
        $taskDuplicates = @($taskInstalled | Where-Object { $_.Name -eq ($taskAssembly + $taskExtension) -and $_.FullName -ne $taskDestination })
        if ($taskDuplicates.Count) { throw "Duplicate $taskAssembly found outside the install folder: $($taskDuplicates.FullName -join ', '). Remove the duplicate installation before proceeding." }
        $taskFiles += [pscustomobject]@{ Source=$taskSource; Destination=$taskDestination }
    }
}
if ($WaitForGameExit -and !$WhatIfPreference) {
    Write-Output 'Waiting for this Thunderstore profile to exit normally before installing. No processes will be closed by this script.'
    $taskDeadline = [DateTime]::UtcNow.AddSeconds($WaitTimeoutSeconds)
    $taskNormalizedProfile = $taskProfile.Replace('\','/')
    do {
        $taskRunning = @(Get-CimInstance Win32_Process -Filter "Name = 'VRising.exe' OR Name = 'VRisingServer.exe'" |
            Where-Object { $_.CommandLine -and $_.CommandLine.Replace('\','/').IndexOf($taskNormalizedProfile, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
        if ($taskRunning.Count -eq 0) { break }
        if ([DateTime]::UtcNow -ge $taskDeadline) { throw 'Timed out waiting for game exit; no installation changes were made.' }
        Start-Sleep -Seconds 1
    } while ($true)
}
# Detect locked existing binaries before making any changes. Running games may
# keep existing DLLs locked; new files can be staged for the next normal restart.
foreach ($taskFile in $taskFiles) {
    if (Test-Path -LiteralPath $taskFile.Destination) {
        if ((Get-InstallHash $taskFile.Source) -eq (Get-InstallHash $taskFile.Destination)) { continue }
        if (!$WhatIfPreference) {
            $taskProbe = [IO.File]::Open($taskFile.Destination, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            $taskProbe.Dispose()
        }
    }
}
Write-Output "Profile: $taskProfile"
Write-Output "ServerLaunchFix: installed. Bloodpebble: $taskBloodpebble (optional)."
foreach ($taskFile in $taskFiles) { Write-Output ("Install: " + $taskFile.Destination) }
if (!$PSCmdlet.ShouldProcess($taskProfile, 'Install CombatMeter client/server with built-in native transport')) { return }
$taskBackup = Join-Path $taskRoot ('artifacts/install-backups/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Force -Path $taskBackup | Out-Null
$taskChanged = @()
try {
    foreach ($taskFile in $taskFiles) {
        if ((Test-Path -LiteralPath $taskFile.Destination) -and
            (Get-InstallHash $taskFile.Source) -eq (Get-InstallHash $taskFile.Destination)) { continue }
        $taskBackupFile = $null
        if (Test-Path -LiteralPath $taskFile.Destination) {
            $taskBackupFile = Join-Path $taskBackup ([IO.Path]::GetFileName($taskFile.Destination))
            Copy-Item -LiteralPath $taskFile.Destination -Destination $taskBackupFile
        }
        $taskChanged += [pscustomobject]@{ Destination=$taskFile.Destination; Backup=$taskBackupFile }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $taskFile.Destination) | Out-Null
        Copy-Item -LiteralPath $taskFile.Source -Destination $taskFile.Destination -Force
        if ((Get-InstallHash $taskFile.Source) -ne (Get-InstallHash $taskFile.Destination)) {
            throw "Installed file failed checksum verification: $($taskFile.Destination)"
        }
    }
    if ($EnableOpenWorld -or $EnableDiagnostics) {
        foreach ($taskConfigName in @('client','server')) {
            $taskLoaderName = if ($taskConfigName -eq 'client') { 'BepInEx' } else { 'BepInEx_Server' }
            $taskConfigPath = Join-Path $taskProfile ($taskLoaderName + '/config/io.combatmeter.' + $taskConfigName + '.cfg')
            if (!(Test-Path -LiteralPath $taskConfigPath)) { throw 'Start CombatMeter once before applying configuration switches.' }
            $taskConfig = Get-Content -LiteralPath $taskConfigPath -Raw
            if ($EnableOpenWorld) { $taskConfig = $taskConfig -replace '(?m)^OpenWorld\s*=.*$', 'OpenWorld = true' }
            if ($EnableDiagnostics -and $taskConfigName -eq 'server') { $taskConfig = $taskConfig -replace '(?m)^LogResolvedDamage\s*=.*$', 'LogResolvedDamage = true' }
            $taskConfigBackup = Join-Path $taskBackup ('io.combatmeter.' + $taskConfigName + '.cfg')
            Copy-Item -LiteralPath $taskConfigPath -Destination $taskConfigBackup
            $taskChanged += [pscustomobject]@{ Destination=$taskConfigPath; Backup=$taskConfigBackup }
            Set-Content -LiteralPath $taskConfigPath -Value $taskConfig -Encoding UTF8
        }
    }
} catch {
    foreach ($taskChange in $taskChanged) {
        if ($taskChange.Backup) { Copy-Item -LiteralPath $taskChange.Backup -Destination $taskChange.Destination -Force }
        elseif (Test-Path -LiteralPath $taskChange.Destination) { Remove-Item -LiteralPath $taskChange.Destination }
    }
    throw
}
$taskReceipt = [ordered]@{
    profile=$taskProfile; installedAt=(Get-Date).ToString('o'); backupDirectory=$taskBackup; configuration=$Configuration
    serverLaunchFix=$true; bloodpebble=$taskBloodpebble; requiresRestart=$true
    files=@($taskFiles | ForEach-Object { [ordered]@{ path=$_.Destination; sha256=(Get-InstallHash $_.Destination) } })
}
$taskReceipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskBackup 'receipt.json') -Encoding UTF8
Write-Output 'Installation verified. Restart V Rising through Thunderstore, then open your private game.'
Write-Output "Receipt and any replaced DLLs: $taskBackup"


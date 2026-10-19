param(
    [ValidateSet('both','server','client')][string]$Mode = 'both',
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskKinds = if ($Mode -eq 'both') { @('Server','Client') } else { @((Get-Culture).TextInfo.ToTitleCase($Mode)) }
foreach ($taskKind in $taskKinds) {
    $taskTarget = Join-Path $taskRoot ('.profiles/' + $taskKind.ToLowerInvariant() + '/BepInEx/plugins/CombatMeter')
    New-Item -ItemType Directory -Force -Path $taskTarget | Out-Null
    $taskNames = @(('CombatMeter.' + $taskKind), 'CombatMeter.Core', 'CombatMeter.Shared', 'CombatMeter.Runtime')
    foreach ($taskName in $taskNames) {
        foreach ($taskExtension in @('.dll','.pdb')) {
            Copy-Item -LiteralPath (Join-Path $taskRoot ('src/' + $taskName + '/bin/' + $Configuration + '/net6.0/' + $taskName + $taskExtension)) -Destination $taskTarget -Force
        }
    }
}
Write-Output 'Compiled plugins deployed to development profiles.'

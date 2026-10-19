$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskVersion = (Get-Content -LiteralPath (Join-Path $taskRoot 'docs/versions.json') -Raw | ConvertFrom-Json).combatMeter
foreach ($taskKind in @('Server','Client')) {
    $taskStage = Join-Path $taskRoot ('artifacts/packages/CombatMeter-' + $taskKind)
    $taskPluginPath = Join-Path $taskStage 'BepInEx/plugins/CombatMeter'
    New-Item -ItemType Directory -Force -Path $taskPluginPath | Out-Null
    foreach ($taskAssembly in @(('CombatMeter.' + $taskKind), 'CombatMeter.Core', 'CombatMeter.Shared', 'CombatMeter.Runtime')) {
        $taskDll = Join-Path $taskRoot ('src/CombatMeter.' + $taskKind + '/bin/Release/net6.0/' + $taskAssembly + '.dll')
        Copy-Item -LiteralPath $taskDll -Destination $taskPluginPath -Force
    }
    $taskDocs = Join-Path $taskStage 'docs'
    New-Item -ItemType Directory -Force -Path $taskDocs | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination $taskStage -Force
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/VALIDATION.md'),(Join-Path $taskRoot 'docs/DEVELOPMENT.md'),(Join-Path $taskRoot 'docs/versions.json') -Destination $taskDocs -Force
    foreach ($taskOldDoc in @('VALIDATION.md','versions.json')) {
        $taskOldPath = Join-Path $taskStage $taskOldDoc
        if (Test-Path -LiteralPath $taskOldPath) { Remove-Item -LiteralPath $taskOldPath }
    }
    $taskZip = Join-Path $taskRoot ('artifacts/CombatMeter-' + $taskKind + '-' + $taskVersion + '.zip')
    Compress-Archive -Path (Join-Path $taskStage '*') -DestinationPath $taskZip -Force
    Get-FileHash -LiteralPath $taskZip -Algorithm SHA256 | Select-Object Path,Hash
}


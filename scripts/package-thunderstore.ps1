[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskMetadata = Join-Path $taskRoot 'thunderstore'
$taskManifest = Get-Content -LiteralPath (Join-Path $taskMetadata 'manifest.json') -Raw | ConvertFrom-Json
$taskVersion = (Get-Content -LiteralPath (Join-Path $taskRoot 'docs/versions.json') -Raw | ConvertFrom-Json).combatMeter
if ($taskManifest.version_number -ne $taskVersion) { throw 'Thunderstore manifest version does not match docs/versions.json.' }
if ($taskManifest.name -notmatch '^[A-Za-z0-9_]{1,128}$' -or $taskManifest.description.Length -gt 250) { throw 'Invalid Thunderstore manifest.' }
Add-Type -AssemblyName System.Drawing
$taskIcon = [Drawing.Image]::FromFile((Join-Path $taskMetadata 'icon.png'))
try { if ($taskIcon.Width -ne 256 -or $taskIcon.Height -ne 256) { throw 'Thunderstore icon must be 256 x 256.' } }
finally { $taskIcon.Dispose() }

# A fresh stage avoids accidentally publishing obsolete DLLs or local settings.
$taskStage = Join-Path $taskRoot ('artifacts/thunderstore/stage-' + [Guid]::NewGuid().ToString('N'))
$taskPlugins = Join-Path $taskStage 'BepInEx/plugins/CombatMeter'
New-Item -ItemType Directory -Force -Path $taskPlugins | Out-Null
foreach ($taskName in @('manifest.json', 'README.md', 'CHANGELOG.md', 'icon.png')) {
    Copy-Item -LiteralPath (Join-Path $taskMetadata $taskName) -Destination $taskStage
}
foreach ($taskName in @('Client', 'Server', 'Core', 'Shared', 'Runtime')) {
    $taskDll = Join-Path $taskRoot "src/CombatMeter.$taskName/bin/Release/net6.0/CombatMeter.$taskName.dll"
    if (!(Test-Path -LiteralPath $taskDll)) { throw "Missing Release DLL: $taskDll. Run scripts/build.ps1 first." }
    $taskAssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($taskDll).Version
    if ($taskAssemblyVersion.ToString(3) -ne $taskVersion) { throw "Assembly version mismatch: $taskDll" }
    Copy-Item -LiteralPath $taskDll -Destination $taskPlugins
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskZip = Join-Path $taskRoot "artifacts/CombatMeter-Thunderstore-$taskVersion.zip"
# Write a fresh archive before replacing the prior artifact.
$taskTemporaryZip = "$taskZip.$([Guid]::NewGuid().ToString('N')).tmp"
$taskWriter = [IO.Compression.ZipFile]::Open($taskTemporaryZip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskFile in Get-ChildItem -LiteralPath $taskStage -File -Recurse) {
        $taskEntryName = $taskFile.FullName.Substring($taskStage.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskWriter, $taskFile.FullName, $taskEntryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $taskWriter.Dispose() }
for ($taskAttempt = 0; ; $taskAttempt++) {
    try { Move-Item -LiteralPath $taskTemporaryZip -Destination $taskZip -Force; break }
    catch [IO.IOException] {
        if ($taskAttempt -ge 5) { throw }
        # Endpoint scanning can briefly lock a freshly written assembly archive.
        Start-Sleep -Milliseconds 500
    }
}
$taskArchive = [IO.Compression.ZipFile]::OpenRead($taskZip)
try {
    $taskEntries = @($taskArchive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    if ($taskEntries.Count -ne 9) { throw 'Unexpected files in Thunderstore package.' }
    foreach ($taskName in @('manifest.json', 'README.md', 'CHANGELOG.md', 'icon.png')) {
        if ($taskName -cnotin $taskEntries) { throw "Missing root file: $taskName" }
    }
} finally { $taskArchive.Dispose() }
Get-FileHash -LiteralPath $taskZip -Algorithm SHA256 | Select-Object Path, Hash

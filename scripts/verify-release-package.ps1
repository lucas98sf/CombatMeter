param([Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$AssetDirectory, [string]$SourceRoot)
$ErrorActionPreference = 'Stop'
$taskRoot = if ($SourceRoot) { (Resolve-Path -LiteralPath $SourceRoot).Path } else { Split-Path -Parent $PSScriptRoot }
. (Join-Path $PSScriptRoot 'release-policy.ps1')
if (!(Get-ReleasePolicy -Tag $Version).Version) { throw 'Invalid package version.' }
Assert-ReleaseVersion -Root $taskRoot -Version $Version
$taskCommit = git -C $taskRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve checked-out tag.' }
$taskZip = Join-Path $AssetDirectory "CombatMeter-Thunderstore-$Version.zip"
$taskRecord = Get-Content -LiteralPath (Join-Path $AssetDirectory "CombatMeter-Build-$Version.json") -Raw | ConvertFrom-Json
if ($taskRecord.version -ne $Version -or $taskRecord.sourceCommit -ne $taskCommit.Trim() -or $taskRecord.sha256 -ne (Get-FileHash -LiteralPath $taskZip).Hash) {
    throw 'Release asset does not match its version, source commit, or SHA256 record.'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskArchive = [IO.Compression.ZipFile]::OpenRead($taskZip)
$taskTemporary = Join-Path ([IO.Path]::GetTempPath()) ('combatmeter-verify-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskTemporary | Out-Null
try {
    $taskAllowed = @('README.md','CHANGELOG.md','manifest.json','icon.png') + @('Client','Server','Core','Shared','Runtime' | ForEach-Object { "BepInEx/plugins/CombatMeter/CombatMeter.$_.dll" })
    $taskNames = @($taskArchive.Entries | ForEach-Object { $_.FullName })
    if ($taskNames.Count -ne $taskAllowed.Count -or @($taskNames | Select-Object -Unique).Count -ne $taskAllowed.Count) { throw 'Unexpected or duplicate ZIP entries.' }
    foreach ($taskEntry in $taskArchive.Entries) {
        if ($taskEntry.FullName -cnotin $taskAllowed -or $taskEntry.Length -gt 16MB) { throw "Unexpected ZIP entry: $($taskEntry.FullName)" }
        $taskInput = $taskEntry.Open()
        try {
            if ($taskEntry.FullName -notlike '*.dll') {
                $taskExpected = [IO.File]::ReadAllBytes((Join-Path $taskRoot ('thunderstore/' + $taskEntry.FullName)))
                $taskMemory = New-Object IO.MemoryStream
                try {
                    $taskInput.CopyTo($taskMemory)
                    if ([Convert]::ToBase64String($taskMemory.ToArray()) -cne [Convert]::ToBase64String($taskExpected)) { throw "Packaged metadata differs from tagged source: $($taskEntry.FullName)" }
                } finally { $taskMemory.Dispose() }
            } else {
                # Whitelisted basename only; never extract arbitrary archive paths.
                $taskDll = Join-Path $taskTemporary ([IO.Path]::GetFileName($taskEntry.FullName))
                $taskOutput = [IO.File]::Create($taskDll)
                try { $taskInput.CopyTo($taskOutput) } finally { $taskOutput.Dispose() }
                $taskIdentity = [Reflection.AssemblyName]::GetAssemblyName($taskDll)
                if ($taskIdentity.Version.ToString(3) -ne $Version -or $taskIdentity.Name -ne [IO.Path]::GetFileNameWithoutExtension($taskDll)) { throw "DLL identity/version mismatch: $($taskEntry.FullName)" }
            }
        } finally { $taskInput.Dispose() }
    }
} finally {
    $taskArchive.Dispose()
    # Only the unique temporary directory created above is eligible for cleanup.
    if ([IO.Path]::GetFullPath($taskTemporary).StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $taskTemporary -Recurse -Force
    }
}
Write-Output "Verified $Version against commit $($taskCommit.Trim())."

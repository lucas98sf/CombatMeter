# Local integration checks use existing Release DLLs in an isolated temporary fixture.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskVersion = (Get-Content (Join-Path $taskRoot 'thunderstore/manifest.json') -Raw | ConvertFrom-Json).version_number
$taskFixture = Join-Path ([IO.Path]::GetTempPath()) ('combatmeter-release-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskFixture | Out-Null
try {
    $taskSourceFiles = @('README.md','Directory.Build.props','docs/versions.json','src/CombatMeter.Client/Plugin.cs','src/CombatMeter.Server/Plugin.cs','src/CombatMeter.Runtime/RuntimeHost.cs','src/CombatMeter.Shared/Contracts.cs')
    foreach ($taskFile in $taskSourceFiles) {
        $taskTarget = Join-Path $taskFixture $taskFile
        New-Item -ItemType Directory -Path (Split-Path -Parent $taskTarget) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $taskRoot $taskFile) -Destination $taskTarget
    }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'thunderstore') -Destination $taskFixture -Recurse
    & git -C $taskFixture init --quiet
    & git -C $taskFixture add --all
    & git -C $taskFixture -c user.name=Fixture -c user.email=fixture@example.invalid commit --quiet -m fixture
    if ($LASTEXITCODE -ne 0) { throw 'Could not create test source commit.' }
    $taskCommit = git -C $taskFixture rev-parse HEAD
    $taskZip = Join-Path $taskFixture "CombatMeter-Thunderstore-$taskVersion.zip"
    $taskArchive = [IO.Compression.ZipFile]::Open($taskZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($taskFile in @('manifest.json','README.md','CHANGELOG.md','icon.png')) {
            $taskMetadataPath = if ($taskFile -ceq 'README.md') { 'README.md' } else { "thunderstore/$taskFile" }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, (Join-Path $taskFixture $taskMetadataPath), $taskFile) | Out-Null
        }
        foreach ($taskKind in @('Client','Server','Core','Shared','Runtime')) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, (Join-Path $taskRoot "src/CombatMeter.$taskKind/bin/Release/net6.0/CombatMeter.$taskKind.dll"), "BepInEx/plugins/CombatMeter/CombatMeter.$taskKind.dll") | Out-Null
        }
    } finally { $taskArchive.Dispose() }
    $taskRecord = Join-Path $taskFixture "CombatMeter-Build-$taskVersion.json"
    function Write-TestRecord([string]$Commit) {
        @{ version=$taskVersion; sourceCommit=$Commit.Trim(); sha256=(Get-FileHash $taskZip).Hash } | ConvertTo-Json | Set-Content $taskRecord
    }
    function Assert-Rejected([string]$Name) {
        $taskRejected = $false
        try { & (Join-Path $PSScriptRoot 'verify-release-package.ps1') -Version $taskVersion -AssetDirectory $taskFixture -SourceRoot $taskFixture }
        catch { $taskRejected = $true }
        if (!$taskRejected) { throw "Accepted invalid release: $Name" }
    }
    Write-TestRecord $taskCommit
    & (Join-Path $PSScriptRoot 'verify-release-package.ps1') -Version $taskVersion -AssetDirectory $taskFixture -SourceRoot $taskFixture
    Write-TestRecord ('0' * 40)
    Assert-Rejected 'wrong source commit'
    Write-TestRecord $taskCommit
    Add-Content (Join-Path $taskFixture 'README.md') 'Unpackaged modification'
    Assert-Rejected 'README differs from source'
    Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination (Join-Path $taskFixture 'README.md') -Force
    $taskArchive = [IO.Compression.ZipFile]::Open($taskZip, [IO.Compression.ZipArchiveMode]::Update)
    try { $taskArchive.CreateEntry('../unexpected.txt') | Out-Null } finally { $taskArchive.Dispose() }
    Write-TestRecord $taskCommit
    Assert-Rejected 'unexpected/traversing archive entry'
    Write-Output '4 release package integration checks passed.'
} finally {
    if ([IO.Path]::GetFullPath($taskFixture).StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $taskFixture -Recurse -Force
    }
}

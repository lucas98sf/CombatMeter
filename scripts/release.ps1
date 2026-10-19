[CmdletBinding()]
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'release-policy.ps1')
$taskPolicy = Get-ReleasePolicy -Tag $Version
if (!$taskPolicy.Version) { throw 'Use a stable version such as 0.1.8, 0.2.0, or 1.0.0.' }
$Version = $taskPolicy.Version
Assert-ReleaseVersion -Root $taskRoot -Version $Version
Push-Location $taskRoot
try {
    if ($taskPolicy.Deploy) {
        $taskDirty = git status --porcelain
        if ($LASTEXITCODE -ne 0 -or $taskDirty) { throw 'Commit changes before preparing a public release.' }
        $taskCommit = git rev-parse HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve release commit.' }
        $taskRemote = git ls-remote origin refs/heads/main
        if ($LASTEXITCODE -ne 0 -or !$taskRemote -or $taskRemote.Split()[0] -ne $taskCommit) { throw 'Push this commit to origin/main before preparing a release.' }
    }
    & (Join-Path $PSScriptRoot 'build.ps1')
    & (Join-Path $PSScriptRoot 'package-thunderstore.ps1')
    $taskZip = Join-Path $taskRoot "artifacts/CombatMeter-Thunderstore-$Version.zip"
    if (!$taskPolicy.Deploy) {
        Write-Output "Local build only: $taskZip. No tag, GitHub Release, or Thunderstore upload created."
        return
    }
    if (git status --porcelain) { throw 'Build changed tracked source files; commit and rebuild before publishing.' }
    $taskProvenance = Join-Path $taskRoot "artifacts/CombatMeter-Build-$Version.json"
    [ordered]@{ version=$Version; sourceCommit=$taskCommit.Trim(); sha256=(Get-FileHash -LiteralPath $taskZip).Hash } |
        ConvertTo-Json | Set-Content -LiteralPath $taskProvenance -Encoding UTF8
    # Only a draft is created. Publishing that draft is the deliberate deployment trigger.
    & gh release create "v$Version" $taskZip $taskProvenance --repo lucas98sf/CombatMeter --target $taskCommit --draft --title "CombatMeter $Version" --notes-file (Join-Path $taskRoot 'thunderstore/CHANGELOG.md')
    if ($LASTEXITCODE -ne 0) { throw 'GitHub draft release creation failed.' }
    Write-Output 'Draft release prepared. Publish it on GitHub to trigger Thunderstore deployment.'
} finally { Pop-Location }

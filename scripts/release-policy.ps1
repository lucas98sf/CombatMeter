function Get-ReleasePolicy {
    param([Parameter(Mandatory)][string]$Tag, [bool]$Prerelease = $false)
    if ($Tag -notmatch '^v?((0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*))$') {
        return [pscustomobject]@{ Version=''; Deploy=$false; Reason='Not a stable three-part version tag.' }
    }
    $taskVersion = $Matches[1]
    $taskDeploy = !$Prerelease -and $taskVersion -ne '0.0.0'
    [pscustomobject]@{
        Version=$taskVersion; Deploy=$taskDeploy
        Reason=if ($taskDeploy) { 'Published three-part version, including patch releases.' } else { 'Prerelease and 0.0.0 builds do not deploy.' }
    }
}

function Assert-ReleaseVersion {
    param([string]$Root, [string]$Version)
    $taskManifest = Get-Content -LiteralPath (Join-Path $Root 'thunderstore/manifest.json') -Raw | ConvertFrom-Json
    $taskRecorded = Get-Content -LiteralPath (Join-Path $Root 'docs/versions.json') -Raw | ConvertFrom-Json
    [xml]$taskProps = Get-Content -LiteralPath (Join-Path $Root 'Directory.Build.props') -Raw
    if ($taskManifest.version_number -ne $Version -or $taskRecorded.combatMeter -ne $Version -or $taskProps.Project.PropertyGroup.Version -ne $Version) {
        throw 'Tag, manifest, docs/versions.json and Directory.Build.props versions must match.'
    }
    foreach ($taskFile in @('src/CombatMeter.Client/Plugin.cs','src/CombatMeter.Server/Plugin.cs','src/CombatMeter.Runtime/RuntimeHost.cs')) {
        $taskText = Get-Content -LiteralPath (Join-Path $Root $taskFile) -Raw
        if ($taskText -notmatch ('BepInPlugin\([^\r\n]+"' + [regex]::Escape($Version) + '"\)')) { throw "Plugin version mismatch: $taskFile" }
    }
    $taskContracts = Get-Content -LiteralPath (Join-Path $Root 'src/CombatMeter.Shared/Contracts.cs') -Raw
    if ($taskContracts -notmatch ('ClientVersion[^\r\n]+"' + [regex]::Escape($Version) + '"')) { throw 'Registration ClientVersion must match the release.' }
}

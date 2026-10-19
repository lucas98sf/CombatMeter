$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-policy.ps1')
$taskCases = @(
    @('0.0.1',$false,$true), @('v0.1.7',$false,$true), @('1.2.3',$false,$true),
    @('v0.2.1',$false,$true), @('v0.2.1',$true,$false),
    @('0.2.0',$false,$true), @('v1.0.0',$false,$true), @('2.3.0',$false,$true),
    @('v0.2.0',$true,$false), @('v0.2.0-rc.1',$false,$false), @('0.0.0',$false,$false),
    @('v01.2.0',$false,$false), @('main',$false,$false), @('0.2.0;echo unsafe',$false,$false)
)
foreach ($taskCase in $taskCases) {
    $taskResult = Get-ReleasePolicy -Tag $taskCase[0] -Prerelease $taskCase[1]
    if ($taskResult.Deploy -ne $taskCase[2]) { throw "Wrong deployment policy for $($taskCase[0])" }
}
Write-Output "$($taskCases.Count) release policy checks passed."

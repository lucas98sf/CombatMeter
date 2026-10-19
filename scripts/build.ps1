param(
    [switch]$SkipTests,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskDotnet = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $taskDotnet -PathType Leaf)) {
    throw 'Workspace .NET SDK is missing. Run powershell -NoProfile -ExecutionPolicy Bypass -File scripts/bootstrap.ps1 from the repository, then retry the build.'
}
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools/cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $taskRoot
try {
    & $taskDotnet build CombatMeter.sln -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if (!$SkipTests) {
        & $taskDotnet test tests/CombatMeter.Tests/CombatMeter.Tests.csproj -c $Configuration --no-build --logger 'trx;LogFileName=core.trx' --results-directory artifacts/test-results
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    }
} finally { Pop-Location }

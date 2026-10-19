# Dot-source this script: . ./scripts/enter-dev.ps1
# Environment changes apply only to the current PowerShell session and children.
$taskDevRoot = Split-Path -Parent $PSScriptRoot
$taskSdkPath = Join-Path $taskDevRoot '.tools/dotnet'
if (!(Test-Path -LiteralPath (Join-Path $taskSdkPath 'dotnet.exe'))) { throw 'Run scripts/bootstrap.ps1 first.' }
$env:DOTNET_ROOT = $taskSdkPath
$env:DOTNET_CLI_HOME = Join-Path $taskDevRoot '.tools/cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if (!($env:PATH.Split(';') -contains $taskSdkPath)) { $env:PATH = $taskSdkPath + ';' + $env:PATH }
& (Join-Path $taskSdkPath 'dotnet.exe') --version

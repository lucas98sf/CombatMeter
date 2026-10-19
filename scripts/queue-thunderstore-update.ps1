[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ProfilePath = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\VRising\profiles\Default",
    [switch]$EnableOpenWorld,
    [switch]$EnableDiagnostics,
    [switch]$HotReloadSetup,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskProfile = (Resolve-Path -LiteralPath $ProfilePath).Path
$taskInstaller = Join-Path $PSScriptRoot $(if ($HotReloadSetup) { 'enable-hot-reload.ps1' } else { 'install-thunderstore.ps1' })
$taskRegularInstaller = Join-Path $PSScriptRoot 'install-thunderstore.ps1'
$taskDevelopmentInstaller = Join-Path $PSScriptRoot 'enable-hot-reload.ps1'
$taskExisting = @(Get-CimInstance Win32_Process -Filter "Name = 'powershell.exe'" | Where-Object {
    $_.CommandLine -and ($_.CommandLine.Contains($taskRegularInstaller) -or $_.CommandLine.Contains($taskDevelopmentInstaller)) -and $_.CommandLine.Contains('-WaitForGameExit')
})
if ($taskExisting.Count) { Write-Output 'An installer is already waiting for game exit.'; return }
if (!$PSCmdlet.ShouldProcess($taskProfile, 'Queue updated CombatMeter installation after normal game exit')) { return }
$taskLogFolder = Join-Path $taskRoot 'artifacts/install-queue'
New-Item -ItemType Directory -Force -Path $taskLogFolder | Out-Null
$taskStamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$taskLog = Join-Path $taskLogFolder ($taskStamp + '.log')
$taskErrorLog = Join-Path $taskLogFolder ($taskStamp + '.error.log')
$taskArguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $taskInstaller + '" -ProfilePath "' + $taskProfile + '" -WaitForGameExit -Configuration ' + $Configuration
if (!$HotReloadSetup -and $EnableOpenWorld) { $taskArguments += ' -EnableOpenWorld' }
if (!$HotReloadSetup -and $EnableDiagnostics) { $taskArguments += ' -EnableDiagnostics' }
$taskWorker = Start-Process -FilePath 'powershell.exe' -ArgumentList $taskArguments -WindowStyle Hidden -RedirectStandardOutput $taskLog -RedirectStandardError $taskErrorLog -PassThru
@{ pid=$taskWorker.Id; profile=$taskProfile; log=$taskLog; errorLog=$taskErrorLog } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskLogFolder 'latest.json') -Encoding UTF8
Write-Output "Update queued in hidden installer PID $($taskWorker.Id). Exit the game normally; allow installation to finish before relaunching."
Write-Output "Installation log: $taskLog"

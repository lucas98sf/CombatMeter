param([ValidateSet('server','client')][string]$Mode = 'server')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskProfile = Join-Path $taskRoot ('.profiles/' + $Mode)
$taskExecutable = if ($Mode -eq 'server') { 'VRisingServer.exe' } else { 'VRising.exe' }
if ($Mode -eq 'client' -and (Get-Process VRising -ErrorAction SilentlyContinue)) { throw 'Close the running V Rising client before opening the development profile.' }
$taskArguments = '-logFile logs/Unity.log'
if ($Mode -eq 'server') { $taskArguments += ' -batchmode -nographics -persistentDataPath saves -saveName CombatMeterValidation' }
$taskWindow = if ($Mode -eq 'server') { 'Hidden' } else { 'Normal' }
$taskProcess = Start-Process -FilePath (Join-Path $taskProfile $taskExecutable) -WorkingDirectory $taskProfile -ArgumentList $taskArguments -WindowStyle $taskWindow -PassThru
@{ pid=$taskProcess.Id; mode=$Mode; executable=(Join-Path $taskProfile $taskExecutable) } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskProfile 'process.json') -Encoding UTF8
Write-Output ("Started isolated {0} (PID {1}). Logs: {2}" -f $Mode,$taskProcess.Id,(Join-Path $taskProfile 'logs/Unity.log'))

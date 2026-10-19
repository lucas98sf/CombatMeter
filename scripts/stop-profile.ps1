param([ValidateSet('server','client')][string]$Mode = 'server')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskRecord = Join-Path $taskRoot ('.profiles/' + $Mode + '/process.json')
if (!(Test-Path -LiteralPath $taskRecord)) { return }
$taskRecorded = Get-Content -LiteralPath $taskRecord -Raw | ConvertFrom-Json
$taskProcess = Get-Process -Id $taskRecorded.pid -ErrorAction SilentlyContinue
if ($null -eq $taskProcess) { return }
$taskExpected = [IO.Path]::GetFullPath($taskRecorded.executable)
$taskProfileRoot = [IO.Path]::GetFullPath((Join-Path $taskRoot '.profiles')) + [IO.Path]::DirectorySeparatorChar
if (!$taskExpected.StartsWith($taskProfileRoot, [StringComparison]::OrdinalIgnoreCase) -or
    ![IO.Path]::GetFullPath($taskProcess.Path).Equals($taskExpected, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The recorded process does not belong to this development profile.'
}
Stop-Process -Id $taskProcess.Id
Write-Output ('Stopped development ' + $Mode + ' PID ' + $taskProcess.Id)

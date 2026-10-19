[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet('Client','Server','Both')][string]$Mode = 'Client',
    [switch]$Watch
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskDotnet = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $taskDotnet -PathType Leaf)) {
    throw 'Workspace .NET SDK is missing. Run powershell -NoProfile -ExecutionPolicy Bypass -File scripts/bootstrap.ps1 from the repository, then restart this watcher.'
}
$taskReceiptPath = Join-Path $taskRoot 'artifacts/hot-reload/profile.json'
if (!(Test-Path -LiteralPath $taskReceiptPath)) {
    $taskQueuePath = Join-Path $taskRoot 'artifacts/install-queue/latest.json'
    if (Test-Path -LiteralPath $taskQueuePath) {
        $taskQueue = Get-Content -LiteralPath $taskQueuePath -Raw | ConvertFrom-Json
        $taskWorker = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + [int]$taskQueue.pid)
        if ($taskWorker -and $taskWorker.CommandLine -and $taskWorker.CommandLine.Contains('enable-hot-reload.ps1')) {
            throw "Hot-reload setup is still queued. Exit V Rising normally, wait for Development profile configured in $($taskQueue.log), then relaunch through Thunderstore and run this watcher."
        }
    }
    throw 'Hot-reload setup has not completed. Close V Rising normally, run scripts/enable-hot-reload.ps1, and relaunch through Thunderstore before running this watcher. Queued installs can time out; inspect artifacts/install-queue/latest.json for their logs.'
}
$taskReceipt = Get-Content -LiteralPath $taskReceiptPath -Raw | ConvertFrom-Json
$taskKinds = @(if ($Mode -eq 'Both') { 'Client'; 'Server' } else { $Mode })
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools/cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
function Get-DevelopmentHash([string]$Path) {
    # Windows PowerShell's Get-FileHash inherits WhatIf during path resolution.
    # Hashing is read-only and must still run during deployment previews.
    $taskStream = [IO.File]::OpenRead($Path)
    $taskHasher = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($taskHasher.ComputeHash($taskStream)).Replace('-', '') }
    finally { $taskStream.Dispose(); $taskHasher.Dispose() }
}
function Get-SourceFingerprint {
    $taskPaths = @((Join-Path $taskRoot 'Directory.Build.props'))
    foreach ($taskKind in @($taskKinds + @('Runtime','Core','Shared') | Select-Object -Unique)) {
        $taskPaths += @(Get-ChildItem -LiteralPath (Join-Path $taskRoot ('src/CombatMeter.' + $taskKind)) -Recurse -File |
            Where-Object { $_.Extension -in @('.cs','.csproj') -and $_.FullName -notmatch '\\(bin|obj)\\' } | Select-Object -ExpandProperty FullName)
    }
    return ($taskPaths | Sort-Object | ForEach-Object { $taskInfo = Get-Item -LiteralPath $_; "$($_):$($taskInfo.Length):$($taskInfo.LastWriteTimeUtc.Ticks)" }) -join '|'
}
do {
    $taskFingerprint = Get-SourceFingerprint
    try {
        foreach ($taskKind in $taskKinds) {
            & $taskDotnet build (Join-Path $taskRoot ('src/CombatMeter.' + $taskKind + '/CombatMeter.' + $taskKind + '.csproj')) -c $taskReceipt.configuration --nologo
            if ($LASTEXITCODE -ne 0) { throw 'Build failed. The running game keeps its previous plugin.' }
        }
        foreach ($taskStable in $taskReceipt.stable.PSObject.Properties) {
            $taskDll = Join-Path $taskRoot ('src/' + $taskStable.Name + '/bin/' + $taskReceipt.configuration + '/net6.0/' + $taskStable.Name + '.dll')
            $taskInstalled = Join-Path $taskReceipt.profile ('BepInEx/plugins/CombatMeter/' + $taskStable.Name + '.dll')
            if ((Get-DevelopmentHash $taskDll) -ne $taskStable.Value -or (Get-DevelopmentHash $taskInstalled) -ne $taskStable.Value) {
                throw "$($taskStable.Name) changed. Refresh the development profile and restart once; native runtime/core/contracts cannot be hot-replaced. Nothing was deployed."
            }
        }
        foreach ($taskKind in $taskKinds) {
            if (!$PSCmdlet.ShouldProcess($taskReceipt.reloadFolder, "Publish CombatMeter.$taskKind for Bloodpebble automatic reload")) { continue }
            foreach ($taskExtension in @('.pdb','.dll')) {
                $taskName = 'CombatMeter.' + $taskKind + $taskExtension
                $taskSource = Join-Path $taskRoot ('src/CombatMeter.' + $taskKind + '/bin/' + $taskReceipt.configuration + '/net6.0/' + $taskName)
                $taskDestination = Join-Path $taskReceipt.reloadFolder $taskName
                if ((Test-Path -LiteralPath $taskDestination) -and (Get-DevelopmentHash $taskSource) -eq (Get-DevelopmentHash $taskDestination)) { continue }
                $taskStaged = $taskDestination + '.staged'
                Copy-Item -LiteralPath $taskSource -Destination $taskStaged -Force
                Move-Item -LiteralPath $taskStaged -Destination $taskDestination -Force
                Write-Output "Published $taskName. Bloodpebble should reload in about two seconds; verify its Load/Unload log messages."
            }
        }
    } catch {
        if (!$Watch) { throw }
        Write-Warning $_.Exception.Message
    }
    if ($Watch) {
        Write-Output 'Watching source changes. Ctrl+C stops this development loop.'
        do { Start-Sleep -Milliseconds 500; $taskNext = Get-SourceFingerprint } while ($taskNext -eq $taskFingerprint)
        Start-Sleep -Milliseconds 500
    }
} while ($Watch)

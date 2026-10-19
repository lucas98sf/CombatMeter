[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ProfilePath = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\VRising\profiles\Default",
    [string]$GameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\VRising',
    [ValidateRange(5, 60)][int]$ShutdownTimeoutSeconds = 45
)
$ErrorActionPreference = 'Stop'
$taskProfile = (Resolve-Path -LiteralPath $ProfilePath).Path
$taskGameRoot = (Resolve-Path -LiteralPath $GameDirectory).Path
$taskExe = Join-Path $taskGameRoot 'VRising.exe'
$taskLoader = Join-Path $taskProfile 'BepInEx/core/BepInEx.Unity.IL2CPP.dll'
if (!(Test-Path -LiteralPath $taskExe) -or !(Test-Path -LiteralPath $taskLoader)) {
    throw 'The selected game executable or Thunderstore profile loader is missing.'
}
$taskClients = @(Get-CimInstance Win32_Process -Filter "Name = 'VRising.exe'")
if ($taskClients.Count -gt 1) { throw 'Multiple V Rising clients are running; close extras before restarting this profile.' }
$taskClient = $null
$taskServers = @()
if ($taskClients.Count -eq 1) {
    $taskCurrent = $taskClients[0]
    if (!$taskCurrent.ExecutablePath.Equals($taskExe, [StringComparison]::OrdinalIgnoreCase) -or
        !$taskCurrent.CommandLine.Replace('\','/').Contains($taskLoader.Replace('\','/'))) {
        throw 'The running client does not belong to the selected Thunderstore profile. No process was closed.'
    }
    $taskClient = Get-Process -Id $taskCurrent.ProcessId
    $taskServers = @(Get-CimInstance Win32_Process -Filter "Name = 'VRisingServer.exe'" |
        Where-Object { $_.CommandLine -match ('(?:^|\s)-parentPID\s+' + $taskClient.Id + '(?:\s|$)') } |
        ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue })
}
Write-Output "Profile: $taskProfile"
if (!$PSCmdlet.ShouldProcess($taskExe, 'Gracefully close the selected client, wait for its local server to save/exit, and relaunch with the Thunderstore profile')) { return }
if ($taskClient) {
    Write-Output "Requesting normal shutdown of V Rising PID $($taskClient.Id)."
    if (!$taskClient.CloseMainWindow()) { throw 'Could not request normal shutdown. Exit V Rising normally, then rerun this script.' }
    if (!$taskClient.WaitForExit($ShutdownTimeoutSeconds * 1000)) {
        throw 'V Rising is still open, possibly awaiting confirmation. Confirm exit in the game and rerun. No process was force-killed.'
    }
    foreach ($taskServer in $taskServers) {
        Write-Output "Waiting for local server PID $($taskServer.Id) to save and exit."
        if (!$taskServer.HasExited -and !$taskServer.WaitForExit($ShutdownTimeoutSeconds * 1000)) {
            throw 'The local server is still saving/running. Relaunch cancelled to avoid opening a second session. No server was force-killed.'
        }
    }
}
# These are the same Doorstop launch arguments used by Thunderstore's Modded
# launch. Native ServerLaunchFix handles starting the private server afterward.
$taskArguments = '--doorstop-enabled true --doorstop-target-assembly "' + $taskLoader.Replace('\','/') + '"'
$taskLaunched = Start-Process -FilePath $taskExe -WorkingDirectory $taskGameRoot -ArgumentList $taskArguments -WindowStyle Normal -PassThru
Write-Output "Launched modded V Rising PID $($taskLaunched.Id)."
Write-Output 'Choose Continue / your private game, then press F8 to open CombatMeter.'

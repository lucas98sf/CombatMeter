param([switch]$SkipSdk)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskCache = Join-Path $taskRoot '.cache/downloads'
New-Item -ItemType Directory -Force -Path $taskCache | Out-Null
function Get-Download([string]$Url, [string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) {
        & curl.exe --fail --location --silent --show-error --retry 2 --output $Path $Url
        if ($LASTEXITCODE -ne 0) { throw "Download failed: $Url" }
    }
}
if (!$SkipSdk) {
    $taskSdkRoot = Join-Path $taskRoot '.tools/dotnet'
    if (!(Test-Path -LiteralPath (Join-Path $taskSdkRoot 'sdk/10.0.401'))) {
        $taskSdkZip = Join-Path $taskCache 'dotnet-sdk-10.0.401-win-x64.zip'
        Get-Download 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-win-x64.zip' $taskSdkZip
        $taskExpected = '24b670ad3d923bfcf47df6c3b034152398b42f6dbc388e10d783aee1cfb5e5817d399fc0ae2a12cfa822a55e61d34830ccb15c50ef6efee437ab874bb7c79430'
        if ((Get-FileHash -LiteralPath $taskSdkZip -Algorithm SHA512).Hash.ToLowerInvariant() -ne $taskExpected) { throw 'SDK checksum mismatch.' }
        New-Item -ItemType Directory -Force -Path $taskSdkRoot | Out-Null
        & tar.exe -xf $taskSdkZip -C $taskSdkRoot
        if ($LASTEXITCODE -ne 0) { throw 'SDK extraction failed.' }
    }
    & (Join-Path $taskSdkRoot 'dotnet.exe') --version
}
$taskPackZip = Join-Path $taskCache 'BepInExPack_V_Rising-1.733.2.zip'
Get-Download 'https://thunderstore.io/package/download/BepInEx/BepInExPack_V_Rising/1.733.2/' $taskPackZip
$taskPackRoot = Join-Path $taskRoot '.tools/bepinex'
if (!(Test-Path -LiteralPath $taskPackRoot)) {
    New-Item -ItemType Directory -Force -Path $taskPackRoot | Out-Null
    & tar.exe -xf $taskPackZip -C $taskPackRoot
    if ($LASTEXITCODE -ne 0) { throw 'BepInEx extraction failed.' }
}
Write-Output 'SDK and loader dependencies are ready.'

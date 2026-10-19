# Original vector-style package artwork; no game textures are redistributed.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskBitmap = New-Object Drawing.Bitmap 1024,1024
$taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
$taskGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$taskGraphics.Clear([Drawing.Color]::FromArgb(18,15,22))
$taskBorder = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(176,145,98)),12
$taskRed = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(170,49,67))
$taskGold = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(196,157,90))
$taskCream = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(244,231,205))
$taskPath = New-Object Drawing.Drawing2D.GraphicsPath
$taskOutput = $null
$taskOutputGraphics = $null
try {
    $taskGraphics.DrawRectangle($taskBorder, 42,42,940,940)
    $taskGraphics.FillRectangle($taskRed, 156,410,712,114)
    $taskGraphics.FillRectangle($taskGold, 156,556,528,114)
    $taskGraphics.FillRectangle($taskRed, 156,702,344,114)
    $taskFormat = New-Object Drawing.StringFormat
    $taskFormat.Alignment = [Drawing.StringAlignment]::Center
    $taskPath.AddString('CM', [Drawing.FontFamily]::GenericSerif, [int][Drawing.FontStyle]::Bold, 256, [Drawing.RectangleF]::new(100,95,824,310), $taskFormat)
    $taskGraphics.FillPath($taskCream, $taskPath)
    $taskFormat.Dispose()
    $taskOutput = New-Object Drawing.Bitmap 256,256
    $taskOutputGraphics = [Drawing.Graphics]::FromImage($taskOutput)
    $taskOutputGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $taskOutputGraphics.DrawImage($taskBitmap, 0,0,256,256)
    $taskOutput.Save((Join-Path $taskRoot 'thunderstore/icon.png'), [Drawing.Imaging.ImageFormat]::Png)
} finally {
    if ($taskOutputGraphics) { $taskOutputGraphics.Dispose() }
    if ($taskOutput) { $taskOutput.Dispose() }
    $taskPath.Dispose(); $taskBorder.Dispose(); $taskRed.Dispose(); $taskGold.Dispose(); $taskCream.Dispose()
    $taskGraphics.Dispose(); $taskBitmap.Dispose()
}

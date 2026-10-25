$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class IconNative2 {
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
}
'@ -Language CSharp

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

function New-HexagonPoints([single]$size) {
    $cx = $size / 2.0
    $cy = $size / 2.0
    $rx = $size * 0.47
    $ry = $size * 0.45
    $points = New-Object System.Drawing.PointF[] 6
    for ($i = 0; $i -lt 6; $i++) {
        $angle = (60.0 * $i - 90.0) * [Math]::PI / 180.0
        $points[$i] = New-Object System.Drawing.PointF(
            [single]($cx + $rx * [Math]::Cos($angle)),
            [single]($cy + $ry * [Math]::Sin($angle)))
    }
    return $points
}

function New-CompetitiveBadge([int]$size, [bool]$enabled) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $objects = New-Object System.Collections.ArrayList
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $background = [System.Drawing.Color]::FromArgb(255, 11, 15, 23)
        $borderColor = if ($enabled) { [System.Drawing.Color]::FromArgb(255, 255, 59, 48) } else { [System.Drawing.Color]::FromArgb(255, 71, 85, 105) }
        $arrowColor = if ($enabled) { [System.Drawing.Color]::FromArgb(255, 248, 250, 252) } else { [System.Drawing.Color]::FromArgb(255, 148, 163, 184) }
        $accentColor = if ($enabled) { [System.Drawing.Color]::FromArgb(255, 34, 211, 238) } else { [System.Drawing.Color]::FromArgb(255, 100, 116, 139) }

        $fill = New-Object System.Drawing.SolidBrush($background); [void]$objects.Add($fill)
        $border = New-Object System.Drawing.Pen($borderColor, [single][Math]::Max(1.5, $size * 0.045)); [void]$objects.Add($border)
        $border.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Miter
        $linePen = New-Object System.Drawing.Pen($arrowColor, [single][Math]::Max(2.0, $size * 0.070)); [void]$objects.Add($linePen)
        $linePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $linePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $arrowBrush = New-Object System.Drawing.SolidBrush($arrowColor); [void]$objects.Add($arrowBrush)
        $accentBrush = New-Object System.Drawing.SolidBrush($accentColor); [void]$objects.Add($accentBrush)

        $points = New-HexagonPoints $size
        $graphics.FillPolygon($fill, $points)
        $graphics.DrawPolygon($border, $points)

        $s = [single]$size
        $yTop = [single]($s * 0.372)
        $yBottom = [single]($s * 0.628)

        $graphics.DrawLine($linePen, [single]($s * 0.235), $yTop, [single]($s * 0.695), $yTop)
        $graphics.FillPolygon($arrowBrush, [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF([single]($s * 0.790), $yTop)),
            (New-Object System.Drawing.PointF([single]($s * 0.665), [single]($s * 0.282))),
            (New-Object System.Drawing.PointF([single]($s * 0.665), [single]($s * 0.462)))))

        $graphics.DrawLine($linePen, [single]($s * 0.305), $yBottom, [single]($s * 0.765), $yBottom)
        $graphics.FillPolygon($arrowBrush, [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF([single]($s * 0.210), $yBottom)),
            (New-Object System.Drawing.PointF([single]($s * 0.335), [single]($s * 0.538))),
            (New-Object System.Drawing.PointF([single]($s * 0.335), [single]($s * 0.718)))))

        $d = [single]($s * 0.055)
        $graphics.FillPolygon($accentBrush, [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF([single]($s * 0.5), [single]($s * 0.5 - $d))),
            (New-Object System.Drawing.PointF([single]($s * 0.5 + $d), [single]($s * 0.5))),
            (New-Object System.Drawing.PointF([single]($s * 0.5), [single]($s * 0.5 + $d))),
            (New-Object System.Drawing.PointF([single]($s * 0.5 - $d), [single]($s * 0.5)))))

        return @{ Bitmap = $bitmap; Graphics = $graphics; Objects = $objects }
    }
    catch {
        foreach ($o in $objects) { $o.Dispose() }
        $graphics.Dispose()
        $bitmap.Dispose()
        throw
    }
}

function Save-Icon([int]$size, [bool]$enabled, [string]$path) {
    $badge = New-CompetitiveBadge $size $enabled
    $handle = [IntPtr]::Zero
    try {
        $handle = $badge.Bitmap.GetHicon()
        $icon = [System.Drawing.Icon]::FromHandle($handle)
        try {
            $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Create)
            try { $icon.Save($stream) } finally { $stream.Dispose() }
        }
        finally { $icon.Dispose() }
    }
    finally {
        if ($handle -ne [IntPtr]::Zero) { [void][IconNative2]::DestroyIcon($handle) }
        $badge.Graphics.Dispose()
        foreach ($o in $badge.Objects) { $o.Dispose() }
        $badge.Bitmap.Dispose()
    }
}

function Save-Preview([int]$size, [bool]$enabled, [string]$path) {
    $badge = New-CompetitiveBadge $size $enabled
    try { $badge.Bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png) }
    finally {
        $badge.Graphics.Dispose()
        foreach ($o in $badge.Objects) { $o.Dispose() }
        $badge.Bitmap.Dispose()
    }
}

Save-Icon 128 $true (Join-Path $root 'app.ico')
Save-Icon 32 $true (Join-Path $root 'tray-on.ico')
Save-Icon 32 $false (Join-Path $root 'tray-off.ico')
Save-Preview 256 $true (Join-Path $env:TEMP 'fps-socd-icon-preview-on.png')
Save-Preview 256 $false (Join-Path $env:TEMP 'fps-socd-icon-preview-off.png')

Write-Host "Generated app.ico, tray-on.ico, tray-off.ico and previews."

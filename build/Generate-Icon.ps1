$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assetDirectory = Join-Path $projectRoot 'assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
$iconPath = Join-Path $assetDirectory 'local-transfer.ico'
$previewPath = Join-Path $assetDirectory 'local-transfer-icon-256.png'

function New-RoundedRectanglePath([System.Drawing.RectangleF]$Rectangle, [float]$Radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($Rectangle.X, $Rectangle.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Rectangle.X, $Rectangle.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$Size) {
    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $background = [System.Drawing.Color]::FromArgb(255, 37, 99, 235)
        $white = [System.Drawing.Color]::White
        $margin = [Math]::Max(1, $Size * 0.035)
        $radius = [Math]::Max(2, $Size * 0.22)
        $rect = [System.Drawing.RectangleF]::new(
            [single]$margin,
            [single]$margin,
            [single]($Size - (2 * $margin)),
            [single]($Size - (2 * $margin)))
        $path = New-RoundedRectanglePath $rect $radius
        $brush = New-Object System.Drawing.SolidBrush($background)
        $graphics.FillPath($brush, $path)

        $stroke = [Math]::Max(1.2, $Size * 0.045)
        $pen = New-Object System.Drawing.Pen($white, $stroke)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

        $graphics.DrawRectangle($pen, $Size * 0.18, $Size * 0.23, $Size * 0.24, $Size * 0.54)
        $graphics.DrawRectangle($pen, $Size * 0.59, $Size * 0.29, $Size * 0.23, $Size * 0.39)
        $graphics.DrawLine($pen, $Size * 0.65, $Size * 0.76, $Size * 0.76, $Size * 0.76)
        $graphics.DrawLine($pen, $Size * 0.705, $Size * 0.68, $Size * 0.705, $Size * 0.76)

        $graphics.DrawLine($pen, $Size * 0.44, $Size * 0.40, $Size * 0.56, $Size * 0.40)
        $graphics.DrawLine($pen, $Size * 0.51, $Size * 0.35, $Size * 0.56, $Size * 0.40)
        $graphics.DrawLine($pen, $Size * 0.51, $Size * 0.45, $Size * 0.56, $Size * 0.40)
        $graphics.DrawLine($pen, $Size * 0.56, $Size * 0.59, $Size * 0.44, $Size * 0.59)
        $graphics.DrawLine($pen, $Size * 0.49, $Size * 0.54, $Size * 0.44, $Size * 0.59)
        $graphics.DrawLine($pen, $Size * 0.49, $Size * 0.64, $Size * 0.44, $Size * 0.59)

        $pen.Dispose()
        $brush.Dispose()
        $path.Dispose()
        return $bitmap
    }
    finally {
        $graphics.Dispose()
    }
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = New-Object System.Collections.Generic.List[byte[]]
foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size
    try {
        $stream = New-Object IO.MemoryStream
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $images.Add($stream.ToArray())
        $stream.Dispose()
        if ($size -eq 256) { $bitmap.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png) }
    }
    finally { $bitmap.Dispose() }
}

$file = [IO.File]::Create($iconPath)
$writer = New-Object IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $sizeByte = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$sizeByte)
        $writer.Write([byte]$sizeByte)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }
    foreach ($image in $images) { $writer.Write($image) }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Host "Application icon ready: $iconPath"

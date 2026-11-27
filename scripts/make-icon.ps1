# 生成应用图标：按 beads 官方 logo（docs/images/logo.svg，32x32 圆角方块 #25C2A0 + 白色粗体 "bd"）
# 重绘为多尺寸 ICO，供 Windows 可执行文件与窗口图标使用。
# 用法：pwsh -File scripts/make-icon.ps1
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\Bd.App\Assets\beads.ico')
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$accent = [System.Drawing.ColorTranslator]::FromHtml('#25C2A0')
$pngs = @()

foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $radius = [Math]::Max(1.0, $size * 6.0 / 32.0)
    $d = $radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.SolidBrush($accent)
    $graphics.FillPath($brush, $path)

    $fontSize = $size * 18.0 / 32.0
    $font = New-Object System.Drawing.Font('Segoe UI', $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $box = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $graphics.DrawString('bd', $font, [System.Drawing.Brushes]::White, $box, $format)

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $stream.ToArray()

    $stream.Dispose()
    $font.Dispose()
    $brush.Dispose()
    $path.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($ico)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $data = $pngs[$i]
    # 256 在 ICO 目录里用 0 表示
    $side = if ($size -ge 256) { 0 } else { $size }
    $writer.Write([Byte]$side)
    $writer.Write([Byte]$side)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$data.Length)
    $writer.Write([UInt32]$offset)
    $offset += $data.Length
}

foreach ($data in $pngs) {
    $writer.Write($data)
}

$writer.Flush()
$resolved = [System.IO.Path]::GetFullPath($OutputPath, (Get-Location).Path)
New-Item -ItemType Directory -Force -Path (Split-Path $resolved) | Out-Null
[System.IO.File]::WriteAllBytes($resolved, $ico.ToArray())

$writer.Dispose()
$ico.Dispose()

# 应用内展示用 PNG（取最大尺寸，避免 16px 放大发虚）
$pngPath = [System.IO.Path]::Combine([System.IO.Path]::GetDirectoryName($resolved), 'beads.png')
[System.IO.File]::WriteAllBytes($pngPath, $pngs[$sizes.Count - 1])

Write-Output "wrote $resolved ($((Get-Item $resolved).Length) bytes, $($sizes.Count) sizes)"
Write-Output "wrote $pngPath ($((Get-Item $pngPath).Length) bytes)"

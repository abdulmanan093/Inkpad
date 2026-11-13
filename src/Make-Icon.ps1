$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bitmap = [System.Drawing.Bitmap]::new(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$navy = [System.Drawing.ColorTranslator]::FromHtml('#1A7193')
$cyan = [System.Drawing.ColorTranslator]::FromHtml('#C9EFF7')
$white = [System.Drawing.Color]::White
$tile = [System.Drawing.Drawing2D.GraphicsPath]::new()
$tile.AddArc(18, 18, 60, 60, 180, 90)
$tile.AddArc(178, 18, 60, 60, 270, 90)
$tile.AddArc(178, 178, 60, 60, 0, 90)
$tile.AddArc(18, 178, 60, 60, 90, 90)
$tile.CloseFigure()
$gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.Rectangle]::new(18, 18, 220, 220), $cyan, [System.Drawing.ColorTranslator]::FromHtml('#67BEDD'), 60.0)
$g.FillPath($gradient, $tile)
$g.FillRectangle([System.Drawing.SolidBrush]::new($white), 65, 53, 119, 152)
$stroke = [System.Drawing.Pen]::new($navy, 9)
$stroke.StartCap = $stroke.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
foreach ($offset in 86, 112, 138, 164) { $g.DrawLine($stroke, 86, $offset, 161, $offset) }
foreach ($offset in 90, 124, 158) { $g.DrawLine($stroke, $offset, 44, $offset, 61) }
$pencil = [System.Drawing.Point[]]@([System.Drawing.Point]::new(122, 199), [System.Drawing.Point]::new(130, 172), [System.Drawing.Point]::new(197, 105), [System.Drawing.Point]::new(217, 126), [System.Drawing.Point]::new(151, 193))
$g.FillPolygon([System.Drawing.SolidBrush]::new($white), $pencil)
$g.DrawPolygon($stroke, $pencil)
$g.DrawLine($stroke, 186, 117, 207, 137)
$g.DrawLine($stroke, 130, 172, 151, 193)
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($size in $sizes) {
    $image = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $draw = [System.Drawing.Graphics]::FromImage($image)
    $draw.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $draw.DrawImage($bitmap, 0, 0, $size, $size)
    $buffer = [System.IO.MemoryStream]::new()
    $image.Save($buffer, [System.Drawing.Imaging.ImageFormat]::Png)
    ,$buffer.ToArray()
    $buffer.Dispose()
    $draw.Dispose()
    $image.Dispose()
}
$file = [System.IO.File]::Create((Join-Path $PSScriptRoot 'Inkpad.ico'))
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)
$position = 6 + 16 * $sizes.Count
for ($index = 0; $index -lt $sizes.Count; $index++) {
    $side = [byte]($sizes[$index] % 256)
    $writer.Write($side)
    $writer.Write($side)
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$index].Length)
    $writer.Write([uint32]$position)
    $position += $images[$index].Length
}
foreach ($payload in $images) { $writer.Write([byte[]]$payload) }
$writer.Dispose()
$g.Dispose()
$bitmap.Dispose()

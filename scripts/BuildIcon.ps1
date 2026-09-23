# Rebuild the vector-style icon in Assets/OmniMemo.svg at Windows icon sizes.
param([string]$Output='src/Memoit/Assets/OmniMemo.ico')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$images=New-Object 'System.Collections.Generic.List[byte[]]'
$sizes=@(16,20,24,32,48,64,128,256)
foreach ($size in $sizes) {
    $canvas=New-Object System.Drawing.Bitmap ($size*4),($size*4)
    $g=[System.Drawing.Graphics]::FromImage($canvas)
    $g.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform(($size*4/64.0),($size*4/64.0))
    $ink=New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#244A60'))
    $paper=New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#FFF4D6'))
    $fold=New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#ADCEDF'))
    $outer=New-Object System.Drawing.Drawing2D.GraphicsPath
    $outer.AddArc(2,2,28,28,180,90); $outer.AddArc(34,2,28,28,270,90)
    $outer.AddArc(34,34,28,28,0,90); $outer.AddArc(2,34,28,28,90,90); $outer.CloseFigure()
    $g.FillPath($ink,$outer)
    $sheet=New-Object System.Drawing.Drawing2D.GraphicsPath
    $sheet.AddLine(20,12,40,12); $sheet.AddLine(40,12,50,22); $sheet.AddLine(50,22,50,48)
    $sheet.AddBezier(50,48,50,51,49,52,46,52); $sheet.AddLine(46,52,20,52)
    $sheet.AddBezier(20,52,17,52,16,51,16,48); $sheet.AddLine(16,48,16,16)
    $sheet.AddBezier(16,16,16,13,17,12,20,12); $sheet.CloseFigure()
    $g.FillPath($paper,$sheet)
    $corner=New-Object System.Drawing.Drawing2D.GraphicsPath
    $corner.AddLine(40,12,40,19); $corner.AddBezier(40,19,40,21,41,22,43,22)
    $corner.AddLine(43,22,50,22); $corner.CloseFigure(); $g.FillPath($fold,$corner)
    $pen=New-Object System.Drawing.Pen $ink,3
    $pen.StartCap=$pen.EndCap=[System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($pen,24,32,42,32); $g.DrawLine($pen,24,40,36,40)
    $g.Dispose()
    $small=New-Object System.Drawing.Bitmap $size,$size
    $g2=[System.Drawing.Graphics]::FromImage($small)
    $g2.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g2.DrawImage($canvas,0,0,$size,$size); $g2.Dispose()
    $stream=New-Object IO.MemoryStream
    $small.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $images.Add($stream.ToArray())
    $stream.Dispose(); $small.Dispose(); $canvas.Dispose()
    $outer.Dispose(); $sheet.Dispose(); $corner.Dispose(); $pen.Dispose(); $ink.Dispose(); $paper.Dispose(); $fold.Dispose()
}
$path=[IO.Path]::GetFullPath($Output)
$file=[IO.File]::Create($path)
$writer=New-Object IO.BinaryWriter $file
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset=6+16*$sizes.Count
    for ($i=0;$i -lt $sizes.Count;$i++) {
        $size=$sizes[$i]; if ($size -eq 256) { $size=0 }
        $writer.Write([byte]$size); $writer.Write([byte]$size); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($png in $images) { $writer.Write($png) }
} finally { $writer.Dispose() }
Write-Output "Icon generated: $path (16, 20, 24, 32, 48, 64, 128, 256px)"

Add-Type -AssemblyName System.Drawing

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

$green = [System.Drawing.Color]::FromArgb(45, 164, 78)
$pen = New-Object System.Drawing.Pen $green, 36
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

$points = New-Object 'System.Drawing.PointF[]' 3
$points[0] = New-Object System.Drawing.PointF 50, 140
$points[1] = New-Object System.Drawing.PointF 110, 200
$points[2] = New-Object System.Drawing.PointF 210, 70
$g.DrawLines($pen, $points)
$pen.Dispose()
$g.Dispose()

$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $ms.ToArray()
$ms.Dispose()
$bmp.Dispose()

$icoPath = Join-Path $PSScriptRoot "appicon.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter $fs
# ICONDIR
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]1)
# ICONDIRENTRY
$bw.Write([byte]0)
$bw.Write([byte]0)
$bw.Write([byte]0)
$bw.Write([byte]0)
$bw.Write([uint16]1)
$bw.Write([uint16]32)
$bw.Write([uint32]$pngBytes.Length)
$bw.Write([uint32]22)
$bw.Write($pngBytes)
$bw.Close()
$fs.Close()
Write-Output "Wrote $icoPath ($($pngBytes.Length) bytes PNG inside ICO)"

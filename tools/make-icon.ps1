# Generates ResSwitch\app.ico (monitor with a 4:3 picture inside). Run once; the .ico is committed.
Add-Type -AssemblyName System.Drawing

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $s = $size / 256.0
    $blue = [System.Drawing.Color]::FromArgb(0, 120, 212)
    $dark = [System.Drawing.Color]::FromArgb(30, 34, 44)
    # monitor body (16:9)
    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 28 * $s
    $x = 12 * $s; $y = 36 * $s; $w = 232 * $s; $h = 150 * $s
    $body.AddArc($x, $y, $r, $r, 180, 90); $body.AddArc($x + $w - $r, $y, $r, $r, 270, 90)
    $body.AddArc($x + $w - $r, $y + $h - $r, $r, $r, 0, 90); $body.AddArc($x, $y + $h - $r, $r, $r, 90, 90)
    $body.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $dark), $body)
    # 4:3 picture
    $ph = 118 * $s; $pw = $ph * 4 / 3
    $px = (256 * $s - $pw) / 2; $py = 52 * $s
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $blue), $px, $py, $pw, $ph)
    # stand
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $dark), 108 * $s, 186 * $s, 40 * $s, 26 * $s)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush $dark), 78 * $s, 210 * $s, 100 * $s, 16 * $s)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = New-Frame $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
$dest = Join-Path (Split-Path $PSScriptRoot -Parent) 'ResSwitch\app.ico'
[System.IO.File]::WriteAllBytes($dest, $out.ToArray())
Write-Host "Wrote $dest"

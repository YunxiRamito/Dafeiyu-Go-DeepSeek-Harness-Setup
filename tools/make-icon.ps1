# 生成安装器图标 assets\DSHInstaller.ico
#
# 图案**不是自己画的**:直接取 DSH 官方的鲸鱼标(和界面里用的是同一份路径数据,
# 见 src\DshInstaller\Controls\DshBrandData.cs 里的 MarkPath,来自前端产物 favicon.svg,
# viewBox 50x50)。这样安装器图标、窗口里的小图标、网页 favicon 三处长得一模一样。
#
# 路径里只有 M / C / Z 三个命令(全绝对坐标),所以这里手写一个最小解析器就够 ——
# 不引任何 SVG 库,也不依赖联网。
#
# 帧规格:16/24/32/48/64 用传统 DIB 帧(兼容性最好),128/256 用 PNG 帧(体积小)。
#
# 用法:pwsh -File tools\make-icon.ps1

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'assets'
$brandFile = Join-Path $root 'src\DshInstaller\Controls\DshBrandData.cs'
if (-not (Test-Path $assets)) { New-Item -ItemType Directory -Path $assets -Force | Out-Null }
$target = Join-Path $assets 'DSHInstaller.ico'

# ---------------------------------------------------------------- 取官方路径
if (-not (Test-Path $brandFile)) { throw "找不到品牌数据:$brandFile" }
$brandText = Get-Content $brandFile -Raw
$markMatch = [regex]::Match($brandText, 'MarkPath\s*=\s*"([^"]+)"')
if (-not $markMatch.Success) { throw 'DshBrandData.cs 里没抠到 MarkPath' }
$pathData = $markMatch.Groups[1].Value
Write-Host ("官方鲸鱼路径:{0} 个字符" -f $pathData.Length)

# ---------------------------------------------------------------- 最小 SVG 路径解析
# 返回 @{ Figures = @( @{ Start = PointF; Curves = @( @(...4 个 PointF) ) } ) }
function ConvertFrom-MarkPath {
    param([string]$Data)

    $tokens = [regex]::Matches($Data, '[MmLlHhVvCcSsQqTtAaZz]|-?\d*\.?\d+(?:[eE][-+]?\d+)?') |
        ForEach-Object { $_.Value }

    $figures = New-Object System.Collections.ArrayList
    $current = $null
    $cursor = $null
    $i = 0

    while ($i -lt $tokens.Count) {
        $t = $tokens[$i]

        switch -CaseSensitive ($t) {
            'M' {
                $x = [double]$tokens[$i + 1]; $y = [double]$tokens[$i + 2]; $i += 3
                $current = @{ Start = (New-Object System.Drawing.PointF($x, $y)); Curves = (New-Object System.Collections.ArrayList) }
                [void]$figures.Add($current)
                $cursor = $current.Start
            }
            'C' {
                if ($null -eq $current) { throw 'C 出现在 M 之前' }
                $p1 = New-Object System.Drawing.PointF([double]$tokens[$i + 1], [double]$tokens[$i + 2])
                $p2 = New-Object System.Drawing.PointF([double]$tokens[$i + 3], [double]$tokens[$i + 4])
                $p3 = New-Object System.Drawing.PointF([double]$tokens[$i + 5], [double]$tokens[$i + 6])
                $i += 7
                [void]$current.Curves.Add(@($cursor, $p1, $p2, $p3))
                $cursor = $p3
            }
            'Z' {
                $i++
            }
            default {
                throw "这个路径命令本图标脚本还不认:$t(要是品牌数据换了新写法,这里得跟着补)"
            }
        }
    }

    return $figures
}

$figures = ConvertFrom-MarkPath -Data $pathData
Write-Host ("解析出 {0} 个子路径" -f $figures.Count)

# 包围盒(含控制点,够用)
$minX = [double]::MaxValue; $minY = [double]::MaxValue
$maxX = [double]::MinValue; $maxY = [double]::MinValue
foreach ($fig in $figures) {
    foreach ($curve in $fig.Curves) {
        foreach ($p in $curve) {
            if ($p.X -lt $minX) { $minX = $p.X }
            if ($p.Y -lt $minY) { $minY = $p.Y }
            if ($p.X -gt $maxX) { $maxX = $p.X }
            if ($p.Y -gt $maxY) { $maxY = $p.Y }
        }
    }
}
$markW = $maxX - $minX
$markH = $maxY - $minY
Write-Host ("鲸鱼原始包围盒:{0:N2} x {1:N2}" -f $markW, $markH)

# ---------------------------------------------------------------- 画一张
function New-RoundedRect {
    param([single]$x, [single]$y, [single]$w, [single]$h, [single]$radius)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$Size, [switch]$Bare)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $Size / 256.0

    # 底:圆角方块 + 斜向蓝渐变(留出四周一点边距,任务栏里不糊边)
    if (-not $Bare) {
        $bg = New-RoundedRect -x (8 * $s) -y (8 * $s) -w (240 * $s) -h (240 * $s) -radius (54 * $s)
        $rect = New-Object System.Drawing.RectangleF((8 * $s), (8 * $s), (240 * $s), (240 * $s))
        $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $rect,
            [System.Drawing.Color]::FromArgb(255, 77, 107, 254),
            [System.Drawing.Color]::FromArgb(255, 20, 45, 150),
            55.0)
        $g.FillPath($gradient, $bg)
    }

    # 鲸鱼:等比缩放到方块里(宽为主),居中
    $inner = $Size * 0.74
    $scale = $inner / [Math]::Max($markW, $markH * (1.0))
    # 鲸鱼偏扁,所以按宽度定比例后再检查高度会不会顶出去
    $scaleW = $Size * 0.74 / $markW
    $scaleH = $Size * 0.62 / $markH
    $scale = [Math]::Min($scaleW, $scaleH)

    $drawW = $markW * $scale
    $drawH = $markH * $scale
    $ox = ($Size - $drawW) / 2.0 - $minX * $scale
    $oy = ($Size - $drawH) / 2.0 - $minY * $scale

    $whale = New-Object System.Drawing.Drawing2D.GraphicsPath
    # 官方那份用的是 even-odd(见 Controls\SvgPath.cs 里的 FillRule.EvenOdd),
    # System.Drawing 里对应的就是 Alternate —— 眼睛才是"挖空"而不是实心点
    $whale.FillMode = [System.Drawing.Drawing2D.FillMode]::Alternate

    foreach ($fig in $figures) {
        foreach ($curve in $fig.Curves) {
            $pts = New-Object System.Drawing.PointF[] 4
            for ($k = 0; $k -lt 4; $k++) {
                $pts[$k] = New-Object System.Drawing.PointF(
                    [single]($curve[$k].X * $scale + $ox),
                    [single]($curve[$k].Y * $scale + $oy))
            }
            $whale.AddBezier($pts[0], $pts[1], $pts[2], $pts[3])
        }
        $whale.CloseFigure()
    }

    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPath($white, $whale)

    $whale.Dispose()
    $g.Dispose()
    return $bmp
}

# ---------------------------------------------------------------- 编码
function Get-DibFrame {
    param([System.Drawing.Bitmap]$Bitmap)

    $size = $Bitmap.Width
    $stride = $size * 4
    $xor = New-Object byte[] ($stride * $size)
    $maskStride = [int]([math]::Ceiling($size / 32.0) * 4)
    $and = New-Object byte[] ($maskStride * $size)

    # DIB 自下而上存
    for ($y = 0; $y -lt $size; $y++) {
        $srcY = $size - 1 - $y
        for ($x = 0; $x -lt $size; $x++) {
            $c = $Bitmap.GetPixel($x, $srcY)
            $o = $y * $stride + $x * 4
            $xor[$o] = $c.B
            $xor[$o + 1] = $c.G
            $xor[$o + 2] = $c.R
            $xor[$o + 3] = $c.A
        }
    }

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    $writer.Write([int]40)
    $writer.Write([int]$size)
    $writer.Write([int]($size * 2))   # 高度写两倍:图标格式的约定(XOR + AND)
    $writer.Write([int16]1)
    $writer.Write([int16]32)
    $writer.Write([int]0)
    $writer.Write([int]($xor.Length + $and.Length))
    $writer.Write([int]0); $writer.Write([int]0)
    $writer.Write([int]0); $writer.Write([int]0)
    $writer.Write($xor)
    $writer.Write($and)
    $writer.Flush()

    $bytes = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()
    return , $bytes
}

function Get-PngFrame {
    param([System.Drawing.Bitmap]$Bitmap)

    $stream = New-Object System.IO.MemoryStream
    $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return , $bytes
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()

foreach ($size in $sizes) {
    $bmp = New-IconBitmap -Size $size
    if ($size -ge 128) { $data = Get-PngFrame -Bitmap $bmp } else { $data = Get-DibFrame -Bitmap $bmp }
    $frames += [pscustomobject]@{ Size = $size; Data = $data }
    $bmp.Dispose()
}

# ---------------------------------------------------------------- 组装 ICO
$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)

$writer.Write([int16]0)
$writer.Write([int16]1)
$writer.Write([int16]$frames.Count)

$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $dim = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$dim)
    $writer.Write([byte]$dim)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([int16]1)
    $writer.Write([int16]32)
    $writer.Write([int]$frame.Data.Length)
    $writer.Write([int]$offset)
    $offset += $frame.Data.Length
}

foreach ($frame in $frames) {
    # 必须显式给 byte[] + 偏移 + 长度:PowerShell 会把 $writer.Write($byteArray)
    # 解析成 Write(byte) 重载,只写进去一个字节 —— 生成的 ico 会变成 0.1 KB(实测踩过)。
    $payload = [byte[]]$frame.Data
    $writer.Write($payload, 0, $payload.Length)
}

$writer.Flush()
[System.IO.File]::WriteAllBytes($target, $stream.ToArray())
$writer.Dispose()
$stream.Dispose()

Write-Host ("图标已生成:{0}  ({1:N1} KB,{2} 帧,图案=官方鲸鱼)" -f `
    $target, ((Get-Item $target).Length / 1KB), $frames.Count) -ForegroundColor Green

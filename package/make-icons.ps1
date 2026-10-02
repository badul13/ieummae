# 아이콘 그리기 - 양털 구름 + 단추 눈 + 분홍 코 (시안의 양털 머리를 작게)
# 결과: src/Ieummae.App/Assets/ieummae.ico (16~256), package/Assets/*.png (패키지 로고)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent

function Draw([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 64.0
    function C($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }
    $wool = New-Object System.Drawing.SolidBrush (C "#FFFDF7")
    $shade = New-Object System.Drawing.SolidBrush (C "#D9CBB4")
    $felt = New-Object System.Drawing.SolidBrush (C "#6B4B3D")
    $pink = New-Object System.Drawing.SolidBrush (C "#E7A3A1")
    $eye = New-Object System.Drawing.SolidBrush (C "#2C2420")

    # 처진 귀 - 양쪽 아래로
    $g.TranslateTransform(10 * $s, 34 * $s); $g.RotateTransform(-25); $g.FillEllipse($felt, -9 * $s, -4.5 * $s, 18 * $s, 9 * $s); $g.ResetTransform()
    $g.TranslateTransform(54 * $s, 34 * $s); $g.RotateTransform(25); $g.FillEllipse($felt, -9 * $s, -4.5 * $s, 18 * $s, 9 * $s); $g.ResetTransform()

    # 양털 구름 - 큰 원 둘레에 작은 원, 아래쪽 그림자 먼저
    $bumps = @()
    for ($i = 0; $i -lt 10; $i++) {
        $a = $i * [Math]::PI * 2 / 10
        $bumps += , @((32 + [Math]::Cos($a) * 19), (32 + [Math]::Sin($a) * 18))
    }
    foreach ($b in $bumps) { $g.FillEllipse($shade, ($b[0] - 9) * $s, ($b[1] - 8) * $s, 18 * $s, 18 * $s) }
    $g.FillEllipse($shade, 13 * $s, 14 * $s, 38 * $s, 38 * $s)
    foreach ($b in $bumps) { $g.FillEllipse($wool, ($b[0] - 9) * $s, ($b[1] - 9) * $s, 18 * $s, 18 * $s) }
    $g.FillEllipse($wool, 13 * $s, 13 * $s, 38 * $s, 38 * $s)

    # 단추 눈 - 작은 크기는 점만, 큰 크기는 X 자 실
    foreach ($x in @(23, 41)) {
        $g.FillEllipse($eye, ($x - 5.5) * $s, 27 * $s, 11 * $s, 11 * $s)
        if ($size -ge 48) {
            $pen = New-Object System.Drawing.Pen (C "#FFFDF7"), ([Math]::Max(1, 1.3 * $s))
            $d = 2.2 * $s; $cx = $x * $s; $cy = 32.5 * $s
            $g.DrawLine($pen, $cx - $d, $cy - $d, $cx + $d, $cy + $d)
            $g.DrawLine($pen, $cx + $d, $cy - $d, $cx - $d, $cy + $d)
        }
    }
    # 분홍 코
    $pts = [System.Drawing.PointF[]]@([System.Drawing.PointF]::new(28 * $s, 40 * $s), [System.Drawing.PointF]::new(36 * $s, 40 * $s), [System.Drawing.PointF]::new(32 * $s, 44.5 * $s))
    $g.FillPolygon($pink, $pts)
    $g.Dispose()
    return $bmp
}

function Png($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    # 바이트 배열이 파이프라인에서 낱개로 풀리지 않게
    return , $ms.ToArray()
}

# 아이콘 항목 - 256 미만은 BMP(DIB): 머리 40바이트 + 아래에서 위로 BGRA + AND 마스크(알파 있어 전부 0)
function Dib($bmp) {
    $n = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([uint32]40); $w.Write([int32]$n); $w.Write([int32]($n * 2)); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]0); $w.Write([uint32]0); $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $n - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $n; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([Math]::Ceiling($n / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $n)))
    return , $ms.ToArray()
}

# .ico - 작은 크기는 BMP, 256 은 PNG (윈도우 표준 조합, 오래된 GDI+ 도 읽음)
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($n in $sizes) { if ($n -ge 256) { , (Png (Draw $n)) } else { , (Dib (Draw $n)) } }
$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $n = $sizes[$i]
    $w.Write([byte]($n % 256)); $w.Write([byte]($n % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
[System.IO.File]::WriteAllBytes((Join-Path $root "src\Ieummae.App\Assets\ieummae.ico"), $ico.ToArray())

# 탐색기 아이콘 표시 - 왼쪽 아래 작은 단추 + 기호 (정상 ✓, 수정 •, 충돌 !, 추가 +)
function Overlay([int]$size, [string]$kind) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"
    $g.Clear([System.Drawing.Color]::Transparent)
    $colors = @{ normal = "#5BA36B"; modified = "#D9893A"; conflict = "#C9483E"; added = "#4C86C6" }
    $c = [System.Drawing.ColorTranslator]::FromHtml($colors[$kind])
    $s = $size / 16.0
    # 단추 - 진한 테두리, 흰 실 기호
    $dark = [System.Drawing.Color]::FromArgb(255, [int]($c.R * 0.65), [int]($c.G * 0.65), [int]($c.B * 0.65))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $c), 0.5 * $s, 0.5 * $s, 15 * $s, 15 * $s)
    $g.DrawEllipse((New-Object System.Drawing.Pen $dark, ([Math]::Max(1, 1.1 * $s))), 0.5 * $s, 0.5 * $s, 15 * $s, 15 * $s)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.2, 1.9 * $s))
    $pen.StartCap = "Round"; $pen.EndCap = "Round"
    switch ($kind) {
        "normal" { $g.DrawLines($pen, [System.Drawing.PointF[]]@([System.Drawing.PointF]::new(4.5 * $s, 8.2 * $s), [System.Drawing.PointF]::new(7 * $s, 10.8 * $s), [System.Drawing.PointF]::new(11.5 * $s, 5.5 * $s))) }
        "modified" { $g.FillEllipse([System.Drawing.Brushes]::White, 5.3 * $s, 5.3 * $s, 5.4 * $s, 5.4 * $s) }
        "conflict" { $g.DrawLine($pen, 8 * $s, 4 * $s, 8 * $s, 8.8 * $s); $g.FillEllipse([System.Drawing.Brushes]::White, 6.9 * $s, 10.3 * $s, 2.2 * $s, 2.2 * $s) }
        "added" { $g.DrawLine($pen, 8 * $s, 4.5 * $s, 8 * $s, 11.5 * $s); $g.DrawLine($pen, 4.5 * $s, 8 * $s, 11.5 * $s, 8 * $s) }
    }
    $g.Dispose()
    return $bmp
}

function Ico([int[]]$sizes, [scriptblock]$draw, [string]$path) {
    $images = foreach ($n in $sizes) { , (Dib (& $draw $n)) }
    $ico = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ico
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $n = $sizes[$i]
        $w.Write([byte]$n); $w.Write([byte]$n); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($img in $images) { $w.Write($img) }
    [System.IO.File]::WriteAllBytes($path, $ico.ToArray())
}

$ovDir = Join-Path $PSScriptRoot "overlays"
New-Item -ItemType Directory -Force $ovDir | Out-Null
foreach ($kind in "normal", "modified", "conflict", "added") {
    $k = $kind
    Ico @(8, 10, 12, 16, 20, 24, 32, 48) { param($n) Overlay $n $k } (Join-Path $ovDir "$kind.ico")
}

# 패키지 로고
foreach ($p in @(@("Square44x44Logo.png", 44), @("Square150x150Logo.png", 150), @("StoreLogo.png", 50))) {
    [System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot "Assets\$($p[0])"), (Png (Draw $p[1])))
}
"아이콘 생성 완료"

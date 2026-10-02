# 아이콘 그리기 - 앱 아이콘(다크 모드 눈 단추), 탐색기 표시 아이콘
# 결과: src/Ieummae.App/Assets/ieummae.ico (16~256), package/Assets/*.png (패키지 로고)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent

# 앱 아이콘 - 다크 모드 양털 머리의 눈 단추 (상아 단추 + 진한 X 자 실)
function Draw([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 64.0
    function C($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }
    # 다크 테마 Pupil(#BFAF98) 이 테두리, 면은 한 톤 밝게 (WoolHeader.DrawEye 와 같은 계산)
    $rim = C "#BFAF98"
    $face = [System.Drawing.Color]::FromArgb(255, [Math]::Min(255, $rim.R + 34), [Math]::Min(255, $rim.G + 32), [Math]::Min(255, $rim.B + 34))
    $thread = C "#4A3C33"
    $cx = 32 * $s; $cy = 31 * $s; $r = 27 * $s
    # 그림자
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x40, 0, 0, 0))), $cx - $r, $cy - $r + 2.6 * $s, 2 * $r, 2 * $r)
    # 단추 면 + 테두리 + 안쪽 홈
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $face), $cx - $r, $cy - $r, 2 * $r, 2 * $r)
    $g.DrawEllipse((New-Object System.Drawing.Pen $rim, ([Math]::Max(1, 2.6 * $s))), $cx - $r, $cy - $r, 2 * $r, 2 * $r)
    if ($size -ge 24) {
        $ri = $r - 7 * $s
        $g.DrawEllipse((New-Object System.Drawing.Pen $rim, ([Math]::Max(1, 1.8 * $s))), $cx - $ri, $cy - $ri, 2 * $ri, 2 * $ri)
        # 빛 반사 - 왼쪽 위 호
        $hi = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(128, 255, 255, 255)), ([Math]::Max(1, 2.8 * $s))
        $hi.StartCap = "Round"; $hi.EndCap = "Round"
        $ra = $r - 4.5 * $s
        $g.DrawArc($hi, $cx - $ra, $cy - $ra, 2 * $ra, 2 * $ra, 185, 80)
    }
    # X 자 실 + 구멍 넷
    $hd = 6.2 * $s
    $pen = New-Object System.Drawing.Pen $thread, ([Math]::Max(1.2, 3.4 * $s))
    $pen.StartCap = "Round"; $pen.EndCap = "Round"
    $g.DrawLine($pen, $cx - $hd, $cy - $hd, $cx + $hd, $cy + $hd)
    $g.DrawLine($pen, $cx + $hd, $cy - $hd, $cx - $hd, $cy + $hd)
    if ($size -ge 32) {
        $hole = New-Object System.Drawing.SolidBrush $rim
        foreach ($d in @(@(-1, -1), @(1, -1), @(-1, 1), @(1, 1))) {
            $g.FillEllipse($hole, $cx + $d[0] * $hd - 2.4 * $s, $cy + $d[1] * $hd - 2.4 * $s, 4.8 * $s, 4.8 * $s)
        }
    }
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

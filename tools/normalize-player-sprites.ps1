# Unity-safe PNG rewrite + pose-aware 128 canvas for idle_player.
# Stand: fill ~118px height, feet on bottom.
# Sit/crouch (idle_sitting): ~70% of stand height.
# Fall/death (die_death_frame): fit width, keep low.
# Encoder: WPF PngBitmapEncoder (not System.Drawing.Save).

param(
    [string]$SrcRoot = "C:\Users\riadw\Desktop\assets\player\assets\idle_player",
    [string]$LiveRoot = "C:\Users\riadw\Desktop\Cursor Projects\gAAAcha\client\Unity\gatcha1\Assets\StreamingAssets\Sprites\player"
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$canvas = 128
$alphaMin = 12
$padX = 4
$padBottom = 2
$padTop = 4
$maxFitW = $canvas - (2 * $padX)

function Get-Pose([string]$rel) {
    if ($rel -match 'die_death_frame') { return 'fall' }
    if ($rel -match 'idle_sitting') { return 'sit' }
    return 'stand'
}

function Get-TargetH([string]$pose) {
    switch ($pose) {
        'sit' { return 84 }
        'fall' { return 88 }
        default { return 118 }
    }
}

function Get-BBox([System.Drawing.Bitmap]$bmp) {
    $minX = $bmp.Width; $minY = $bmp.Height; $maxX = -1; $maxY = -1
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        for ($x = 0; $x -lt $bmp.Width; $x++) {
            if ($bmp.GetPixel($x, $y).A -ge $alphaMin) {
                if ($x -lt $minX) { $minX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt 0) { return $null }
    return @{ X = $minX; Y = $minY; W = ($maxX - $minX + 1); H = ($maxY - $minY + 1) }
}

function Load-Bmp([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $ms = New-Object IO.MemoryStream(,$bytes)
    $src = New-Object System.Drawing.Bitmap $ms
    $clone = New-Object System.Drawing.Bitmap $src.Width, $src.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($clone)
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.DrawImageUnscaled($src, 0, 0)
    $g.Dispose()
    $src.Dispose()
    $ms.Dispose()
    return $clone
}

function Save-UnityPng([System.Drawing.Bitmap]$bmp, [string]$path) {
    $stride = 0
    $rect = New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
    $bits = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $bits.Stride
        $len = $stride * $bits.Height
        $bgra = New-Object byte[] $len
        [Runtime.InteropServices.Marshal]::Copy($bits.Scan0, $bgra, 0, $len)
    } finally {
        $bmp.UnlockBits($bits)
    }

    $fmt = [System.Windows.Media.PixelFormats]::Bgra32
    $wb = New-Object System.Windows.Media.Imaging.WriteableBitmap $bmp.Width, $bmp.Height, 96, 96, $fmt, $null
    $wb.WritePixels((New-Object System.Windows.Int32Rect 0, 0, $bmp.Width, $bmp.Height), $bgra, $stride, 0)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($wb))
    $fs = [IO.File]::Open($path, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $enc.Save($fs) } finally { $fs.Dispose() }
}

$dirs = @((Get-Item $SrcRoot)) + @(Get-ChildItem -Path $SrcRoot -Recurse -Directory)
$processed = 0
$report = @()

foreach ($dir in $dirs) {
    $files = @(Get-ChildItem -Path $dir.FullName -File -Filter *.png)
    if ($files.Count -eq 0) { continue }

    $rel = $dir.FullName.Substring($SrcRoot.Length).TrimStart('\')
    if ([string]::IsNullOrEmpty($rel)) { $rel = "(root)" }
    $pose = Get-Pose $rel
    $targetH = Get-TargetH $pose

    $loaded = @()
    $maxW = 1; $maxH = 1
    foreach ($f in $files) {
        $bmp = Load-Bmp $f.FullName
        $bb = Get-BBox $bmp
        if ($null -eq $bb) {
            $bmp.Dispose()
            continue
        }
        if ($bb.W -gt $maxW) { $maxW = $bb.W }
        if ($bb.H -gt $maxH) { $maxH = $bb.H }
        $loaded += @{ Path = $f.FullName; Bmp = $bmp; BBox = $bb }
    }
    if ($loaded.Count -eq 0) { continue }

    $scale = [Math]::Min($maxFitW / [double]$maxW, $targetH / [double]$maxH)
    if ($scale -gt 2.5) { $scale = 2.5 }

    $report += [PSCustomObject]@{
        Folder = $rel; Pose = $pose; Files = $loaded.Count
        MaxW = $maxW; MaxH = $maxH; Scale = [Math]::Round($scale, 3)
    }

    foreach ($item in $loaded) {
        $bb = $item.BBox
        $sw = [Math]::Max(1, [int][Math]::Round($bb.W * $scale))
        $sh = [Math]::Max(1, [int][Math]::Round($bb.H * $scale))
        if ($sw -gt $maxFitW) { $sw = $maxFitW }
        if ($sh -gt $targetH) { $sh = $targetH }
        $dx = [int][Math]::Round(($canvas - $sw) / 2.0)
        $dy = $canvas - $padBottom - $sh
        if ($dx -lt 0) { $dx = 0 }
        if ($dy -lt $padTop) { $dy = $padTop }
        if ($dx + $sw -gt $canvas) { $sw = $canvas - $dx }
        if ($dy + $sh -gt $canvas) { $sh = $canvas - $dy }

        $dst = New-Object System.Drawing.Bitmap $canvas, $canvas, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($dst)
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
        $srcRect = New-Object System.Drawing.Rectangle $bb.X, $bb.Y, $bb.W, $bb.H
        $dstRect = New-Object System.Drawing.Rectangle $dx, $dy, $sw, $sh
        $g.DrawImage($item.Bmp, $dstRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
        $g.Dispose()
        $item.Bmp.Dispose()
        Save-UnityPng $dst $item.Path
        $dst.Dispose()
        $processed++
    }
}

Write-Host "processed=$processed folders=$($report.Count)"
$report | Group-Object Pose | ForEach-Object { Write-Host ("pose {0}: {1} folders" -f $_.Name, $_.Count) }
Write-Host "---- sit/fall ----"
$report | Where-Object { $_.Pose -ne 'stand' } | ForEach-Object {
    "{0,-5} s={1:N3}  {2}x{3}  n={4}  {5}" -f $_.Pose, $_.Scale, $_.MaxW, $_.MaxH, $_.Files, $_.Folder
}

Get-ChildItem -Path $SrcRoot -Recurse -Filter *.png | ForEach-Object {
    $r = $_.FullName.Substring($SrcRoot.Length).TrimStart('\')
    $dest = Join-Path $LiveRoot $r
    $destDir = Split-Path $dest -Parent
    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir | Out-Null }
    Copy-Item -Force $_.FullName $dest
}
Write-Host "copied to live"

$report | ConvertTo-Csv -NoTypeInformation | Set-Content -Encoding UTF8 (Join-Path $PSScriptRoot "player-sprite-folder-sizes.csv")
Write-Host "wrote tools/player-sprite-folder-sizes.csv"

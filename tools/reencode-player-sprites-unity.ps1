# Re-encode player PNGs with WPF PngBitmapEncoder so Unity LoadImage can read them.
# Keeps original pixel size. No crop, no zoom, no 128 canvas.

param(
    [string]$SrcRoot = "C:\Users\riadw\Desktop\assets\player\assets\idle_player",
    [string]$LiveRoot = "C:\Users\riadw\Desktop\Cursor Projects\gAAAcha\client\Unity\gatcha1\Assets\StreamingAssets\Sprites\player"
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

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

$n = 0
Get-ChildItem $SrcRoot -Recurse -Filter *.png | ForEach-Object {
    $bmp = Load-Bmp $_.FullName
    Save-UnityPng $bmp $_.FullName
    $bmp.Dispose()
    $n++
    if ($n % 200 -eq 0) { Write-Host "encoded $n" }
}
Write-Host "encoded $n on Desktop source"
Copy-Item -Path (Join-Path $SrcRoot "*") -Destination $LiveRoot -Recurse -Force
Write-Host "copied to StreamingAssets"

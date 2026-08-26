# Offline sprite auditor. Mirrors SpriteCatalog.PlayerAnims facing aliases / fallbacks.
# Catches Unity-hostile PNGs, invisible frames, missing facings, flip/neighbor fallbacks,
# and likely mislabeled directions (mask similarity).
#
#   powershell -NoProfile -File tools/audit-sprites.ps1
#   powershell -NoProfile -File tools/audit-sprites.ps1 -Sheet
#   powershell -NoProfile -File tools/audit-sprites.ps1 -Full

param(
    [string]$PlayerRoot = "",
    [string]$SheetsRoot = "",
    [switch]$Sheet,
    [switch]$Full,
    [switch]$Pose,
    [switch]$Matte,
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Drawing

$ToolsDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PlayerRoot)) {
    $PlayerRoot = Join-Path $ToolsDir "..\client\Unity\gatcha1\Assets\_Project\Art\Sprites\player\female"
}
if ([string]::IsNullOrWhiteSpace($SheetsRoot)) {
    $SheetsRoot = Join-Path $ToolsDir "..\client\Unity\gatcha1\Assets\StreamingAssets\Sprites"
}
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $ToolsDir "sprite-audit-out"
}

$PlayerRoot = [IO.Path]::GetFullPath($PlayerRoot)
$SheetsRoot = [IO.Path]::GetFullPath($SheetsRoot)
$OutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$Contract = Get-Content (Join-Path $ToolsDir "sprite-audit-clips.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$FacingNames = @($Contract.facingNames)
$script:AlphaMin = 21
$Issues = New-Object System.Collections.Generic.List[object]
$script:PixCache = @{}
$ClipPix = @{}

function Add-Issue([string]$Severity, [string]$Code, [string]$Clip, [string]$Facing, [string]$Path, [string]$Detail) {
    $script:Issues.Add([pscustomobject]@{
            severity = $Severity
            code     = $Code
            clip     = $Clip
            facing   = $Facing
            path     = $Path
            detail   = $Detail
        }) | Out-Null
}

function Get-FacingAliases([int]$facing) {
    switch ((($facing % 8) + 8) % 8) {
        1 { @("south-east", "south_east", "southeast", "southE", "east2") }
        2 { @("east", "west2") }
        3 { @("north-east", "north_east", "northeast", "northE") }
        4 { @("north", "idle_north") }
        5 { @("north-west", "north_west", "northwest", "northW") }
        6 { @("west") }
        7 { @("south-west", "south_west", "southwest", "southW") }
        default { @("south", "idle_south") }
    }
}

function Get-ParseFacing([string]$name) {
    if ([string]::IsNullOrWhiteSpace($name)) { return -1 }
    $s = $name.ToLowerInvariant().Replace('_', '-')
    if ($s -eq "west2" -or $s.Contains("west2")) { return 2 }
    if ($s -eq "east2" -or $s.Contains("east2")) { return 1 }
    if ($s.Contains("south-east") -or $s.Contains("southeast") -or $s.Contains("southe")) { return 1 }
    if ($s.Contains("north-east") -or $s.Contains("northeast") -or $s.Contains("northe")) { return 3 }
    if ($s.Contains("south-west") -or $s.Contains("southwest")) { return 7 }
    if ($s.Contains("north-west") -or $s.Contains("northwest")) { return 5 }
    if ($s -eq "south" -or $s.StartsWith("south-") -or $s.EndsWith("-south") -or $s.Contains("-south-")) { return 0 }
    if ($s -eq "north" -or $s.StartsWith("north-") -or $s.EndsWith("-north") -or $s.Contains("-north-")) { return 4 }
    if ($s -eq "east" -or $s.StartsWith("east-") -or $s.EndsWith("-east") -or $s.Contains("-east-")) { return 2 }
    if ($s -eq "west" -or $s.StartsWith("west-") -or $s.EndsWith("-west") -or $s.Contains("-west-")) { return 6 }
    return -1
}

function Get-PngFiles([string]$dir) {
    if (-not (Test-Path -LiteralPath $dir)) { return @() }
    $frames = @(Get-ChildItem -LiteralPath $dir -File -Filter "frame_*.png" -ErrorAction SilentlyContinue | Sort-Object Name)
    if ($frames.Count -gt 0) { return $frames }
    return @(Get-ChildItem -LiteralPath $dir -File -Filter "*.png" -ErrorAction SilentlyContinue | Sort-Object Name)
}

function Get-SequenceDir([string]$dir) {
    if (-not (Test-Path -LiteralPath $dir)) { return $null }
    if ((Get-PngFiles $dir).Count -gt 0) { return $dir }
    $nested = Join-Path $dir (Split-Path $dir -Leaf)
    if ((Test-Path -LiteralPath $nested) -and (Get-PngFiles $nested).Count -gt 0) { return $nested }
    return $null
}

function Get-PngHeader([string]$path) {
    $fs = [IO.File]::OpenRead($path)
    try {
        $hdr = New-Object byte[] 33
        if ($fs.Read($hdr, 0, 33) -lt 33) { return $null }
        $sig = [byte[]](137, 80, 78, 71, 13, 10, 26, 10)
        for ($i = 0; $i -lt 8; $i++) { if ($hdr[$i] -ne $sig[$i]) { return $null } }
        if ([Text.Encoding]::ASCII.GetString($hdr, 12, 4) -ne "IHDR") { return $null }
        $width = ([int]$hdr[16] -shl 24) -bor ([int]$hdr[17] -shl 16) -bor ([int]$hdr[18] -shl 8) -bor $hdr[19]
        $height = ([int]$hdr[20] -shl 24) -bor ([int]$hdr[21] -shl 16) -bor ([int]$hdr[22] -shl 8) -bor $hdr[23]
        return [pscustomobject]@{
            width     = $width
            height    = $height
            bitDepth  = [int]$hdr[24]
            colorType = [int]$hdr[25]
            interlace = [int]$hdr[28]
        }
    } finally { $fs.Close() }
}

function Get-PixelStats([string]$path) {
    if ($script:PixCache.ContainsKey($path)) { return $script:PixCache[$path] }
    $uri = New-Object Uri $path
    $dec = New-Object System.Windows.Media.Imaging.PngBitmapDecoder(
        $uri,
        [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
        [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $conv = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap
    $conv.BeginInit()
    $conv.Source = $dec.Frames[0]
    $conv.DestinationFormat = [System.Windows.Media.PixelFormats]::Bgra32
    $conv.EndInit()
    $conv.Freeze()
    $w = $conv.PixelWidth
    $h = $conv.PixelHeight
    $pixels = New-Object byte[] ($w * 4 * $h)
    $conv.CopyPixels($pixels, $w * 4, 0)

    $visible = 0; $halo = 0
    $minX = $w; $minY = $h; $maxX = -1; $maxY = -1
    $len = $pixels.Length
    for ($i = 0; $i -lt $len; $i += 4) {
        $a = $pixels[$i + 3]
        if ($a -lt $script:AlphaMin) {
            if ($pixels[$i] -or $pixels[$i + 1] -or $pixels[$i + 2]) { $halo++ }
            continue
        }
        $visible++
        $idx = [int]($i / 4)
        $px = $idx % $w
        $py = [int][Math]::Floor($idx / $w)
        if ($px -lt $minX) { $minX = $px }
        if ($py -lt $minY) { $minY = $py }
        if ($px -gt $maxX) { $maxX = $px }
        if ($py -gt $maxY) { $maxY = $py }
    }

    $mask = New-Object float[] 576
    $bboxW = 0; $bboxH = 0
    if ($visible -gt 0) {
        $bboxW = $maxX - $minX + 1
        $bboxH = $maxY - $minY + 1
        for ($my = 0; $my -lt 24; $my++) {
            for ($mx = 0; $mx -lt 24; $mx++) {
                $sx = $minX + [int](($mx + 0.5) * $bboxW / 24.0)
                $sy = $minY + [int](($my + 0.5) * $bboxH / 24.0)
                if ($sx -ge $w) { $sx = $w - 1 }
                if ($sy -ge $h) { $sy = $h - 1 }
                $a = $pixels[(($sy * $w) + $sx) * 4 + 3]
                $mask[$my * 24 + $mx] = $(if ($a -ge $script:AlphaMin) { 1.0 } else { 0.0 })
            }
        }
    }

    $stats = [pscustomobject]@{
        path    = $path
        w       = $w
        h       = $h
        visible = $visible
        halo    = $halo
        bboxW   = $bboxW
        bboxH   = $bboxH
        mask    = $mask
    }
    $script:PixCache[$path] = $stats
    return $stats
}

function Get-MaskSim($a, $b, [switch]$FlipB) {
    if ($null -eq $a -or $null -eq $b) { return 0.0 }
    $dot = 0.0; $na = 0.0; $nb = 0.0
    for ($y = 0; $y -lt 24; $y++) {
        for ($x = 0; $x -lt 24; $x++) {
            $va = $a[$y * 24 + $x]
            $bx = $(if ($FlipB) { 23 - $x } else { $x })
            $vb = $b[$y * 24 + $bx]
            $dot += $va * $vb
            $na += $va * $va
            $nb += $vb * $vb
        }
    }
    if ($na -lt 1e-6 -or $nb -lt 1e-6) { return 0.0 }
    return $dot / [Math]::Sqrt($na * $nb)
}

function Test-PngFile([string]$Path, [string]$Clip, [string]$Facing) {
    $hdr = Get-PngHeader $Path
    if ($null -eq $hdr) {
        Add-Issue "error" "png_corrupt" $Clip $Facing $Path "not a PNG / truncated IHDR"
        return $null
    }
    if ($hdr.colorType -ne 6 -or $hdr.bitDepth -ne 8) {
        $sev = $(if ($hdr.colorType -eq 3 -or $hdr.colorType -eq 0) { "error" } else { "warn" })
        Add-Issue $sev "png_encoding" $Clip $Facing $Path (
            "colorType=$($hdr.colorType) bitDepth=$($hdr.bitDepth) (Unity LoadImage wants 8-bit RGBA)")
    }
    if ($hdr.interlace -ne 0) {
        Add-Issue "warn" "png_interlace" $Clip $Facing $Path "Adam7 interlace"
    }
    try {
        $px = Get-PixelStats $Path
    } catch {
        Add-Issue "error" "png_decode" $Clip $Facing $Path $_.Exception.Message
        return $null
    }
    if ($px.visible -eq 0) {
        Add-Issue "error" "invisible" $Clip $Facing $Path "$($px.w)x$($px.h) fully transparent"
        return $px
    }
    if ($px.bboxW -lt 8 -or $px.bboxH -lt 8) {
        Add-Issue "error" "tiny_pose" $Clip $Facing $Path "opaque bbox $($px.bboxW)x$($px.bboxH)"
    }
    $transparent = ($px.w * $px.h) - $px.visible
    if ($Matte -and $transparent -gt 0 -and ($px.halo / [double]$transparent) -gt 0.5) {
        Add-Issue "warn" "black_halo" $Clip $Facing $Path "haloPixels=$($px.halo) of $transparent transparent"
    }
    return $px
}

function Resolve-Facing([string]$Parent, [int]$Facing) {
    foreach ($name in (Get-FacingAliases $Facing)) {
        $seq = Get-SequenceDir (Join-Path $Parent $name)
        if ($seq) {
            return [pscustomobject]@{ how = "exact"; flip = $false; dir = $seq; still = $null; sourceFacing = $Facing }
        }
    }
    foreach ($name in (Get-FacingAliases $Facing)) {
        $still = Join-Path $Parent ($name + ".png")
        if (Test-Path -LiteralPath $still) {
            return [pscustomobject]@{ how = "still"; flip = $false; dir = $null; still = $still; sourceFacing = $Facing }
        }
    }
    $mirror = switch ($Facing) { 1 { 7 } 2 { 6 } 3 { 5 } 5 { 3 } 6 { 2 } 7 { 1 } default { -1 } }
    if ($mirror -ge 0) {
        foreach ($name in (Get-FacingAliases $mirror)) {
            $seq = Get-SequenceDir (Join-Path $Parent $name)
            if ($seq) {
                return [pscustomobject]@{ how = "flip"; flip = $true; dir = $seq; still = $null; sourceFacing = $mirror }
            }
        }
        foreach ($name in (Get-FacingAliases $mirror)) {
            $still = Join-Path $Parent ($name + ".png")
            if (Test-Path -LiteralPath $still) {
                return [pscustomobject]@{ how = "flip"; flip = $true; dir = $null; still = $still; sourceFacing = $mirror }
            }
        }
    }
    if (Test-Path -LiteralPath $Parent) {
        $best = $null; $bestDelta = 99; $bestFace = -1
        Get-ChildItem -LiteralPath $Parent -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $parsed = Get-ParseFacing $_.Name
            if ($parsed -lt 0) { return }
            $d = [Math]::Abs($Facing - $parsed)
            if ($d -gt 4) { $d = 8 - $d }
            if ($d -lt $bestDelta) { $bestDelta = $d; $best = $_.FullName; $bestFace = $parsed }
        }
        if ($best) {
            $seq = Get-SequenceDir $best
            $flip = (($Facing -in 5, 6, 7) -ne ($bestFace -in 5, 6, 7))
            $how = $(if ($bestDelta -eq 0) { "named" } else { "neighbor" })
            if ($seq) {
                return [pscustomobject]@{ how = $how; flip = $flip; dir = $seq; still = $null; sourceFacing = $bestFace }
            }
        }
    }
    $southSeq = Get-SequenceDir (Join-Path $Parent "south")
    if ($southSeq) {
        return [pscustomobject]@{ how = "south-fallback"; flip = $false; dir = $southSeq; still = $null; sourceFacing = 0 }
    }
    $southStill = Join-Path $Parent "south.png"
    if (Test-Path -LiteralPath $southStill) {
        return [pscustomobject]@{ how = "south-fallback"; flip = $false; dir = $null; still = $southStill; sourceFacing = 0 }
    }
    $parentSeq = Get-SequenceDir $Parent
    if ($parentSeq) {
        return [pscustomobject]@{ how = "parent-frames"; flip = $false; dir = $parentSeq; still = $null; sourceFacing = 0 }
    }
    return $null
}

function Get-SamplePath($resolved) {
    if ($resolved.still) { return $resolved.still }
    $files = Get-PngFiles $resolved.dir
    if ($files.Count -eq 0) { return $null }
    return $files[0].FullName
}

Write-Host "Player pack: $PlayerRoot"
if (-not (Test-Path -LiteralPath $PlayerRoot)) {
    throw "Player root missing: $PlayerRoot"
}

foreach ($clip in $Contract.clips) {
    $clipRoot = $PlayerRoot
    if ($clip.root -eq "streaming") { $clipRoot = $SheetsRoot }
    $parent = Join-Path $clipRoot (($clip.folder -replace '/', [IO.Path]::DirectorySeparatorChar))
    $ClipPix[$clip.id] = @{}
    if (-not (Test-Path -LiteralPath $parent)) {
        Add-Issue "error" "missing_folder" $clip.id "*" $parent "wired folder is missing"
        continue
    }

    Get-ChildItem -LiteralPath $parent -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        if ((Get-ParseFacing $_.Name) -lt 0 -and $_.Name -notmatch '^(frame|anim)') {
            Add-Issue "warn" "orphan_dir" $clip.id "*" $_.FullName (
                "name is not a facing the loader parses (west2 / nested grip packs are ignored)")
        }
    }

    $aliasHits = @{}
    foreach ($want in @($clip.want)) {
        $key = [string][int]$want
        foreach ($name in (Get-FacingAliases ([int]$want))) {
            if ((Test-Path -LiteralPath (Join-Path $parent $name)) -or
                (Test-Path -LiteralPath (Join-Path $parent ($name + ".png")))) {
                if (-not $aliasHits.ContainsKey($key)) { $aliasHits[$key] = New-Object System.Collections.Generic.List[string] }
                $aliasHits[$key].Add($name)
            }
        }
    }
    foreach ($key in @($aliasHits.Keys)) {
        if ($aliasHits[$key].Count -gt 1) {
            Add-Issue "warn" "duplicate_alias" $clip.id $FacingNames[[int]$key] $parent (
                "loader keeps first only: " + ($aliasHits[$key] -join ", "))
        }
    }

    $sizes = New-Object System.Collections.Generic.List[string]
    foreach ($want in @($clip.want)) {
        $fi = [int]$want
        $faceName = $FacingNames[$fi]
        $resolved = Resolve-Facing $parent $fi
        if ($null -eq $resolved) {
            $fb = [string]$clip.fallback
            $detail = "no sequence, still, flip, or neighbor"
            if ($fb) { $detail += "; runtime fallback=$fb (body still draws)" }
            $sev = $(if ($clip.strict) { "error" } else { "warn" })
            Add-Issue $sev "missing_facing" $clip.id $faceName $parent $detail
            continue
        }
        if ($resolved.how -eq "flip") {
            $sev = $(if ($clip.strict) { "error" } else { "warn" })
            Add-Issue $sev "flip_fallback" $clip.id $faceName $parent (
                "using mirrored $($FacingNames[$resolved.sourceFacing])")
        } elseif ($resolved.how -eq "neighbor" -or $resolved.how -eq "south-fallback") {
            $sev = $(if ($clip.strict) { "error" } else { "warn" })
            Add-Issue $sev "wrong_facing_fallback" $clip.id $faceName $parent (
                "$($resolved.how) from $($FacingNames[$resolved.sourceFacing])")
        }

        $sample = Get-SamplePath $resolved
        if (-not $sample) {
            Add-Issue "error" "empty_folder" $clip.id $faceName "$($resolved.dir)" "facing folder has no PNGs"
            continue
        }

        $files = @()
        if ($resolved.dir) {
            $files = @(Get-PngFiles $resolved.dir)
            if ($files.Count -eq 1 -and $clip.id -match 'run|breath') {
                Add-Issue "warn" "single_frame_loop" $clip.id $faceName $resolved.dir "run/breath folder has 1 frame"
            }
        } else {
            $files = @(Get-Item -LiteralPath $sample)
        }

        $toCheck = $files
        if (-not $Full -and $files.Count -gt 2) { $toCheck = @($files[0], $files[$files.Count - 1]) }
        $px0 = $null
        foreach ($f in $toCheck) {
            $px = Test-PngFile -Path $f.FullName -Clip $clip.id -Facing $faceName
            if ($null -eq $px0) { $px0 = $px }
        }
        if ($px0) {
            $ClipPix[$clip.id][$fi] = $px0
            $sizes.Add("$($px0.w)x$($px0.h)")
            if ($Pose -and $clip.pose -eq "stand" -and $px0.bboxH -gt 0 -and $px0.bboxH -lt 70) {
                Add-Issue "warn" "pose_short" $clip.id $faceName $sample "stand bboxH=$($px0.bboxH) (expect ~118)"
            }
            if ($Pose -and $clip.pose -eq "sit" -and $px0.bboxH -gt 110) {
                Add-Issue "warn" "pose_tall" $clip.id $faceName $sample "sit bboxH=$($px0.bboxH) (expect ~84)"
            }
            if ($Pose -and $clip.pose -eq "fall" -and $px0.bboxH -gt 110) {
                Add-Issue "warn" "pose_tall" $clip.id $faceName $sample "fall bboxH=$($px0.bboxH) (expect <=88)"
            }
        }
    }

    $uniq = @($sizes | Select-Object -Unique)
    if ($uniq.Count -gt 2) {
        Add-Issue "warn" "mixed_canvas" $clip.id "*" $parent ("sizes: " + ($uniq -join ", "))
    }

    $have = $ClipPix[$clip.id]
    if ($have.Count -ge 4) {
        foreach ($i in @($have.Keys)) {
            $best = -1; $bestSim = -1.0
            foreach ($j in @($have.Keys)) {
                if ($i -eq $j) { continue }
                $s = Get-MaskSim $have[$i].mask $have[$j].mask
                if ($s -gt $bestSim) { $bestSim = $s; $best = $j }
            }
            if ($best -lt 0) { continue }
            $delta = [Math]::Abs($i - $best)
            if ($delta -gt 4) { $delta = 8 - $delta }
            $opp = ($i + 4) % 8
            if ($delta -ge 3 -and $have.ContainsKey($opp)) {
                $simOpp = Get-MaskSim $have[$i].mask $have[$opp].mask
                $nb = ($i + 1) % 8
                $simNb = 0.0
                if ($have.ContainsKey($nb)) { $simNb = Get-MaskSim $have[$i].mask $have[$nb].mask }
                if ($simOpp -gt ($simNb + 0.2) -and $simOpp -gt 0.82 -and $simNb -lt 0.5) {
                    Add-Issue "warn" "facing_looks_like_opposite" $clip.id $FacingNames[$i] $have[$i].path (
                        "mask closer to $($FacingNames[$opp]) ($([Math]::Round($simOpp,3))) than neighbor ($([Math]::Round($simNb,3)))")
                }
            }
        }
        if ($have.ContainsKey(2) -and $have.ContainsKey(6)) {
            $simFlip = Get-MaskSim $have[2].mask $have[6].mask -FlipB
            $simSame = Get-MaskSim $have[2].mask $have[6].mask
            if ($simSame -gt ($simFlip + 0.12) -and $simSame -gt 0.75) {
                Add-Issue "warn" "east_west_not_mirrors" $clip.id "east/west" $parent (
                    "unflipped sim=$([Math]::Round($simSame,3)) > flipped sim=$([Math]::Round($simFlip,3))")
            }
        }
    }
}

foreach ($g in @("*_idle.png", "*_walk.png", "*_run.png", "*_attack.png", "*_hurt.png", "*_death.png")) {
    Get-ChildItem -LiteralPath $SheetsRoot -File -Filter $g -ErrorAction SilentlyContinue | ForEach-Object {
        $px = Test-PngFile -Path $_.FullName -Clip $_.BaseName -Facing "sheet"
        if ($null -eq $px) { return }
        if ($px.h % 4 -ne 0) {
            Add-Issue "error" "sheet_height" $_.BaseName "*" $_.FullName "height $($px.h) not divisible by 4"
            return
        }
        $rowH = [int]($px.h / 4)
        if ($rowH -gt 0 -and $px.w % $rowH -ne 0) {
            Add-Issue "warn" "sheet_width" $_.BaseName "*" $_.FullName "$($px.w)x$($px.h) width not divisible by row $rowH"
        }
        $uri = New-Object Uri $_.FullName
        $dec = New-Object System.Windows.Media.Imaging.PngBitmapDecoder(
            $uri,
            [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
            [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        $conv = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap
        $conv.BeginInit(); $conv.Source = $dec.Frames[0]
        $conv.DestinationFormat = [System.Windows.Media.PixelFormats]::Bgra32
        $conv.EndInit(); $conv.Freeze()
        $w = $conv.PixelWidth; $h = $conv.PixelHeight
        $pixels = New-Object byte[] ($w * 4 * $h)
        $conv.CopyPixels($pixels, $w * 4, 0)
        $dirNames = @("down", "left", "right", "up")
        for ($row = 0; $row -lt 4; $row++) {
            $y0 = $row * $rowH
            $vis = 0
            $stepY = [Math]::Max(1, [int]($rowH / 16))
            $stepX = [Math]::Max(1, [int]($w / 32))
            for ($y = $y0; $y -lt ($y0 + $rowH) -and $vis -eq 0; $y += $stepY) {
                for ($x = 0; $x -lt $w; $x += $stepX) {
                    if ($pixels[(($y * $w) + $x) * 4 + 3] -ge $AlphaMin) { $vis++; break }
                }
            }
            if ($vis -eq 0) {
                Add-Issue "error" "empty_sheet_row" $_.BaseName $dirNames[$row] $_.FullName "facing row has no visible pixels"
            }
        }
    }
}

if ($Sheet) {
    $cell = 96
    $rows = @($Contract.clips)
    $rowCount = [int]$rows.Length
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList ([int]($cell * 8)), ([int]($cell * $rowCount))
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.Clear([System.Drawing.Color]::FromArgb(30, 30, 36))
    $font = New-Object System.Drawing.Font "Consolas", 8
    $brush = [System.Drawing.Brushes]::White
    $red = [System.Drawing.Pens]::OrangeRed
    for ($r = 0; $r -lt $rowCount; $r++) {
        $clip = $rows[$r]
        $gfx.DrawString([string]$clip.id, $font, $brush, 2, [float]($r * $cell + 2))
        $have = $ClipPix[$clip.id]
        for ($f = 0; $f -lt 8; $f++) {
            $x = $f * $cell
            $y = $r * $cell
            if ($have -and $have.ContainsKey($f)) {
                try {
                    $img = [System.Drawing.Image]::FromFile($have[$f].path)
                    $gfx.DrawImage($img, $x, $y, $cell, $cell)
                    $img.Dispose()
                } catch {}
            } else {
                $gfx.DrawRectangle($red, $x + 4, $y + 16, $cell - 8, $cell - 20)
            }
            $label = [string]$FacingNames[$f]
            if ($label.Length -gt 7) { $label = $label.Substring(0, 7) }
            $gfx.DrawString($label, $font, $brush, [float]($x + 4), [float]($y + $cell - 14))
        }
    }
    $sheetPath = Join-Path $OutDir "contact-sheet.png"
    $bmp.Save($sheetPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $gfx.Dispose(); $bmp.Dispose()
    Write-Host "Contact sheet: $sheetPath"
}

$reportPath = Join-Path $OutDir "report.json"
$Issues | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 $reportPath
$err = @($Issues | Where-Object { $_.severity -eq "error" })
$warn = @($Issues | Where-Object { $_.severity -eq "warn" })
Write-Host ""
Write-Host ("Sprite audit: {0} error(s), {1} warning(s), {2} total" -f $err.Count, $warn.Count, $Issues.Count)
if ($Issues.Count -gt 0) {
    $Issues |
        Sort-Object @{ Expression = { if ($_.severity -eq "error") { 0 } else { 1 } } }, code, clip |
        Format-Table severity, code, clip, facing, detail -AutoSize
}
Write-Host "JSON: $reportPath"
if ($err.Count -gt 0) { exit 2 }
if ($warn.Count -gt 0) { exit 1 }
exit 0

# Restore original (176/180) player frames from pinkhair export zips.
# Overwrites Desktop idle_player + StreamingAssets. Does NOT bbox-normalize.

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zipDir = "C:\Users\riadw\Desktop\pinkhairassets\refined\data"
$desk = "C:\Users\riadw\Desktop\assets\player\assets\idle_player"
$live = "C:\Users\riadw\Desktop\Cursor Projects\gAAAcha\client\Unity\gatcha1\Assets\StreamingAssets\Sprites\player"
$stage = Join-Path $env:TEMP "idle_player_orig_restore"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

function Expand-ZipTo($zipPath, $dest) {
    if (-not (Test-Path $zipPath)) { throw "missing zip $zipPath" }
    if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest -Force | Out-Null }
    $tmp = Join-Path $dest ("_" + [IO.Path]::GetFileNameWithoutExtension($zipPath))
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    New-Item -ItemType Directory -Path $tmp | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $tmp)
    Get-ChildItem $tmp -Force | ForEach-Object {
        $target = Join-Path $dest $_.Name
        if ($_.Name -eq "metadata.json") { return }
        if (Test-Path $target) { Remove-Item $target -Recurse -Force }
        Move-Item $_.FullName $target
    }
    Remove-Item $tmp -Recurse -Force
}

Expand-ZipTo (Join-Path $zipDir "elgend_very_slender_fema-Idle.zip") $stage
Expand-ZipTo (Join-Path $zipDir "elgend_very_slender_fema-Idle_copy.zip") $stage
Expand-ZipTo (Join-Path $zipDir "elgend_very_slender_fema-boxingstance_fighti.zip") $stage
Expand-ZipTo (Join-Path $zipDir "elgend_very_slender_fema-holding_a_longbow_h.zip") $stage
Expand-ZipTo (Join-Path $zipDir "elgend_very_slender_fema-holding_daggers_due.zip") $stage
Expand-ZipTo (Join-Path $zipDir "elgend_very_slender_fema-holding_staff_long.zip") $stage
Expand-ZipTo (Join-Path $zipDir "elgend_very_slender_fema-holding_zweihander.zip") $stage

function Facing-Aliases([string]$name) {
    $n = $name.ToLowerInvariant()
    $aliases = @($name)
    switch -Regex ($n) {
        '^(south-east|south_east|southeast|southe|east2)$' {
            $aliases += @("south-east", "south_east", "east2")
        }
        '^(north-east|north_east|northeast|northe)$' {
            $aliases += @("north-east", "north_east")
        }
        '^(north-west|north_west|northwest|northw)$' {
            $aliases += @("north-west", "north_west")
        }
        '^(south-west|south_west|southwest|southw)$' {
            $aliases += @("south-west", "south_west")
        }
    }
    return $aliases | Select-Object -Unique
}

function Copy-Stills([string]$srcRot, [string]$dstDir) {
    if (-not (Test-Path $srcRot)) { return 0 }
    if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
    $n = 0
    Get-ChildItem $srcRot -Filter *.png | ForEach-Object {
        $base = [IO.Path]::GetFileNameWithoutExtension($_.Name)
        foreach ($alias in @(Facing-Aliases $base)) {
            Copy-Item $_.FullName (Join-Path $dstDir ($alias + ".png")) -Force
            $n++
        }
    }
    return $n
}

function Copy-Seq([string]$srcAnim, [string]$dstParent) {
    if (-not (Test-Path $srcAnim)) { return 0 }
    if (-not (Test-Path $dstParent)) { New-Item -ItemType Directory -Path $dstParent -Force | Out-Null }
    $n = 0
    Get-ChildItem $srcAnim -Directory | ForEach-Object {
        $frames = @(Get-ChildItem $_.FullName -Filter "frame_*.png")
        if ($frames.Count -eq 0) { return }
        foreach ($alias in @(Facing-Aliases $_.Name)) {
            $dest = Join-Path $dstParent $alias
            if (Test-Path $dest) {
                Get-ChildItem $dest -Filter "frame_*.png" -ErrorAction SilentlyContinue | Remove-Item -Force
            } else {
                New-Item -ItemType Directory -Path $dest -Force | Out-Null
            }
            foreach ($f in $frames) {
                Copy-Item $f.FullName (Join-Path $dest $f.Name) -Force
                $n++
            }
        }
    }
    return $n
}

$copied = 0
$copied += Copy-Stills (Join-Path $stage "Idle\rotations") (Join-Path $desk "idle_rotation_calm")
$copied += Copy-Seq (Join-Path $stage "Idle_copy\animations\Breathing_Idle") (Join-Path $desk "idle_rotation_breathing")
$copied += Copy-Seq (Join-Path $stage "Idle\animations\fasr_running_forward_sprinting_ahead_fast_movement") (Join-Path $desk "idle_running")
$copied += Copy-Seq (Join-Path $stage "Idle\animations\sitting_on_floor_legs_to_the_side_calm_relaxed_pos") (Join-Path $desk "idle_sitting")
$copied += Copy-Seq (Join-Path $stage "Idle\animations\flinching_step_back_pushed_fo_fall_on_back_lying_o") (Join-Path $desk "die_death_frame")
$copied += Copy-Seq (Join-Path $stage "Idle\animations\getting_hit_crossing_arms_in_front_of_face_flinchi") (Join-Path $desk "idle_dmg_frame")
$copied += Copy-Seq (Join-Path $stage "Idle\animations\getting_hit_taking_dmg_crouching_pushback_flinchin") (Join-Path $desk "idle_pickup")

$copied += Copy-Stills (Join-Path $stage "boxingstance_fighti\rotations") (Join-Path $desk "holding_unarmed\rotations")
$copied += Copy-Seq (Join-Path $stage "boxingstance_fighti\animations\Cross_Punch") (Join-Path $desk "holding_unarmed\animations\Cross_Punch")
$copied += Copy-Seq (Join-Path $stage "boxingstance_fighti\animations\Roundhouse_Kick") (Join-Path $desk "holding_unarmed\animations\Roundhouse_Kick")

$copied += Copy-Stills (Join-Path $stage "holding_a_longbow_h\rotations") (Join-Path $desk "holding_a_longbow_h\rotations")
$copied += Copy-Seq (Join-Path $stage "holding_a_longbow_h\animations\running_forward_running_switfly_elegant_stride") (Join-Path $desk "holding_a_longbow_h\animations\bow_idle_running")
$copied += Copy-Seq (Join-Path $stage "holding_a_longbow_h\animations\loading_bow_with_arrow_shooting_arrow_stable_uprig") (Join-Path $desk "holding_a_longbow_h\animations\bow_idle_shooting")

$copied += Copy-Stills (Join-Path $stage "holding_daggers_due\rotations") (Join-Path $desk "holding_daggers_due\rotations")
$copied += Copy-Seq (Join-Path $stage "holding_daggers_due\animations\forward_leaning_very_fast_steps_jumping_running_ru") (Join-Path $desk "holding_daggers_due\animations\daggers_idle_running")
$copied += Copy-Seq (Join-Path $stage "holding_daggers_due\animations\sprinting_leaning_forward_running_fast_swift_steps") (Join-Path $desk "holding_daggers_due\animations\daggers_idle_runstartframe")

$copied += Copy-Stills (Join-Path $stage "holding_staff_long\rotations") (Join-Path $desk "holding_staff_long\rotations_staff")
$copied += Copy-Seq (Join-Path $stage "holding_staff_long\animations\running_foeward_running_while_holding_thwo_handed") (Join-Path $desk "holding_staff_long\animations_staff\running_foeward_running_while_holding_thwo_handed")
$staffChant = Join-Path $stage "holding_staff_long\animations\holding_staff_closing_eyes_chanting_sticking_one_a"
$copied += Copy-Seq $staffChant (Join-Path $desk "holding_staff_long\animations_staff\holding_staff_closing_eyes_chanting_sticking_one_a")
$copied += Copy-Seq $staffChant (Join-Path $desk "holding_staff_long\animations_staff\holding_staff_closing_eyes_chanting_sticking_one_a\staffskill1")
$breathDst = Join-Path $desk "holding_staff_long\animations_staff\holding_staff_closing_eyes_chanting_sticking_one_a\idle_breathing"
if (Test-Path (Join-Path $staffChant "north")) {
    $copied += Copy-Seq $staffChant $breathDst
    $nDir = Join-Path $breathDst "north"
    $sDir = Join-Path $breathDst "south"
    if (Test-Path $nDir) { Copy-Item $nDir (Join-Path $breathDst "idle_north") -Recurse -Force }
    if (Test-Path $sDir) { Copy-Item $sDir (Join-Path $breathDst "idle_south") -Recurse -Force }
}

$copied += Copy-Stills (Join-Path $stage "holding_zweihander\rotations") (Join-Path $desk "holding_zweihander\rotations_zweihander")
$copied += Copy-Seq (Join-Path $stage "holding_zweihander\animations\running_forward_carrying_zweihander_with_both_arms") (Join-Path $desk "holding_zweihander\animations_zweihander\idle_running_zweihander")
$copied += Copy-Seq (Join-Path $stage "holding_zweihander\animations\warcry_holding_sword_up_into_the_air_ready_up_shou") (Join-Path $desk "holding_zweihander\animations_zweihander\swordattacks\swordskill5_Buff")
$copied += Copy-Seq (Join-Path $stage "holding_zweihander\animations\frontal_combo_attack_3_fast_hits_swiping_with_zwei") (Join-Path $desk "holding_zweihander\animations_zweihander\swordattacks\autoattack_sword1")
$copied += Copy-Seq (Join-Path $stage "holding_zweihander\animations\overhead_slash_using_overhead_attack_with_sword_fa") (Join-Path $desk "holding_zweihander\animations_zweihander\swordattacks\swordskills2_slasher")

Write-Host "copied file-writes: $copied"
if (-not (Test-Path $live)) { New-Item -ItemType Directory -Path $live -Force | Out-Null }
Copy-Item -Path (Join-Path $desk "*") -Destination $live -Recurse -Force

Add-Type -AssemblyName System.Drawing
$sizes = @{}
Get-ChildItem $live -Recurse -Filter *.png | ForEach-Object {
    try {
        $img = [System.Drawing.Image]::FromFile($_.FullName)
        $k = "$($img.Width)x$($img.Height)"
        $img.Dispose()
        if (-not $sizes.ContainsKey($k)) { $sizes[$k] = 0 }
        $sizes[$k]++
    } catch {}
}
Write-Host "==== LIVE sizes after restore ===="
$sizes.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { "$($_.Value)  $($_.Key)" }

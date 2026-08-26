# Copy canonical C# from client/Stubs into the active Unity project.
# Stubs are the source of truth. Unity copies under _Project/Scripts live for Play Mode.
#
#   powershell -NoProfile -File tools/sync-stubs.ps1
#   powershell -NoProfile -File tools/sync-stubs.ps1 -Check
#   powershell -NoProfile -File tools/sync-stubs.ps1 -Force
#
# -Check  report drift only; exit 1 if any file differs
# -Force  overwrite Unity even when its copy is newer

param(
    [switch]$Check,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$StubDir = Join-Path $RepoRoot "client\Stubs"
$UnityScripts = Join-Path $RepoRoot "client\Unity\gatcha1\Assets\_Project\Scripts"

if (-not (Test-Path -LiteralPath $StubDir)) {
    throw "Stubs folder missing: $StubDir"
}
if (-not (Test-Path -LiteralPath $UnityScripts)) {
    throw "Unity scripts folder missing: $UnityScripts"
}

$Map = @{
    "NetworkBootstrap.cs"            = "Core"
    "GameLog.cs"                     = "Core"
    "GrayBoxWorld.cs"                = "World"
    "WorldCoords.cs"                 = "World"
    "MapPathing.cs"                  = "World"
    "SpriteCatalog.cs"               = "World"
    "SpriteCatalog.PlayerAnims.cs"   = "World"
    "SpriteCatalog.AnimKits.cs"      = "World"
    "VfxCatalog.cs"                  = "World"
    "SoundCatalog.cs"                = "World"
    "UiChrome.cs"                    = "World"
    "NetClient.cs"                   = "Network"
    "InputSender.cs"                 = "Network"
    "JsonUtil.cs"                    = "Network"
    "PredictionReconciler.cs"        = "Network"
    "VirtualJoystick.cs"             = "UI"
}

function Get-FileSha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

$copied = 0
$matched = 0
$missing = 0
$blocked = 0
$drift = 0
$unknown = @()

$stubFiles = Get-ChildItem -LiteralPath $StubDir -File -Filter "*.cs"
foreach ($f in $stubFiles) {
    if (-not $Map.ContainsKey($f.Name)) {
        $unknown += $f.Name
    }
}

foreach ($name in ($Map.Keys | Sort-Object)) {
    $folder = $Map[$name]
    $src = Join-Path $StubDir $name
    $dstDir = Join-Path $UnityScripts $folder
    $dst = Join-Path $dstDir $name

    if (-not (Test-Path -LiteralPath $src)) {
        Write-Host "MISSING STUB  $name"
        $missing++
        continue
    }

    $srcHash = Get-FileSha256 $src
    $srcTime = (Get-Item -LiteralPath $src).LastWriteTimeUtc

    if (Test-Path -LiteralPath $dst) {
        $dstHash = Get-FileSha256 $dst
        if ($srcHash -eq $dstHash) {
            Write-Host "OK     $folder/$name"
            $matched++
            continue
        }

        $dstTime = (Get-Item -LiteralPath $dst).LastWriteTimeUtc
        $unityNewer = $dstTime -gt $srcTime
        Write-Host "DRIFT  $folder/$name  stub=$($srcHash.Substring(0,12))  unity=$($dstHash.Substring(0,12))  unityNewer=$unityNewer"
        $drift++

        if ($Check) {
            continue
        }

        if ($unityNewer -and -not $Force) {
            Write-Host "BLOCK  $folder/$name  Unity copy is newer. Re-run with -Force to overwrite, or copy Unity back to Stubs first."
            $blocked++
            continue
        }
    }
    else {
        Write-Host "NEW    $folder/$name"
        $drift++
        if ($Check) {
            continue
        }
    }

    if (-not (Test-Path -LiteralPath $dstDir)) {
        New-Item -ItemType Directory -Force -Path $dstDir | Out-Null
    }
    Copy-Item -LiteralPath $src -Destination $dst -Force
    Write-Host "COPY   $name -> $folder/"
    $copied++
}

if ($unknown.Count -gt 0) {
    Write-Host ("UNMAPPED STUBS  " + ($unknown -join ", "))
}

Write-Host ""
Write-Host "matched=$matched  drifted=$drift  copied=$copied  blocked=$blocked  missingStub=$missing"

$failed = ($missing -gt 0) -or ($blocked -gt 0) -or ($unknown.Count -gt 0) -or ($Check -and $drift -gt 0)
if ($failed) {
    exit 1
}
exit 0

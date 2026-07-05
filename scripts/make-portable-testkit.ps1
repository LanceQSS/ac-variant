# Stages a portable clean-machine test tree for the M9 beta gate: both published
# exes plus a minimal fake AC install (two Kunos cars + the matching slice of the
# global sfx/GUIDs.txt), so the app can be gated on a machine with no SDK, no
# runtime and no Assetto Corsa.
#
# PERSONAL-USE FIXTURE ONLY. It contains Kunos data - never distribute it and
# never commit it. Copy the folder to the test laptop by hand, delete after.
#
#   .\scripts\make-portable-testkit.ps1            # -> Desktop\acvariant-testkit
[CmdletBinding()]
param(
    [string]$AcPath = "C:\Program Files (x86)\Steam\steamapps\common\assettocorsa",
    [string]$Out = (Join-Path $env:USERPROFILE "Desktop\acvariant-testkit"),
    [string[]]$Cars = @('abarth500', 'bmw_m3_e30')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

$gui = Join-Path $repoRoot "publish\gui\AC Variant.exe"
$cli = Join-Path $repoRoot "publish\cli\acvc.exe"
if (-not (Test-Path $gui) -or -not (Test-Path $cli)) {
    throw "Published exes not found - run the publish step first (see CLAUDE.md Commands)."
}

New-Item -ItemType Directory -Force -Path $Out | Out-Null
Copy-Item $gui (Join-Path $Out "AC Variant.exe") -Force
Copy-Item $cli (Join-Path $Out "acvc.exe") -Force

$fakeCars = Join-Path $Out "fake-ac\content\cars"
$fakeSfx = Join-Path $Out "fake-ac\content\sfx"
New-Item -ItemType Directory -Force -Path $fakeCars, $fakeSfx | Out-Null

$globalGuids = Get-Content (Join-Path $AcPath "content\sfx\GUIDs.txt")
$slice = @()
foreach ($car in $Cars) {
    $src = Join-Path $AcPath "content\cars\$car"
    if (-not (Test-Path (Join-Path $src 'data.acd'))) {
        throw "No data.acd for '$car' under $AcPath."
    }
    $dst = Join-Path $fakeCars $car
    New-Item -ItemType Directory -Force -Path (Join-Path $dst 'ui') | Out-Null
    Copy-Item (Join-Path $src 'data.acd') $dst -Force
    Copy-Item (Join-Path $src 'ui\ui_car.json') (Join-Path $dst 'ui') -Force

    # First real skin so the emitter has something to link/copy.
    $firstSkin = Get-ChildItem (Join-Path $src 'skins') -Directory | Sort-Object Name | Select-Object -First 1
    Copy-Item $firstSkin.FullName (Join-Path $dst "skins\$($firstSkin.Name)") -Recurse -Force

    # Bank + the car's slice of the global GUIDs map (audio-generation path).
    $bank = Join-Path $src "sfx\$car.bank"
    if (Test-Path $bank) {
        New-Item -ItemType Directory -Force -Path (Join-Path $dst 'sfx') | Out-Null
        Copy-Item $bank (Join-Path $dst 'sfx') -Force
    }
    $slice += $globalGuids | Where-Object { $_ -match [regex]::Escape("/cars/$car/") -or $_ -match ([regex]::Escape("bank:/$car") + '$') }
}
Set-Content -Path (Join-Path $fakeSfx 'GUIDs.txt') -Value $slice -Encoding ascii

Set-Content -Path (Join-Path $Out 'README.txt') -Encoding ascii -Value @"
AC Variant - clean-machine gate kit (PERSONAL USE ONLY, contains game data:
do not distribute, do not commit, delete after the gate).

Gate steps on a machine with no .NET SDK/runtime and no Assetto Corsa:
 1. Launch "AC Variant.exe" - it must start (self-contained) and report that
    no install was autodetected, without crashing.
 2. Click the ... button and pick the fake-ac folder in this kit.
 3. Both cars should list with "Kunos" badges. Pick one, set a tune name,
    move a slider, watch the preview update.
 4. Build. It must succeed; the variant appears under fake-ac\content\cars.
    (Junction skins may fall back to copy-first on non-NTFS drives - the
    build note will say so. That fallback is expected behavior, not a bug.)
 5. Optional CLI check: acvc.exe survey --ac-path fake-ac
"@

$size = (Get-ChildItem $Out -Recurse | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("kit staged at {0} ({1:0} MB). Copy to the test machine by hand; delete after the gate." -f $Out, $size)

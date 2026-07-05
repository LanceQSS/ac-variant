# Populates tests/fixtures/ from a local Assetto Corsa install.
# Fixtures are gitignored — Kunos data never goes in the repo (CLAUDE.md).
# Defaults cover both engine.ini shapes: bmw_m3_e30 (NA) and abarth500 (turbo).
#
# Usage:
#   .\scripts\make-fixtures.ps1
#   .\scripts\make-fixtures.ps1 -AcPath "D:\Games\assettocorsa" -Cars ks_toyota_ae86,ks_toyota_supra_mkiv
[CmdletBinding()]
param(
    [string]$AcPath = "C:\Program Files (x86)\Steam\steamapps\common\assettocorsa",
    [string[]]$Cars = @('bmw_m3_e30', 'abarth500')
)

$ErrorActionPreference = 'Stop'

$carsRoot = Join-Path $AcPath 'content\cars'
if (-not (Test-Path $carsRoot)) {
    throw "'$AcPath' does not look like an Assetto Corsa install ($carsRoot not found). Pass -AcPath."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$fixturesRoot = Join-Path $repoRoot 'tests\fixtures'

foreach ($car in $Cars) {
    $src = Join-Path $carsRoot "$car\data.acd"
    if (-not (Test-Path $src)) {
        throw "No data.acd for '$car' at $src. Pick a stock car that ships packed (pass -Cars)."
    }
    $dstDir = Join-Path $fixturesRoot $car
    New-Item -ItemType Directory -Force -Path $dstDir | Out-Null
    Copy-Item $src (Join-Path $dstDir 'data.acd') -Force
    Write-Host "copied $car\data.acd -> tests\fixtures\$car\"

    # ui_car.json is needed by the M5 UI-regeneration tests (spec strings + curve shapes).
    $ui = Join-Path $carsRoot "$car\ui\ui_car.json"
    if (Test-Path $ui) {
        Copy-Item $ui (Join-Path $dstDir 'ui_car.json') -Force
        Write-Host "copied $car\ui\ui_car.json -> tests\fixtures\$car\"
    }
}

Write-Host "Done. Fixtures are gitignored and stay local to this machine."

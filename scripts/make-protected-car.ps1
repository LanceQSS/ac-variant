# Creates (or removes) a synthetic CSP/x4fab-style protected car in the live
# install, for the M8 manual gate: the GUI must show it grayed with the refusal
# reason as tooltip. The data.acd parses as a container but its content is noise,
# so decryption fails the plausibility check -> classified "encrypted".
#
#   .\scripts\make-protected-car.ps1                # create
#   .\scripts\make-protected-car.ps1 -Remove        # delete after the gate
[CmdletBinding()]
param(
    [string]$AcPath = "C:\Program Files (x86)\Steam\steamapps\common\assettocorsa",
    [string]$CarName = "zz_acvc_protected_test",
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'
$carDir = Join-Path $AcPath "content\cars\$CarName"

if ($Remove) {
    if (Test-Path $carDir) {
        Remove-Item -Recurse -Force $carDir
        Write-Host "removed $carDir"
    } else {
        Write-Host "nothing to remove at $carDir"
    }
    return
}

New-Item -ItemType Directory -Force -Path $carDir | Out-Null

# Container layout per the ACD format: [nameLen][name][size][size x int32 fields],
# with the 8-byte versioned header. Content bytes are deterministic noise that no
# ROT key turns into INI text.
$ms = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($ms)
$w.Write([int]-1111)         # 0xFFFFFBA9 marker
$w.Write([int]0x000A4691)
foreach ($name in @('car.ini', 'engine.ini')) {
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($name)
    $w.Write([int]$bytes.Length)
    $w.Write($bytes)
    $size = 2048
    $w.Write([int]$size)
    for ($i = 0; $i -lt $size; $i++) {
        $w.Write([int](($i * 197 + 31) -band 0xFF))
    }
}
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $carDir 'data.acd'), $ms.ToArray())

Write-Host "created protected-style test car: $carDir"
Write-Host "gate: it must appear grayed in the GUI with the refusal reason as tooltip."
Write-Host "afterwards: .\scripts\make-protected-car.ps1 -Remove"

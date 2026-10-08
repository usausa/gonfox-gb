# Original DMG background/input example, MIT licensed (see LICENSE).
param([switch]$Mbc1)
$ErrorActionPreference = 'Stop'
$code = [Collections.Generic.List[byte]]::new()
$labels = @{}
$fixups = [Collections.Generic.List[object]]::new()
function Emit([int[]]$bytes) { foreach ($value in $bytes) { $code.Add([byte]$value) } }
function Label([string]$name) { $labels[$name] = $code.Count + 0x150 }
function Relative([int]$opcode, [string]$label) {
    Emit @($opcode, 0)
    $fixups.Add(@{ Offset = $code.Count - 1; Label = $label })
}
Emit @(0xF3, 0x31, 0xFE, 0xFF)                 # DI; LD SP,FFFE
Emit @(0xAF, 0xE0, 0x40)                       # LCD off while initializing VRAM
Emit @(0x21, 0x00, 0x04, 0x11, 0x00, 0x80, 0x01, 0x00, 0x01)
Label 'copy'
Emit @(0x2A, 0x12, 0x13, 0x0B, 0x78, 0xB1)    # Copy 256 bytes of tiles to 8000
Relative 0x20 'copy'
Emit @(0x21, 0x00, 0x98, 0x01, 0x00, 0x04)    # Fill the 32x32 tile map
Label 'map'
Emit @(0x79, 0xE6, 0x0F, 0x22, 0x0B, 0x78, 0xB1)
Relative 0x20 'map'
Emit @(0x3E, 0xE4, 0xE0, 0x47, 0xAF, 0xE0, 0x42, 0xE0, 0x43)
if ($Mbc1) {
    Emit @(0x3E, 0x0A, 0xEA, 0x00, 0x00)     # Enable RAM, restore the scroll
    Emit @(0xFA, 0x00, 0xA0, 0xE0, 0x43, 0xFA, 0x01, 0xA0, 0xE0, 0x42)
}
Emit @(0x3E, 0x91, 0xE0, 0x40, 0xAF, 0xE0, 0x0F, 0x3E, 0x01, 0xEA, 0xFF, 0xFF, 0xFB, 0x00)
Label 'frame'
Emit @(0x76)                                  # HALT; VBlank vector 0040 is RETI
Emit @(0x3E, 0x10, 0xE0, 0x00, 0xF0, 0x00, 0x47) # Button row into B
Emit @(0xCB, 0x58)                             # BIT 3,B: Start holds auto-scroll
Relative 0x28 'directions'
Emit @(0xF0, 0x43, 0x3C, 0xE0, 0x43)
Label 'directions'
Emit @(0x3E, 0x20, 0xE0, 0x00, 0xF0, 0x00, 0x4F) # Direction row into C
foreach ($entry in @(@(0,0x43,0x3C), @(1,0x43,0x3D), @(2,0x42,0x3D), @(3,0x42,0x3C))) {
    Emit @(0xCB, (0x41 + 8 * $entry[0]))        # BIT n,C
    Relative 0x20 "skip$($entry[0])"
    Emit @(0xF0, $entry[1], $entry[2], 0xE0, $entry[1])
    Label "skip$($entry[0])"
}
Emit @(0x3E, 0xE4, 0xCB, 0x40)                # A reverses shades while held
Relative 0x20 'palette'
Emit @(0x3E, 0x1B)
Label 'palette'
Emit @(0xE0, 0x47, 0x3E, 0x91, 0xCB, 0x48)    # B hides background while held
Relative 0x20 'lcd'
Emit @(0x3E, 0x90)
Label 'lcd'
Emit @(0xE0, 0x40, 0xCB, 0x50)                # Select resets scroll
Relative 0x20 'next'
Emit @(0xAF, 0xE0, 0x42, 0xE0, 0x43)
Label 'next'
if ($Mbc1) {
    Emit @(0xF0, 0x43, 0xEA, 0x00, 0xA0, 0xF0, 0x42, 0xEA, 0x01, 0xA0)
}
Relative 0x18 'frame'
foreach ($fixup in $fixups) {
    $delta = $labels[$fixup.Label] - (0x150 + $fixup.Offset + 1)
    if ($delta -lt -128 -or $delta -gt 127) { throw 'Relative branch out of range' }
    $code[$fixup.Offset] = [byte]($delta -band 255)
}
if (0x150 + $code.Count -gt 0x400) { throw 'Code overlaps tile data' }
$rom = [byte[]]::new($(if ($Mbc1) { 65536 } else { 32768 }))
$rom[0x40] = 0xD9
$rom[0x100] = 0xC3; $rom[0x101] = 0x50; $rom[0x102] = 0x01
[Text.Encoding]::ASCII.GetBytes($(if ($Mbc1) { 'MBC1 INPUT DEMO' } else { 'BG INPUT DEMO' })).CopyTo($rom, 0x134)
if ($Mbc1) { $rom[0x147] = 3; $rom[0x148] = 1; $rom[0x149] = 3 }
$code.CopyTo($rom, 0x150)
for ($tile = 0; $tile -lt 16; $tile++) {
    for ($y = 0; $y -lt 8; $y++) {
        $low = 0; $high = 0
        for ($x = 0; $x -lt 8; $x++) {
            # Diagonals within a tile, alternating tiles, all four shades.
            $shade = ($x + $y + $tile) % 4
            $low = $low -bor (($shade -band 1) -shl (7 - $x))
            $high = $high -bor ((($shade -shr 1) -band 1) -shl (7 - $x))
        }
        $rom[0x400 + $tile * 16 + $y * 2] = [byte]$low
        $rom[0x401 + $tile * 16 + $y * 2] = [byte]$high
    }
}
$sum = 0
for ($i = 0x134; $i -le 0x14C; $i++) { $sum += $rom[$i] }
$rom[0x14D] = [byte]((-$sum - 25) -band 255)
# Logo bytes intentionally remain zero: this example targets boot-ROM bypass.
$sum = ($rom | Measure-Object -Sum).Sum
$rom[0x14E] = [byte](([int]$sum -shr 8) -band 255); $rom[0x14F] = [byte]([int]$sum -band 255)
$path = Join-Path $PSScriptRoot $(if ($Mbc1) { 'mbc1-demo.gb' } else { 'background-demo.gb' })
[IO.File]::WriteAllBytes($path, $rom)
Get-FileHash -LiteralPath $path -Algorithm SHA256

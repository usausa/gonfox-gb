# Original DMG sound check ROM, MIT licensed (see LICENSE).
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
function Put([int]$register, [int]$value) { Emit @(0x3E, $value, 0xE0, $register) } # LD A,value; LDH (register),A

Emit @(0xF3, 0x31, 0xFE, 0xFF)                # DI; LD SP,FFFE
Put 0x40 0x00                                 # LCD off: the screen stays blank
Put 0x26 0x80; Put 0x24 0x77; Put 0x25 0x00   # APU on, volume 8, nothing routed
$wave = @(0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF, 0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10)
for ($i = 0; $i -lt 16; $i++) { Put (0x30 + $i) $wave[$i] } # A triangle
Put 0x1A 0x80; Put 0x1C 0x20                  # CH3 DAC on, level 100%
Put 0x11 0x80; Put 0x12 0xC0                  # CH1 duty 50%, volume 12, no envelope
Put 0x16 0x40; Put 0x17 0xC0                  # CH2 duty 25%, volume 12
Put 0x21 0xA0; Put 0x22 0x34                  # CH4 volume 10, LFSR every 512 T-cycles
Put 0x05 0xC0; Put 0x06 0xC0; Put 0x07 0x04   # Timer: 4096 Hz from C0, 64 Hz interrupt
Put 0xFF 0x04; Put 0x0F 0x00                  # IE = Timer only
Emit @(0x0E, 0x00)                            # C = phase 0..3, half a second each
Label 'phase'
Emit @(0x79, 0xFE, 0x00); Relative 0x20 'right'
Put 0x25 0x10; Put 0x13 0xD6; Put 0x14 0x86   # CH1 left only: period 6D6, 439.8 Hz
Relative 0x18 'wait'
Label 'right'
Emit @(0xFE, 0x01); Relative 0x20 'wave'
Put 0x25 0x02; Put 0x18 0x39; Put 0x19 0x87   # CH2 right only: period 739, 658.7 Hz
Relative 0x18 'wait'
Label 'wave'
Emit @(0xFE, 0x02); Relative 0x20 'noise'
Put 0x25 0x44; Put 0x1D 0xD6; Put 0x1E 0x86   # CH3 on both sides: period 6D6, 219.9 Hz
Relative 0x18 'wait'
Label 'noise'
Put 0x25 0x88; Put 0x23 0x80                  # CH4 on both sides
Label 'wait'
Emit @(0x06, 0x20, 0xFB)                      # LD B,32; EI
Label 'tick'
Emit @(0x76, 0x05); Relative 0x20 'tick'      # HALT until the timer interrupt; DEC B
Emit @(0x0C, 0x79, 0xE6, 0x03, 0x4F)          # Next phase: C = (C + 1) & 3
Relative 0x18 'phase'
foreach ($fixup in $fixups) {
    $delta = $labels[$fixup.Label] - (0x150 + $fixup.Offset + 1)
    if ($delta -lt -128 -or $delta -gt 127) { throw 'Relative branch out of range' }
    $code[$fixup.Offset] = [byte]($delta -band 255)
}
$rom = [byte[]]::new(32768)
$rom[0x50] = 0xD9                             # Timer vector: RETI
$rom[0x100] = 0xC3; $rom[0x101] = 0x50; $rom[0x102] = 0x01
[Text.Encoding]::ASCII.GetBytes('SOUND CHECK').CopyTo($rom, 0x134)
$code.CopyTo($rom, 0x150)
$sum = 0
for ($i = 0x134; $i -le 0x14C; $i++) { $sum += $rom[$i] }
$rom[0x14D] = [byte]((-$sum - 25) -band 255)
# Logo bytes intentionally remain zero: this example targets boot-ROM bypass.
$sum = ($rom | Measure-Object -Sum).Sum
$rom[0x14E] = [byte](([int]$sum -shr 8) -band 255); $rom[0x14F] = [byte]([int]$sum -band 255)
$path = Join-Path $PSScriptRoot 'sound-check.gb'
[IO.File]::WriteAllBytes($path, $rom)
Get-FileHash -LiteralPath $path -Algorithm SHA256

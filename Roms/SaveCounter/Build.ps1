$ErrorActionPreference = 'Stop'
# Original SM83 program: increment battery RAM once, expose it in SCX and the background shade.
[byte[]]$rom = New-Object byte[] 65536
$rom[0x100] = 0xC3; $rom[0x101] = 0x50; $rom[0x102] = 1
[Text.Encoding]::ASCII.GetBytes('P07 SAVE COUNTER').CopyTo($rom, 0x134)
$rom[0x147] = 3 # Standard MBC1 + RAM + battery
$rom[0x148] = 1 # 64 KiB ROM
$rom[0x149] = 3 # 32 KiB RAM
[byte[]]$program = @(
    0xF3,                   # DI
    0x3E, 0x0A,             # LD A,0A
    0xEA, 0x00, 0x00,       # LD (0000),A : RAM enable
    0xFA, 0x00, 0xA0,       # LD A,(A000)
    0x3C,                   # INC A
    0xEA, 0x00, 0xA0,       # LD (A000),A
    0xE0, 0x43,             # LDH (SCX),A : show the counter
    0xE0, 0x47,             # LDH (BGP),A : shade from the counter
    0x76,                   # HALT : PPU continues running
    0x18, 0xFD              # JR back to HALT
)
$program.CopyTo($rom, 0x150)
$headerChecksum = 0
for ($i = 0x134; $i -le 0x14C; $i++) { $headerChecksum = ($headerChecksum - $rom[$i] - 1) -band 255 }
$rom[0x14D] = [byte]$headerChecksum
$sum = 0
foreach ($value in $rom) { $sum = ($sum + $value) -band 65535 }
$rom[0x14E] = [byte]($sum -shr 8); $rom[0x14F] = [byte]($sum -band 255)
$path = Join-Path $PSScriptRoot 'save-counter.gb'
[IO.File]::WriteAllBytes($path, $rom)
Get-FileHash -LiteralPath $path -Algorithm SHA256

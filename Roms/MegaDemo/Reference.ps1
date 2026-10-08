# Renders megademo.gb reference frames with binjgb; -Verify compares them with the committed files.
param([switch]$Offline, [switch]$Verify)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$demoRoot = $PSScriptRoot
$root = Split-Path (Split-Path $demoRoot -Parent) -Parent
$binjgb = [ordered]@{
    Name = 'binjgb'; Version = 'v0.1.11'; License = 'MIT'
    Url = 'https://github.com/binji/binjgb/releases/download/v0.1.11/binjgb-windows.tar.gz'
    ArchiveSha256 = '86EBB0F68D760B30C8162F788BDA472BA856B9AC7DD7EE89C69AD751456F3E66'
    TesterSha256 = 'E43C0EEEC94738F051CF705EF017FE5D2689B8DDE8938D560A4819CBDA5E32E6'
    LicenseUrl = 'https://github.com/binji/binjgb/blob/v0.1.11/LICENSE'
    LicenseSha256 = '89807ACF2309BD285F033404EE78581602F3CD9B819A16AC2F0E5F60FF4A473E'
}
$frameTicks = 70224

# Buttons held from (frame + 0.5) * 70224 T, away from the joypad read; never opposite directions.
$inputs = @(
    @(0, @()), @(40, @('Right')), @(41, @()), @(50, @('Up')), @(90, @()),
    @(100, @('A')), @(101, @()), @(110, @('B')), @(111, @()), @(130, @('Select')), @(131, @()),
    @(200, @('A')), @(201, @()), @(230, @('Left')), @(290, @()), @(300, @('Select')), @(301, @()),
    @(330, @('A')), @(331, @()), @(340, @('A')), @(341, @()), @(380, @('Start')), @(381, @()),
    @(400, @('Down')), @(470, @()), @(480, @('Start')), @(481, @()), @(490, @('Select')), @(491, @()),
    @(520, @('Left', 'Down')), @(548, @()), @(555, @('Select', 'Down')), @(556, @())
)
# Captured frames, each at least 8 frames after a scene change (binjgb skips 4 after LCD on).
$frames = @(
    @(30, 'wave, power-on parameters'), @(60, 'wave, speed 4 after Right, amplitude rising'),
    @(95, 'wave, amplitude 7 (maximum) after held Up'), @(105, 'wave, palette 1 after A'),
    @(125, 'wave, reversed after B'), @(160, 'plasma, power-on parameters'),
    @(203, 'plasma, texture 1 halfway through its four 64-byte uploads'), @(215, 'plasma, texture 1 complete'),
    @(260, 'plasma, speed 0 (minimum) after held Left'), @(320, 'orbit, power-on parameters'),
    @(335, 'orbit, pattern 1 (X flips) after A'), @(350, 'orbit, pattern 2 (Y flips) after A'),
    @(395, 'orbit, stopped by Start'), @(410, 'orbit, stopped, spread lowered by Down'),
    @(460, 'orbit, spread 0: 40 objects on the same lines, 10 shown'), @(486, 'orbit, running again after Start'),
    @(500, 'wave again, its parameters restored'), @(545, 'wave, Left and Down held together both repeat'),
    @(570, 'plasma, Select and Down in one frame lower the new scene''s strength')
)
$binjgbBits = @{ Down = 7; Up = 6; Left = 5; Right = 4; Start = 3; Select = 2; B = 1; A = 0 }

function Assert-Hash([string]$Path, [string]$Expected) {
    if (!(Test-Path -LiteralPath $Path) -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Expected) {
        throw "Missing file or SHA-256 mismatch: $Path"
    }
}

function Get-Sha256([byte[]]$Bytes) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

# binjgb-tester needs the boot logo, so a temporary copy gets it (logo and checksums only).
& (Join-Path $demoRoot 'Build.ps1') -Verify -Offline:$Offline | Out-Null
$cacheRoot = Join-Path $root '.cache/binjgb'
$archive = Join-Path $cacheRoot 'binjgb-windows.tar.gz'
New-Item -ItemType Directory -Force $cacheRoot | Out-Null
if (!(Test-Path -LiteralPath $archive)) {
    if ($Offline) { throw "Offline cache is missing: $archive" }
    Invoke-WebRequest -Uri $binjgb.Url -OutFile "$archive.download" -TimeoutSec 60
    Assert-Hash "$archive.download" $binjgb.ArchiveSha256
    Move-Item -LiteralPath "$archive.download" -Destination $archive -Force
}
Assert-Hash $archive $binjgb.ArchiveSha256
$tester = Join-Path $cacheRoot "binjgb-$($binjgb.Version)/bin/binjgb-tester.exe"
if (!(Test-Path -LiteralPath $tester) -or (Get-FileHash -LiteralPath $tester -Algorithm SHA256).Hash -ne $binjgb.TesterSha256) {
    tar -xzf $archive -C $cacheRoot "binjgb-$($binjgb.Version)/bin/binjgb-tester.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Could not extract binjgb-tester.exe.' }
}
Assert-Hash $tester $binjgb.TesterSha256

$work = Join-Path $root '.cache/megademo-reference'
Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $work | Out-Null
$rom = Join-Path $demoRoot 'megademo.gb'
$romCopy = Join-Path $work 'megademo-logo.gb'
Copy-Item -LiteralPath $rom -Destination $romCopy
& (Join-Path $root '.cache/rgbds/v1.0.4/rgbfix.exe') -f lhg -Wno-overwrite $romCopy
if ($LASTEXITCODE -ne 0) { throw 'rgbfix failed.' }
$original = [IO.File]::ReadAllBytes($rom); $patched = [IO.File]::ReadAllBytes($romCopy)
for ($i = 0; $i -lt $original.Length; $i++) {
    if ($original[$i] -ne $patched[$i] -and !(($i -ge 0x104 -and $i -le 0x133) -or ($i -ge 0x14D -and $i -le 0x14F))) {
        throw "The reference copy differs outside the logo and checksums at $('{0:X4}' -f $i)."
    }
}

# Write the binjgb joypad file: per change a u64 tick, a button byte and seven zero bytes.
$joypad = Join-Path $work 'input.joyp'
$stream = [IO.File]::Create($joypad)
$inputList = @()
try {
    foreach ($entry in $inputs) {
        $tick = [uint64]($entry[0] * $frameTicks + $(if ($entry[0] -eq 0) { 0 } else { $frameTicks / 2 }))
        $packed = 0
        foreach ($button in $entry[1]) { $packed = $packed -bor (1 -shl $binjgbBits[$button]) }
        $record = [byte[]]::new(16)
        [BitConverter]::GetBytes($tick).CopyTo($record, 0)
        $record[8] = [byte]$packed
        $stream.Write($record, 0, 16)
        $inputList += [ordered]@{ tick = $tick; buttons = @($entry[1]) }
    }
} finally { $stream.Dispose() }

# PNG writer for 160x144 two-bit grayscale (filter 0), the format the tests already decode.
$crcTable = [uint32[]]::new(256)
for ($n = 0; $n -lt 256; $n++) {
    $c = [uint32]$n
    for ($k = 0; $k -lt 8; $k++) { $c = if ($c -band 1) { [uint32](0xEDB88320 -bxor ($c -shr 1)) } else { [uint32]($c -shr 1) } }
    $crcTable[$n] = $c
}
function Add-Chunk([IO.Stream]$Output, [string]$Type, [byte[]]$Data) {
    $typed = [byte[]]([Text.Encoding]::ASCII.GetBytes($Type) + $Data)
    $length = [BitConverter]::GetBytes([int]$Data.Length); [Array]::Reverse($length)
    $Output.Write($length, 0, 4); $Output.Write($typed, 0, $typed.Length)
    $crc = [uint32]::MaxValue
    foreach ($b in $typed) { $crc = [uint32]($crcTable[($crc -bxor $b) -band 0xFF] -bxor ($crc -shr 8)) }
    $crcBytes = [BitConverter]::GetBytes([uint32]($crc -bxor [uint32]::MaxValue)); [Array]::Reverse($crcBytes)
    $Output.Write($crcBytes, 0, 4)
}
function ConvertTo-Png([byte[]]$Levels) {
    $raw = [byte[]]::new(144 * 41)
    for ($y = 0; $y -lt 144; $y++) {
        for ($x = 0; $x -lt 160; $x++) {
            $index = $y * 41 + 1 + ($x -shr 2)
            $raw[$index] = $raw[$index] -bor ($Levels[$y * 160 + $x] -shl (6 - 2 * ($x -band 3)))
        }
    }
    $compressed = [IO.MemoryStream]::new()
    $zlib = [IO.Compression.ZLibStream]::new($compressed, [IO.Compression.CompressionLevel]::SmallestSize, $true)
    $zlib.Write($raw, 0, $raw.Length); $zlib.Dispose()
    $png = [IO.MemoryStream]::new()
    $png.Write([byte[]](137, 80, 78, 71, 13, 10, 26, 10), 0, 8)
    Add-Chunk $png 'IHDR' ([byte[]](0, 0, 0, 160, 0, 0, 0, 144, 2, 0, 0, 0, 0))
    Add-Chunk $png 'IDAT' $compressed.ToArray()
    Add-Chunk $png 'IEND' ([byte[]]@())
    return $png.ToArray()
}

$referenceRoot = Join-Path $demoRoot 'reference'
$manifestPath = Join-Path $demoRoot 'reference.json'
$committed = if ($Verify) { Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json } else { $null }
if (!$Verify) {
    Remove-Item -LiteralPath $referenceRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $referenceRoot | Out-Null
}
$frameList = @()
foreach ($frame in $frames) {
    $number = $frame[0]
    $ppm = Join-Path $work "frame-$number.ppm"
    $output = & $tester -j $joypad -f $number -o $ppm $romCopy
    if ($LASTEXITCODE -ne 0) { throw "binjgb-tester failed for frame $number." }
    $tokens = (Get-Content -LiteralPath $ppm -Raw).Split([char[]]" `r`n`t", [StringSplitOptions]::RemoveEmptyEntries)
    if ($tokens[0] -ne 'P3' -or $tokens[1] -ne '160' -or $tokens[2] -ne '144' -or $tokens[3] -ne '255') { throw 'Unexpected PPM header.' }
    # binjgb's DMG palette: 255, 170, 85, 0 for shades 0-3. PNG level = 3 - shade (white = 3).
    $levels = [byte[]]::new(160 * 144)
    for ($i = 0; $i -lt $levels.Length; $i++) {
        $r = [int]$tokens[4 + $i * 3]
        if ($r -notin 255, 170, 85, 0 -or $tokens[5 + $i * 3] -ne $tokens[4 + $i * 3] -or $tokens[6 + $i * 3] -ne $tokens[4 + $i * 3]) {
            throw "Unexpected colour in frame $number."
        }
        $levels[$i] = [byte]($r / 85)
    }
    $name = 'frame-{0:D4}.png' -f $number
    $pixelHash = Get-Sha256 $levels
    if ($Verify) {
        $expected = $committed.frames | Where-Object { $_.frame -eq $number }
        if ($null -eq $expected -or $expected.pixelSha256 -ne $pixelHash) { throw "Frame $number differs from reference.json." }
        Assert-Hash (Join-Path $referenceRoot $name) $expected.pngSha256.ToUpperInvariant()
        Write-Output "frame $number reproduced"
        continue
    }
    $pngBytes = ConvertTo-Png $levels
    [IO.File]::WriteAllBytes((Join-Path $referenceRoot $name), $pngBytes)
    $frameList += [ordered]@{ frame = $number; file = "reference/$name"; description = $frame[1]
        pngSha256 = Get-Sha256 $pngBytes; pixelSha256 = $pixelHash }
}
if ($Verify) { Write-Output 'All reference frames reproduced.'; return }

$manifest = [ordered]@{
    rom = [ordered]@{ file = 'megademo.gb'; sha256 = (Get-FileHash -LiteralPath $rom -Algorithm SHA256).Hash.ToLowerInvariant() }
    renderer = [ordered]@{ name = $binjgb.Name; version = $binjgb.Version; license = $binjgb.License; url = $binjgb.Url
        archiveSha256 = $binjgb.ArchiveSha256.ToLowerInvariant(); testerSha256 = $binjgb.TesterSha256.ToLowerInvariant()
        licenseUrl = $binjgb.LicenseUrl; licenseSha256 = $binjgb.LicenseSha256.ToLowerInvariant()
        command = 'binjgb-tester -j input.joyp -f <frame> -o frame.ppm megademo-logo.gb'
        romCopy = 'megademo.gb with the boot logo and both checksums written by rgbfix -f lhg (0104-0133, 014D-014F)' }
    frameTicks = $frameTicks
    selection = 'The image is the first frame completed at or after frame * frameTicks T-cycles from power-on (binjgb-tester -f).'
    pixels = 'reference/*.png: 160x144 two-bit grayscale; level 3 - DMG shade. pixelSha256 hashes the 23040 levels.'
    inputs = $inputList
    frames = $frameList
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Write-Output "Wrote $($frameList.Count) reference frames with $($binjgb.Name) $($binjgb.Version)."

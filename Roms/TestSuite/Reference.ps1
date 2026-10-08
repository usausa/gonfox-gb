# Runs the test ROMs (all, or -Name) on SameBoy and binjgb and records the bytes each shows in reference/<name>.json; -Show only prints them.
param([string[]]$Name, [switch]$Offline, [switch]$Show)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$suiteRoot = $PSScriptRoot
$root = Split-Path (Split-Path $suiteRoot -Parent) -Parent
$binjgb = [ordered]@{
    Name = 'binjgb'; Version = 'v0.1.11'; License = 'MIT'
    Url = 'https://github.com/binji/binjgb/releases/download/v0.1.11/binjgb-windows.tar.gz'
    ArchiveSha256 = '86EBB0F68D760B30C8162F788BDA472BA856B9AC7DD7EE89C69AD751456F3E66'
    TesterSha256 = 'E43C0EEEC94738F051CF705EF017FE5D2689B8DDE8938D560A4819CBDA5E32E6'
}
$sameBoy = [ordered]@{
    Name = 'SameBoy'; Version = '1.0.3'; Commit = 'c458e7c5d2d350fb37a1931c40da9f758d28d240'; License = 'MIT'
    Url = 'https://github.com/LIJI32/SameBoy/archive/c458e7c5d2d350fb37a1931c40da9f758d28d240.zip'
    ArchiveSha256 = '6ABA4CE153CE211DFE61351D32E6DA9A7BA13A0277AABB34C320D4F8FF62E8C3'
}

function Assert-Hash([string]$Path, [string]$Expected) {
    if (!(Test-Path -LiteralPath $Path) -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Expected) {
        throw "Missing file or SHA-256 mismatch: $Path"
    }
}

function Get-Download([string]$Url, [string]$Path, [string]$Sha256) {
    if (!(Test-Path -LiteralPath $Path)) {
        if ($Offline) { throw "Offline cache is missing: $Path" }
        Invoke-WebRequest -Uri $Url -OutFile "$Path.download" -TimeoutSec 120
        Assert-Hash "$Path.download" $Sha256
        Move-Item -LiteralPath "$Path.download" -Destination $Path -Force
    }
    Assert-Hash $Path $Sha256
}

# binjgb's headless tester from its release.
function Get-BinjgbTester {
    $cache = Join-Path $root '.cache/binjgb'
    New-Item -ItemType Directory -Force $cache | Out-Null
    $archive = Join-Path $cache 'binjgb-windows.tar.gz'
    Get-Download $binjgb.Url $archive $binjgb.ArchiveSha256
    $tester = Join-Path $cache "binjgb-$($binjgb.Version)/bin/binjgb-tester.exe"
    if (!(Test-Path -LiteralPath $tester) -or (Get-FileHash -LiteralPath $tester -Algorithm SHA256).Hash -ne $binjgb.TesterSha256) {
        tar -xzf $archive -C $cache "binjgb-$($binjgb.Version)/bin/binjgb-tester.exe" "binjgb-$($binjgb.Version)/bin/SDL2.dll"
        if ($LASTEXITCODE -ne 0) { throw 'Could not extract binjgb-tester.exe.' }
    }
    Assert-Hash $tester $binjgb.TesterSha256
    $tester
}

# SameBoy's headless tester, built from the pinned source with the clang of Visual Studio, with SameBoy's DMG boot ROM.
function Get-SameBoyTester {
    $cache = Join-Path $root '.cache/sameboy-src'
    $tester = Join-Path $cache 'bin/sameboy_tester.exe'
    if (Test-Path -LiteralPath $tester) { return $tester }
    New-Item -ItemType Directory -Force $cache | Out-Null
    $archive = Join-Path $cache "SameBoy-$($sameBoy.Commit.Substring(0, 8)).zip"
    Get-Download $sameBoy.Url $archive $sameBoy.ArchiveSha256
    $source = Join-Path $cache 'src'
    Remove-Item -LiteralPath $source -Recurse -Force -ErrorAction SilentlyContinue
    Expand-Archive -LiteralPath $archive -DestinationPath $cache -Force
    Move-Item -LiteralPath (Join-Path $cache "SameBoy-$($sameBoy.Commit)") -Destination $source
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Llvm.Clang -property installationPath
    if (!$vs) { throw 'Visual Studio with the C++ Clang tools is needed to build the SameBoy tester.' }
    $objects = Join-Path $cache 'obj'
    New-Item -ItemType Directory -Force $objects, (Split-Path $tester -Parent) | Out-Null
    $flags = '--target=x86_64-pc-windows-msvc -O2 -std=gnu11 -D_GNU_SOURCE -DGB_VERSION="\"1.0.3\"" -DGB_COPYRIGHT_YEAR="\"2025\"" ' +
        '-I. -IWindows -D_USE_MATH_DEFINES -Drandom=rand -D_CRT_SECURE_NO_WARNINGS -w'
    $lines = @('@echo off', "call `"$vs\VC\Auxiliary\Build\vcvars64.bat`" >nul || exit /b 1", "cd /d `"$source`" || exit /b 1")
    $linked = @()
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $source 'Core') -Filter '*.c') {
        $lines += "clang $flags -DGB_INTERNAL -c Core\$($file.Name) -o `"$objects\core_$($file.BaseName).o`" || exit /b 1"
        $linked += "`"$objects\core_$($file.BaseName).o`""
    }
    foreach ($file in @('Windows\dirent.c', 'Windows\stdio.c', 'Tester\main.c')) {
        $base = [IO.Path]::GetFileNameWithoutExtension($file)
        $lines += "clang $flags -c $file -o `"$objects\other_$base.o`" || exit /b 1"
        $linked += "`"$objects\other_$base.o`""
    }
    $lines += "clang --target=x86_64-pc-windows-msvc $($linked -join ' ') -o `"$tester`" -Wl,/subsystem:console || exit /b 1"
    $script = Join-Path $cache 'build-tester.cmd'
    Set-Content -LiteralPath $script -Value $lines -Encoding ascii
    & cmd /c $script
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the SameBoy tester.' }
    $tester
}

function Read-Bmp([string]$Path) {
    $data = [IO.File]::ReadAllBytes($Path)
    $offset = [BitConverter]::ToInt32($data, 10)
    $width = [BitConverter]::ToInt32($data, 18)
    $height = [BitConverter]::ToInt32($data, 22)
    $step = [BitConverter]::ToInt16($data, 28) / 8
    $pixels = [int[]]::new(160 * 144)
    for ($y = 0; $y -lt 144; $y++) {
        $row = if ($height -lt 0) { $y } else { 143 - $y }
        for ($x = 0; $x -lt 160; $x++) {
            $at = $offset + (($row * $width) + $x) * $step
            $pixels[($y * 160) + $x] = ([int]$data[$at + 2] -shl 16) -bor ([int]$data[$at + 1] -shl 8) -bor [int]$data[$at]
        }
    }
    , $pixels
}

function Read-Ppm([string]$Path) {
    $tokens = (Get-Content -LiteralPath $Path -Raw) -split '\s+' | Where-Object { $_ }
    if ($tokens[0] -ne 'P3') { throw "Not a text PPM: $Path" }
    $pixels = [int[]]::new(160 * 144)
    for ($i = 0; $i -lt $pixels.Length; $i++) {
        $at = 4 + ($i * 3)
        $pixels[$i] = ([int]$tokens[$at] -shl 16) -bor ([int]$tokens[$at + 1] -shl 8) -bor [int]$tokens[$at + 2]
    }
    , $pixels
}

# Tiles 0-254 in reading order hold the length word and the results as 2bpp pixels; the bottom row shows colours 0-3.
function ConvertFrom-Screen([int[]]$Pixels) {
    $colours = @{}
    for ($c = 0; $c -lt 4; $c++) { $colours[$Pixels[(136 * 160) + ($c * 2)]] = $c }
    if ($colours.Count -ne 4) { throw 'The calibration row does not show four colours.' }
    $bytes = [byte[]]::new(255 * 16)
    for ($tile = 0; $tile -lt 255; $tile++) {
        $left = ($tile % 20) * 8
        $top = [Math]::Floor($tile / 20) * 8
        for ($y = 0; $y -lt 8; $y++) {
            $low = 0
            $high = 0
            for ($x = 0; $x -lt 8; $x++) {
                $pixel = $Pixels[(($top + $y) * 160) + $left + $x]
                if (!$colours.ContainsKey($pixel)) { throw "Unexpected colour $('{0:X6}' -f $pixel) in tile $tile." }
                $c = $colours[$pixel]
                $low = $low -bor (($c -band 1) -shl (7 - $x))
                $high = $high -bor (($c -shr 1) -shl (7 - $x))
            }
            $bytes[($tile * 16) + ($y * 2)] = $low
            $bytes[($tile * 16) + ($y * 2) + 1] = $high
        }
    }
    $length = [int]$bytes[0] -bor ([int]$bytes[1] -shl 8)
    if ($length -gt $bytes.Length - 2) { throw "Result length $length is too large." }
    [Convert]::ToHexString($bytes, 2, $length).ToLowerInvariant()
}

$binjgbTester = Get-BinjgbTester
$sameBoyTester = Get-SameBoyTester
$bootRom = Join-Path (Split-Path $sameBoyTester -Parent) 'dmg_boot.bin'
if (!(Test-Path -LiteralPath $bootRom)) { Copy-Item -LiteralPath (Join-Path $root 'Roms/External/sameboy/dmg_boot.bin') -Destination $bootRom }
$rgbfix = Join-Path $root '.cache/rgbds/v1.0.4/rgbfix.exe'
$work = Join-Path $root '.cache/testsuite-reference'
New-Item -ItemType Directory -Force $work, (Join-Path $suiteRoot 'reference') | Out-Null
$names = if ($Name) { $Name } else { Get-ChildItem -LiteralPath (Join-Path $suiteRoot 'src') -Filter '*.asm' | ForEach-Object { $_.BaseName } }
& (Join-Path $suiteRoot 'Build.ps1') -Verify -Offline:$Offline -Name $names | Out-Null
foreach ($rom in $names) {
    $path = Join-Path $suiteRoot 'reference' "$rom.json"
    $settings = if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json } else { $null }
    $reference = if ($settings) { $settings.reference } else { 'sameboy' }
    $frames = if ($settings) { [int]$settings.frames } else { 600 }
    # The testers need the boot logo, so a temporary copy gets it (logo and checksums only).
    $copy = Join-Path $work "$rom.gb"
    Copy-Item -LiteralPath (Join-Path $suiteRoot "$rom.gb") -Destination $copy -Force
    & $rgbfix -f lhg -Wno-overwrite $copy
    if ($LASTEXITCODE -ne 0) { throw "rgbfix failed for $rom." }
    $bmp = [IO.Path]::ChangeExtension($copy, '.bmp')
    $ppm = [IO.Path]::ChangeExtension($copy, '.ppm')
    Remove-Item -LiteralPath $bmp, $ppm -ErrorAction SilentlyContinue
    & $sameBoyTester --dmg --length ([Math]::Ceiling($frames / 60) + 3) $copy 2>$null
    & $binjgbTester -f $frames -o $ppm --force-dmg $copy | Out-Null
    $sameBoyBytes = ConvertFrom-Screen (Read-Bmp $bmp)
    $binjgbBytes = ConvertFrom-Screen (Read-Ppm $ppm)
    if ($Show) {
        Write-Output "$rom sameboy ($($sameBoyBytes.Length / 2) bytes): $sameBoyBytes"
        Write-Output "$rom binjgb  ($($binjgbBytes.Length / 2) bytes): $binjgbBytes"
        Write-Output "$rom the two agree: $($sameBoyBytes -eq $binjgbBytes)"
        continue
    }
    $record = [ordered]@{
        rom = "$rom.gb"
        sha256 = (Get-FileHash -LiteralPath (Join-Path $suiteRoot "$rom.gb") -Algorithm SHA256).Hash.ToLowerInvariant()
        reference = $reference
        frames = $frames
        maxTCycles = if ($settings) { [long]$settings.maxTCycles } else { 200000000 }
        cases = [object[]]@(if ($settings -and $settings.cases) { $settings.cases })
        sameboy = $sameBoyBytes
        binjgb = $binjgbBytes
    }
    Set-Content -LiteralPath $path -Value ($record | ConvertTo-Json -Depth 5) -Encoding utf8
    Write-Output "$rom recorded: SameBoy and binjgb $(if ($sameBoyBytes -eq $binjgbBytes) { 'agree' } else { 'differ' })"
}

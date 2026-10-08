# Compares the demo's sound with binjgb over -Seconds; results go to .cache/megademo-audio.
param([switch]$Offline, [double]$Seconds = 14)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$demoRoot = $PSScriptRoot
$root = Split-Path (Split-Path $demoRoot -Parent) -Parent
$binjgb = [ordered]@{
    Version = 'v0.1.11'
    Url = 'https://github.com/binji/binjgb/releases/download/v0.1.11/binjgb-windows.tar.gz'
    ArchiveSha256 = '86EBB0F68D760B30C8162F788BDA472BA856B9AC7DD7EE89C69AD751456F3E66'
    Files = [ordered]@{
        'binjgb.exe' = '33955789BC71A4BC16543DD7BC2597804E347376F900BF905FBC24E835B6282C'
        'SDL2.dll' = 'F65B50D693484D5D5A2BB8DF1CF520628DD744E99E9A937BB936839B990943A0'
    }
}

function Assert-Hash([string]$Path, [string]$Expected) {
    if (!(Test-Path -LiteralPath $Path) -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Expected) {
        throw "Missing file or SHA-256 mismatch: $Path"
    }
}

# Check the committed ROM and the pinned RGBDS (for rgbfix), then fetch and verify binjgb.
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
foreach ($file in $binjgb.Files.GetEnumerator()) {
    $path = Join-Path $cacheRoot "binjgb-$($binjgb.Version)/bin/$($file.Key)"
    if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.Value) {
        tar -xzf $archive -C $cacheRoot "binjgb-$($binjgb.Version)/bin/$($file.Key)"
        if ($LASTEXITCODE -ne 0) { throw "Could not extract $($file.Key)." }
    }
    Assert-Hash $path $file.Value
}
$player = Join-Path $cacheRoot "binjgb-$($binjgb.Version)/bin/binjgb.exe"

# binjgb needs the boot logo, so a temporary copy gets it (logo and checksums only).
$work = Join-Path $root '.cache/megademo-audio'
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
        throw "The copy differs outside the logo and checksums at $('{0:X4}' -f $i)."
    }
}

# Record binjgb's raw float stereo; closing its window, not killing it, completes the file.
$recording = Join-Path $work 'binjgb.f32'
$env:SDL_AUDIODRIVER = 'disk'; $env:SDL_DISKAUDIOFILE = $recording
try { $process = Start-Process -FilePath $player -ArgumentList ('"' + $romCopy + '"') -PassThru }
finally { Remove-Item Env:SDL_AUDIODRIVER, Env:SDL_DISKAUDIOFILE }
try {
    Start-Sleep -Milliseconds ([int](($Seconds + 2) * 1000))
    [void]$process.CloseMainWindow()
    if (!$process.WaitForExit(10000)) { throw 'binjgb did not close.' }
}
finally { if (!$process.HasExited) { $process.Kill() } }
if (!(Test-Path -LiteralPath $recording) -or (Get-Item -LiteralPath $recording).Length -eq 0) { throw 'binjgb wrote no sound.' }

dotnet run --file (Join-Path $demoRoot 'CompareAudio.cs') -c Release -- $rom $recording ([string]::Format([cultureinfo]::InvariantCulture, '{0}', $Seconds)) $work
if ($LASTEXITCODE -ne 0) { throw 'The comparison failed.' }
$metrics = Get-Content -LiteralPath (Join-Path $work 'metrics.json') -Raw | ConvertFrom-Json
# Thresholds a little below the recorded result.
$failures = @()
foreach ($side in 'Left', 'Right') {
    if ($metrics.$side.Mean -lt 0.98) { $failures += "$side mean similarity $($metrics.$side.Mean) < 0.98" }
    if ($metrics.$side.Percentile10 -lt 0.95) { $failures += "$side 10th percentile $($metrics.$side.Percentile10) < 0.95" }
}
if ($metrics.StrongestFrequencyMatches -lt 0.95 * $metrics.StrongestFrequencyWindows) { $failures += "strongest frequencies agree in $($metrics.StrongestFrequencyMatches) of $($metrics.StrongestFrequencyWindows)" }
if ($metrics.EnvelopeCorrelation -lt 0.9) { $failures += "envelope correlation $($metrics.EnvelopeCorrelation) < 0.9" }
if ($failures) { throw "The sound differs from binjgb more than the recorded result: $($failures -join '; ')" }
"Matches binjgb. Listen to ours.wav and binjgb.wav in $work"

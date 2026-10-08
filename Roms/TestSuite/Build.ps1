# Assembles the test ROMs (all, or -Name); -Verify compares fresh builds with the committed files.
param([string[]]$Name, [switch]$Offline, [switch]$Verify)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$suiteRoot = $PSScriptRoot
$root = Split-Path (Split-Path $suiteRoot -Parent) -Parent
$version = 'v1.0.4'
$archiveUrl = "https://github.com/gbdev/rgbds/releases/download/$version/rgbds-win64.zip"
$archiveHash = 'BD1D7E386F290AFD57F4EA9A7353BFCD4AE10884A94C5C1F2FC42A75DB7F97E6'
$toolHashes = [ordered]@{
    'rgbasm.exe' = '26017C1B2FE7F4255963C644EE3E76A619A5E3BAFA97EAF78DDD142411C64427'
    'rgblink.exe' = 'B59DF72CF2A9CD9FDE529F8E564C43E9E3BE2326D19C2B1781C1FF8EBA4BD28D'
    'rgbfix.exe' = '5E558415ACB76751E1D7970CC778456AEAA40F30C9768E669751BD3AD773F88A'
}
$cacheRoot = Join-Path $root '.cache/rgbds'
$toolRoot = Join-Path $cacheRoot $version
New-Item -ItemType Directory -Force $cacheRoot, $toolRoot | Out-Null

function Assert-Hash([string]$Path, [string]$Expected) {
    if (!(Test-Path -LiteralPath $Path) -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Expected) {
        throw "Missing file or SHA-256 mismatch: $Path"
    }
}

function Invoke-Tool([string]$Tool, [string[]]$Arguments) {
    & (Join-Path $toolRoot $Tool) @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Tool failed with exit code $LASTEXITCODE." }
}

$archive = Join-Path $cacheRoot 'rgbds-win64.zip'
if (!(Test-Path -LiteralPath $archive)) {
    if ($Offline) { throw "Offline cache is missing: $archive" }
    Invoke-WebRequest -Uri $archiveUrl -OutFile "$archive.download" -TimeoutSec 60
    Assert-Hash "$archive.download" $archiveHash
    Move-Item -LiteralPath "$archive.download" -Destination $archive -Force
}
Assert-Hash $archive $archiveHash
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($tool in $toolHashes.GetEnumerator()) {
    $path = Join-Path $toolRoot $tool.Key
    if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $tool.Value) {
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            $entry = $zip.GetEntry($tool.Key)
            if ($null -eq $entry) { throw "Archive entry is missing: $($tool.Key)" }
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "$path.download", $true)
        } finally { $zip.Dispose() }
        Assert-Hash "$path.download" $tool.Value
        Move-Item -LiteralPath "$path.download" -Destination $path -Force
    }
}

$source = Join-Path $suiteRoot 'src'
$names = if ($Name) { $Name } else { Get-ChildItem -LiteralPath $source -Filter '*.asm' | ForEach-Object { $_.BaseName } }
foreach ($rom in $names) {
    # Assemble in a scratch folder; only the ROM and symbol file are kept in the repository.
    $work = Join-Path $root ".cache/testsuite-build/$rom"
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $work | Out-Null
    $object = Join-Path $work "$rom.o"
    Invoke-Tool 'rgbasm.exe' @('-Wall', '-Wextra', '-I', $source, '-o', $object, (Join-Path $source "$rom.asm"))
    Invoke-Tool 'rgblink.exe' @('-d', '-t', '-p', '0xFF', '-n', (Join-Path $work "$rom.sym"), '-o', (Join-Path $work "$rom.gb"), $object)
    # Write the header title and checksums only, leaving out the Nintendo logo.
    $title = $rom.ToUpperInvariant()
    if ($title.Length -gt 15) { $title = $title.Substring(0, 15) }
    Invoke-Tool 'rgbfix.exe' @('-f', 'hg', '-p', '0xFF', '-t', $title, (Join-Path $work "$rom.gb"))
    foreach ($file in @("$rom.gb", "$rom.sym")) {
        $built = Join-Path $work $file
        $committed = Join-Path $suiteRoot $file
        if ($Verify) {
            if (!(Test-Path -LiteralPath $committed)) { throw "Committed file is missing: $committed" }
            if ((Get-FileHash -LiteralPath $built -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $committed -Algorithm SHA256).Hash) {
                throw "Rebuilt $file differs from the committed file."
            }
            Write-Output "$file reproduced"
        } else {
            Copy-Item -LiteralPath $built -Destination $committed -Force
            Write-Output "$file written: SHA-256 $((Get-FileHash -LiteralPath $committed -Algorithm SHA256).Hash.ToLowerInvariant())"
        }
    }
}

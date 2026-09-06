<#
.SYNOPSIS
    Builds the Windows installer (dist\RapidRawPlugin-Setup-<version>.exe).

.DESCRIPTION
    1. Builds the plugin in Release via tools\build-plugin.ps1 (note: that also
       re-points your own sideload .link at bin\Release; installing the setup
       afterwards points it at the install folder instead).
    2. Reads the version from src\package\metadata\LoupedeckPackage.yaml.
    3. Compiles installer\RapidRawPlugin.iss with Inno Setup 6.

    Inno Setup is the only extra tool. Install it once with:
        winget install -e --id JRSoftware.InnoSetup

.PARAMETER SkipBuild
    Use the existing bin\Release as-is.

.PARAMETER Iscc
    Path to ISCC.exe if it is somewhere unusual.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1
#>

[CmdletBinding()]
param(
    [switch] $SkipBuild,
    [string] $Iscc
)

$ErrorActionPreference = 'Stop'
if (Test-Path Variable:\PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

function Write-Ok   { param([string]$m) Write-Host "  OK    $m" -ForegroundColor Green }
function Write-Info { param([string]$m) Write-Host "        $m" -ForegroundColor DarkGray }
function Fail {
    param([string]$Problem, [string[]]$Fix)
    Write-Host "  STOP  $Problem" -ForegroundColor Red
    foreach ($line in $Fix) { Write-Host "        $line" -ForegroundColor Yellow }
    exit 1
}

$repo = Split-Path -Parent $PSScriptRoot
$release = Join-Path $repo 'bin\Release'
$iss = Join-Path $repo 'installer\RapidRawPlugin.iss'
$yaml = Join-Path $repo 'src\package\metadata\LoupedeckPackage.yaml'

Write-Host "`n  RapidRAW plugin - installer build`n  $repo`n" -ForegroundColor White

# --- 1. release build -------------------------------------------------------
if (-not $SkipBuild) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build-plugin.ps1') -Configuration Release
    if ($LASTEXITCODE -ne 0) { Fail 'The Release build failed; see above.' @() }
}
foreach ($f in 'bin\RapidRawPlugin.dll', 'metadata\LoupedeckPackage.yaml', 'actionicons') {
    if (-not (Test-Path (Join-Path $release $f))) {
        Fail "bin\Release is missing $f" @('Run without -SkipBuild, or run tools\build-plugin.ps1 -Configuration Release first.')
    }
}
Write-Ok "Release build: $release"

# --- 2. version ---------------------------------------------------------------
$version = (Get-Content $yaml | Where-Object { $_ -match '^\s*version:\s*(\S+)' } | ForEach-Object { $Matches[1] } | Select-Object -First 1)
if (-not $version) { Fail "Could not read 'version:' from $yaml" @() }
Write-Ok "Version: $version"

# --- 3. Inno Setup ------------------------------------------------------------
if (-not $Iscc) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    $Iscc = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $Iscc) {
        $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($cmd) { $Iscc = $cmd.Source }
    }
}
if (-not $Iscc -or -not (Test-Path $Iscc)) {
    Fail 'Inno Setup 6 (ISCC.exe) was not found.' @(
        'Install it once:   winget install -e --id JRSoftware.InnoSetup',
        'or download from https://jrsoftware.org/isdl.php, then re-run this script.',
        'If it is installed somewhere unusual:  -Iscc "C:\path\to\ISCC.exe"'
    )
}
Write-Ok "Inno Setup: $Iscc"

New-Item -ItemType Directory -Path (Join-Path $repo 'dist') -Force | Out-Null

# --- 4. compile ---------------------------------------------------------------
& $Iscc "/DMyAppVersion=$version" "/DSourceDir=$release" "/DRepoDir=$repo" $iss
if ($LASTEXITCODE -ne 0) { Fail "ISCC failed (exit code $LASTEXITCODE); see above." @() }

$out = Join-Path $repo "dist\RapidRawPlugin-Setup-$version.exe"
if (-not (Test-Path $out)) { Fail "ISCC reported success but $out is missing." @() }
$size = [math]::Round((Get-Item $out).Length / 1MB, 1)
Write-Host ''
Write-Ok "Installer: $out ($size MB)"
Write-Info 'Per-user install, no admin needed. Test it here, then attach it to a GitHub release.'
Write-Host ''

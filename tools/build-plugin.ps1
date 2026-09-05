<#
.SYNOPSIS
    Checks prerequisites, builds the plugin, and sideloads it into Logi Options+.

.DESCRIPTION
    Run this instead of calling `dotnet build` by hand. It verifies each thing
    the build depends on and tells you exactly what to do about anything that is
    missing, rather than failing with a wall of compiler errors.

    Right-click the file > "Run with PowerShell", or from a terminal:

        powershell -ExecutionPolicy Bypass -File tools\build-plugin.ps1

.PARAMETER PluginApiDir
    Folder containing PluginApi.dll, if it is somewhere unusual. Normally found
    automatically.

.PARAMETER Configuration
    Debug (default) or Release.

.PARAMETER CheckOnly
    Run the prerequisite checks and stop without building.
#>

[CmdletBinding()]
param(
    [string] $PluginApiDir,
    [ValidatePattern('^net\d+\.0$')]
    [string] $HostTargetFramework,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [switch] $CheckOnly
)

$ErrorActionPreference = 'Stop'

# PowerShell 7.4+ turns a non-zero exit from a native command into a thrown
# exception, which would bypass the friendly build-failure message below.
if (Test-Path Variable:\PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

# --- output helpers ---------------------------------------------------------

$script:Step = 0
function Write-Step  { param([string]$m) $script:Step++; Write-Host "`n[$script:Step] $m" -ForegroundColor Cyan }
function Write-Ok    { param([string]$m) Write-Host "    OK    $m" -ForegroundColor Green }
function Write-Warn2 { param([string]$m) Write-Host "    WARN  $m" -ForegroundColor Yellow }
function Write-Info  { param([string]$m) Write-Host "          $m" -ForegroundColor DarkGray }
function Fail {
    param([string]$Problem, [string[]]$Fix)
    Write-Host "    STOP  $Problem" -ForegroundColor Red
    Write-Host ''
    Write-Host '    How to fix it:' -ForegroundColor Yellow
    foreach ($line in $Fix) { Write-Host "      $line" -ForegroundColor Yellow }
    Write-Host ''
    exit 1
}

$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'src\RapidRawPlugin.csproj'

Write-Host ''
Write-Host '  RapidRAW plugin - build and sideload' -ForegroundColor White
Write-Host "  $repo" -ForegroundColor DarkGray

# --- 1. the project itself --------------------------------------------------

Write-Step 'Locating the project'
if (-not (Test-Path $proj)) {
    Fail "Could not find src\RapidRawPlugin.csproj under $repo" @(
        'Run this script from inside the project folder - it expects to live in tools\.',
        'The layout should be:  <folder>\src\RapidRawPlugin.csproj  and  <folder>\tools\build-plugin.ps1'
    )
}
Write-Ok "Project found: $proj"

# --- 2. .NET 8 SDK ----------------------------------------------------------

Write-Step 'Checking for the .NET SDK'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Fail 'The dotnet command was not found.' @(
        'Install the .NET SDK (the SDK, not just the Runtime):',
        '    https://dotnet.microsoft.com/download/dotnet',
        'Then close and reopen this terminal.'
    )
}

$sdks = @(& dotnet --list-sdks 2>$null)
$sdkMajors = @($sdks | ForEach-Object { if ($_ -match '^(\d+)\.') { [int]$Matches[1] } } | Sort-Object -Unique)
if ($sdkMajors.Count -eq 0) {
    Fail 'dotnet is installed but reports no SDKs (you may have only the Runtime).' @(
        'Install the .NET SDK:  https://dotnet.microsoft.com/download/dotnet'
    )
}
Write-Ok "SDKs present: $($sdkMajors -join ', ')"

# --- 3. PluginApi.dll -------------------------------------------------------

Write-Step 'Locating PluginApi.dll (ships with Logi Options+)'

if ($PluginApiDir) {
    $candidates = @((Join-Path $PluginApiDir 'PluginApi.dll'))
}
else {
    $candidates = @(
        'C:\Program Files\Logi\LogiPluginService\PluginApi.dll'
        'C:\Program Files\Logitech\LogiPluginService\PluginApi.dll'
        'C:\Program Files (x86)\Logi\LogiPluginService\PluginApi.dll'
        'C:\Program Files\Loupedeck\LoupedeckService\PluginApi.dll'
        'C:\Program Files (x86)\Loupedeck\LoupedeckService\PluginApi.dll'
    )
}

$apiDll = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $apiDll -and -not $PluginApiDir) {
    Write-Info 'Not in the usual places - searching Program Files (this takes a moment)...'
    $apiDll = Get-ChildItem 'C:\Program Files', 'C:\Program Files (x86)' `
                  -Recurse -Filter 'PluginApi.dll' -ErrorAction SilentlyContinue |
              Select-Object -First 1 -ExpandProperty FullName
}

if (-not $apiDll) {
    Fail 'PluginApi.dll was not found anywhere.' @(
        'This file comes from Logitech Options+ - there is no NuGet package for it,',
        'so Options+ must be installed on this machine to build the plugin.',
        '',
        '  1. Install Logi Options+:  https://www.logitech.com/software/options.html',
        '  2. Launch it once so it installs the Logi Plugin Service.',
        '  3. Re-run this script.',
        '',
        'If Options+ IS installed, find the file yourself and pass its folder:',
        "    Get-ChildItem 'C:\' -Recurse -Filter PluginApi.dll -ErrorAction SilentlyContinue",
        '    .\tools\build-plugin.ps1 -PluginApiDir "C:\That\Folder"'
    )
}

$apiDirNoSlash = Split-Path -Parent $apiDll
$apiDir = $apiDirNoSlash + '\'
Write-Ok "PluginApi.dll: $apiDll"

# --- 3b. which .NET does the host run on? -----------------------------------
#
# You cannot compile against a PluginApi.dll that references a NEWER
# System.Runtime than your target framework (error CS1705). Logitech's published
# samples say net8.0, but Options+ 6.4 ships on .NET 10 - so ask the installed
# service what it actually runs on rather than trusting the docs. The service's
# runtimeconfig.json states its required framework version.

Write-Step 'Determining which .NET the plugin service runs on'
$hostMajor = $null

if ($HostTargetFramework) {
    $hostMajor = [int]($HostTargetFramework -replace '^net(\d+)\.0$', '$1')
    Write-Info "overridden on the command line: $HostTargetFramework"
}

$rc = if ($hostMajor) { $null } else {
    Get-ChildItem $apiDirNoSlash -Filter '*.runtimeconfig.json' -ErrorAction SilentlyContinue |
        Select-Object -First 1
}
if ($rc) {
    try {
        $cfg = Get-Content $rc.FullName -Raw | ConvertFrom-Json
        $fw = $cfg.runtimeOptions.framework
        if (-not $fw) { $fw = @($cfg.runtimeOptions.frameworks)[0] }
        if ($fw.version -match '^(\d+)\.') { $hostMajor = [int]$Matches[1] }
        if ($hostMajor) { Write-Info "from $($rc.Name): framework $($fw.version)" }
    }
    catch { Write-Warn2 "Could not parse $($rc.Name): $($_.Exception.Message)" }
}

if (-not $hostMajor) {
    $hostMajor = 10
    Write-Warn2 "Could not determine it; assuming .NET $hostMajor."
    Write-Info  'If the build fails with CS1705, the number in that error is the one to use:'
    Write-Info  '    .\tools\build-plugin.ps1 -HostTargetFramework netN.0'
}

$tfm = "net$hostMajor.0"
Write-Ok "Target framework: $tfm"

if ($sdkMajors -notcontains $hostMajor) {
    Fail "The plugin service runs on .NET $hostMajor, but no $hostMajor.x SDK is installed (found: $($sdkMajors -join ', '))." @(
        "Install the .NET $hostMajor SDK - the SDK, not the Runtime:",
        "    https://dotnet.microsoft.com/download/dotnet/$hostMajor.0",
        'Pick the Windows x64 SDK installer, then reopen this terminal.',
        '',
        'Why this exact version: the plugin is compiled against PluginApi.dll,',
        "which itself targets .NET $hostMajor. Building against an older target",
        'fails with error CS1705 - a lower runtime cannot reference a higher one.'
    )
}

# --- 4. the plugin service --------------------------------------------------

Write-Step 'Checking the Logi Plugin Service'
$svc = Get-Process -Name 'LogiPluginService', 'LoupedeckService' -ErrorAction SilentlyContinue
if ($svc) { Write-Ok "Running ($($svc[0].ProcessName))" }
else       { Write-Warn2 'Not running. Start Logi Options+ before testing the plugin.' }

$pluginDir = Join-Path $env:LOCALAPPDATA 'Logi\LogiPluginService\Plugins'
if (-not (Test-Path $pluginDir)) {
    Write-Warn2 "Sideload folder does not exist yet; creating $pluginDir"
    New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
}
Write-Ok "Sideload folder: $pluginDir"

if ($CheckOnly) {
    Write-Host "`n  All prerequisites satisfied. Re-run without -CheckOnly to build.`n" -ForegroundColor Green
    exit 0
}

# --- 5. build ---------------------------------------------------------------

Write-Step "Building ($Configuration)"

# Pass the path via the environment, NOT as -p:PluginApiDir="...".
# $apiDir ends in a backslash, and Windows' native-command argument parser reads
# the resulting \" as an escaped quote rather than a closing one - the argument
# never terminates and swallows whatever token follows it. MSBuild picks up
# environment variables as properties, and nothing quotes anything on this path.
$env:PluginApiDir = $apiDir
$env:HostTargetFramework = $tfm
& dotnet build $proj -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    Fail "The build failed (exit code $LASTEXITCODE)." @(
        'Read the first error above - later ones are usually knock-on effects.',
        '',
        'CS1705 ("uses System.Runtime Version=N.0.0.0 which has a higher version"):',
        '  PluginApi.dll targets a newer .NET than we built for. Use the N from',
        '  that message:   .\tools\build-plugin.ps1 -HostTargetFramework netN.0',
        '  and install the matching SDK if you do not have it.',
        '',
        'CS0246 ("type or namespace Plugin / PluginDynamicCommand not found"):',
        '  the reference resolved but the SDK surface differs from what this code',
        '  expects. Send Claude the exact error and it can adjust the code.'
    )
}

# --- 6. verify the output layout -------------------------------------------

Write-Step 'Verifying the output layout'
$baseDir = Join-Path $repo "bin\$Configuration"
$checks = @(
    @{ Path = Join-Path $baseDir 'bin\RapidRawPlugin.dll';            What = 'plugin assembly' }
    @{ Path = Join-Path $baseDir 'metadata\LoupedeckPackage.yaml';     What = 'manifest' }
    @{ Path = Join-Path $baseDir 'metadata\Icon256x256.png';           What = 'plugin icon' }
)
$missing = @()
foreach ($c in $checks) {
    if (Test-Path $c.Path) { Write-Ok "$($c.What): $($c.Path)" }
    else { $missing += $c; Write-Host "    MISS  $($c.What): $($c.Path)" -ForegroundColor Red }
}
if ($missing.Count -gt 0) {
    Fail 'The build succeeded but did not produce the expected layout.' @(
        'The service needs  bin\<Config>\  to contain BOTH  metadata\  and  bin\.',
        'Try a clean rebuild:   dotnet clean src\RapidRawPlugin.csproj',
        'then re-run this script. If it persists, send Claude this output.'
    )
}

# --- 7. verify the sideload link -------------------------------------------

Write-Step 'Verifying the sideload link'
$link = Join-Path $pluginDir 'RapidRawPlugin.link'
if (-not (Test-Path $link)) {
    Write-Warn2 'The build did not write the .link file. Writing it now.'
    Set-Content -Path $link -Value $baseDir -Encoding ASCII
}
$linkTarget = (Get-Content $link -Raw).Trim()
Write-Ok "$link"
Write-Info "points to: $linkTarget"
if (-not (Test-Path $linkTarget)) {
    Fail "The .link file points at a folder that does not exist: $linkTarget" @(
        "Overwrite it with the correct path:",
        "    Set-Content '$link' '$baseDir'",
        'then restart Logi Options+.'
    )
}

# --- done -------------------------------------------------------------------

Write-Host ''
Write-Host '  Build complete and sideloaded.' -ForegroundColor Green
Write-Host ''
Write-Host '  Next:' -ForegroundColor White
Write-Host '    1. Open Logi Options+. The plugin appears as "RapidRAW" under the MX Creative'
Write-Host '       Console. If it does not, quit Options+ fully (system tray) and reopen it.'
Write-Host '    2. Its status reads "RapidRAW not reachable" until the external-control build of'
Write-Host '       RapidRAW is running - start RapidRAW and it flips to Normal within ~2 s.'
Write-Host '    3. Open an image in RapidRAW, then run tools\smoke-test.ps1 to prove the socket'
Write-Host '       works with no hardware, and assign "RapidRAW > Exposure" to a dial in Options+.'
Write-Host ''
Write-Host '  After editing the C# later, just re-run this script.' -ForegroundColor DarkGray
Write-Host '  To remove the sideload:  dotnet clean src\RapidRawPlugin.csproj' -ForegroundColor DarkGray
Write-Host ''

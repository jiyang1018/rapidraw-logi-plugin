<#
.SYNOPSIS
    Optional: give the RapidRAW plugin the same action icons as Logitech's own
    Lightroom Classic plugin. Nothing from Logitech ships with this plugin; this
    script copies the icons from a Logi plugin YOU installed, on YOUR machine.

.DESCRIPTION
    Requirements:
      1. Logi Options+ with the RapidRAW plugin installed (Marketplace or sideload).
      2. "Lightroom Classic by Logi" installed from the Options+ Marketplace.
         (It does not need Lightroom itself, and you can uninstall it afterwards;
         the icons are copied.)

    What it does:
      - finds your RapidRAW plugin folder and the Lightroom plugin's icon folder
      - backs up the plugin's current icons to <plugin>\icons-backup\ (once)
      - copies the matching Lightroom icon over 136 of the 155 RapidRAW actions;
        the rest keep the plugin's own icons
      - asks Options+ to reload the plugin

    Undo at any time with:   .\use-logi-icons.ps1 -Restore

.PARAMETER Restore
    Put the plugin's own icons back from the backup.

.PARAMETER PluginDir
    The RapidRAW plugin base folder, if it is somewhere unusual (the folder that
    contains metadata\ and actionicons\). Found automatically otherwise.

.PARAMETER LightroomDir
    The Lightroom plugin's actionicons folder, if it is somewhere unusual.

.PARAMETER Pause
    Wait for Enter before closing. Used by the Start Menu shortcuts the installer
    creates, so the result stays readable.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\use-logi-icons.ps1
    powershell -ExecutionPolicy Bypass -File tools\use-logi-icons.ps1 -Restore
#>

[CmdletBinding()]
param(
    [switch] $Restore,
    [string] $PluginDir,
    [string] $LightroomDir,
    [switch] $Pause
)

$ErrorActionPreference = 'Stop'

$Ns  = 'Loupedeck.RapidRawPlugin'
$Adj = "$Ns.RapidRawAdjustments"
$Cmd = "$Ns.RapidRawCommands"

function Write-Ok   { param([string]$m) Write-Host "  OK    $m" -ForegroundColor Green }
function Write-Info { param([string]$m) Write-Host "        $m" -ForegroundColor DarkGray }
function Finish([int]$code) {
    if ($Pause) { Write-Host ''; Read-Host 'Press Enter to close' | Out-Null }
    exit $code
}
function Fail {
    param([string]$Problem, [string[]]$Fix)
    Write-Host "  STOP  $Problem" -ForegroundColor Red
    foreach ($line in $Fix) { Write-Host "        $line" -ForegroundColor Yellow }
    Finish 1
}

# --- locate the RapidRAW plugin ----------------------------------------------

$serviceDir = Join-Path $env:LOCALAPPDATA 'Logi\LogiPluginService'
$pluginsDir = Join-Path $serviceDir 'Plugins'

if (-not $PluginDir) {
    # Sideloaded build: the .link file names the plugin base folder.
    $link = Join-Path $pluginsDir 'RapidRawPlugin.link'
    if (Test-Path $link) {
        $PluginDir = (Get-Content $link -Raw).Trim()
    }
    elseif (Test-Path (Join-Path $pluginsDir 'RapidRaw')) {
        # Marketplace install.
        $PluginDir = Join-Path $pluginsDir 'RapidRaw'
    }
}

if (-not $PluginDir -or -not (Test-Path (Join-Path $PluginDir 'metadata\LoupedeckPackage.yaml'))) {
    Fail 'Could not find the installed RapidRAW plugin.' @(
        'Install it first (Options+ Marketplace, or tools\build-plugin.ps1 for a sideload),',
        'or pass the plugin folder explicitly:  -PluginDir "C:\path\to\RapidRaw"'
    )
}
Write-Ok "RapidRAW plugin: $PluginDir"

$symbolsDir = Join-Path $PluginDir 'actionsymbols'
$iconsDir   = Join-Path $PluginDir 'actionicons'
$backupDir  = Join-Path $PluginDir 'icons-backup'

# --- restore ----------------------------------------------------------------

if ($Restore) {
    if (-not (Test-Path $backupDir)) {
        Fail 'No backup found; the plugin still has its own icons.' @("Expected: $backupDir")
    }
    foreach ($folder in 'actionsymbols', 'actionicons') {
        $src = Join-Path $backupDir $folder
        if (Test-Path $src) {
            Copy-Item (Join-Path $src '*.svg') (Join-Path $PluginDir $folder) -Force
        }
    }
    Write-Ok 'Original icons restored.'
    Start-Process 'loupedeck:plugin/RapidRaw/reload' -ErrorAction SilentlyContinue
    Write-Host "`n  Done. Options+ has been asked to reload the plugin.`n" -ForegroundColor Green
    Finish 0
}

# --- locate the Lightroom plugin's icons ------------------------------------

if (-not $LightroomDir) {
    $LightroomDir = Join-Path $pluginsDir 'Lightroom\actionicons'
}
if (-not (Test-Path (Join-Path $LightroomDir 'Exposure.svg'))) {
    Fail '"Lightroom Classic by Logi" is not installed, so there are no icons to copy.' @(
        'Open Logi Options+  >  Marketplace  >  search "Lightroom Classic by Logi"  >  Install.',
        'You do not need Lightroom itself. Then run this script again.',
        "(Looked in: $LightroomDir)"
    )
}
Write-Ok "Lightroom icons: $LightroomDir"

# --- mapping: RapidRAW action parameter -> Lightroom icon file ---------------
# $null = no counterpart, keep the plugin's own icon.

$adjustments = [ordered]@{
    'exposure' = 'Exposure'; 'brightness' = $null; 'contrast' = 'Contrast'; 'highlights' = 'Highlights'
    'shadows' = 'Shadows'; 'whites' = 'Whites'; 'blacks' = 'Blacks'
    'temperature' = 'Temperature'; 'tint' = 'Tint'; 'vibrance' = 'Vibrance'; 'saturation' = 'Saturation'
    'hue' = 'LocalizedHue'
    'colorGrading.blending' = 'ColorGradeBlending'; 'colorGrading.balance' = 'ColorGradeBalance'
    'colorCalibration.shadowsTint' = $null
    'colorCalibration.redHue' = 'RedHue'; 'colorCalibration.redSaturation' = 'RedSaturation'
    'colorCalibration.greenHue' = 'GreenHue'; 'colorCalibration.greenSaturation' = 'GreenSaturation'
    'colorCalibration.blueHue' = 'BlueHue'; 'colorCalibration.blueSaturation' = 'BlueSaturation'
    'sharpness' = 'Sharpness'; 'sharpnessThreshold' = 'SharpenEdgeMasking'; 'clarity' = 'Clarity'
    'dehaze' = 'Dehaze'; 'structure' = 'Texture'
    'lumaNoiseReduction' = 'LuminanceSmoothing'; 'colorNoiseReduction' = 'ColorNoiseReduction'
    'chromaticAberrationRedCyan' = 'DefringePurpleAmount'; 'chromaticAberrationBlueYellow' = 'DefringeGreenAmount'
    'glowAmount' = $null; 'halationAmount' = $null; 'flareAmount' = $null
    'lensBlurAmount' = $null; 'lensBlurDiffusion' = $null
    'vignetteAmount' = 'PostCropVignetteAmount'; 'vignetteMidpoint' = 'PostCropVignetteMidpoint'
    'vignetteRoundness' = 'PostCropVignetteRoundness'; 'vignetteFeather' = 'PostCropVignetteFeather'
    'grainAmount' = 'GrainAmount'; 'grainSize' = 'GrainSize'; 'grainRoughness' = 'GrainFrequency'
    'lutIntensity' = $null
    'rotation' = 'RotateCropArea'; 'transformRotate' = 'PerspectiveRotate'
    'transformVertical' = 'PerspectiveVertical'; 'transformHorizontal' = 'PerspectiveHorizontal'
    'transformDistortion' = 'LensManualDistortionAmount'; 'transformAspect' = 'PerspectiveAspect'
    'transformScale' = 'PerspectiveScale'; 'transformXOffset' = 'PerspectiveX'; 'transformYOffset' = 'PerspectiveY'
}
# RapidRAW's field is spelled with an accent (centre); built from a char code so
# this file stays pure ASCII for Windows PowerShell 5.1.
$adjustments[('centr' + [char]0x00E9)] = $null

$colors = @{ reds = 'Red'; oranges = 'Orange'; yellows = 'Yellow'; greens = 'Green'
             aquas = 'Aqua'; blues = 'Blue'; purples = 'Purple'; magentas = 'Magenta' }
foreach ($c in $colors.Keys) {
    $adjustments["hsl.${c}.hue"]        = "HueAdjustment$($colors[$c])"
    $adjustments["hsl.${c}.saturation"] = "SaturationAdjustment$($colors[$c])"
    $adjustments["hsl.${c}.luminance"]  = "LuminanceAdjustment$($colors[$c])"
}
$ranges = @{ shadows = 'Shadow'; midtones = 'Midtone'; highlights = 'Highlight'; global = 'Global' }
$parts  = @{ hue = 'Hue'; saturation = 'Sat'; luminance = 'Lum' }
foreach ($r in $ranges.Keys) {
    foreach ($p in $parts.Keys) {
        $adjustments["colorGrading.${r}.${p}"] = "ColorGrade$($ranges[$r])$($parts[$p])"
    }
}

$commands = [ordered]@{
    'reset_active' = $null
    'undo' = 'Undo'; 'redo' = 'Redo'
    'copy_adjustments' = 'CopyDevelopSettings'; 'paste_adjustments' = 'PasteDevelopSettings'
    'show_original' = 'ToggleBeforeAfter'; 'rotate_left' = 'RotateLeft'; 'rotate_right' = 'RotateRight'
    'preview_prev' = 'MoveToPreviousPhoto'; 'preview_next' = 'MoveToNextPhoto'; 'open_image' = 'LibraryLoupeView'
    'rate_0' = 'ClearRating'; 'rate_1' = 'RatingStar1'; 'rate_2' = 'RatingStar2'; 'rate_3' = 'RatingStar3'
    'rate_4' = 'RatingStar4'; 'rate_5' = 'RatingStar5'
    'color_label_none' = $null; 'color_label_red' = 'RatingColor1'; 'color_label_yellow' = 'RatingColor2'
    'color_label_green' = 'RatingColor3'; 'color_label_blue' = 'RatingColor4'; 'color_label_purple' = 'RatingColor5'
    'zoom_fit' = 'Zoom'; 'zoom_100' = 'ZoomToOneToOne'; 'zoom_in' = 'ZoomIn'; 'zoom_out' = 'ZoomOut'
    'cycle_zoom' = 'ToggleZoomView'; 'toggle_fullscreen' = 'FullScreen'
    'toggle_left_panel' = 'TogglePanels'; 'toggle_right_panel' = 'TogglePanelsExceptRight'; 'toggle_bottom_panel' = $null
    'toggle_adjustments' = $null; 'toggle_crop_panel' = 'ActivateDevelopTool___Crop'
    'toggle_masks' = 'ActivateDevelopTool___masking'; 'toggle_presets' = $null
    'toggle_export' = 'OpenExportDialog'; 'toggle_metadata' = 'CycleInfoDisplay'
    'brush_size_up' = $null; 'brush_size_down' = $null
    'delete_selected' = 'DeletePhoto'; 'select_all' = 'SelectAll'
}
$resets = @{
    exposure = 'ResetExposure'; contrast = 'ResetContrast'; highlights = 'ResetHighlights'
    shadows = 'ResetShadows'; whites = 'ResetWhites'; blacks = 'ResetBlacks'
    temperature = 'ResetTemperature'; tint = 'ResetTint'; vibrance = 'ResetVibrance'
    saturation = 'ResetSaturation'; hue = 'ResetLocalizedHue'
    sharpness = 'ResetSharpness'; sharpnessThreshold = 'ResetSharpenEdgeMasking'; clarity = 'ResetClarity'
    dehaze = 'ResetDehaze'; structure = 'ResetTexture'
    lumaNoiseReduction = 'ResetLuminanceSmoothing'; colorNoiseReduction = 'ResetColorNoiseReduction'
    chromaticAberrationRedCyan = 'ResetDefringePurpleAmount'; chromaticAberrationBlueYellow = 'ResetDefringeGreenAmount'
}
foreach ($k in $resets.Keys) { $commands["reset-$k"] = $resets[$k] }

$plan = [ordered]@{}
foreach ($k in $adjustments.Keys) { $plan["$Adj" + '___' + $k + '.svg'] = $adjustments[$k] }
foreach ($k in $commands.Keys)    { $plan["$Cmd" + '___' + $k + '.svg'] = $commands[$k] }
$plan["$Adj.svg"] = 'Exposure'

# --- back up once, then copy -------------------------------------------------

if (-not (Test-Path $backupDir)) {
    foreach ($folder in 'actionsymbols', 'actionicons') {
        $src = Join-Path $PluginDir $folder
        if (Test-Path $src) {
            New-Item -ItemType Directory -Path (Join-Path $backupDir $folder) -Force | Out-Null
            Copy-Item (Join-Path $src '*.svg') (Join-Path $backupDir $folder) -Force
        }
    }
    Write-Ok "Backed up the plugin's own icons to $backupDir"
}
else {
    Write-Info "Backup already exists at $backupDir (kept)"
}

$used = 0; $kept = 0; $missing = @()
foreach ($name in $plan.Keys) {
    $lr = $plan[$name]
    if (-not $lr) { $kept++; continue }
    $src = Join-Path $LightroomDir "$lr.svg"
    if (-not (Test-Path $src)) { $missing += $lr; $kept++; continue }
    foreach ($dir in $symbolsDir, $iconsDir) {
        if (Test-Path $dir) { Copy-Item $src (Join-Path $dir $name) -Force }
    }
    $used++
}

Write-Ok "Lightroom icons applied: $used   (kept the plugin's own: $kept)"
if ($missing.Count -gt 0) {
    Write-Info ('Not present in this Lightroom plugin version, kept original: ' + ($missing -join ', '))
}

Start-Process 'loupedeck:plugin/RapidRaw/reload' -ErrorAction SilentlyContinue
Write-Host ''
Write-Host '  Done. Options+ has been asked to reload the plugin; if the icons do not change,' -ForegroundColor Green
Write-Host '  quit Options+ from the tray and reopen it.' -ForegroundColor Green
Write-Host "  Undo:  .\use-logi-icons.ps1 -Restore" -ForegroundColor DarkGray
Write-Host ''
Finish 0

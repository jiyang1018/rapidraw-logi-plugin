#!/usr/bin/env bash
# Builds the RapidRAW plugin on macOS and sideloads it into Logi Options+.
# Counterpart of tools/build-plugin.ps1. Run from anywhere:
#
#     bash tools/build-plugin.sh [--release] [--check-only] [--plugin-api-dir DIR] [--tfm netN.0]
#
# What it checks, in order, with a fix-it hint for each:
#   1. the .NET SDK (the SDK, not just the runtime)
#   2. PluginApi.dll from the Logi Plugin Service (installed by Options+)
#   3. which .NET the service runs on (from its runtimeconfig.json), so the build
#      targets the same major version - see the csproj for why this matters
#   4. the matching .NET SDK is installed
# then builds, verifies the output layout, and checks the .link sideload file.

set -u

CONFIG=Debug
CHECK_ONLY=0
PLUGIN_API_DIR=""
TFM=""

while [ $# -gt 0 ]; do
    case "$1" in
        --release) CONFIG=Release ;;
        --check-only) CHECK_ONLY=1 ;;
        --plugin-api-dir) PLUGIN_API_DIR="$2"; shift ;;
        --tfm) TFM="$2"; shift ;;
        -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
        *) echo "unknown option: $1" >&2; exit 2 ;;
    esac
    shift
done

step=0
ok()   { printf '    OK    %s\n' "$1"; }
warn() { printf '    WARN  %s\n' "$1"; }
info() { printf '          %s\n' "$1"; }
step() { step=$((step + 1)); printf '\n[%d] %s\n' "$step" "$1"; }
fail() {
    printf '    STOP  %s\n\n    How to fix it:\n' "$1"
    shift
    for line in "$@"; do printf '      %s\n' "$line"; done
    printf '\n'
    exit 1
}

REPO="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$REPO/src/RapidRawPlugin.csproj"

printf '\n  RapidRAW plugin - build and sideload (macOS)\n  %s\n' "$REPO"

# --- 1. project -------------------------------------------------------------
step "Locating the project"
[ -f "$PROJ" ] || fail "Could not find src/RapidRawPlugin.csproj under $REPO" \
    "This script expects to live in tools/ inside the repo."
ok "Project found: $PROJ"

# --- 2. dotnet --------------------------------------------------------------
step "Checking for the .NET SDK"
command -v dotnet >/dev/null 2>&1 || fail "The dotnet command was not found." \
    "Install the .NET SDK (the SDK, not just the Runtime):" \
    "    https://dotnet.microsoft.com/download/dotnet" \
    "Then open a new terminal."
SDK_MAJORS="$(dotnet --list-sdks 2>/dev/null | sed -E 's/^([0-9]+)\..*/\1/' | sort -un | tr '\n' ' ')"
[ -n "$SDK_MAJORS" ] || fail "dotnet is installed but reports no SDKs (you may have only the Runtime)." \
    "Install the .NET SDK:  https://dotnet.microsoft.com/download/dotnet"
ok "SDKs present: $SDK_MAJORS"

# --- 3. PluginApi.dll -------------------------------------------------------
step "Locating PluginApi.dll (ships with Logi Options+)"
API_DLL=""
if [ -n "$PLUGIN_API_DIR" ]; then
    [ -f "$PLUGIN_API_DIR/PluginApi.dll" ] && API_DLL="$PLUGIN_API_DIR/PluginApi.dll"
else
    for c in \
        "/Applications/Utilities/LogiPluginService.app/Contents/MonoBundle/PluginApi.dll" \
        "/Applications/LogiPluginService.app/Contents/MonoBundle/PluginApi.dll" \
        "/Applications/Utilities/LogiPluginService.app/Contents/Resources/PluginApi.dll" \
        "/Applications/Loupedeck.app/Contents/MonoBundle/PluginApi.dll"; do
        if [ -f "$c" ]; then API_DLL="$c"; break; fi
    done
    if [ -z "$API_DLL" ]; then
        info "Not in the usual places - searching /Applications and ~/Library (a moment)..."
        API_DLL="$(find /Applications "$HOME/Library" -name PluginApi.dll -print 2>/dev/null | head -n 1)"
    fi
fi
[ -n "$API_DLL" ] || fail "PluginApi.dll was not found anywhere." \
    "It comes with Logi Options+ - there is no NuGet package - so Options+ must be installed:" \
    "  1. Install Logi Options+ and launch it once (it installs the Logi Plugin Service)." \
    "  2. Re-run this script." \
    "If it IS installed, find the file and pass its folder:" \
    "    mdfind -name PluginApi.dll" \
    "    bash tools/build-plugin.sh --plugin-api-dir /path/to/folder"
API_DIR="$(dirname "$API_DLL")"
ok "PluginApi.dll: $API_DLL"

# --- 3b. which .NET does the host run on? -----------------------------------
step "Determining which .NET the plugin service runs on"
HOST_MAJOR=""
if [ -n "$TFM" ]; then
    HOST_MAJOR="$(printf '%s' "$TFM" | sed -E 's/^net([0-9]+)\.0$/\1/')"
    info "overridden on the command line: $TFM"
else
    RC="$(ls "$API_DIR"/*.runtimeconfig.json 2>/dev/null | head -n 1)"
    if [ -n "$RC" ]; then
        # "version": "10.0.0" under runtimeOptions.framework (or frameworks[0])
        HOST_MAJOR="$(grep -o '"version"[[:space:]]*:[[:space:]]*"[0-9]*' "$RC" | head -n 1 | grep -o '[0-9]*$')"
        [ -n "$HOST_MAJOR" ] && info "from $(basename "$RC"): framework $HOST_MAJOR.x"
    fi
fi
if [ -z "$HOST_MAJOR" ]; then
    HOST_MAJOR=10
    warn "Could not determine it; assuming .NET $HOST_MAJOR."
    info "If the build fails with CS1705, the number in that error is the one to use: --tfm netN.0"
fi
TFM="net$HOST_MAJOR.0"
ok "Target framework: $TFM"
case " $SDK_MAJORS " in
    *" $HOST_MAJOR "*) ;;
    *) fail "The plugin service runs on .NET $HOST_MAJOR, but no $HOST_MAJOR.x SDK is installed (found: $SDK_MAJORS)." \
        "Install the .NET $HOST_MAJOR SDK - the SDK, not the Runtime:" \
        "    https://dotnet.microsoft.com/download/dotnet/$HOST_MAJOR.0" \
        "Why this exact version: the plugin is compiled against PluginApi.dll, which itself" \
        "targets .NET $HOST_MAJOR. A lower target fails with error CS1705." ;;
esac

# --- 4. the plugin service --------------------------------------------------
step "Checking the Logi Plugin Service"
if pgrep -qf "LogiPluginService" 2>/dev/null; then ok "Running"; else warn "Not running. Start Logi Options+ before testing the plugin."; fi
PLUGIN_DIR="$HOME/Library/Application Support/Logi/LogiPluginService/Plugins"
mkdir -p "$PLUGIN_DIR"
ok "Sideload folder: $PLUGIN_DIR"

if [ "$CHECK_ONLY" = 1 ]; then
    printf '\n  All prerequisites satisfied. Re-run without --check-only to build.\n\n'
    exit 0
fi

# --- 5. build ---------------------------------------------------------------
step "Building ($CONFIG)"
# Passed through the environment so a path with spaces or a trailing slash
# never has to survive shell quoting; MSBuild reads env vars as properties.
export PluginApiDir="$API_DIR/"
export HostTargetFramework="$TFM"
if ! dotnet build "$PROJ" -c "$CONFIG" --nologo; then
    fail "The build failed." \
        "Read the first error above - later ones are usually knock-on effects." \
        "CS1705: PluginApi.dll targets a newer .NET than we built for; use --tfm netN.0 with the N from the message." \
        "CS0246 (Plugin / PluginDynamicCommand not found): the SDK surface differs; send the exact error."
fi

# --- 6. verify the output layout -------------------------------------------
step "Verifying the output layout"
BASE="$REPO/bin/$CONFIG"
missing=0
for f in "bin/RapidRawPlugin.dll" "metadata/LoupedeckPackage.yaml" "metadata/Icon256x256.png" "actionicons/Loupedeck.RapidRawPlugin.RapidRawAdjustments___exposure.svg"; do
    if [ -f "$BASE/$f" ]; then ok "$f"; else printf '    MISS  %s\n' "$f"; missing=1; fi
done
[ "$missing" = 0 ] || fail "The build succeeded but did not produce the expected layout." \
    "The service needs bin/$CONFIG/ to contain BOTH metadata/ and bin/." \
    "Try:  dotnet clean src/RapidRawPlugin.csproj   then re-run this script."

# --- 7. verify the sideload link -------------------------------------------
step "Verifying the sideload link"
LINK="$PLUGIN_DIR/RapidRawPlugin.link"
if [ ! -f "$LINK" ]; then
    warn "The build did not write the .link file. Writing it now."
    printf '%s/\n' "$BASE" > "$LINK"
fi
TARGET="$(tr -d '\r\n' < "$LINK")"
ok "$LINK"
info "points to: $TARGET"
[ -d "$TARGET" ] || fail "The .link file points at a folder that does not exist: $TARGET" \
    "Overwrite it:   printf '%s/\\n' \"$BASE\" > \"$LINK\"   then restart Logi Options+."

open "loupedeck:plugin/RapidRaw/reload" 2>/dev/null || true

printf '\n  Build complete and sideloaded.\n\n  Next:\n'
printf '    1. Open Logi Options+. The plugin appears as "RapidRAW". If not, quit Options+ fully and reopen.\n'
printf '    2. Its status reads "RapidRAW not reachable" until the external-control build of RapidRAW runs.\n'
printf '    3. Open an image in RapidRAW and assign "RapidRAW > Exposure" to a dial.\n\n'
printf '  After editing the C# later, just re-run this script.\n'
printf '  To remove the sideload:  dotnet clean src/RapidRawPlugin.csproj\n\n'

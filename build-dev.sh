#!/usr/bin/env bash
# Build, deploy to ~/.local/bin/, and (on Windows) restart the tray app.
# Cross-platform: Windows (Git Bash / MSYS) builds Imrdy.Windows; Linux builds
# Imrdy.Linux (hook-only — no tray to restart).
# Usage: ./build-dev.sh [rid]
#   rid defaults to win-x64 on Windows and linux-x64 on Linux.
set -euo pipefail

case "$(uname -s)" in
    Linux*)               PLATFORM=linux ;;
    MINGW*|MSYS*|CYGWIN*) PLATFORM=windows ;;
    *) echo "ERROR: unsupported platform $(uname -s)" >&2; exit 1 ;;
esac

if [[ "$PLATFORM" == "windows" ]]; then
    PROJECT="src/Imrdy.Windows/Imrdy.Windows.csproj"
    RID="${1:-win-x64}"
    DEST="$HOME/.local/bin/imrdy.exe"
    PUBLISH_PATH_GLOB="*/${RID}/publish/imrdy.exe"
else
    PROJECT="src/Imrdy.Linux/Imrdy.Linux.csproj"
    RID="${1:-linux-x64}"
    DEST="$HOME/.local/bin/imrdy"
    PUBLISH_PATH_GLOB="*/${RID}/publish/imrdy"
fi

# 1. Publish.
dotnet publish "$PROJECT" -c Release -r "$RID"

# 2. Find the publish output (avoids hardcoding the TFM).
#    Pick the newest match — stale TFM dirs from prior builds can otherwise win
#    the alphabetical race (e.g. net10.0-windows vs net10.0-windows10.0.17763.0).
PUBLISH_BIN=$(find "$(dirname "$PROJECT")/bin/Release" -path "$PUBLISH_PATH_GLOB" -type f -printf '%T@ %p\n' \
    | sort -nr | head -1 | cut -d' ' -f2-)
if [[ -z "$PUBLISH_BIN" ]]; then
    echo "ERROR: published binary not found at $PUBLISH_PATH_GLOB" >&2
    exit 1
fi

mkdir -p "$(dirname "$DEST")"

if [[ "$PLATFORM" == "windows" ]]; then
    # 3. Stop gracefully, then force-kill any stragglers (hook respawns).
    "$DEST" stop 2>/dev/null || true
    taskkill //IM imrdy.exe //F > /dev/null 2>&1 || true

    # 4. Rename the old binary out of the way (works even if briefly locked),
    #    then copy the new one in. Hooks that fire during the gap fail harmlessly.
    mv "$DEST" "$DEST.old" 2>/dev/null || true
    cp "$PUBLISH_BIN" "$DEST"
    rm -f "$DEST.old"
else
    # 3+4. Atomic swap via temp-in-same-dir + mv. Avoids ETXTBSY if a concurrent
    #      hook process is mid-exec on the old binary.
    TMP="${DEST}.new.$$"
    install -m 0755 "$PUBLISH_BIN" "$TMP"
    mv -f "$TMP" "$DEST"
fi

# 5. Drop a dev-build marker so the hook/tray defaults to Debug logging.
#    ServiceRegistration.AddSerilog reads ~/.imrdy/.dev-build to flip the level
#    without requiring IMRDY_LOG=1 in every shell that triggers a hook.
#    The file body contains the repo root so the tray's Manage → Dev menu
#    can enumerate fixtures from tests/fixtures/dashboards/.
#    Remove the file (rm ~/.imrdy/.dev-build) to test prod-like log levels.
mkdir -p "$HOME/.imrdy"
if [[ "$PLATFORM" == "windows" ]]; then
    # `pwd -W` yields the Windows-form path (D:/…) that .NET understands.
    # Plain $PWD is MSYS form (/d/…) which fails Directory.Exists on the tray side.
    REPO_ROOT=$(pwd -W 2>/dev/null || pwd)
else
    REPO_ROOT="$PWD"
fi
printf '%s\n' "$REPO_ROOT" > "$HOME/.imrdy/.dev-build"

# 6. On Windows, spawn the tray immediately so we don't wait for the next
#    Claude hook event to respawn it. `cmd //c start` detaches the process
#    from this shell's tree, so the tray survives after build-dev.sh exits
#    and is not tied to the Claude process that invoked the script.
#    On Linux there is no tray — the hook binary is invoked per event.
if [[ "$PLATFORM" == "windows" ]]; then
    cmd //c start "" "$DEST" >/dev/null 2>&1
    echo "Deployed and relaunched. (Debug logging enabled via ~/.imrdy/.dev-build)"
else
    echo "Deployed $DEST ($RID). Hook ready. (Debug logging enabled via ~/.imrdy/.dev-build)"
fi

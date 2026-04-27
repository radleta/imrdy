#!/usr/bin/env bash
# Build, deploy to ~/.local/bin/, and restart the tray app.
# Usage: ./build-dev.sh
set -euo pipefail

DEST="$HOME/.local/bin/imrdy.exe"

# 1. Publish to the normal output dir (no lock contention)
dotnet publish src/Imrdy.Windows/Imrdy.Windows.csproj -c Release

# 2. Find the publish output (avoids hardcoding the TFM).
#    Pick the newest match — stale TFM dirs from prior builds can otherwise win
#    the alphabetical race (e.g. net10.0-windows vs net10.0-windows10.0.17763.0).
PUBLISH_EXE=$(find src/Imrdy.Windows/bin/Release -path '*/win-x64/publish/imrdy.exe' -type f -printf '%T@ %p\n' \
    | sort -nr | head -1 | cut -d' ' -f2-)
if [[ -z "$PUBLISH_EXE" ]]; then
    echo "ERROR: published imrdy.exe not found" >&2
    exit 1
fi

# 3. Stop gracefully, then force-kill any stragglers (hook respawns)
"$DEST" stop 2>/dev/null || true
taskkill //IM imrdy.exe //F > /dev/null 2>&1 || true

# 4. Rename the old binary out of the way (works even if briefly locked),
#    then copy the new one in. Hooks that fire during the gap fail harmlessly.
mkdir -p "$(dirname "$DEST")"
mv "$DEST" "$DEST.old" 2>/dev/null || true
cp "$PUBLISH_EXE" "$DEST"
rm -f "$DEST.old"

# 5. Drop a dev-build marker so the tray defaults to Debug logging.
#    ServiceRegistration.AddSerilog reads ~/.imrdy/.dev-build to flip the level
#    without requiring IMRDY_LOG=1 in every shell that triggers a hook.
#    The file body contains the repo root so the tray's Manage → Dev menu
#    can enumerate fixtures from tests/fixtures/dashboards/.
#    Remove the file (rm ~/.imrdy/.dev-build) to test prod-like log levels.
mkdir -p "$HOME/.imrdy"
# `pwd -W` yields the Windows-form path (D:/…) that .NET understands.
# Plain $PWD is MSYS form (/d/…) which fails Directory.Exists on the tray side.
REPO_WIN=$(pwd -W 2>/dev/null || pwd)
printf '%s\n' "$REPO_WIN" > "$HOME/.imrdy/.dev-build"

# 6. Spawn the tray immediately so we don't wait for the next Claude hook event
#    to respawn it. `cmd //c start` detaches the process from this shell's tree,
#    so the tray survives after build-dev.sh exits and is not tied to the Claude
#    process that invoked the script.
cmd //c start "" "$DEST" >/dev/null 2>&1

echo "Deployed and relaunched. (Debug logging enabled via ~/.imrdy/.dev-build)"

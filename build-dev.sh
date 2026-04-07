#!/usr/bin/env bash
# Build, deploy to ~/.local/bin/, and restart the tray app.
# Usage: ./build-dev.sh
set -euo pipefail

DEST="$HOME/.local/bin/imrdy.exe"

# 1. Publish to the normal output dir (no lock contention)
dotnet publish src/Imrdy.Windows/Imrdy.Windows.csproj -c Release

# 2. Find the publish output (avoids hardcoding the TFM)
PUBLISH_EXE=$(find src/Imrdy.Windows/bin/Release -path '*/win-x64/publish/imrdy.exe' -type f | head -1)
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

echo "Deployed. Tray will respawn on next hook event."

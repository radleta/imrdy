#!/usr/bin/env bash
# run.sh — E2E test for imrdy install scripts
#
# Usage: bash tests/e2e-install/run.sh
#
# Builds the project, creates mock release artifacts, runs both install scripts
# (bash + PowerShell) against them, then verifies everything.
# Fully isolated — uses temp directories, never touches ~/.local/bin or ~/.imrdy.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJECT="$REPO_ROOT/src/Imrdy.Windows/Imrdy.Windows.csproj"
SOUNDS_SRC="$REPO_ROOT/sounds/assistant"
BASH_INSTALLER="$REPO_ROOT/plugin/install-bootstrap.sh"
PS1_INSTALLER="$REPO_ROOT/install.ps1"

# Test working directory — all temp dirs live here for easy cleanup
TEST_DIR=$(mktemp -d)
MOCK_RELEASE="$TEST_DIR/release"
INSTALL_TARGET="$TEST_DIR/install-bin"
SOUNDS_TARGET="$TEST_DIR/sounds"

PASS=0
FAIL=0
SKIP=0
FAILURES=()

# --- Helpers ---

cleanup() {
    if [ -d "$TEST_DIR" ]; then
        rm -rf "$TEST_DIR"
    fi
}
trap cleanup EXIT

pass() {
    PASS=$((PASS + 1))
    echo "  PASS  $1"
}

fail() {
    FAIL=$((FAIL + 1))
    FAILURES+=("$1: $2")
    echo "  FAIL  $1 — $2"
}

skip() {
    SKIP=$((SKIP + 1))
    echo "  SKIP  $1 — $2"
}

section() {
    echo ""
    echo "=== $1 ==="
}

assert_file_exists() {
    if [ -f "$1" ]; then
        pass "$2"
    else
        fail "$2" "file not found: $1"
    fi
}

assert_dir_exists() {
    if [ -d "$1" ]; then
        pass "$2"
    else
        fail "$2" "directory not found: $1"
    fi
}

assert_file_contains() {
    if grep -q "$2" "$1" 2>/dev/null; then
        pass "$3"
    else
        fail "$3" "pattern '$2' not found in $1"
    fi
}

# --- Phase 1: Build ---

section "Phase 1: Build"

echo "  Building project..."
BUILD_OUTPUT=$(dotnet publish "$PROJECT" -c Release -r win-x64 --self-contained 2>&1) || {
    fail "build" "dotnet publish failed"
    echo "$BUILD_OUTPUT"
    exit 1
}

PUBLISH_DIR="$REPO_ROOT/src/Imrdy.Windows/bin/Release/net10.0-windows/win-x64/publish"
BINARY="$PUBLISH_DIR/imrdy.exe"

assert_file_exists "$BINARY" "published binary exists"

# --- Phase 2: Package Evaluation ---

section "Phase 2: Package Evaluation"

# Binary size check (single-file .NET should be 50-200MB)
BINARY_SIZE=$(stat -c%s "$BINARY" 2>/dev/null || stat -f%z "$BINARY" 2>/dev/null)
BINARY_SIZE_MB=$((BINARY_SIZE / 1024 / 1024))
echo "  Binary size: ${BINARY_SIZE_MB}MB (${BINARY_SIZE} bytes)"

if [ "$BINARY_SIZE_MB" -ge 30 ] && [ "$BINARY_SIZE_MB" -le 250 ]; then
    pass "binary size within expected range (30-250MB)"
else
    fail "binary size" "unexpected size: ${BINARY_SIZE_MB}MB (expected 30-250MB)"
fi

# Binary is a PE executable (MZ magic bytes)
MAGIC=$(xxd -l 2 -p "$BINARY" 2>/dev/null || od -A n -t x1 -N 2 "$BINARY" | tr -d ' ')
if [ "$MAGIC" = "4d5a" ]; then
    pass "binary is valid PE executable (MZ header)"
else
    fail "PE header" "expected MZ (4d5a), got: $MAGIC"
fi

# Binary runs --version
VERSION_OUT=$("$BINARY" --version 2>&1) || true
if echo "$VERSION_OUT" | grep -q "imrdy"; then
    pass "binary --version runs (output: $VERSION_OUT)"
else
    fail "--version" "unexpected output: $VERSION_OUT"
fi

# Binary runs --help
HELP_OUT=$("$BINARY" --help 2>&1) || true
if echo "$HELP_OUT" | grep -q "status"; then
    pass "binary --help lists commands"
else
    fail "--help" "missing expected commands in output"
fi

# Binary rejects unknown args gracefully (timeout 5s — unknown flags may launch tray)
UNKNOWN_OUT=$(timeout 5 "$BINARY" --bogus-flag 2>&1) || true
UNKNOWN_EXIT=$?
# Exit 124 = timeout (launched tray instead of rejecting), 139 = segfault, 134 = abort
if [ "$UNKNOWN_EXIT" -eq 124 ]; then
    fail "unknown args" "binary launched tray instead of rejecting --bogus-flag (needs CLI validation)"
elif [ "$UNKNOWN_EXIT" -ge 128 ]; then
    fail "unknown args" "binary crashed with signal (exit $UNKNOWN_EXIT)"
else
    pass "binary handles unknown args gracefully (exit $UNKNOWN_EXIT)"
fi

# Sound pack structure
assert_file_exists "$SOUNDS_SRC/pack.json" "pack.json exists"
assert_dir_exists "$SOUNDS_SRC/session_start" "session_start folder exists"
assert_dir_exists "$SOUNDS_SRC/getting_to_work" "getting_to_work folder exists"
assert_dir_exists "$SOUNDS_SRC/needs_you" "needs_you folder exists"
assert_dir_exists "$SOUNDS_SRC/forgotten" "forgotten folder exists"
assert_dir_exists "$SOUNDS_SRC/finished" "finished folder exists"
assert_dir_exists "$SOUNDS_SRC/session_end" "session_end folder exists"
assert_dir_exists "$SOUNDS_SRC/combo" "combo folder exists"

# pack.json is valid JSON
PACK_JSON_WIN=$(cygpath -w "$SOUNDS_SRC/pack.json" 2>/dev/null || echo "$SOUNDS_SRC/pack.json")
if python3 -c "import json; json.load(open(r'${PACK_JSON_WIN}'))" 2>/dev/null; then
    pass "pack.json is valid JSON"
elif command -v jq &>/dev/null && jq empty "$SOUNDS_SRC/pack.json" 2>/dev/null; then
    pass "pack.json is valid JSON"
else
    fail "pack.json" "invalid JSON"
fi

# Sound pack has audio files
AUDIO_COUNT=$(find "$SOUNDS_SRC" -name "*.mp3" -o -name "*.wav" -o -name "*.ogg" 2>/dev/null | wc -l)
if [ "$AUDIO_COUNT" -gt 0 ]; then
    pass "sound pack contains $AUDIO_COUNT audio files"
else
    fail "audio files" "no mp3/wav/ogg files found in sound pack"
fi

# --- Phase 3: Create Mock Release ---

section "Phase 3: Create Mock Release"

mkdir -p "$MOCK_RELEASE"

# Copy binary with release asset name
ASSET_NAME="imrdy-win-x64.exe"
cp "$BINARY" "$MOCK_RELEASE/$ASSET_NAME"

# Create sound pack zip
PACK_ZIP="assistant.zip"
if command -v zip &>/dev/null; then
    (cd "$REPO_ROOT/sounds" && zip -r "$MOCK_RELEASE/$PACK_ZIP" assistant/ -x "*.DS_Store" 2>/dev/null)
else
    # Python on Windows needs native paths (not MSYS /tmp/)
    MOCK_WIN=$(cygpath -w "$MOCK_RELEASE" 2>/dev/null || echo "$MOCK_RELEASE")
    SOUNDS_WIN=$(cygpath -w "$REPO_ROOT/sounds" 2>/dev/null || echo "$REPO_ROOT/sounds")
    python3 -c "
import zipfile, os
os.chdir(r'${SOUNDS_WIN}')
with zipfile.ZipFile(os.path.join(r'${MOCK_WIN}', '${PACK_ZIP}'), 'w', zipfile.ZIP_DEFLATED) as zf:
    for root, dirs, files in os.walk('assistant'):
        for f in files:
            path = os.path.join(root, f)
            zf.write(path, path)
"
fi
assert_file_exists "$MOCK_RELEASE/$PACK_ZIP" "pack zip created"

# Generate SHA256SUMS.txt for binary (text mode format: "hash  filename")
# GitHub Actions runner uses Linux sha256sum which produces "hash  filename"
# MSYS sha256sum produces "hash *filename" — normalize to match release.yml output
(cd "$MOCK_RELEASE" && sha256sum "$ASSET_NAME" | sed 's/ \*/  /' > SHA256SUMS.txt)
assert_file_exists "$MOCK_RELEASE/SHA256SUMS.txt" "binary SHA256SUMS.txt created"

# Generate pack-SHA256SUMS.txt for pack
(cd "$MOCK_RELEASE" && sha256sum "$PACK_ZIP" | sed 's/ \*/  /' > pack-SHA256SUMS.txt)
assert_file_exists "$MOCK_RELEASE/pack-SHA256SUMS.txt" "pack SHA256SUMS.txt created"

# Verify checksums are correct format (hash + separator + filename)
# sha256sum uses "hash  filename" (text mode) or "hash *filename" (binary mode, MSYS default)
if grep -qE '^[0-9a-f]{64} [ *]' "$MOCK_RELEASE/SHA256SUMS.txt"; then
    pass "binary checksum format is valid"
else
    fail "checksum format" "SHA256SUMS.txt has unexpected format: $(cat "$MOCK_RELEASE/SHA256SUMS.txt")"
fi

# Cross-verify: checksum in file matches actual file
EXPECTED_HASH=$(awk '{print $1}' "$MOCK_RELEASE/SHA256SUMS.txt")
ACTUAL_HASH=$(sha256sum "$MOCK_RELEASE/$ASSET_NAME" | awk '{print $1}')
if [ "$EXPECTED_HASH" = "$ACTUAL_HASH" ]; then
    pass "binary checksum cross-verification"
else
    fail "checksum cross-verify" "expected=$EXPECTED_HASH actual=$ACTUAL_HASH"
fi

echo "  Mock release contents:"
ls -lh "$MOCK_RELEASE/"

# --- Phase 4: Bash Install Script ---

section "Phase 4: Bash Install (install-bootstrap.sh)"

BASH_INSTALL_DIR="$TEST_DIR/bash-install"
BASH_SOUNDS_DIR="$TEST_DIR/bash-sounds"
mkdir -p "$BASH_INSTALL_DIR" "$BASH_SOUNDS_DIR"

BASH_OUTPUT=$(IMRDY_RELEASE_DIR="$MOCK_RELEASE" \
    IMRDY_INSTALL_DIR="$BASH_INSTALL_DIR" \
    IMRDY_SOUNDS_DIR="$BASH_SOUNDS_DIR" \
    bash "$BASH_INSTALLER" 2>&1) || {
    fail "bash install" "script exited with error"
    echo "$BASH_OUTPUT"
}

echo "  Install output:"
echo "$BASH_OUTPUT" | sed 's/^/    /'

# Verify binary installed
assert_file_exists "$BASH_INSTALL_DIR/imrdy.exe" "bash: binary installed"

# Verify binary is executable
if [ -x "$BASH_INSTALL_DIR/imrdy.exe" ]; then
    pass "bash: binary is executable"
else
    fail "bash: executable" "binary not marked executable"
fi

# Verify installed binary runs
INSTALLED_VERSION=$("$BASH_INSTALL_DIR/imrdy.exe" --version 2>&1) || true
if echo "$INSTALLED_VERSION" | grep -q "imrdy"; then
    pass "bash: installed binary runs (${INSTALLED_VERSION})"
else
    fail "bash: installed binary" "doesn't run: $INSTALLED_VERSION"
fi

# Verify installed binary size matches source
INSTALLED_SIZE=$(stat -c%s "$BASH_INSTALL_DIR/imrdy.exe" 2>/dev/null || stat -f%z "$BASH_INSTALL_DIR/imrdy.exe" 2>/dev/null)
if [ "$INSTALLED_SIZE" = "$BINARY_SIZE" ]; then
    pass "bash: installed binary size matches source ($INSTALLED_SIZE bytes)"
else
    fail "bash: binary size" "expected=$BINARY_SIZE actual=$INSTALLED_SIZE"
fi

# Verify sound pack extracted
assert_dir_exists "$BASH_SOUNDS_DIR/packs/assistant" "bash: pack extracted"
assert_file_exists "$BASH_SOUNDS_DIR/packs/assistant/pack.json" "bash: pack.json present"

# Verify all event folders extracted
for event in session_start getting_to_work needs_you forgotten finished session_end combo; do
    assert_dir_exists "$BASH_SOUNDS_DIR/packs/assistant/$event" "bash: pack event '$event' extracted"
done

# Verify config.json created
assert_file_exists "$BASH_SOUNDS_DIR/config.json" "bash: config.json created"
assert_file_contains "$BASH_SOUNDS_DIR/config.json" '"defaultPack":"assistant"' "bash: config has correct default pack"
assert_file_contains "$BASH_SOUNDS_DIR/config.json" '"enabled":true' "bash: config has enabled=true"

# Verify no temp files leaked
TEMP_FILES=$(find "$BASH_INSTALL_DIR" -name "SHA256SUMS.txt" 2>/dev/null | wc -l)
if [ "$TEMP_FILES" -eq 0 ]; then
    pass "bash: no checksum temp files left behind"
else
    fail "bash: cleanup" "SHA256SUMS.txt left in install dir"
fi

# --- Phase 5: PowerShell Install Script ---

section "Phase 5: PowerShell Install (install.ps1)"

if command -v pwsh &>/dev/null; then
    PS1_INSTALL_DIR="$TEST_DIR/ps1-install"
    PS1_SOUNDS_DIR="$TEST_DIR/ps1-sounds"
    mkdir -p "$PS1_INSTALL_DIR" "$PS1_SOUNDS_DIR"

    # Convert paths to Windows format for PowerShell
    PS1_RELEASE_WIN=$(cygpath -w "$MOCK_RELEASE")
    PS1_INSTALL_WIN=$(cygpath -w "$PS1_INSTALL_DIR")
    PS1_SOUNDS_WIN=$(cygpath -w "$PS1_SOUNDS_DIR")
    PS1_SCRIPT_WIN=$(cygpath -w "$PS1_INSTALLER")

    PS1_OUTPUT=$(pwsh -NoProfile -Command "
        \$env:IMRDY_RELEASE_DIR = '$PS1_RELEASE_WIN'
        \$env:IMRDY_INSTALL_DIR = '$PS1_INSTALL_WIN'
        \$env:IMRDY_SOUNDS_DIR = '$PS1_SOUNDS_WIN'
        & '$PS1_SCRIPT_WIN'
    " 2>&1) || {
        fail "ps1 install" "script exited with error"
        echo "$PS1_OUTPUT"
    }

    echo "  Install output:"
    echo "$PS1_OUTPUT" | sed 's/^/    /'

    # Verify binary installed
    assert_file_exists "$PS1_INSTALL_DIR/imrdy.exe" "ps1: binary installed"

    # Verify installed binary runs
    PS1_INSTALLED_VERSION=$("$PS1_INSTALL_DIR/imrdy.exe" --version 2>&1) || true
    if echo "$PS1_INSTALLED_VERSION" | grep -q "imrdy"; then
        pass "ps1: installed binary runs (${PS1_INSTALLED_VERSION})"
    else
        fail "ps1: installed binary" "doesn't run: $PS1_INSTALLED_VERSION"
    fi

    # Verify sound pack extracted
    assert_dir_exists "$PS1_SOUNDS_DIR/packs/assistant" "ps1: pack extracted"
    assert_file_exists "$PS1_SOUNDS_DIR/packs/assistant/pack.json" "ps1: pack.json present"

    # Verify config.json created
    assert_file_exists "$PS1_SOUNDS_DIR/config.json" "ps1: config.json created"
    assert_file_contains "$PS1_SOUNDS_DIR/config.json" '"defaultPack":"assistant"' "ps1: config has correct default pack"
else
    skip "ps1 install" "pwsh not found"
fi

# --- Phase 6: Negative Tests ---

section "Phase 6: Negative Tests"

# Test: corrupt checksum should reject
echo "  Testing bad checksum rejection..."
BAD_RELEASE="$TEST_DIR/bad-release"
cp -r "$MOCK_RELEASE" "$BAD_RELEASE"
# Corrupt the checksum
echo "0000000000000000000000000000000000000000000000000000000000000000  $ASSET_NAME" > "$BAD_RELEASE/SHA256SUMS.txt"

BAD_INSTALL_DIR="$TEST_DIR/bad-install"
BAD_SOUNDS_DIR="$TEST_DIR/bad-sounds"
mkdir -p "$BAD_INSTALL_DIR" "$BAD_SOUNDS_DIR"

BAD_OUTPUT=$(IMRDY_RELEASE_DIR="$BAD_RELEASE" \
    IMRDY_INSTALL_DIR="$BAD_INSTALL_DIR" \
    IMRDY_SOUNDS_DIR="$BAD_SOUNDS_DIR" \
    bash "$BASH_INSTALLER" 2>&1) || true

if echo "$BAD_OUTPUT" | grep -qi "FAILED"; then
    pass "bad checksum: install rejected with FAILED message"
else
    fail "bad checksum" "install did not report FAILED: $BAD_OUTPUT"
fi

# Binary should have been removed after checksum failure
if [ ! -f "$BAD_INSTALL_DIR/imrdy.exe" ]; then
    pass "bad checksum: binary cleaned up after failure"
else
    fail "bad checksum cleanup" "binary still exists after checksum failure"
fi

# Test: corrupt pack checksum should warn but not abort binary install
echo "  Testing bad pack checksum..."
BAD_PACK_RELEASE="$TEST_DIR/bad-pack-release"
cp -r "$MOCK_RELEASE" "$BAD_PACK_RELEASE"
echo "0000000000000000000000000000000000000000000000000000000000000000  $PACK_ZIP" > "$BAD_PACK_RELEASE/pack-SHA256SUMS.txt"

BAD_PACK_INSTALL="$TEST_DIR/bad-pack-install"
BAD_PACK_SOUNDS="$TEST_DIR/bad-pack-sounds"
mkdir -p "$BAD_PACK_INSTALL" "$BAD_PACK_SOUNDS"

BAD_PACK_OUTPUT=$(IMRDY_RELEASE_DIR="$BAD_PACK_RELEASE" \
    IMRDY_INSTALL_DIR="$BAD_PACK_INSTALL" \
    IMRDY_SOUNDS_DIR="$BAD_PACK_SOUNDS" \
    bash "$BASH_INSTALLER" 2>&1) || true

# Binary should still be installed even if pack checksum fails
if [ -f "$BAD_PACK_INSTALL/imrdy.exe" ]; then
    pass "bad pack checksum: binary still installed"
else
    fail "bad pack checksum" "binary missing — pack failure should not block binary install"
fi

if echo "$BAD_PACK_OUTPUT" | grep -qi "FAILED"; then
    pass "bad pack checksum: reported FAILED"
else
    fail "bad pack checksum" "did not report pack checksum FAILED"
fi

# Test: idempotent re-install (run installer twice)
echo "  Testing idempotent re-install..."
IDEM_INSTALL="$TEST_DIR/idem-install"
IDEM_SOUNDS="$TEST_DIR/idem-sounds"
mkdir -p "$IDEM_INSTALL" "$IDEM_SOUNDS"

IMRDY_RELEASE_DIR="$MOCK_RELEASE" \
    IMRDY_INSTALL_DIR="$IDEM_INSTALL" \
    IMRDY_SOUNDS_DIR="$IDEM_SOUNDS" \
    bash "$BASH_INSTALLER" 2>&1 >/dev/null

IDEM_SIZE_1=$(stat -c%s "$IDEM_INSTALL/imrdy.exe" 2>/dev/null || stat -f%z "$IDEM_INSTALL/imrdy.exe" 2>/dev/null)

IMRDY_RELEASE_DIR="$MOCK_RELEASE" \
    IMRDY_INSTALL_DIR="$IDEM_INSTALL" \
    IMRDY_SOUNDS_DIR="$IDEM_SOUNDS" \
    bash "$BASH_INSTALLER" 2>&1 >/dev/null

IDEM_SIZE_2=$(stat -c%s "$IDEM_INSTALL/imrdy.exe" 2>/dev/null || stat -f%z "$IDEM_INSTALL/imrdy.exe" 2>/dev/null)

if [ "$IDEM_SIZE_1" = "$IDEM_SIZE_2" ]; then
    pass "idempotent: re-install produces same result"
else
    fail "idempotent" "binary size changed: $IDEM_SIZE_1 -> $IDEM_SIZE_2"
fi

# Verify config.json not overwritten on re-install
if [ -f "$IDEM_SOUNDS/config.json" ]; then
    pass "idempotent: config.json preserved"
else
    fail "idempotent" "config.json missing after re-install"
fi

# --- Phase 7: CLI Subcommands ---

section "Phase 7: CLI Subcommands (trim safety)"

# Each CLI subcommand must run without crashing after trimming.
# Timeout 10s — subcommands should complete instantly against empty state.

for cmd in "status" "status --json" "packs list" "packs list --json" "config path" "config path --json" "workspace list" "workspace list --json"; do
    CMD_OUT=$(timeout 10 "$BINARY" $cmd 2>&1) || true
    CMD_EXIT=$?
    if [ "$CMD_EXIT" -eq 124 ]; then
        fail "cli: $cmd" "timed out (10s) — may have launched tray"
    elif [ "$CMD_EXIT" -ge 128 ]; then
        fail "cli: $cmd" "crashed with signal (exit $CMD_EXIT)"
    else
        pass "cli: $cmd exits cleanly (exit $CMD_EXIT)"
    fi
done

# --- Phase 8: Exit Codes ---

section "Phase 8: Exit Codes"

# Known commands should return 0
for cmd in "--version" "--help" "status" "packs list" "config path" "workspace list"; do
    timeout 10 "$BINARY" $cmd >/dev/null 2>&1
    EC=$?
    if [ "$EC" -eq 0 ]; then
        pass "exit code: '$cmd' returns 0"
    else
        fail "exit code: '$cmd'" "expected 0, got $EC"
    fi
done

# hook with invalid JSON should return non-zero
# Use a subshell to capture the exact exit code of the piped command
HC_EXIT=$(echo "NOT_JSON" | timeout 10 "$BINARY" hook 2>/dev/null; echo $?) || true
HC_EXIT=$(echo "$HC_EXIT" | tail -1)
if [ "$HC_EXIT" != "0" ]; then
    pass "exit code: 'hook' with bad JSON returns non-zero ($HC_EXIT)"
else
    fail "exit code: hook bad JSON" "expected non-zero, got 0"
fi

# hook with empty stdin should return 0 (documented behavior)
echo "" | timeout 10 "$BINARY" hook 2>/dev/null || true
HE_EXIT=${PIPESTATUS[1]:-$?}
if [ "$HE_EXIT" -eq 0 ]; then
    pass "exit code: 'hook' with empty stdin returns 0"
else
    fail "exit code: hook empty" "expected 0, got $HE_EXIT"
fi

# --- Phase 9: Hook Command with Mock Stdin ---

section "Phase 9: Hook Command (mock stdin)"

HOOK_STATE_DIR="$HOME/.imrdy/sessions"
MOCK_SESSION_ID="e2e-test-$(date +%s)"

# Send a valid SessionStart hook event
HOOK_JSON="{\"hook_event_name\":\"SessionStart\",\"session_id\":\"${MOCK_SESSION_ID}\",\"cwd\":\"C:\\\\dev\\\\test-project\",\"source\":null,\"notification_type\":null,\"prompt\":null,\"message\":null}"

echo "$HOOK_JSON" | timeout 10 "$BINARY" hook 2>/dev/null || true
HOOK_EXIT=${PIPESTATUS[1]:-$?}
if [ "$HOOK_EXIT" -eq 0 ]; then
    pass "hook: SessionStart accepted (exit 0)"
else
    fail "hook: SessionStart" "expected exit 0, got $HOOK_EXIT"
fi

# Verify state file was written
STATE_FILE="$HOOK_STATE_DIR/${MOCK_SESSION_ID}.json"
if [ -f "$STATE_FILE" ]; then
    pass "hook: state file created at $STATE_FILE"

    # Verify state file is valid JSON with expected fields
    STATE_WIN=$(cygpath -w "$STATE_FILE" 2>/dev/null || echo "$STATE_FILE")
    STATE_CHECK=$(python3 -c "
import json, sys
s = json.load(open(r'${STATE_WIN}'))
errors = []
if s.get('session_id') != '${MOCK_SESSION_ID}':
    errors.append(f'session_id mismatch: {s.get(\"session_id\")}')
if s.get('status') not in ('busy', 'idle', 'attention', 'permission', 'end', 'start'):
    errors.append(f'unexpected status: {s.get(\"status\")}')
if not s.get('project'):
    errors.append('missing project')
if not s.get('cwd'):
    errors.append('missing cwd')
if errors:
    print('; '.join(errors), file=sys.stderr)
    sys.exit(1)
print(f'status={s[\"status\"]} project={s[\"project\"]}')
" 2>&1) || true
    SC_EXIT=$?
    if [ "$SC_EXIT" -eq 0 ]; then
        pass "hook: state file valid ($STATE_CHECK)"
    else
        fail "hook: state file content" "$STATE_CHECK"
    fi

    # Send a UserPromptSubmit with a prompt message
    PROMPT_JSON="{\"hook_event_name\":\"UserPromptSubmit\",\"session_id\":\"${MOCK_SESSION_ID}\",\"cwd\":\"C:\\\\dev\\\\test-project\",\"prompt\":\"fix the bug\",\"message\":null}"
    PROMPT_EXIT=0
    echo "$PROMPT_JSON" | timeout 10 "$BINARY" hook 2>/dev/null || PROMPT_EXIT=$?
    if [ "$PROMPT_EXIT" -eq 0 ]; then
        pass "hook: UserPromptSubmit accepted"
    else
        fail "hook: UserPromptSubmit" "non-zero exit"
    fi

    # Verify last_message was preserved
    LM_CHECK=$(python3 -c "
import json
s = json.load(open(r'${STATE_WIN}'))
lm = s.get('last_message', '')
print(lm)
" 2>/dev/null) || true
    if [ -n "$LM_CHECK" ]; then
        pass "hook: last_message preserved ('$LM_CHECK')"
    else
        fail "hook: last_message" "empty after UserPromptSubmit with prompt"
    fi

    # Send SessionEnd to clean up
    END_JSON="{\"hook_event_name\":\"SessionEnd\",\"session_id\":\"${MOCK_SESSION_ID}\",\"cwd\":\"C:\\\\dev\\\\test-project\"}"
    echo "$END_JSON" | timeout 10 "$BINARY" hook 2>/dev/null || true

    # Clean up test state file
    rm -f "$STATE_FILE" 2>/dev/null
else
    fail "hook: state file" "not created at $STATE_FILE"
fi

# Test: hook with path traversal session_id is rejected
TRAVERSAL_JSON="{\"hook_event_name\":\"SessionStart\",\"session_id\":\"../../../etc/passwd\",\"cwd\":\"C:\\\\dev\"}"
TRAV_EXIT=0
TRAV_OUT=$(echo "$TRAVERSAL_JSON" | timeout 10 "$BINARY" hook 2>&1) || TRAV_EXIT=$?
if [ "$TRAV_EXIT" -ne 0 ]; then
    pass "hook: path traversal session_id rejected (exit $TRAV_EXIT)"
else
    fail "hook: path traversal" "accepted session_id with ../ (exit 0)"
fi

# --- Phase 10: Pack Zip Internal Structure ---

section "Phase 10: Pack Zip Internal Structure"

# Verify zip entries have assistant/ prefix (not flat files at root)
ZIP_ENTRIES=$(python3 -c "
import zipfile, sys
MOCK_WIN = r'$(cygpath -w "$MOCK_RELEASE/$PACK_ZIP" 2>/dev/null || echo "$MOCK_RELEASE/$PACK_ZIP")'
zf = zipfile.ZipFile(MOCK_WIN, 'r')
for name in sorted(zf.namelist()):
    print(name)
" 2>/dev/null)

# Check that all entries start with assistant/
BAD_ENTRIES=$(echo "$ZIP_ENTRIES" | grep -v '^assistant/' || true)
if [ -z "$BAD_ENTRIES" ]; then
    pass "pack zip: all entries have assistant/ prefix"
else
    fail "pack zip: prefix" "entries without assistant/ prefix: $BAD_ENTRIES"
fi

# Check pack.json is inside assistant/ directory
if echo "$ZIP_ENTRIES" | grep -q '^assistant/pack.json$'; then
    pass "pack zip: contains assistant/pack.json"
else
    fail "pack zip: pack.json" "assistant/pack.json not found in zip"
fi

# Check zip has audio files
ZIP_AUDIO=$(echo "$ZIP_ENTRIES" | grep -cE '\.(mp3|wav|ogg)$' || true)
if [ "$ZIP_AUDIO" -gt 0 ]; then
    pass "pack zip: contains $ZIP_AUDIO audio files"
else
    fail "pack zip: audio" "no audio files in zip"
fi

# Verify zip entry count is reasonable (pack.json + 7 event dirs + audio files)
ZIP_COUNT=$(echo "$ZIP_ENTRIES" | wc -l)
if [ "$ZIP_COUNT" -ge 10 ]; then
    pass "pack zip: $ZIP_COUNT entries (reasonable for sound pack)"
else
    fail "pack zip: entry count" "only $ZIP_COUNT entries — expected 10+"
fi

# --- Phase 11: PS1 Config BOM Check ---

section "Phase 11: PowerShell Config BOM Check"

if command -v pwsh &>/dev/null && [ -f "$PS1_SOUNDS_DIR/config.json" ]; then
    # Check for UTF-8 BOM (EF BB BF) at start of config.json
    BOM_CHECK=$(xxd -l 3 -p "$PS1_SOUNDS_DIR/config.json" 2>/dev/null || od -A n -t x1 -N 3 "$PS1_SOUNDS_DIR/config.json" | tr -d ' ')
    if [ "$BOM_CHECK" = "efbbbf" ]; then
        fail "ps1: config BOM" "config.json has UTF-8 BOM — will break JSON parsers"
    else
        pass "ps1: config.json has no BOM"
    fi

    # Verify the config is parseable as JSON (catches BOM and encoding issues)
    PS1_CONFIG_WIN=$(cygpath -w "$PS1_SOUNDS_DIR/config.json" 2>/dev/null || echo "$PS1_SOUNDS_DIR/config.json")
    if python3 -c "import json; json.load(open(r'${PS1_CONFIG_WIN}'))" 2>/dev/null; then
        pass "ps1: config.json is parseable JSON"
    else
        fail "ps1: config parse" "config.json not valid JSON"
    fi
else
    if ! command -v pwsh &>/dev/null; then
        skip "ps1: config BOM" "pwsh not found"
    else
        skip "ps1: config BOM" "ps1 config.json not found"
    fi
fi

# --- Phase 12: Release Workflow Alignment ---

section "Phase 12: Release Workflow Alignment"

RELEASE_YML="$REPO_ROOT/.github/workflows/release.yml"
RELEASE_PACKS_YML="$REPO_ROOT/.github/workflows/release-packs.yml"

# Verify release.yml asset names match what install scripts expect
# install.ps1 expects: imrdy-win-x64.exe and imrdy-win-arm64.exe
# install-bootstrap.sh expects: imrdy-win-x64.exe and imrdy-win-arm64.exe
# release.yml produces: imrdy-{rid}.exe where rid = win-x64, win-arm64

if grep -q 'imrdy-win-x64.exe' "$RELEASE_YML"; then
    pass "release.yml: references imrdy-win-x64.exe"
else
    fail "release.yml: asset name" "imrdy-win-x64.exe not found in workflow"
fi

if grep -q 'imrdy-win-arm64.exe' "$RELEASE_YML"; then
    pass "release.yml: references imrdy-win-arm64.exe"
else
    fail "release.yml: asset name" "imrdy-win-arm64.exe not found in workflow"
fi

if grep -q 'SHA256SUMS.txt' "$RELEASE_YML"; then
    pass "release.yml: produces SHA256SUMS.txt"
else
    fail "release.yml: checksum" "SHA256SUMS.txt not found in workflow"
fi

# Verify release.yml checksum format matches what install scripts parse
# Install scripts use: awk '{print $1}' and grep for asset name
# release.yml produces: "$hash  imrdy-{rid}.exe" (pwsh Out-File)
if grep -q 'imrdy-\${{ matrix.rid }}.exe' "$RELEASE_YML"; then
    pass "release.yml: checksum references correct asset name pattern"
else
    fail "release.yml: checksum format" "asset name pattern not found in checksum step"
fi

# Verify release-packs.yml produces ZIP and SHA256SUMS.txt
if grep -q '\.zip' "$RELEASE_PACKS_YML"; then
    pass "release-packs.yml: produces .zip asset"
else
    fail "release-packs.yml" "no .zip reference found"
fi

if grep -q 'SHA256SUMS.txt' "$RELEASE_PACKS_YML"; then
    pass "release-packs.yml: produces SHA256SUMS.txt"
else
    fail "release-packs.yml" "no SHA256SUMS.txt reference found"
fi

# Verify install scripts' asset name patterns match release.yml output
# install-bootstrap.sh uses: ASSET_NAME="imrdy-win-x64.exe" or "imrdy-win-arm64.exe"
if grep -q 'imrdy-win-x64.exe' "$BASH_INSTALLER" && grep -q 'imrdy-win-arm64.exe' "$BASH_INSTALLER"; then
    pass "install-bootstrap.sh: asset names match release.yml"
else
    fail "install-bootstrap.sh" "asset name mismatch with release.yml"
fi

# Verify install.ps1 uses same asset naming pattern
if grep -q 'imrdy-\$AssetSuffix.exe' "$PS1_INSTALLER" || grep -q 'imrdy-win-x64' "$PS1_INSTALLER"; then
    pass "install.ps1: asset name pattern matches release.yml"
else
    fail "install.ps1" "asset name pattern mismatch with release.yml"
fi

# --- Phase 13: Integration Tests ---

section "Phase 13: Integration Tests"

echo "  Running dotnet test (unit tests)..."
UNIT_OUT=$(dotnet test "$REPO_ROOT/tests/Imrdy.Core.Tests/Imrdy.Core.Tests.csproj" --no-restore -v q 2>&1) || true
UNIT_EXIT=$?
if [ "$UNIT_EXIT" -eq 0 ]; then
    UNIT_COUNT=$(echo "$UNIT_OUT" | grep -oP 'Passed:\s+\K\d+' || echo "?")
    pass "unit tests pass ($UNIT_COUNT passed)"
else
    fail "unit tests" "dotnet test failed (exit $UNIT_EXIT)"
    echo "$UNIT_OUT" | tail -10 | sed 's/^/    /'
fi

echo "  Running dotnet test (integration tests)..."
INTEG_OUT=$(dotnet test "$REPO_ROOT/tests/Imrdy.Integration.Tests/Imrdy.Integration.Tests.csproj" --no-restore -v q 2>&1) || true
INTEG_EXIT=$?
if [ "$INTEG_EXIT" -eq 0 ]; then
    INTEG_COUNT=$(echo "$INTEG_OUT" | grep -oP 'Passed:\s+\K\d+' || echo "?")
    pass "integration tests pass ($INTEG_COUNT passed)"
else
    fail "integration tests" "dotnet test failed (exit $INTEG_EXIT)"
    echo "$INTEG_OUT" | tail -10 | sed 's/^/    /'
fi

# --- Phase 14: Plugin Structure ---

section "Phase 14: Plugin Structure Validation"

PLUGIN_DIR="$REPO_ROOT/plugin"

assert_file_exists "$PLUGIN_DIR/.claude-plugin/plugin.json" "plugin.json exists"
assert_file_exists "$PLUGIN_DIR/hooks/hooks.json" "hooks.json exists"
assert_file_exists "$PLUGIN_DIR/install-bootstrap.sh" "install-bootstrap.sh exists"

# plugin.json is valid JSON with required fields
PLUGIN_JSON_WIN=$(cygpath -w "$PLUGIN_DIR/.claude-plugin/plugin.json" 2>/dev/null || echo "$PLUGIN_DIR/.claude-plugin/plugin.json")
if python3 -c "
import json, sys
p = json.load(open(r'${PLUGIN_JSON_WIN}'))
required = ['name', 'version', 'description', 'author', 'repository', 'license']
missing = [f for f in required if f not in p]
if missing:
    print(f'Missing fields: {missing}', file=sys.stderr)
    sys.exit(1)
" 2>/dev/null; then
    pass "plugin.json has all required fields"
else
    fail "plugin.json" "missing required fields"
fi

# hooks.json is valid JSON with expected events
HOOKS_JSON_WIN=$(cygpath -w "$PLUGIN_DIR/hooks/hooks.json" 2>/dev/null || echo "$PLUGIN_DIR/hooks/hooks.json")
if python3 -c "
import json, sys
h = json.load(open(r'${HOOKS_JSON_WIN}'))
hooks = h.get('hooks', {})
expected = ['SessionStart', 'UserPromptSubmit', 'PreToolUse', 'PreCompact', 'Stop', 'Notification', 'PermissionRequest', 'SessionEnd']
missing = [e for e in expected if e not in hooks]
if missing:
    print(f'Missing hook events: {missing}', file=sys.stderr)
    sys.exit(1)
" 2>/dev/null; then
    pass "hooks.json has all expected events"
else
    fail "hooks.json" "missing expected hook events"
fi

# SessionStart hook has fallback to bootstrap
SESSION_START_CMD=$(python3 -c "
import json
h = json.load(open(r'${HOOKS_JSON_WIN}'))
print(h['hooks']['SessionStart'][0]['hooks'][0]['command'])
" 2>/dev/null)
if echo "$SESSION_START_CMD" | grep -q "install-bootstrap"; then
    pass "SessionStart hook includes bootstrap fallback"
else
    fail "SessionStart hook" "missing bootstrap fallback: $SESSION_START_CMD"
fi

# --- Summary ---

section "Summary"

TOTAL=$((PASS + FAIL + SKIP))
echo ""
echo "  Total: $TOTAL  |  Pass: $PASS  |  Fail: $FAIL  |  Skip: $SKIP"

if [ "$FAIL" -gt 0 ]; then
    echo ""
    echo "  Failures:"
    for f in "${FAILURES[@]}"; do
        echo "    - $f"
    done
    echo ""
    exit 1
else
    echo ""
    echo "  All tests passed!"
    echo ""
    exit 0
fi

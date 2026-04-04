#!/usr/bin/env bash
# install-bootstrap.sh — Downloads imrdy from GitHub Releases and re-runs the hook.
# Called by SessionStart hook when imrdy is not yet installed.
#
# Environment overrides (for testing):
#   IMRDY_RELEASE_DIR  — local directory with release assets (skip download)
#   IMRDY_INSTALL_DIR  — override install target (default: ~/.local/bin)
#   IMRDY_SOUNDS_DIR   — override sounds base dir (default: ~/.claude/sounds)
set -euo pipefail

# Override: local release directory skips all downloads
RELEASE_DIR="${IMRDY_RELEASE_DIR:-}"

# When testing with RELEASE_DIR, skip stdin caching (no hook to re-run)
if [ -z "${RELEASE_DIR}" ]; then
    STDIN_CACHE=$(mktemp)
    trap 'rm -f "${STDIN_CACHE}"' EXIT
    cat > "${STDIN_CACHE}"
fi

REPO="radleta/imrdy"
INSTALL_DIR="${IMRDY_INSTALL_DIR:-${HOME}/.local/bin}"
BINARY_NAME="imrdy.exe"
INSTALL_PATH="${INSTALL_DIR}/${BINARY_NAME}"
ASSET_NAME=""
CHECKSUMS_NAME="SHA256SUMS.txt"

# Detect architecture
ARCH=$(uname -m)
case "${ARCH}" in
    x86_64|amd64)  ASSET_NAME="imrdy-win-x64.exe" ;;
    aarch64|arm64) ASSET_NAME="imrdy-win-arm64.exe" ;;
    *)
        echo "imrdy: unsupported architecture: ${ARCH}" >&2
        exit 1
        ;;
esac

mkdir -p "${INSTALL_DIR}"

# Download binary and checksum
if [ -n "${RELEASE_DIR}" ]; then
    echo "imrdy: installing from local release dir: ${RELEASE_DIR}" >&2
    cp "${RELEASE_DIR}/${ASSET_NAME}" "${INSTALL_PATH}"
    [ -f "${RELEASE_DIR}/${CHECKSUMS_NAME}" ] && cp "${RELEASE_DIR}/${CHECKSUMS_NAME}" "${INSTALL_DIR}/${CHECKSUMS_NAME}"
elif command -v gh &>/dev/null; then
    echo "imrdy: downloading ${ASSET_NAME} via gh..." >&2
    gh release download --repo "${REPO}" --pattern "${ASSET_NAME}" --output "${INSTALL_PATH}" --clobber
    gh release download --repo "${REPO}" --pattern "${CHECKSUMS_NAME}" --output "${INSTALL_DIR}/${CHECKSUMS_NAME}" --clobber
elif command -v curl &>/dev/null; then
    # Get latest release asset URLs
    RELEASE_JSON=$(curl -fsSL "https://api.github.com/repos/${REPO}/releases/latest")

    BINARY_URL=$(echo "${RELEASE_JSON}" | grep -o "\"browser_download_url\":[[:space:]]*\"[^\"]*${ASSET_NAME}\"" \
        | head -1 | sed 's/"browser_download_url":[[:space:]]*"//;s/"$//')
    CHECKSUM_URL=$(echo "${RELEASE_JSON}" | grep -o "\"browser_download_url\":[[:space:]]*\"[^\"]*${CHECKSUMS_NAME}\"" \
        | head -1 | sed 's/"browser_download_url":[[:space:]]*"//;s/"$//')

    if [ -z "${BINARY_URL:-}" ]; then
        echo "imrdy: no release asset found for ${ASSET_NAME}" >&2
        exit 1
    fi

    # Validate URLs point to GitHub
    for url in "${BINARY_URL}" "${CHECKSUM_URL:-}"; do
        if [ -n "${url}" ] && [[ ! "${url}" =~ ^https://(github\.com|objects\.githubusercontent\.com)/ ]]; then
            echo "imrdy: unexpected download URL domain: ${url}" >&2
            exit 1
        fi
    done

    echo "imrdy: downloading ${ASSET_NAME}..." >&2
    curl -fsSL -o "${INSTALL_PATH}" "${BINARY_URL}"

    if [ -n "${CHECKSUM_URL:-}" ]; then
        curl -fsSL -o "${INSTALL_DIR}/${CHECKSUMS_NAME}" "${CHECKSUM_URL}"
    fi
else
    echo "imrdy: neither gh nor curl found, cannot download" >&2
    exit 1
fi

# Verify SHA256 checksum
CHECKSUMS_PATH="${INSTALL_DIR}/${CHECKSUMS_NAME}"
if [ -f "${CHECKSUMS_PATH}" ]; then
    EXPECTED_HASH=$(grep "${ASSET_NAME}" "${CHECKSUMS_PATH}" | awk '{print $1}')
    if [ -n "${EXPECTED_HASH}" ]; then
        ACTUAL_HASH=$(sha256sum "${INSTALL_PATH}" | awk '{print $1}')
        if [ "${ACTUAL_HASH}" != "${EXPECTED_HASH}" ]; then
            echo "imrdy: checksum verification FAILED" >&2
            echo "  expected: ${EXPECTED_HASH}" >&2
            echo "  actual:   ${ACTUAL_HASH}" >&2
            rm -f "${INSTALL_PATH}"
            exit 1
        fi
        echo "imrdy: checksum verified" >&2
    else
        echo "imrdy: warning: no checksum found for ${ASSET_NAME}" >&2
    fi
    rm -f "${CHECKSUMS_PATH}"
else
    echo "imrdy: warning: checksum file not available, skipping verification" >&2
fi

chmod +x "${INSTALL_PATH}"
echo "imrdy: installed to ${INSTALL_PATH}" >&2

# --- Download default sound pack (graceful — failure does not abort) ---
(
    SOUNDS_DIR="${IMRDY_SOUNDS_DIR:-${HOME}/.claude/sounds}"
    PACKS_DIR="${SOUNDS_DIR}/packs"
    CONFIG_PATH="${SOUNDS_DIR}/config.json"

    if [ -n "${RELEASE_DIR}" ]; then
        # Local mode — copy pack zip from release dir
        PACK_ZIP=$(find "${RELEASE_DIR}" -maxdepth 1 -name "*.zip" | head -1)
        if [ -z "${PACK_ZIP}" ]; then
            echo "imrdy: no pack ZIP found in release dir, skipping" >&2
            exit 0
        fi
        PACK_TMP_DIR=$(mktemp -d)
        trap '[ -n "${PACK_TMP_DIR:-}" ] && rm -rf "${PACK_TMP_DIR}"' EXIT
        PACK_ZIP_PATH="${PACK_TMP_DIR}/pack.zip"
        cp "${PACK_ZIP}" "${PACK_ZIP_PATH}"

        # Verify pack checksum if available
        PACK_CHECKSUMS_PATH="${RELEASE_DIR}/pack-SHA256SUMS.txt"
        if [ -f "${PACK_CHECKSUMS_PATH}" ]; then
            PACK_ZIP_NAME=$(basename "${PACK_ZIP}")
            PACK_EXPECTED_HASH=$(grep -F " ${PACK_ZIP_NAME}" "${PACK_CHECKSUMS_PATH}" | awk '{print $1}')
            if [ -n "${PACK_EXPECTED_HASH}" ]; then
                PACK_ACTUAL_HASH=$(sha256sum "${PACK_ZIP_PATH}" | awk '{print $1}')
                if [ "${PACK_ACTUAL_HASH}" != "${PACK_EXPECTED_HASH}" ]; then
                    echo "imrdy: pack checksum verification FAILED" >&2
                    echo "  expected: ${PACK_EXPECTED_HASH}" >&2
                    echo "  actual:   ${PACK_ACTUAL_HASH}" >&2
                    exit 0
                fi
                echo "imrdy: pack checksum verified" >&2
            fi
        fi
    else
        # Find latest pack-* release and extract asset URLs
        PACK_ZIP_URL=""
        PACK_CHECKSUMS_URL=""

        if command -v gh &>/dev/null; then
            # Single API call, extract both URLs (zip on line 1, checksums on line 2)
            PACK_URLS=$(gh api "repos/${REPO}/releases" --jq '
                [.[] | select(.tag_name | startswith("pack-"))][0].assets
                | (map(select(.name | endswith(".zip")))[0].browser_download_url // ""),
                  (map(select(.name == "SHA256SUMS.txt"))[0].browser_download_url // "")' 2>/dev/null || true)
            if [ -n "${PACK_URLS}" ]; then
                PACK_ZIP_URL=$(echo "${PACK_URLS}" | head -1)
                PACK_CHECKSUMS_URL=$(echo "${PACK_URLS}" | tail -1)
            fi
        elif command -v curl &>/dev/null; then
            ALL_RELEASES=$(curl -fsSL "https://api.github.com/repos/${REPO}/releases" 2>/dev/null || true)
            if [ -n "${ALL_RELEASES}" ]; then
                if command -v jq &>/dev/null; then
                    PACK_ZIP_URL=$(echo "${ALL_RELEASES}" | jq -r '
                        [.[] | select(.tag_name | startswith("pack-"))][0].assets[]
                        | select(.name | endswith(".zip"))
                        | .browser_download_url // empty' 2>/dev/null || true)
                    PACK_CHECKSUMS_URL=$(echo "${ALL_RELEASES}" | jq -r '
                        [.[] | select(.tag_name | startswith("pack-"))][0].assets[]
                        | select(.name == "SHA256SUMS.txt")
                        | .browser_download_url // empty' 2>/dev/null || true)
                elif command -v python3 &>/dev/null; then
                    # Single python3 call outputs zip URL on line 1, checksums URL on line 2
                    PACK_PY_URLS=$(echo "${ALL_RELEASES}" | python3 -c "
import json, sys
zip_url, cs_url = '', ''
for r in json.load(sys.stdin):
    if r.get('tag_name','').startswith('pack-'):
        for a in r.get('assets', []):
            if a['name'].endswith('.zip'):
                zip_url = a['browser_download_url']
            elif a['name'] == 'SHA256SUMS.txt':
                cs_url = a['browser_download_url']
        break
print(zip_url)
print(cs_url)
" 2>/dev/null || true)
                    if [ -n "${PACK_PY_URLS}" ]; then
                        PACK_ZIP_URL=$(echo "${PACK_PY_URLS}" | head -1)
                        PACK_CHECKSUMS_URL=$(echo "${PACK_PY_URLS}" | tail -1)
                    fi
                else
                    echo "imrdy: no jq or python3 available, cannot parse pack releases" >&2
                    exit 0
                fi
            fi
        fi

        if [ -z "${PACK_ZIP_URL}" ]; then
            echo "imrdy: no sound pack release found, skipping pack download" >&2
            exit 0
        fi

        # Validate URLs point to GitHub
        for url in "${PACK_ZIP_URL}" "${PACK_CHECKSUMS_URL:-}"; do
            if [ -n "${url}" ] && [[ ! "${url}" =~ ^https://(github\.com|objects\.githubusercontent\.com)/ ]]; then
                echo "imrdy: unexpected pack download URL domain: ${url}" >&2
                exit 0
            fi
        done

        PACK_TMP_DIR=$(mktemp -d)
        trap '[ -n "${PACK_TMP_DIR:-}" ] && rm -rf "${PACK_TMP_DIR}"' EXIT

        PACK_ZIP_PATH="${PACK_TMP_DIR}/pack.zip"
        echo "imrdy: downloading sound pack..." >&2
        curl -fsSL -o "${PACK_ZIP_PATH}" "${PACK_ZIP_URL}"

        # Verify pack checksum
        if [ -n "${PACK_CHECKSUMS_URL:-}" ]; then
            PACK_CHECKSUMS_PATH="${PACK_TMP_DIR}/SHA256SUMS.txt"
            curl -fsSL -o "${PACK_CHECKSUMS_PATH}" "${PACK_CHECKSUMS_URL}"

            PACK_ZIP_NAME=$(basename "${PACK_ZIP_URL%%\?*}")
            PACK_EXPECTED_HASH=$(grep -F " ${PACK_ZIP_NAME}" "${PACK_CHECKSUMS_PATH}" | awk '{print $1}')
            if [ -n "${PACK_EXPECTED_HASH}" ]; then
                PACK_ACTUAL_HASH=$(sha256sum "${PACK_ZIP_PATH}" | awk '{print $1}')
                if [ "${PACK_ACTUAL_HASH}" != "${PACK_EXPECTED_HASH}" ]; then
                    echo "imrdy: pack checksum verification FAILED" >&2
                    echo "  expected: ${PACK_EXPECTED_HASH}" >&2
                    echo "  actual:   ${PACK_ACTUAL_HASH}" >&2
                    exit 0
                fi
                echo "imrdy: pack checksum verified" >&2
            else
                echo "imrdy: warning: no checksum found for pack ZIP" >&2
            fi
        fi
    fi

    # Extract pack to packs directory (with zip-slip protection)
    mkdir -p "${PACKS_DIR}"
    PACKS_DIR_REAL=$(cd "${PACKS_DIR}" && pwd -P)
    if command -v python3 &>/dev/null; then
        python3 -c "
import zipfile, sys, os
zf = zipfile.ZipFile(sys.argv[1], 'r')
dest = os.path.realpath(sys.argv[2])
for member in zf.namelist():
    target = os.path.realpath(os.path.join(dest, member))
    if not target.startswith(dest + os.sep) and target != dest:
        print(f'imrdy: path traversal detected in pack ZIP: {member}', file=sys.stderr)
        sys.exit(1)
zf.extractall(sys.argv[2])
" "${PACK_ZIP_PATH}" "${PACKS_DIR_REAL}"
    elif command -v unzip &>/dev/null; then
        # Validate entries before extraction
        UNSAFE_ENTRY=$(unzip -l "${PACK_ZIP_PATH}" | awk 'NR>3 && !/^-/ {print $NF}' | grep -E '(^/|\.\./)' || true)
        if [ -n "${UNSAFE_ENTRY}" ]; then
            echo "imrdy: path traversal detected in pack ZIP" >&2
            exit 0
        fi
        unzip -qo "${PACK_ZIP_PATH}" -d "${PACKS_DIR}"
    else
        echo "imrdy: no python3 or unzip available, cannot extract pack" >&2
        exit 0
    fi
    echo "imrdy: sound pack installed to ${PACKS_DIR}" >&2

    # Create default config.json if it doesn't exist
    if [ ! -f "${CONFIG_PATH}" ]; then
        mkdir -p "${SOUNDS_DIR}"
        echo '{"default":"assistant","soundEnabled":true}' > "${CONFIG_PATH}"
        echo "imrdy: created default sound config" >&2
    fi
) || echo "imrdy: warning: sound pack download failed, continuing without sounds" >&2

# Re-run the hook with the cached SessionStart payload (skip in test mode)
if [ -z "${RELEASE_DIR}" ]; then
    exec "${INSTALL_PATH}" hook < "${STDIN_CACHE}"
fi

#!/usr/bin/env bash
# install-bootstrap.sh — Downloads imrdy from GitHub Releases and re-runs the hook.
# Called by SessionStart hook when imrdy is not yet installed.
set -euo pipefail

# Cache stdin before it's consumed — we need to relay it to the hook after install
STDIN_CACHE=$(mktemp)
trap 'rm -f "${STDIN_CACHE}"' EXIT
cat > "${STDIN_CACHE}"

REPO="radleta/imrdy"
INSTALL_DIR="${HOME}/.local/bin"
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
if command -v gh &>/dev/null; then
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

# Re-run the hook with the cached SessionStart payload
exec "${INSTALL_PATH}" hook < "${STDIN_CACHE}"

# install.ps1 — Quick-start installer for imrdy on Windows.
# Downloads the latest release binary to ~/.local/bin/ and provides plugin instructions.
#
# Usage: irm https://raw.githubusercontent.com/radleta/imrdy/main/install.ps1 | iex
#
# Environment overrides (for testing):
#   IMRDY_RELEASE_DIR  — local directory with release assets (skip download)
#   IMRDY_INSTALL_DIR  — override install target (default: ~/.local/bin)
#   IMRDY_SOUNDS_DIR   — override sounds base dir (default: ~/.imrdy/sounds)

$ErrorActionPreference = "Stop"

$ReleaseDir = $env:IMRDY_RELEASE_DIR
$Repo = "radleta/imrdy"
$InstallDir = if ($env:IMRDY_INSTALL_DIR) { $env:IMRDY_INSTALL_DIR } else { Join-Path $env:USERPROFILE ".local\bin" }
$BinaryName = "imrdy.exe"
$InstallPath = Join-Path $InstallDir $BinaryName

# Detect architecture
$Arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
switch ($Arch) {
    "X64"  { $AssetSuffix = "win-x64" }
    "Arm64" { $AssetSuffix = "win-arm64" }
    default {
        Write-Error "Unsupported architecture: $Arch"
        exit 1
    }
}

$AssetName = "imrdy-$AssetSuffix.exe"

if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

if ($ReleaseDir) {
    # Local mode — copy from release directory (for testing)
    Write-Host "Installing from local release dir: $ReleaseDir" -ForegroundColor Cyan
    $LocalAsset = Join-Path $ReleaseDir $AssetName
    if (-not (Test-Path $LocalAsset)) {
        Write-Error "No release asset found: $LocalAsset"
        exit 1
    }
    Copy-Item $LocalAsset -Destination $InstallPath -Force

    # Verify checksum if available
    $LocalChecksum = Join-Path $ReleaseDir "SHA256SUMS.txt"
    if (Test-Path $LocalChecksum) {
        $ChecksumContent = Get-Content $LocalChecksum
        $ExpectedLine = $ChecksumContent | Where-Object { $_ -match $AssetName }
        if ($ExpectedLine) {
            $ExpectedHash = ($ExpectedLine -split "\s+")[0]
            $ActualHash = (Get-FileHash $InstallPath -Algorithm SHA256).Hash.ToLower()
            if ($ActualHash -ne $ExpectedHash) {
                Remove-Item $InstallPath -Force
                Write-Error "Checksum verification FAILED! Expected: $ExpectedHash, Got: $ActualHash"
                exit 1
            }
            Write-Host "Checksum verified" -ForegroundColor Green
        }
    }
} else {
    # Download from GitHub Releases
    Write-Host "Fetching latest release..." -ForegroundColor Cyan
    $Release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers @{
        "User-Agent" = "imrdy-installer"
    }

    $Asset = $Release.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
    if (-not $Asset) {
        Write-Error "No release asset found: $AssetName"
        exit 1
    }

    $ChecksumAsset = $Release.assets | Where-Object { $_.name -eq "SHA256SUMS.txt" } | Select-Object -First 1

    Write-Host "Downloading $AssetName..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $Asset.browser_download_url -OutFile $InstallPath

    # Verify SHA256 checksum
    if ($ChecksumAsset) {
        $ChecksumPath = Join-Path $InstallDir "SHA256SUMS.txt"
        Invoke-WebRequest -Uri $ChecksumAsset.browser_download_url -OutFile $ChecksumPath

        $ChecksumContent = Get-Content $ChecksumPath
        $ExpectedLine = $ChecksumContent | Where-Object { $_ -match $AssetName }
        if ($ExpectedLine) {
            $ExpectedHash = ($ExpectedLine -split "\s+")[0]
            $ActualHash = (Get-FileHash $InstallPath -Algorithm SHA256).Hash.ToLower()
            if ($ActualHash -ne $ExpectedHash) {
                Remove-Item $InstallPath -Force
                Remove-Item $ChecksumPath -Force
                Write-Error "Checksum verification FAILED! Expected: $ExpectedHash, Got: $ActualHash"
                exit 1
            }
            Write-Host "Checksum verified" -ForegroundColor Green
        } else {
            Write-Host "Warning: no checksum found for $AssetName" -ForegroundColor Yellow
        }
        Remove-Item $ChecksumPath -Force
    } else {
        Write-Host "Warning: checksum file not available, skipping verification" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Installed imrdy to $InstallPath" -ForegroundColor Green

# --- Download default sound pack (graceful — failure does not abort) ---
try {
    $ImrdyHome = if ($env:IMRDY_SOUNDS_DIR) { $env:IMRDY_SOUNDS_DIR } else { Join-Path $env:USERPROFILE ".imrdy" }
    $SoundsDir = if ($env:IMRDY_SOUNDS_DIR) { $env:IMRDY_SOUNDS_DIR } else { Join-Path $ImrdyHome "sounds" }
    $PacksDir = Join-Path $SoundsDir "packs"
    $ConfigPath = Join-Path $ImrdyHome "config.json"

    $PackZipPath = $null

    if ($ReleaseDir) {
        # Local mode — copy pack zip from release directory
        $PackZipFile = Get-ChildItem $ReleaseDir -Filter "*.zip" | Select-Object -First 1
        if (-not $PackZipFile) {
            Write-Host "No pack ZIP found in release dir, skipping" -ForegroundColor Yellow
        } else {
            $PackTmpDir = Join-Path ([System.IO.Path]::GetTempPath()) "imrdy-pack-$([System.Guid]::NewGuid())"
            New-Item -ItemType Directory -Path $PackTmpDir -Force | Out-Null
            $PackZipPath = Join-Path $PackTmpDir "pack.zip"
            Copy-Item $PackZipFile.FullName -Destination $PackZipPath

            # Verify checksum if available
            $PackChecksumFile = Join-Path $ReleaseDir "pack-SHA256SUMS.txt"
            if (Test-Path $PackChecksumFile) {
                $ChecksumContent = Get-Content $PackChecksumFile
                $ExpectedLine = $ChecksumContent | Where-Object { $_ -match [regex]::Escape($PackZipFile.Name) }
                if ($ExpectedLine) {
                    $PackExpectedHash = ($ExpectedLine -split "\s+")[0]
                    $PackActualHash = (Get-FileHash $PackZipPath -Algorithm SHA256).Hash.ToLower()
                    if ($PackActualHash -ne $PackExpectedHash) {
                        Write-Warning "Pack checksum verification FAILED! Expected: $PackExpectedHash, Got: $PackActualHash"
                        throw "Pack checksum mismatch"
                    }
                    Write-Host "Pack checksum verified" -ForegroundColor Green
                }
            }
        }
    } else {
        # Download from GitHub Releases
        Write-Host "Fetching latest sound pack release..." -ForegroundColor Cyan
        $AllReleases = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases" -Headers @{
            "User-Agent" = "imrdy-installer"
        }

        $PackRelease = $AllReleases | Where-Object { $_.tag_name -like "pack-*" } | Select-Object -First 1
        if (-not $PackRelease) {
            Write-Host "No sound pack release found, skipping pack download" -ForegroundColor Yellow
        } else {
            $PackZipAsset = $PackRelease.assets | Where-Object { $_.name -like "*.zip" } | Select-Object -First 1
            $PackChecksumAsset = $PackRelease.assets | Where-Object { $_.name -eq "SHA256SUMS.txt" } | Select-Object -First 1

            if (-not $PackZipAsset) {
                Write-Host "No pack ZIP asset found in release" -ForegroundColor Yellow
            } else {
                # Validate URL points to GitHub
                $PackZipUrl = $PackZipAsset.browser_download_url
                if ($PackZipUrl -notmatch '^https://(github\.com|objects\.githubusercontent\.com)/') {
                    Write-Warning "Unexpected pack download URL domain: $PackZipUrl"
                } else {
                    $PackTmpDir = Join-Path ([System.IO.Path]::GetTempPath()) "imrdy-pack-$([System.Guid]::NewGuid())"
                    New-Item -ItemType Directory -Path $PackTmpDir -Force | Out-Null

                    $PackZipPath = Join-Path $PackTmpDir "pack.zip"
                    Write-Host "Downloading sound pack..." -ForegroundColor Cyan
                    Invoke-WebRequest -Uri $PackZipUrl -OutFile $PackZipPath

                    # Verify pack checksum
                    if ($PackChecksumAsset) {
                        $PackChecksumUrl = $PackChecksumAsset.browser_download_url
                        if ($PackChecksumUrl -match '^https://(github\.com|objects\.githubusercontent\.com)/') {
                            $PackChecksumPath = Join-Path $PackTmpDir "SHA256SUMS.txt"
                            Invoke-WebRequest -Uri $PackChecksumUrl -OutFile $PackChecksumPath

                            $PackZipName = $PackZipAsset.name
                            $ChecksumContent = Get-Content $PackChecksumPath
                            $ExpectedLine = $ChecksumContent | Where-Object { $_ -match [regex]::Escape($PackZipName) }
                            if ($ExpectedLine) {
                                $PackExpectedHash = ($ExpectedLine -split "\s+")[0]
                                $PackActualHash = (Get-FileHash $PackZipPath -Algorithm SHA256).Hash.ToLower()
                                if ($PackActualHash -ne $PackExpectedHash) {
                                    Write-Warning "Pack checksum verification FAILED! Expected: $PackExpectedHash, Got: $PackActualHash"
                                    throw "Pack checksum mismatch"
                                }
                                Write-Host "Pack checksum verified" -ForegroundColor Green
                            } else {
                                Write-Host "Warning: no checksum found for pack ZIP" -ForegroundColor Yellow
                            }
                        }
                    }
                }
            }
        }
    }

    if ($PackZipPath -and (Test-Path $PackZipPath)) {
        try {
            # Extract pack to packs directory (with zip-slip protection)
            if (-not (Test-Path $PacksDir)) {
                New-Item -ItemType Directory -Path $PacksDir -Force | Out-Null
            }
            $DestFull = (Resolve-Path $PacksDir).Path
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $ZipArchive = [System.IO.Compression.ZipFile]::OpenRead($PackZipPath)
            try {
                foreach ($entry in $ZipArchive.Entries) {
                    $TargetPath = [System.IO.Path]::GetFullPath((Join-Path $DestFull $entry.FullName))
                    if (-not $TargetPath.StartsWith($DestFull + [System.IO.Path]::DirectorySeparatorChar) -and $TargetPath -ne $DestFull) {
                        throw "Path traversal detected in pack ZIP: $($entry.FullName)"
                    }
                }
            } finally {
                $ZipArchive.Dispose()
            }
            Expand-Archive -Path $PackZipPath -DestinationPath $PacksDir -Force
            Write-Host "Sound pack installed to $PacksDir" -ForegroundColor Green

            # Create default config.json if it doesn't exist
            if (-not (Test-Path $ConfigPath)) {
                if (-not (Test-Path $ImrdyHome)) {
                    New-Item -ItemType Directory -Path $ImrdyHome -Force | Out-Null
                }
                $json = '{"tray":{"enabled":true},"sound":{"enabled":true,"defaultPack":"assistant"}}'
                [IO.File]::WriteAllText($ConfigPath, $json, (New-Object System.Text.UTF8Encoding $false))
                Write-Host "Created default config" -ForegroundColor Green
            }
        } finally {
            # Clean up temp directory
            if ($PackTmpDir -and (Test-Path $PackTmpDir)) {
                Remove-Item $PackTmpDir -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
} catch {
    Write-Warning "Sound pack download failed: $($_.Exception.Message). Continuing without sounds."
}

# Check if install dir is in PATH
$PathDirs = $env:PATH -split ";"
if ($PathDirs -notcontains $InstallDir) {
    Write-Host ""
    Write-Host "Add to PATH (run once):" -ForegroundColor Yellow
    Write-Host "  `$env:PATH += `";$InstallDir`"" -ForegroundColor White
    Write-Host "  [Environment]::SetEnvironmentVariable('PATH', `$env:PATH + ';$InstallDir', 'User')" -ForegroundColor White
}

Write-Host ""
Write-Host "To install the Claude Code plugin:" -ForegroundColor Cyan
Write-Host "  claude plugin add https://github.com/$Repo" -ForegroundColor White
Write-Host ""
Write-Host "Or start the monitor manually:" -ForegroundColor Cyan
Write-Host "  imrdy" -ForegroundColor White

Write-Host ""
Write-Host "  The system tray monitor starts automatically with your next Claude session."
Write-Host "  To start it manually:  imrdy"
Write-Host "  To disable auto-start: imrdy config set tray.enabled false"

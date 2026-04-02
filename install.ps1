# install.ps1 — Quick-start installer for imrdy on Windows.
# Downloads the latest release binary to ~/.local/bin/ and provides plugin instructions.
#
# Usage: irm https://raw.githubusercontent.com/radleta/imrdy/main/install.ps1 | iex

$ErrorActionPreference = "Stop"

$Repo = "radleta/imrdy"
$InstallDir = Join-Path $env:USERPROFILE ".local\bin"
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

# Get latest release info
Write-Host "Fetching latest release..." -ForegroundColor Cyan
$Release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers @{
    "User-Agent" = "imrdy-installer"
}

$AssetName = "imrdy-$AssetSuffix.exe"
$Asset = $Release.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
if (-not $Asset) {
    Write-Error "No release asset found: $AssetName"
    exit 1
}

$ChecksumAsset = $Release.assets | Where-Object { $_.name -eq "SHA256SUMS.txt" } | Select-Object -First 1

# Download
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

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

Write-Host ""
Write-Host "Installed imrdy to $InstallPath" -ForegroundColor Green

# --- Download default sound pack (graceful — failure does not abort) ---
try {
    $PacksDir = Join-Path $env:USERPROFILE ".claude\sounds\packs"
    $SoundsDir = Join-Path $env:USERPROFILE ".claude\sounds"
    $ConfigPath = Join-Path $SoundsDir "config.json"

    # Find latest pack-* release
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

                try {
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
                        if (-not (Test-Path $SoundsDir)) {
                            New-Item -ItemType Directory -Path $SoundsDir -Force | Out-Null
                        }
                        '{"default":"assistant","soundEnabled":true}' | Set-Content -Path $ConfigPath -Encoding UTF8
                        Write-Host "Created default sound config" -ForegroundColor Green
                    }
                } finally {
                    # Clean up temp directory
                    if (Test-Path $PackTmpDir) {
                        Remove-Item $PackTmpDir -Recurse -Force -ErrorAction SilentlyContinue
                    }
                }
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

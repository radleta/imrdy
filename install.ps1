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

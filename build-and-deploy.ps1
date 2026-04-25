# MegaShot Build and Deploy Script
# Builds Release and stages the DLL in Latest Release/MegaShot/ for GitHub upload.
# Profile copy step removed — MegaLoad's auto-updater handles delivery once the
# GitHub release is published and mod-manifest.json is updated.

Write-Host "==================================" -ForegroundColor Cyan
Write-Host " MegaShot Build & Deploy" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Building project..." -ForegroundColor Yellow
dotnet build MegaShot\MegaShot.csproj --configuration Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "`nBuild failed! Check errors above." -ForegroundColor Red
    exit 1
}

Write-Host "`nBuild successful!" -ForegroundColor Green

$srcDll = "MegaShot\bin\Release\netstandard2.1\MegaShot.dll"
$releaseDir = "..\Latest Release\MegaShot"
$releasePath = Join-Path $releaseDir "MegaShot.dll"

if (!(Test-Path $releaseDir)) { New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null }

Copy-Item $srcDll $releasePath -Force

$fileInfo = Get-Item $releasePath
Write-Host "Staged for release: $releasePath" -ForegroundColor Green
Write-Host "  File size: $($fileInfo.Length) bytes" -ForegroundColor Gray
Write-Host "  Modified: $($fileInfo.LastWriteTime)" -ForegroundColor Gray
Write-Host ""
Write-Host "Next: gh release create vX.Y.Z `"$releasePath`" --repo ccmrik/MegaShot" -ForegroundColor Cyan
Write-Host "      then bump MegaLoad/mod-manifest.json and upload to the MegaLoad release." -ForegroundColor Cyan
Write-Host ""

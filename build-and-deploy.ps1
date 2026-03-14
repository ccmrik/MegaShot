# MegaShot Build and Deploy Script
# This script builds the mod and copies it to the plugin folder

Write-Host "==================================" -ForegroundColor Cyan
Write-Host " MegaShot Build & Deploy" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""

# Build the project
Write-Host "Building project..." -ForegroundColor Yellow
dotnet build MegaShot\MegaShot.csproj --configuration Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n? Build failed! Check errors above." -ForegroundColor Red
    exit 1
}

Write-Host "`n? Build successful!" -ForegroundColor Green

$srcDll = "MegaShot\bin\Release\net462\MegaShot.dll"
$pluginDir = "C:\Users\Rik\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Valheim Min Mods\BepInEx\plugins\MegaShot"
$pluginPath = Join-Path $pluginDir "MegaShot.dll"

# Ensure plugin directory exists
if (!(Test-Path $pluginDir)) { New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null }

# Try to deploy, retry with alert if file is locked
$deployed = $false
while (-not $deployed) {
    try {
        Copy-Item $srcDll $pluginPath -Force -ErrorAction Stop
        $deployed = $true
    } catch {
        Write-Host ""
        Write-Host "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!" -ForegroundColor Red
        Write-Host "  DEPLOY FAILED - DLL is locked!" -ForegroundColor Red
        Write-Host "  Close Valheim / log out first, then try again." -ForegroundColor Yellow
        Write-Host "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!" -ForegroundColor Red
        [Console]::Beep(1000, 300); [Console]::Beep(1000, 300); [Console]::Beep(1000, 300)
        Write-Host ""
        Read-Host "Press ENTER to retry deploy"
    }
}

$fileInfo = Get-Item $pluginPath
Write-Host "? Plugin deployed: $pluginPath" -ForegroundColor Green
Write-Host "  File size: $($fileInfo.Length) bytes" -ForegroundColor Gray
Write-Host "  Modified: $($fileInfo.LastWriteTime)" -ForegroundColor Gray
Write-Host ""
Write-Host "Ready to test in game! Launch Valheim through r2modman." -ForegroundColor Cyan
Write-Host ""


# Version Bump Script
# Usage: .\version-bump.ps1 patch|minor|major "Release notes"

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("patch", "minor", "major")]
    [string]$type,
    
    [Parameter(Mandatory=$false)]
    [string]$notes = "Bug fixes and improvements"
)

$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

# Read current version from project file
$csprojPath = "MegaShot\MegaShot.csproj"
$csprojContent = Get-Content $csprojPath -Raw

if ($csprojContent -match '<Version>(\d+)\.(\d+)\.(\d+)</Version>') {
    $major = [int]$matches[1]
    $minor = [int]$matches[2]
    $patch = [int]$matches[3]
    
    Write-Host "Current version: $major.$minor.$patch" -ForegroundColor Cyan
    
    # Increment version
    switch ($type) {
        "major" {
            $major++
            $minor = 0
            $patch = 0
        }
        "minor" {
            $minor++
            $patch = 0
        }
        "patch" {
            $patch++
        }
    }
    
    $newVersion = "$major.$minor.$patch"
    Write-Host "New version: $newVersion" -ForegroundColor Green
    
    # Update csproj
    $csprojContent = $csprojContent -replace '<Version>\d+\.\d+\.\d+</Version>', "<Version>$newVersion</Version>"
    Set-Content $csprojPath $csprojContent -NoNewline
    
    # Update Class1.cs
    $class1Path = "MegaShot\Class1.cs"
    $class1Content = Get-Content $class1Path -Raw
    $class1Content = $class1Content -replace 'PluginVersion = "\d+\.\d+\.\d+"', "PluginVersion = `"$newVersion`""
    Set-Content $class1Path $class1Content -NoNewline
    
    Write-Host "? Updated project files" -ForegroundColor Green
    
    # Git commit and tag
    Write-Host ""
    Write-Host "Committing changes..." -ForegroundColor Yellow
    git add .
    git commit -m "v$newVersion - $notes"
    git tag "v$newVersion"
    
    Write-Host ""
    Write-Host "? Version bumped to v$newVersion and tagged!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Don't forget to update CHANGELOG.md with release notes!" -ForegroundColor Cyan
} else {
    Write-Host "? Could not find version in $csprojPath" -ForegroundColor Red
    exit 1
}

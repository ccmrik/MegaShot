# MegaShot - Quick Reference

## Build and Deploy
```powershell
.\build-and-deploy.ps1
```

## Commit Changes
```powershell
.\quick-commit.ps1 "Your message here"
```

## Bump Version
```powershell
.\version-bump.ps1 patch "Bug fixes"
.\version-bump.ps1 minor "New features"
.\version-bump.ps1 major "Breaking changes"
```

## Key Files

| File | Purpose |
|------|---------|
| MegaShot/Class1.cs | Main plugin code |
| MegaShot/CrossbowPatches.cs | Game patches |
| VALHEIM_API_VERIFIED.md | Verified Valheim API methods |

## Testing Checklist

- Build succeeds via build-and-deploy.ps1
- Launch Valheim via r2modman
- Equip crossbow, hold left mouse for automatic fire
- Right-click for zoom, scroll wheel to adjust
- Fire 1000 rounds to trigger reload
- Edit config file to verify live reload


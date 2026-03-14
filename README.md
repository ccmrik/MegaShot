# MegaShot

A powerful BepInEx mod for Valheim that transforms crossbows into fully-automatic weapons with advanced features.

## Features

- **Automatic Fire**: Hold down the mouse button for rapid automatic fire
- **Magazine System**: Configurable magazine capacity (default 1000 rounds) with automatic reload
- **Zoom System**: Right-click to zoom, scroll wheel to adjust zoom level
- **Customizable Damage**: Adjust damage from 0% to any percentage (100% = vanilla)
- **Fire Rate Control**: Configure shots per second (default 5)
- **Projectile Physics**: Adjustable bolt velocity and optional gravity disable
- **Dynamic Crosshair**: Bow-style crosshair appears when zooming

## Installation

1. Install [BepInEx](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/)
2. Download the latest release
3. Extract `MegaShot.dll` to `BepInEx/plugins/`
4. Launch Valheim

## Configuration

Configuration file is located at `BepInEx/config/com.rikal.MegaShot.cfg`

### General Settings
- **Enabled** (true/false): Enable or disable the mod
- **DamageMultiplier** (default 100): Damage percentage (100 = normal)
- **FireRate** (default 5): Shots per second

### Zoom Settings
- **ZoomMin** (default 2): Minimum zoom level
- **ZoomMax** (default 10): Maximum zoom level

### Projectile Settings
- **Velocity** (default 100): Bolt velocity multiplier
- **NoGravity** (default false): Disable projectile drop

### Magazine Settings
- **Capacity** (default 1000): Number of shots before reload

## Usage

1. Equip any crossbow
2. Hold left mouse button to fire automatically
3. Right-click to zoom in
4. Scroll mouse wheel while zoomed to adjust zoom level
5. Mod will automatically reload when magazine is empty

## Compatibility

- Valheim version: Latest
- Works with vanilla and modded crossbows
- Does not affect other weapons

## Building from Source

1. Clone the repository
2. Update assembly references in `.csproj` to match your Valheim installation
3. Build the solution
4. DLL will automatically copy to the configured plugin folder

## Version History

### 1.0.0
- Initial release
- Automatic fire system
- Magazine and reload mechanics
- Zoom functionality
- Configurable damage, fire rate, and projectile physics

## License

MIT License

## Credits

Created by rikal

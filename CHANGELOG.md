# Changelog

All notable changes to MegaShot will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.6.8] - 2026-04-22

### Fixed
- **Armageddon FX suppression was also eating Guck / honey / ornament loot.** Some destructibles (Guck sacks, beehives, lootable ornaments, etc.) spawn their actual loot via the `m_destroyedEffect` prefab list, not via a separate drop path. The v2.6.6 blanket skip on `EffectList.Create` was deleting those loot spawns along with the cosmetic dust puffs. Now the Prefix inspects each `EffectList` once (cached), and if any prefab in it has an `ItemDrop` component anywhere in its hierarchy, vanilla runs so the loot drops normally. Pure cosmetic effect lists (rock hit sparks, destroy dust, etc.) still get skipped — perf win intact, Stone + Wood still the only bulk drops suppressed.
- **Armageddon no longer kills friendlies.** Direct beam hits and the splash-AOE loop now skip damage on any target that's: a tamed creature, another player, a player-raised undead (PlayerSubjects faction), or a neutral Dvergr. Canonical faction check via `BaseAI.IsEnemy(attacker, target)` — same rule vanilla AI uses to decide aggro. Fixes friendly skeletons getting vaporised and Dvergr outposts being razed when AOE sweeps past them.

## [2.6.7] - 2026-04-22

### Changed
- **Armageddon drop suppression scope trimmed to just Stone and plain Wood.** Previously swallowed finewood, corewood, roundlog, elderbark, yggdrasilwood, ancientbark, resin, flint, branches, sticks, and feathers too. That was eating useful mats when chewing through frost caves / crypts / forests. Now only `$item_stone` and `$item_wood` are deleted on drop; everything else — including all creature loot (Fenris Hair, Red Jute, Wolf Pelt, etc.) and every other wood variant — drops normally.

## [2.6.6] - 2026-04-22

### Added
- **Armageddon FX suppression** — huge perf win in dense rock clusters with large AOE. While the beam is actively firing, a Harmony Prefix on `EffectList.Create` short-circuits all vanilla hit/destroy VFX + SFX prefab instantiation (`m_hitEffect`, `m_destroyedEffect`, splinter dust, etc.). The beam's own bloom, motes, and impact flash already read as "things are exploding"; the vanilla per-rock puffs were purely cosmetic and were spawning hundreds of prefabs per second in big Mistlands rock clusters, freezing the game. Gated on a tight 0.3 s window so unrelated world FX resume immediately after releasing the trigger. New config `SuppressFx` (bool, default `true`) in section `9. Armageddon Mode` — turn off if you want per-rock dust puffs back.

## [2.6.5] - 2026-04-22

### Changed
- **Armageddon drop suppression now time-gated, not proximity-gated.** Previously the suppressor swallowed junk drops only if they spawned within a radius of the raw beam impact (AOE + 5 m, plus the hit object's root bounds). That missed plenty of stone from Mistlands / Plains / Ashlands rocks, MineRock5 ore veins, and AOE cascades that dumped drops far outside the registered circles. It now suppresses all listed junk drops (stone, wood variants, resin, flint, branches, sticks, feathers) worldwide for 5 seconds after the beam last drew. No proximity check.
- **Prefab-name fallback** in `ArmageddonSuppression.IsJunkItem` — matches against the GameObject name (strips `(Clone)` suffix) in case `m_shared.m_name` isn't populated yet at `Awake` time for a given drop.

## [2.6.4] - 2026-04-21

### Added
- **Beam energy motes** — a `ParticleSystem` now seeds tiny glowing particles along the beam's length every frame. Short life (~0.3 s), perpendicular drift outward from the beam plus a small upward rise, orange→dark-red fade with shrink-over-life. Count scales with beam length (4–24 per frame, capped) and gets an extra burst while the hit flash is active. Size is scope-compensated so they don't bloat through the scope's FOV. Cleans up when the beam stops.

## [2.6.3] - 2026-04-21

### Added
- **Armageddon beam hit flash** — when the beam strikes a damageable target (creature, wall, rock, ore, any `WearNTear`/`Destructible`/`MineRock`/`MineRock5`/`Character`) the endpoint flares into a hot orange-white bloom, the beam widens, colour shifts red→orange, and the interior jitter cranks for ~150 ms after the last hit tick. No flash on terrain-only rays.
- **Scope-aware beam width** — while ADS, the world-space beam width is divided by the active zoom magnification so the beam reads the same apparent size through the scope as it does unzoomed. No more telephone-pole laser when peering down the scope.

### Changed
- **`ArmageddonFireRate` config removed.** The beam now ticks damage at a fixed 30 Hz regardless of `FireRate`. The FireRate config still drives the non-Armageddon crossbow firing rate.

### Removed
- Config key `9. Armageddon Mode / FireRate` (safe to leave in user configs; MegaShot simply ignores it now).

## [2.6.2] - 2026-04-21

### Added
- **Armageddon drop suppression** — resource junk spawned by objects the beam destroys is deleted before it hits the ground.
  - Covers: stone, wood (incl. fine/core/roundlog/elderbark/yggdrasilwood/ancientbark), resin, flint, branches, sticks, feathers.
  - Works by catching `ItemDrop.Awake` within a rolling 3-second window around recent beam impacts.
  - Registers impacts at both the crosshair hit point (AOE + 5 m) and the destroyed object's root bounds, so MineRock5 sub-area drops scattered across big ore veins still get swallowed.
  - New config `SuppressDrops` (bool, default `true`) in section `9. Armageddon Mode` — turn off if you want the drops back.

## [2.6.1] - 2026-04-21

### Changed
- **Armageddon beam** re-tuned:
  - Colour is now pure **red** with per-frame intensity flicker and a faint darker-red tail.
  - Beam is **thinner** (start ~0.035 m, end ~0.015 m) with a breathing width pulse.
  - Now a **7-vertex polyline** with Perlin-noise perpendicular jitter on interior points, so the ray looks alive rather than ruler-straight.
- **Config typing + ranges:**
  - `AoeRadius` → **integer metres, 0–100** (was float 0–50).
  - `LaserVolume` → **integer percentage, 0–100** (was float 0–1).
  - New `Range` → **integer metres, 50–1000, default 500**. Controls beam max reach and damage range.

## [2.6.0] - 2026-04-21

### Changed
- **Armageddon Mode is now a beam, not a hail of bolts.** Holding **Shift + LMB** projects a continuous LineRenderer from the weapon to the crosshair impact point, ticking destroy-tagged damage along the ray.
  - No projectiles spawn — eliminates the per-frame instantiate cost and the cleanup churn at 100 rps.
  - `FireRate` now controls **damage ticks per second** instead of bolts per second.
  - Trees and logs are still spared; AOE radius (default 10 m) still applies around the impact.
  - Beam pulses gently in width + alpha, red→amber gradient, draws over distance up to 1000 m.
  - Laser hum continues to play while the beam is active (carries cleanly across the new firing model).

## [2.5.1] - 2026-04-21

### Added
- **Armageddon laser hum** — continuous procedural beam SFX while the fire button is held in Armageddon Mode.
  - Generated on the fly (~1 s looped clip mixing 110/220/440 Hz sines + 1.1 kHz buzz + light noise) so no asset files ship with the DLL.
  - New configs in `9. Armageddon Mode`: `LaserSound` (bool, default `true`) and `LaserVolume` (0–1, default `0.6`).
  - Per-shot bolt SFX is suppressed while the laser is active so the soundscape stays clean.

## [2.5.0] - 2026-04-21

### Added
- **Armageddon Mode** — new section `9. Armageddon Mode`, off by default.
  - Hold the modifier key (default: **LeftShift**) while firing to engage.
  - Full-auto at `FireRate` (default **100 rps**), magazine never depletes, no reload.
  - AOE cranked to `AoeRadius` (default **10 m**) for the destruction sphere.
  - Destroys rocks, saplings, ores, plants, mushrooms and other destructibles within the AOE.
  - Spares trees and logs — direct hits and AOE splash both skip `TreeBase` / `TreeLog`.
  - HouseFire is suppressed during Armageddon so bases don't catch alight from the spam.

## [1.0.0] - 2024-01-XX

### Added
- Initial release of MegaShot mod
- Full-automatic fire system for all crossbows
- Magazine system with configurable capacity (default: 1000 rounds)
- Automatic reload when magazine empties (2-second reload time)
- Zoom functionality:
  - Right-click to enable/disable zoom
  - Mouse scroll wheel to adjust zoom level
  - Configurable min/max zoom levels (default: 2x to 10x)
  - Crosshair display when zoomed
- Configurable damage multiplier (default: 100%)
- Adjustable fire rate (default: 5 shots per second)
- Projectile physics customization:
  - Velocity multiplier (default: 100%)
  - Gravity toggle (default: enabled)
- Configuration file with all settings
- On/Off toggle for entire mod

### Features
- Works with all vanilla crossbows
- Compatible with modded crossbows (detects by name and skill type)
- Does not affect other weapon types
- Player feedback messages for reload status
- Minimal performance impact

### Technical
- Built with BepInEx 5.4.1500
- Uses Harmony for non-invasive game modification
- Proper cleanup on mod disable/unload
- PostBuild auto-deployment to r2modman plugin folder

## [Unreleased]

### Planned Features
- Reload animation integration
- Sound effect synchronization with fire rate
- On-screen magazine counter UI
- Manual reload hotkey
- Per-crossbow-type configuration
- Burst fire mode option
- Camera recoil effects
- Multi-ammo type support

---

## Version Number Guide

**MAJOR.MINOR.PATCH**

- **MAJOR**: Breaking changes or complete rewrites
- **MINOR**: New features, non-breaking changes
- **PATCH**: Bug fixes, small tweaks

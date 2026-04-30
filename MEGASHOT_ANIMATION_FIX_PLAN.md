# MegaShot Animation — Proper Fix Plan

**Last updated:** 2026-04-30 after v2.6.43 ships the bandaid fix.
**Bandaid status:** v2.6.43 uses `crossbow_fire` as the firing trigger because the player's animator is parked in crossbow stance (`statei=10`) due to `m_skillType = Skills.SkillType.Crossbows`. Visible animation works, lockup gone. **But:** the body plays the crossbow trigger-pull animation while the hands hold a Dundr lightning staff. Mesh ↔ stance mismatch.

This doc lays out the proper fix options so we can ship a coherent visual instead of the mismatch.

---

## What we know (from v2.6.42's ANIM-DIAG dump)

Confirmed at runtime via `Animator.GetCurrentAnimatorStateInfo` + `GetParameter` enumeration:

- **Animator name:** `Visual` (vanilla player rig, layer count = 2)
- **Layer 0 — "Base Layer"** (full body): currently in `Idle Crossbow` / `CrossbowWalk` / `Crossbow Strafe L` blend. Driven by `statei` int param.
- **Layer 1 — "upperbody"** (overlay): clips=`(none)` while in crossbow stance. The staff sub-tree's overlay isn't engaged.
- **`statei = 10`** = crossbow stance. Set automatically by Valheim based on the equipped item's `m_skillType`.
- **138 animator parameters total**, including `crossbow_fire`, `bow_fire`, `staff_rapidfire`, `staff_lightningshot`, `staff_charge_attack`, `staff_fireball0/1`, `staff_charging` (Bool — the charge gate, not a stance gate), `reload_crossbow` (Bool), `reload_crossbow_done` (Trigger), `bow_aim` (Bool).
- **Triggers without a transition out of the current state are silently consumed** but Valheim's input gating still sees a "pending attack" and closes E / weapon-switch. That's the v2.6.40-v2.6.42 lockup mechanism.

---

## The mismatch we need to resolve

| Aspect | Current state | Wanted |
|---|---|---|
| Held mesh | Dundr lightning staff | Dundr lightning staff (✓) |
| Body stance | Crossbow (statei=10) | Staff |
| Firing animation | `crossbow_fire` (trigger-pull) | Staff cast pose |
| Skill XP | Crossbows | Crossbows (✓ — Milord's intent) |
| Damage attribution | `hit.m_skill = Crossbows` (used by 4 patches) | Crossbows (✓ — required) |

So we need: **Dundr-staff visual + staff-cast animation + Crossbows skill XP + Crossbows damage attribution.**

That's a four-way constraint. Today we satisfy three; the animation stance is the odd one out.

---

## Option A — Force `statei` directly (recommended)

Have a Harmony Postfix on whichever Valheim method writes `statei` (likely `Humanoid.SetItemAnimationState` or `Player.SetupVisEquipment`). When the equipped item is MegaShot, override `statei` to the staff value.

**Steps:**

1. Find the staff `statei` value. Two ways:
   - **Empirically:** equip vanilla Dundr in-game with DebugMode on; the existing `AnimatorDeepDiag` dump prints `statei`. That value is "the staff one". (Strong bet: 8.)
   - **From source:** dump `Humanoid` / `Player` methods via dnSpy or ILSpy on `assembly_valheim.dll`, find the `SetInteger("statei", ...)` calls and the lookup table behind them.

2. Identify the writer. Candidates in priority order:
   - `Humanoid.SetItemAnimationState(ItemDrop.ItemData)` — most likely
   - `Player.UpdateEquipment()`
   - `VisEquipment.UpdateEquipmentVisuals()`
   - Or a state machine update in `ZSyncAnimation`

3. Patch with a Harmony Postfix that:
   - Detects MegaShot via `weapon?.m_dropPrefab?.name == "MegaShot"` (NOT skillType, since we already set that to Crossbows).
   - Calls `cachedAnimator.SetInteger("statei", staffValue)` to override.

4. Set `m_attack.m_attackAnimation = "staff_rapidfire"` again now that the animator is in staff stance, so the staff cast pose plays.

**Pros:**
- Zero damage / XP / patch impact — all four constraints satisfied.
- One Harmony patch + one config change.
- Reversible / removable cleanly.

**Cons:**
- Needs runtime probe of vanilla Dundr's `statei` to learn the staff value (or assembly inspection).
- If Valheim updates and renames `statei` or moves the writer, we re-probe.

---

## Option B — Clone CrossbowArbalest, retain Dundr's projectile + visual

Stop cloning Dundr. Clone the vanilla CrossbowArbalest as the base prefab, then graft Dundr's lightning bolt projectile + mesh on top.

**Steps:**

1. In `MegaShotItem.CreatePrefab`, change `if (prefab.name == "StaffLightning")` to `if (prefab.name == "CrossbowArbalest")`.
2. After clone, override:
   - `m_attack.m_attackProjectile = dundrProjectile` (look up Dundr's by name first).
   - Mesh / material: replace the crossbow's renderers with Dundr's (more involved — needs prefab tree walk).
3. Drop the `m_skillType = Crossbows` override (CrossbowArbalest already has it).
4. Drop the `m_attackAnimation` override (CrossbowArbalest already has the right trigger).

**Pros:**
- Animator stance matches the held mesh natively — no Harmony patch needed.
- Crossbow reload animation, hold pose, all natural.
- Skill XP / damage attribution — already correct from the source prefab.

**Cons:**
- Mesh swap is fiddly. Dundr's renderers live under specific child paths; copying them onto a CrossbowArbalest skeleton may misalign attach points (right-hand grip, projectile spawn point).
- Lose the existing in-game look-and-feel that Milord has been testing against. The "MegaShot held in hand" silhouette changes.
- More moving parts means more places for a future Valheim update to break us.

---

## Option C — Switch `m_skillType` to a staff type, refactor 4 patches to identify MegaShot by prefab name

Set `m_skillType = Skills.SkillType.ElementalMagic` (or whatever Dundr's native is — probably `BloodMagic` or `ElementalMagic` — confirm via runtime probe). Then change every patch in `CrossbowPatches.cs` that checks `hit.m_skill == Skills.SkillType.Crossbows` to instead identify MegaShot via a different signal:

- Sentinel value on `hitData.m_backstabBonus` (we already use `-7777f` for Armageddon).
- Custom field on `hitData.m_dodgeable` or a HashSet keyed off `hitData.GetHashCode()`.
- Prefab name on `hitData.m_skill` (not possible — it's an enum) → so an out-of-band marker only.

**Pros:**
- Animator stance matches the staff mesh — visual harmony.
- Staff cast trigger works natively.

**Cons:**
- Skill XP goes to ElementalMagic, not Crossbows. **This conflicts with Milord's intent** ("Crossbow & firearms overhaul"). Dealbreaker unless Milord's OK with it.
- 4 patches (lines 2345, 2403, 2562, 2640 of [CrossbowPatches.cs](MegaShot/CrossbowPatches.cs)) need a new identification signal that survives the round-trip through Damage() pipelines. Not trivial — `hitData` is reconstructed in some paths.
- Higher regression surface area than A.

---

## Recommendation

**Ship A.** It's surgical (one Harmony patch + one config change), preserves all four constraints, and the only "unknown" is one int value (the staff `statei`) that we can probe at runtime in 30 seconds.

**Sequence:**

1. v2.6.43 (already shipped) — `crossbow_fire` bandaid. Confirm playable.
2. v2.6.44 (next) — runtime statei probe: when MegaShot is equipped, dump the natural `statei` value from `cachedAnimator.GetInteger("statei")` BEFORE we override it. DebugMode-gated. Ask Milord to switch from MegaShot to Dundr to MegaShot once with DebugMode on, send the log. We'll see staff-statei printed against Dundr-equipped frame.
3. v2.6.45 — apply the Harmony Postfix that forces `statei = staffValue` when MegaShot is equipped. Switch `m_attackAnimation` back to `staff_rapidfire`. Test → ship.

---

## Key files

- [MegaShot/MegaShotItem.cs:210](MegaShot/MegaShotItem.cs#L210) — `m_skillType = Crossbows` (keep).
- [MegaShot/MegaShotItem.cs:223](MegaShot/MegaShotItem.cs#L223) — `m_attackAnimation = "crossbow_fire"` (will revert to staff_rapidfire in v2.6.45).
- [MegaShot/CrossbowPatches.cs](MegaShot/CrossbowPatches.cs) — 4 patches identify MegaShot via `hit.m_skill == Crossbows` (lines 2345, 2403, 2562, 2640).
- [MegaShot/CrossbowPatches.cs](MegaShot/CrossbowPatches.cs) — `AnimatorDeepDiag` already prints `statei`, reuse it for the v2.6.44 probe.

---

## Memory hooks

- `feedback-pluginversion-sync.md` — bump PluginVersion const + csproj `<Version>` together.
- `feedback-bepinex-config-reload.md` — config reload is guarded; don't regress.
- `feedback-debug-gating.md` — runtime probe lines must be gated by DebugMode.

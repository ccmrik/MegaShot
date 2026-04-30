# Changelog

All notable changes to MegaShot will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.6.32] - 2026-04-30

### Fixed
- **Firing animation now actually plays.** The v2.6.31 `ANIM-DIAG` dump revealed the missing piece: the `staff_lightningshot` trigger transition is gated on the `staff_charging` (Bool) animator parameter. Without that BOOL set to true, every prior version's `SetTrigger` was being silently consumed without firing the transition into the cast state. Vanilla Dundr does it as: `SetBool(staff_charging, true)` → `SetTrigger(staff_lightningshot)` → cast plays → `SetBool(staff_charging, false)`. We were only doing step 2.

### Added
- `SetStaffCharging` / `_staffChargingHeld` state. `PulseFiringAnimation` flips `staff_charging` ON before firing the cast trigger; `ReleaseFiringAnimation` clears it when fire input stops, weapon swaps, UI opens, or Armageddon mode releases LMB. The animator returns cleanly to neutral instead of getting stuck in the charge pose.

### Notes
- **Normal / Alt fire**: `staff_charging` pinned ON while firing → cast trigger lands → animation plays. Cleared in the idle path.
- **Armageddon**: `staff_charging` pinned ON while LMB held → cast trigger pulses every frame → continuous looped cast pose alongside the laser visual. `ReleaseFiringAnimation` invoked when LMB releases or the beam is stopped.
- Velocity-affecting-effect feedback (Milord noted high velocity skews the visual cast timing) is on the follow-up list — addressed once the `staff_charging` fix is confirmed working.

### Files touched
- `MegaShot/CrossbowPatches.cs` — `PulseFiringAnimation` now sets `staff_charging` BOOL via Animator + ZSyncAnimation. New `ReleaseFiringAnimation` invoked from idle / weapon-swap / UI-open / Armageddon-stop paths. Removed the dead vanilla-StartAttack scaffolding (kept the unused `_vanillaStartAttackAllowed` flag so `PatchBlockVanillaAttack`'s defensive check still compiles).

## [2.6.31] - 2026-04-30

### Fixed
- **"Trinket"-like sound on every shot, no firing animation.** v2.6.29/30's vanilla `StartAttack` invocation was firing `Attack.m_startEffect` (Dundr's cast SFX = the trinket sound Milord heard) every call, but the cast animation never actually played in the animator — confirmed by the v2.6.30 log where `m_currentAttack` never auto-cleared (vanilla's cast-end animation event never fired) and our 1.5 s safety-net timeout had to step in every cycle. So we paid the audio cost for none of the visual benefit.

### Reverted / Changed
- Removed the entire vanilla-`StartAttack` pipeline: no more induced StartAttack call, no more `m_currentAttack` reflection-clearing, no more `m_attackProjectile` null swap. Restored simple `ZSyncAnimation.SetTrigger` (same call vanilla's `Attack.Start` makes internally for the trigger).
- Animation may still not be visible — but no spurious sounds either. We're back to the same baseline as v2.6.27 with the vanilla-StartAttack mistake undone.

### Added — diagnostic dump
- `DumpAnimatorOnce` writes a one-time dump of the player animator on first fire pulse:
  - Layer count + per-layer names + current state hash + state length + normalizedTime.
  - All animator parameters (name + type + hash).
- This will tell us which BOOLs / INTs the player animator actually has, which is what we need to find the gate condition `staff_lightningshot` is checking. The trigger fires fine; something else along the transition rejects it. Once we have the dump, the next iteration can SetBool/SetInt the right gate state alongside the trigger.

### Files touched
- `MegaShot/CrossbowPatches.cs` — `PulseFiringAnimation` reduced to SetTrigger + animator dump. Stale flag fields kept (compat with `PatchBlockVanillaAttack`'s flag check) but values are dead.

## [2.6.30] - 2026-04-30

### Fixed
- **Animations only "kind of working" at long range, completely missing in Armageddon laser.** Same root cause as the v2.6.28 CrossFade-every-frame regression: `PulseFiringAnimation` was clearing `m_currentAttack` / `m_previousAttack` via reflection on every call so vanilla `StartAttack` would always accept. That **restarted the cast animation from frame 0 every call** — at 10 rps the animation never advanced past ~10% (close-range invisible); when shots were spaced out (long range, between bursts) the cast got time to play (visible). At 60 Hz in Armageddon the cast never advanced past frame 0 at all.

### Changed
- `PulseFiringAnimation` no longer clears `m_currentAttack`. Vanilla's natural rate-limit runs the cast to completion; subsequent calls accept when vanilla auto-clears the field via the cast's animation event chain.
- Removed `PatchSuppressVanillaProjectile` (the `Attack.OnAttackTrigger` Prefix). It was blocking vanilla's own state cleanup that resets `m_currentAttack` when the cast finishes — so the throttle saw "cast still running" forever and the second cast never started. Setting `m_attackProjectile = null` on the Attack clone (via the weapon template) already prevents the duplicate spawn; OnAttackTrigger now runs to completion and tidies state.
- Safety net: if `m_currentAttack` hasn't auto-cleared after 1.5 s (Dundr cast is ~1 s), force-clear it. Stops the animation getting permanently stuck if vanilla's cleanup chain is interrupted.

### Notes
- Per-shot Normal/Alt fire: cast plays naturally at vanilla's pace (~1 s); bolts spawn at the configured fire rate independently.
- Armageddon laser: one cast cycle every ~1 s while LMB is held → continuous-looking cast stream (the cast animation chains back-to-back).
- DebugMode logs:
  - `ANIM: skipped — vanilla cast still running` when the throttle skips a redundant call.
  - `ANIM: vanilla StartAttack returned True/False` when the call goes through (False = vanilla rejected for another reason — stamina/eitr/loaded — so we know to look there).
  - `ANIM: timeout — force-cleared m_currentAttack` when the 1.5 s safety net fires.

### Files touched
- `MegaShot/CrossbowPatches.cs` — `PulseFiringAnimation` no longer force-clears `m_currentAttack`. `PatchSuppressVanillaProjectile` removed entirely. Added stuck-attack timeout. Removed unused `_suppressVanillaProjectile` flag references at call sites.

## [2.6.29] - 2026-04-30

### Fixed
- **Firing animation finally driven through vanilla `Humanoid.StartAttack`.** v2.6.27 (SetTrigger only) confirmed via DebugMode that the trigger fired but the controller transition never took. v2.6.28 (CrossFadeInFixedTime every frame) made it worse — restarting the state from frame 0 every frame froze the animator and clobbered animations of other weapons sharing it. Both reverted. Now `PulseFiringAnimation`:
  1. Clears `m_currentAttack` / `m_previousAttack` via reflection so vanilla rate-limit accepts back-to-back calls.
  2. Temporarily nulls `weapon.m_attack.m_attackProjectile` so the Attack instance vanilla constructs has no projectile to fire on its animation event (preventing duplicate Dundr bolts on top of our manual `FireBolt` spawn).
  3. Toggles `_vanillaStartAttackAllowed` and calls `player.StartAttack(null, false)` — vanilla does the full animator setup (m_currentAttack assignment, animator BOOLs, ZSyncAnimation, etc.) which manual SetTrigger / CrossFade alone couldn't reproduce.
  4. Restores `m_attackProjectile` after the call so the next `FireBolt` still finds the prefab.

### Notes
- Per-shot (Normal + Alt fire): one induced StartAttack per shot → vanilla cast plays.
- Armageddon laser: induced StartAttack every frame while LMB is held → continuous looped cast pose.
- New patch: `Attack.OnAttackTrigger` Prefix returns false during the suppression window so even if the projectile spawn somehow runs synchronously, it short-circuits.

### Files touched
- `MegaShot/CrossbowPatches.cs` — `PulseFiringAnimation` rewritten around vanilla StartAttack invocation. `PatchBlockVanillaAttack` now lets the call through when `_vanillaStartAttackAllowed` is set. New `PatchSuppressVanillaProjectile` on `Attack.OnAttackTrigger`. Removed unused `zanimField` / `zanimFieldCached`.

## [2.6.28] - 2026-04-30

### Fixed
- **Firing animation still invisible after v2.6.27.** Diagnostic log confirmed `staff_lightningshot` was being SetTriggered every shot, but no visible animation. Conclusion: the animator's transition into the cast state is gated on something vanilla `Attack.Start` sets (likely `m_currentAttack != null` BOOL, an `IsBowDrawn`, or similar) and our manual SetTrigger doesn't satisfy it. Switched to **`Animator.CrossFadeInFixedTime`** which forces the state directly, bypassing transition conditions. State layer is auto-detected and cached. SetTrigger is still called on top for ZSyncAnimation network replication so other players see the cast.

### Notes
- Per-shot (Normal + Alt fire): one CrossFade + SetTrigger per shot → full Dundr cast plays.
- Armageddon laser: CrossFade + SetTrigger every frame → state restarts each frame from `normalizedTime=0` → continuous looped cast pose for the beam stream.
- DebugMode logs:
  - One-time `ANIM: cached state '<name>' hash=<n> layer=<n>` on first pulse to show whether the state was found.
  - Per-pulse `ANIM: SetTrigger('<name>') + CrossFade(layer=<n>)` so we can tell if the state is unfound (`layer=-1`).

### Files touched
- `MegaShot/CrossbowPatches.cs` — `PulseFiringAnimation` now does `Animator.CrossFadeInFixedTime` to the cached state hash + layer, plus the SetTrigger call.

## [2.6.27] - 2026-04-30

### Fixed
- **Firing animation invisible across all modes (Normal / Alt-fire / Armageddon).** Two compounding bugs in `PulseFiringAnimation`:
  1. `Animator.Play(currentState, 0, 0f)` was being called immediately after `ZSyncAnimation.SetTrigger(m_attackAnimation)`. Re-entering the current state via `Play` resets the state machine and **cancels the transition the just-set trigger was about to fire**. Vanilla never does this — it relies on `SetTrigger` alone. Removed.
  2. Animator playback speed was scaled by fire rate (capped at 5× in v2.6.26, was uncapped before). At 5× a 1-second cast played in 200 ms; at the original `fireRate` it was effectively invisible. Now stays at 1× — the trigger re-asserts each shot, so high fire rates loop naturally.

### Notes
- Per-shot fire (Normal + Alt) calls `PulseFiringAnimation` once per `FireBolt` → Dundr's full cast plays each shot.
- Armageddon laser pulses the same trigger every frame while LMB is held → the animator keeps transitioning back into Dundr's cast pose, producing a continuous streamed-firing visual.
- Diagnostic line `ANIM: SetTrigger('<name>')` written to the BepInEx log when MegaShot DebugMode is on, so the actual trigger name from `attack.m_attackAnimation` can be verified at runtime.

### Files touched
- `MegaShot/CrossbowPatches.cs` — `PulseFiringAnimation` simplified to SetTrigger + speed-restore. Removed `FIRE_ANIM_MAX_SPEED` / `ARMAGEDDON_ANIM_SPEED` / `ARMAGEDDON_ANIM_RESTART_FRAMES` constants. Idle path defensively restores `Animator.speed = 1f`.

## [2.6.26] - 2026-04-30

### Changed
- **Armageddon laser now drives the same firing animation as Fire mode, looped continuously.** While the beam is firing, the player's body holds the draw/release pose every frame via `ZSyncAnimation.SetTrigger(m_attackAnimation)` plus a periodic `Animator.Play(currentState, 0, 0f)` so the pose doesn't stall on its end frame. Streams smoothly without per-frame jitter.
- **Firing animation now visible at high fire rates again.** The animator-speed cap was effectively `fireRate` itself, which at 100 rps played the entire attack animation in 10 ms — completely invisible. Capped at 5× so even at 100 rps the draw/release motion reads.

### Files touched
- `MegaShot/CrossbowPatches.cs` — extracted `PulseFiringAnimation` helper used by `FireBolt` and `UpdateArmageddonBeam`. Added `FIRE_ANIM_MAX_SPEED`, `ARMAGEDDON_ANIM_SPEED`, `ARMAGEDDON_ANIM_RESTART_FRAMES` constants.

## [2.6.25] - 2026-04-30

### Fixed
- **Alt-fire on Muddy Scrap Pile (and any `Destructible` with a `DropOnDestroyed`) destroyed without dropping loot.** `ForceDestroyIfNeeded` was calling `nview.Destroy()` directly, which removes the GameObject from the scene without ever firing `Destructible.m_destroyedEffect`, `m_dropWhenDestroyed`, `m_spawnWhenDestroyed`, or `m_onDestroyed` (the event `DropOnDestroyed` listens to for iron scrap, leather scraps, withered bones, etc.). Now special-cases `Destructible`: claim ownership, drive `m_health` to 0, and call vanilla `Destructible.Destroy()` so the full event chain fires and drops spawn.
- **Destructible damage RPC race in dungeons.** `Destructible.Damage` is a no-op locally when the player isn't the ZNetView owner (sends an RPC to the remote owner). Our destroy-tagged Postfix then nuked the GameObject before the RPC's drop chain could run. The Prefix now claims ownership BEFORE vanilla Damage runs, so the local client executes the full destruction path.
- **Armageddon beam sparing creatures hidden behind dungeon walls.** When the beam's first hit was a "crypt" / "burialchamber" / decor target on the spare list, we returned early and no AOE splash ran — so a draugr or skeleton standing behind a crypt wall took zero damage. Spare branch now applies a Character-only splash (`ArmageddonAoeRadius`, character layers) around the impact point so creatures inside dungeons still die and drop loot via vanilla `CharacterDrop.OnDeath`.

### Files touched
- `MegaShot/CrossbowPatches.cs` — `DestroyObjectsHelper.ForceDestroyIfNeeded` Destructible branch calls `Destructible.Destroy()`. `PatchDestroyDestructible.Prefix` claims ZNetView ownership when destroy-tagged. New `ApplyArmageddonCharacterSplash` helper invoked from `ApplyBeamHit` spare branch.

## [2.6.24] - 2026-04-28

### Fixed
- **Partial-destroy on big rocks** — beam-hit rocks chunked but the rest of the prefab stayed standing. Two issues:
  - `DestroyRock5` called `DamageArea` for each sub-area but didn't strip the rock's `m_damageModifiers` first. Mistlands MineRock5s with Resistant / Immune flags would shrug off even 999999 damage. Now temporarily zeroes modifiers around the loop, restores them after.
  - Added a force-destroy cleanup pass after all `DamageArea` invocations: any sub-mesh / area Valheim hides but doesn't despawn gets nuked via `ZNetView.Destroy`. Loot already spawned during the per-area damage so nothing is stolen.

### Changed (perf — re: "make large area deletes more efficient")
- **AOE spare-check cached by ZDO prefab hash.** For the duration of a single `TryAOEDestroy` call, the spare-by-Armageddon decision is cached per prefab hash. First instance pays the full multi-candidate name walk; the next 200 identical prefabs (Rock_3, Bush01, etc.) hit O(1) lookups. Cleared per call so allow/block list edits between sweeps still take effect. Big speedup for AOE=100 in dense Black Forest / Mistlands.

### Files touched
- `MegaShot/CrossbowPatches.cs` — `DeferredMineRockDestroy.DestroyRock5` strips modifiers around the DamageArea loop and adds a `ForceDestroyObject` cleanup pass. New `_aoeSpareCache` + `IsSparedAOECached` in `DestroyObjectsHelper`; `TryAOEDestroy` clears the cache per call and routes the spare check through it.

## [2.6.23] - 2026-04-28

### Fixed
- **Ashlands fallen branches (`AshlandsBranch1` / `AshlandsBranch2` / `AshlandsBranch3`) destroyable.** Showed up in the v2.6.22 dump as unmatched. Added `ashlandsbranch` to the allow-name list. Drops `Ashwood`, which survives the v2.6.19 prefix junk-suppression (only `wood` / `stone` / `grausten` prefixes are eaten; `Ashwood` starts with "Ash").

### Notes
- Charred ruins, Dvergr town walls, cave-rock decor, gravestones, Stonehenge `Stone1_huge`, fish, ferns, dropped items (Wood, Blackwood, Resin, Stone) all show up in the unmatched log but are correctly spared per Milord's "ground clutter only" spec — they're not on the destroy list.

### Files touched
- `MegaShot/CrossbowPatches.cs` — `AllowedSubstrings` += `ashlandsbranch`.

## [2.6.22] - 2026-04-28

### Changed
- **Yggdrasil shoots (`YggaShoot1`, `YggaShoot2`, etc.) destroyable.** Removed `yggashoot` and `yggdrasil` from the block-name list per Milord's spec — the shoots break and drop `YggdrasilWood`. The junk-drop prefix suppression only catches names starting with `wood` / `stone` / `grausten`; `YggdrasilWood` starts with "Yggdrasil" so the drop survives.

### Files touched
- `MegaShot/CrossbowPatches.cs` — `BlockedSubstrings` -= `yggashoot`, `yggdrasil`.

## [2.6.21] - 2026-04-28

### Fixed
- **Small trees in Meadows + Black Forest still standing** even though the spare gate cleared them. Two stale Armageddon-skip paths from the original "spare all trees" era:
  - **AOE TreeBase branch** in `TryAOEDestroy` was explicitly skipping damage when `isArmageddon` was true (just adding to `processedRoots` to prevent re-processing). Now damages on both paths — the upstream spare gate is the source of truth, and full-grown trees never reach here in Armageddon.
  - **`ApplyBeamHit` had no TreeBase / TreeLog branch at all.** Direct beam hits on a small tree did nothing locally; only the AOE fallback could maybe catch it (and it was bugged too). Added explicit `TreeBase.Damage()` and `TreeLog.Damage()` calls so direct hits land.

Drops (seeds, ashwood, etc.) flow through vanilla `m_dropWhenDestroyed` — only `Wood` itself gets junk-suppressed by the v2.6.19 prefix match; `BeechSeeds`, `BirchSeeds`, `FirCone`, `PineCone`, `Ashwood` are preserved.

### Files touched
- `MegaShot/CrossbowPatches.cs` — `TryAOEDestroy` TreeBase branch damages on Armageddon too. `ApplyBeamHit` adds `TreeBase` / `TreeLog` direct-damage calls.

## [2.6.20] - 2026-04-28

### Fixed
- **Mistlands rock formations + Ashlands cliffs spared.** The v2.6.19 ZDO-prefab dump revealed prefab names that weren't on the allowlist: `MistlandRockFormation`, `MistlandRockFormation_2_Fractured`, `cliff_mistlands1_frac`, `cliff_mistlands2_frac`, `cliff_ashlands5/6`, `BigRock_cell.*`, `HeathRockPillar`. Added `mistlandrockformation`, `cliff_mistlands`, `cliff_ashlands`, `bigrock`, `heathrockpillar` to the allow-name list.
- **Black Forest small firs (`FirTree_small`, `FirTree_small_dead`) spared.** These don't carry a `TreeBase` component, so v2.6.18's TreeBase branch never fired. Added `_small`, `_dead`, `sapling` as fallback name patterns to the allow list — anything matching those substrings (and not blocked by the block list / component spare) destroys.

### Changed (perf)
- **FX suppression window extended 0.3s → 5s.** Big MineRock5s use a deferred destroy that keeps spawning sub-area shatter / dust / sound effects for several seconds after the beam stops. The old 0.3s window only covered live beam-fire ticks, so post-beam fracture FX spammed and froze the game. Loot-bearing effects (m_destroyedEffect prefabs containing ItemDrop) are still preserved via the cached `EffectListHasItemDrop` check.

### Files touched
- `MegaShot/CrossbowPatches.cs` — `AllowedSubstrings` += `bigrock`, `mistlandrockformation`, `heathrockpillar`, `cliff_mistlands`, `cliff_ashlands`, `_small`, `_dead`, `sapling`. `ArmageddonSuppression.IsBeamFiringNow()` window extended to 5s.

## [2.6.19] - 2026-04-28

### Fixed
- **Mistlands MineRock5 still spared.** For some Mistlands rocks every name candidate resolved to `___MineRock5 m_meshFilter` — the runtime-renamed internal GO that hosts the ZNetView. The actual prefab name (e.g. `rock_mistlands1`) wasn't recoverable from the live hierarchy. Same root cause for Ashlands Grausten. **Fix:** new candidate source — `ZNetView → ZDO → GetPrefab(hash) → ZNetScene.GetPrefab(hash).name` resolves the canonical prefab name from Valheim's prefab registry regardless of runtime renames. Mistlands rocks and Grausten mounds now match their allow patterns.
- **Mistlands `rock_mistlands1` / `rock_mistlands2` etc.** added to the allow-name list (was missing — only `rock1_…rock4_` patterns were there, which don't match the `rock_mistlands` naming).
- **`Pickable_DolmenTreasure` defense in depth.** Added `pickable_` to the block-name list so prefabs named `Pickable_*` are spared even when the actual `Pickable` component lives outside `GetComponentInParent`'s walk.
- **Ashwood / FineWood / Roundlog drops were being eaten by junk-drop suppression.** The substring match on `wood` caught every wood-suffix prefab. Switched to prefix match — only GO names that START WITH `wood` / `stone` / `grausten` (and their stack variants) are suppressed. Ashwood / FineWood / Roundlog / ElderBark / AshlandsWood / Yggdrasil_wood now drop normally during Armageddon sweeps.

### Changed
- **Diagnostics now write to BepInEx LogOutput.log** instead of the separate `MegaShot_Diagnostic.txt`. Goes through the standard MegaLoad LogOutput export — no more copy-paste from a side file. Tagged as `[Info: MegaShot.Diag]` / `[Warning: MegaShot.Diag]`.

### Files touched
- `MegaShot/CrossbowPatches.cs` — `DiagnosticHelper` rewritten around `BepInEx.Logging.ManualLogSource`. `CollectNameCandidates` adds `ResolvePrefabNameFromZDO` as another source. `AllowedSubstrings` += `rock_mistlands`, `BlockedSubstrings` += `pickable_`. `junkPrefabSubstrings` → `junkPrefabPrefixes` with `StartsWith` matching.

## [2.6.18] - 2026-04-28

### Fixed
- **Small fir trees still standing.** v2.6.17 detected `FirTree_small` correctly as a small-tree variant but then *fell through* to the allow-name list — which had no pattern matching `FirTree_small` — so default-spare ate them. TreeBase branch now decides definitively: small/sapling/dead → allow destroy, full-grown → spare. No fall-through.
- **Fallen old logs (`FirTree_oldLog`) spared.** The `_log` allow pattern is a literal substring, but `FirTree_oldLog` has `_oldLog` (underscore between `Tree` and `old`, not between `old` and `Log`). Added `oldlog`, `log_` patterns alongside `_log` and `fallenlog` so all log-naming conventions hit.
- **Pine_tree | FirTree_small mixed-candidate case.** Multi-component objects could have one parent named `Pine_tree` (full-grown) and another `FirTree_small` (small) — the previous TreeBase check used only the TreeBase owner GO and missed the sibling small-tree signal. Now uses the unified candidate list.

### Files touched
- `MegaShot/CrossbowPatches.cs` — `IsSparedByArmageddon` rewritten: builds candidates once at the top, reuses for TreeBase small detection and allow/block matching. New `ContainsAny(candidates, markers)` helper. `oldlog` + `log_` added to `AllowedSubstrings`.

## [2.6.17] - 2026-04-28

### Fixed
- **Mistlands rocks were still spared after v2.6.16's prefab-name fix.** The diagnostic dump showed why: Valheim renames a MineRock5's internal mesh-filter GameObject to `___MineRock5 m_meshFilter` and parks the `ZNetView` on it — so walking the parent chain to the ZNetView root pulls that runtime-internal name, not the prefab name. Meanwhile the actual prefab signal (`Rock_3_…`) lives on the *collider* GO. Single-name resolution misses one or the other.
  - Now matches against a **list of name candidates** built from the collider GO + each parent component's owner GO (`MineRock5`, `MineRock`, `Destructible`, `TreeBase`, `ZNetView`) + `transform.parent` + `transform.root`. If ANY candidate matches an allow pattern, destroy. If ANY matches a block pattern, spare.
- **WearNTear hybrid rocks no longer spared.** Some Mistlands ground prefabs carry both WearNTear and a MineRock / MineRock5 / Destructible — the v2.6.15 blanket WearNTear spare ate them. Now only spares WearNTear when the same root has no clutter component (true structures: fortresses, dungeon walls, player builds).

### Changed
- **Diagnostic spam reduced.** `ARMAG-SPARE(unmatched): …` is now deduped per unique candidate-set per session (instead of writing 100×/sec while the beam sweeps over identical objects). Removed the per-tick `BEAM-SPARE: <colliderName>` line since the unmatched-once diagnostic carries the same info.

### Files touched
- `MegaShot/CrossbowPatches.cs` — `ArmageddonTargetFilter.IsSparedByArmageddon` rewritten around `CollectNameCandidates` + multi-candidate match; WearNTear branch checks for clutter-component co-residence; per-key dedupe via `_loggedUnmatched`; `ApplyBeamHit` no longer writes `BEAM-SPARE`.

## [2.6.16] - 2026-04-28

### Fixed
- **Armageddon was sparing everything** because the new allowlist used `go.name` from `hit.collider.gameObject` — that's typically a child node ("Collider", "model", "area_0"), not the prefab root. With name-substring matching against the wrong string, no allow pattern ever fired and v2.6.15 turned into a no-op. Now resolves the canonical prefab name via `ZNetView.GetPrefabName()` (with a transform-root fallback). Direct hits and AOE on rocks/shrubs/grausten/mountain stone destroy as intended.
- **Black Forest small firs (and other small / sapling / dead trees) now destroyable.** v2.6.15's blanket TreeBase spare was too coarse — Milord's spec has small trees / saplings on the destroy list. TreeBase now spares full-grown trees only; prefab names containing `small`, `sapling`, or `_dead` fall through to the allow path so `FirTree_small1`, `Beech_small2`, `Sapling_Pine`, etc. take damage. Full trees (`FirTree`, `Beech1`, `Pine_tree`, `Oak1`, `SwampTree*`, `AncientTree`) still spared.

### Added
- **Diagnostic line for unmatched targets.** When DebugMode is on, any prefab name that hits the spare-by-default branch (no allow, no block, not a component-spare) gets logged as `ARMAG-SPARE(unmatched): <name>`. Send the dump if anything Milord expects to be destroyed survives — we'll allowlist the name.

### Files touched
- `MegaShot/CrossbowPatches.cs` — `ArmageddonTargetFilter.ResolvePrefabName()` helper added; `IsSparedByArmageddon` switched to it; TreeBase branch now consults the small/sapling/dead name signal before sparing.

## [2.6.15] - 2026-04-28

### Changed
- **Armageddon target gate flipped from blocklist to strict allowlist.** Per Milord's spec, Armageddon now destroys ground clutter only — foliage shrubs, generic rocks, mountain / Mistlands stone, Grausten mounds, fallen logs, and tree stumps. Everything else is spared by default. Previously the gate enumerated specific things to spare and destroyed the rest, so each new bug ("fortresses still falling", "skeletal remains gone", "soft tissue gone", "black marble eaten") meant adding another opt-out. The allowlist makes spare-by-default the rule.

### Fixed
- **Ashlands Fortresses no longer fall under Armageddon — period.** v2.6.10/v2.6.11 only allowlisted three fortress pieces (`Gate_Door`, `Ashland_Stair`, `Ashlands_Wall_2x2_top`), but those pieces propagated structural-support failures through the rest of the fortress and the whole thing collapsed anyway. `WearNTear.Damage` Prefix now hard-blocks every Armageddon hit on every WearNTear piece, the AOE loop's WearNTear branch is gated on `!isArmageddon`, and `ApplyBeamHit` no longer calls `wnt.Damage()` on Armageddon hits. Three layers of defence, no fortress damage. ALT-fire's `IsDestroyableWorldPiece` allowlist is unchanged (you can still demolish fortress doors/stairs with ALT bolts).
- **Skeletal remains and soft tissue sources spared.** Added `bone`, `softtissue`, `soft_tissue`, `ashlandsbone`, `ashlandstorment`, `hairstrand`, `charred_` to the block-name list so Ashlands biomass MineRocks survive Armageddon hits.
- **Obsidian and black marble spared.** Added `marble`, `blackmarble`, `black_marble` to the block-name list. Obsidian was already there.
- **Frost-cave / crypt / dungeon decor spared by default** via the new allowlist — frost-cave loot run no longer eats every wall, stalagmite, and ice block when you sweep the beam.
- **Fallen logs (TreeLog) now destroyable in Armageddon.** Per Milord's spec — fallen logs are explicit ground clutter. `TreeLog` parents bypass the spare gate; the AOE loop's `TreeLog` branch damages on both ALT-fire and Armageddon paths now.

### Files touched
- `MegaShot/CrossbowPatches.cs` — `ArmageddonTargetFilter` rewritten as allowlist, `PatchBuildingDamage.Prefix` hard-blocks all Armageddon WearNTear hits, `ApplyBeamHit` drops the WearNTear damage call on Armageddon, `TryAOEDestroy` gates WearNTear branch on `!isArmageddon` and allows `TreeLog` damage in Armageddon.

## [2.6.14] - 2026-04-26

### Fixed
- **ALT-fire kills now drop everything as normal.** The AOE destroy pipeline was stamping `lastMineRockKillTime` on every MineRock / MineRock5 hit regardless of whether ALT (Destroy) or SHIFT (Armageddon) was the trigger. That opened the 15 s junk-drop suppression window on plain ALT shots, so stone/wood/grausten were getting deleted before they hit the ground. Both `MarkMineRockDestroyed()` calls in `TryAOEDestroy` are now gated on `isArmageddon` — only the Armageddon path can poison the suppression gate. ALT-fire returns to dropping the full loot.

## [2.6.11] - 2026-04-26

### Fixed
- **Ashlands Fortresses no longer vaporise under Armageddon.** The previous gate stripped chop/pickaxe on non-allowlisted WearNTear hits, but the Armageddon HitData carries 999999 on every other damage type — slash/blunt/fire/etc. — so vanilla still flattened the wall. `WearNTear.Damage` is now hard-blocked at the Prefix when the hit is Armageddon-tagged AND the piece isn't allowlisted (`Gate_Door`, `Ashland_Stair`, `Ashlands_Wall_2x2_top`). Alt-fire keeps its existing strip-and-pass behaviour because its bolt damage is small.
- **Grausten from Grausten rocks is now reliably destroyed in Armageddon.** MineRock5 fractures into sub-areas with cascading drops that can spawn well after the initial Damage() call. The 1.5 s suppression window was too narrow under Armageddon's huge AOE radius, so Grausten kept landing on the ground after the window closed. Window extended to 10 s; each fresh MineRock hit refreshes it, so a continuous beam keeps it open.

## [2.6.10] - 2026-04-22

### Fixed
- **Armageddon no longer destroys Ashlands fortress walls** (or any non-allowlisted WearNTear piece). The AOE path already gated WearNTear through `IsDestroyableWorldPiece`, but the direct beam hit in `ApplyBeamHit` was calling `wnt.Damage()` unconditionally — the Prefix stripped chop/pickaxe but left slash/blunt/fire at 999999 so walls still vaporised. Direct beam hit now uses the same allowlist (`Gate_Door`, `Ashland_Stair`, `Ashlands_Wall_2x2_top`) so only fortress doors / stairs / upper walls are demolish-able; generic fortress walls and player builds are spared. Beam hit-flash bloom also gated on the same check so non-damageable WearNTear no longer flashes orange.

## [2.6.9] - 2026-04-22

### Added
- **Grausten from Grausten rocks is now suppressed, but building-demolition Grausten is kept.** Drop suppression is now tiered: Stone and plain Wood are always swallowed; Grausten only when a `MineRock` / `MineRock5` was destroyed within the last 1.5 s. Direct beam hits and AOE destruction both stamp `lastMineRockKillTime` when a rock falls. `WearNTear` buildings never stamp it, so knocking down an Ashlands fort still nets you every Grausten it drops.

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

# Armageddon Beam — Animation & Visual Plan

**Last updated:** 2026-04-30 after v2.6.39 emergency revert
**Current baseline:** MegaShot v2.6.39 — damage logic intact, **no firing animation in any mode, no custom LineRenderer beam visual**. Lockup risk eliminated; visual flair removed pending a proper redesign.

This document is the handoff for the next session. Everything you need to skip the eight-version churn (v2.6.27 → v2.6.39) we just went through.

---

## Goal

While LMB is held in **Armageddon mode** (modifier key, defaults to LeftCtrl), the player should:

1. **Body shows a continuous Dundr cast pose** — looped/chained — never stuck on frame 0, never locking input.
2. **Some kind of beam-or-projectile-stream visual** — could be vanilla Dundr lightning bolts spawned at high rate, or a custom visual that doesn't conflict with the cast pose.
3. **Damage continues at 30 Hz** along the raycast (existing logic — don't break this).
4. **E / interact / weapon-switch must keep working** mid-fire. This is the line we cannot cross.

For Normal / Alt fire, the same cast pose should play per shot. The current v2.6.39 has no animation there either — a fresh state-aware approach should handle both modes uniformly.

---

## What We Know About the Player Animator

Confirmed via the v2.6.31 `ANIM-DIAG` dump (in [`MegaShot/CrossbowPatches.cs`](MegaShot/CrossbowPatches.cs) — it's gone now but the data lives in this doc):

- **2 layers.** Layer 0 = base (full body). Layer 1 = upper-body overlay. Layer 1's blend mode and weight curve are **unknown** — we never decompiled the controller.
- **Total 138 animator parameters.** Movement / state / per-weapon triggers.
- **Cast trigger:** `staff_lightningshot` (Trigger) — Dundr's default. Gated on `staff_charging` (Bool) — setting that BOOL puts the player in the long arm-pull DRAW state which reads as "reload after every shot" at fire rate. Don't use it for our case.
- **Better trigger candidate:** `staff_rapidfire` (Trigger) — rapid-fire-without-charge variant. Plays on **layer 1** with **clip length 0.4666667 s** (measured at runtime via `GetCurrentAnimatorStateInfo`).
- **Other candidates worth trying:** `staff_charge_attack`, `staff_fireball0`, `staff_fireball1`, `bow_fire`, `crossbow_fire` (last two not visually correct for a staff but useful as a sanity check).
- **Layer 1 weight is dropped to 0 by vanilla while sprinting.** That's why holding LeftShift during Armageddon (the original default) hid the cast even when the trigger fired correctly. v2.6.37 changed the default to LeftControl (crouch) which doesn't drop layer 1.

The full parameter list is in the v2.6.31-era log; you can re-dump at runtime with the diagnostic block that lived in `PulseFiringAnimation` before the v2.6.39 stub. Re-add it temporarily if you need to re-check.

---

## The Lockup Mechanism — Why Every Time-Based Retrigger Failed

When `ZSyncAnimation.SetTrigger("staff_rapidfire")` is called, the animator starts the 0.467 s clip on layer 1. Vanilla input gating in `Player.UpdateInput` / `Humanoid.UpdateInput` reads animator state — while a top-layer attack state is active, **E / weapon-switch / inventory hotkey-swap are gated off**. (Confirmed empirically; we never traced the exact vanilla check.)

**If we retrigger before the clip exits, the animator stays in attack state forever.** Normal/Alt at 10 rps = 100 ms between triggers + 0.467 s clip = clip never finishes → input gates never reopen. Armageddon at frame rate (60+ Hz) = same lockup, faster.

We tried throttling to `clip_length + 30 ms` (v2.6.35-38). That works mathematically — animator does get a frame to exit between cycles — but Milord still got locked, suggesting:

- Either vanilla's input gate has a **debounce** (the gate stays closed for a period after the state exits, longer than our 30 ms buffer).
- Or our throttle's 30 ms gap is too tight (animator might enter a transition-out state that vanilla still treats as "attacking" for a few frames).
- Or both.

**The time-based throttle is the wrong abstraction.** Use state instead.

---

## Recommended Approach: State-Aware Retrigger

Instead of "fire trigger every X seconds," the rule is **"fire trigger only when the animator is genuinely back to a non-attacking state."**

```csharp
// One-time setup (cache this hash)
int rapidfireHash = Animator.StringToHash("staff_rapidfire");

// In the per-frame Armageddon loop (or per-shot Normal/Alt path):
bool inRapidfire = false;
for (int layer = 0; layer < cachedAnimator.layerCount; layer++)
{
    var info = cachedAnimator.GetCurrentAnimatorStateInfo(layer);
    if (info.shortNameHash == rapidfireHash || info.fullPathHash == rapidfireHash)
    {
        inRapidfire = true;
        break;
    }
    // Also skip while *transitioning into* rapidfire — the trigger has fired
    // and the animator is mid-blend, retriggering here would restart the blend.
    if (cachedAnimator.IsInTransition(layer))
    {
        var nextInfo = cachedAnimator.GetNextAnimatorStateInfo(layer);
        if (nextInfo.shortNameHash == rapidfireHash || nextInfo.fullPathHash == rapidfireHash)
        {
            inRapidfire = true;
            break;
        }
    }
}

if (!inRapidfire)
{
    zanim.SetTrigger("staff_rapidfire");
    // optionally also a one-frame layer-weight kick if needed
}
```

This guarantees:

- The clip **always plays to completion** before the next trigger.
- The animator **always returns to a non-attacking state** between triggers.
- Vanilla input gates **reopen reliably** in those gaps, even if there's a debounce.
- Works identically for Normal/Alt (per-shot call) and Armageddon (per-frame call) without separate throttling logic.

Trade-off: there will be a small visual gap between cast cycles (the time it takes the animator to transition out and the next frame to detect "no longer in rapidfire"). At ~16 ms per frame that's typically 1-2 frames of "neutral" pose between casts. Should read as continuous casting; if it's perceptibly choppy, consider `CrossFadeInFixedTime` to chain states (but that hits the same lockup we already saw — careful).

---

## Concrete Implementation Steps

1. **Re-add `m_attackAnimation` override** in [`MegaShot/MegaShotItem.cs`](MegaShot/MegaShotItem.cs) — the block at the comment "v2.6.39: m_attackAnimation override removed" reverses cleanly. Set to `"staff_rapidfire"`.
2. **Re-add `PulseFiringAnimation`** in [`MegaShot/CrossbowPatches.cs`](MegaShot/CrossbowPatches.cs). The current stub is in the same file — replace with the state-aware version above. Remove the time-based `_lastArmageddonAnimPulse` / `GetArmageddonAnimInterval` / `ARMAGEDDON_ANIM_INTERVAL` artefacts.
3. **Re-add `SetLayerWeight(1, 1f)` in `UpdateArmageddonBeam`** as belt-and-braces. With LeftCtrl as the default ArmageddonKey, layer 1 shouldn't be suppressed, but a user who rebinds to a sprint-conflicting key still gets the cast.
4. **Test order** — and DO test each step in-game before moving on:
   - **Test A (Normal fire only):** trigger plays per shot, no lockup, animation visible.
   - **Test B (Sustained Normal fire, hold LMB ≥ 5 seconds):** still no lockup, E + weapon-switch still work mid-burst.
   - **Test C (Alt fire / Destroy mode):** same expectations as A and B.
   - **Test D (Armageddon, brief LMB tap):** animation visible, no lockup.
   - **Test E (Armageddon, sustained LMB ≥ 5 seconds):** continuous-feeling cast, E + weapon-switch still work mid-fire.
5. **DebugMode logs** to ship alongside (so the next round of feedback isn't speculation):
   - Per pulse: `ANIM: pulse — inRapidfire=<bool> layer=<n>`
   - Per skip: `ANIM: skipped — already in rapidfire on layer <n> normalizedTime=<f>`
   - First measurement: `ANIM: clip length confirmed = X.XXXs on layer N` (re-measure if/when it changes)

---

## Visual Beam Stream (Optional, Stretch)

The custom LineRenderer beam visual we removed in v2.6.39 was the original "laser cannon" aesthetic. If we want it back without conflicting with the cast pose:

- The LineRenderer is rendered separately from the animator — it can't actually override the cast pose. Milord's hypothesis ("custom beam may be overriding the animation") was a guess; it should be safe to bring back **after** the state-aware cast trigger is confirmed working in step 4.
- Code lives in `EnsureBeamRenderer`, `EmitBeamParticles`, `StopArmageddonBeam` — all still in the file but no longer called from `UpdateArmageddonBeam`. Re-wire by reverting the v2.6.39 diff in `UpdateArmageddonBeam`.
- Alternative: spawn vanilla Dundr lightning bolts at the configured fire rate instead of a custom beam. The cast animation already implies projectile output, so the bolt visuals would land naturally at the cast moments.
- Either way, only re-introduce visuals **after** the cast pose is confirmed working — don't try to debug both at once.

---

## Velocity-Affects-Effect (Queued from Earlier)

Milord's earlier note: at high `Velocity` config values, the firing effect "doesn't look right" — feels like the bolt leaves before the cast plays. He'd like the cast effect to ignore the velocity setting and use vanilla velocity for the visual.

When the cast is working, revisit this. Probably means: spawn the bolt at vanilla `m_projectileVel` for the visual portion, then accelerate to user-config velocity afterwards. Or fire two projectiles — a slow visual "ghost" bolt at vanilla speed, plus the actual hit-doer at config speed (jank but achievable).

---

## Files & Line Anchors

- [`MegaShot/CrossbowPatches.cs`](MegaShot/CrossbowPatches.cs)
  - `PulseFiringAnimation` — current stub. Replace with state-aware retrigger.
  - `UpdateArmageddonBeam` — currently raycasts + ApplyBeamHit only. Re-wire animation pulse + (optionally) beam visual here.
  - `EnsureBeamRenderer` / `EmitBeamParticles` / `StopArmageddonBeam` — beam visual machinery, currently dormant.
  - `_lastArmageddonAnimPulse` / `GetArmageddonAnimInterval` / `_rapidfireClipLength` / `_rapidfireClipMeasured` / `TryMeasureRapidfireClip` — time-based throttle leftovers, **delete** when state-aware version lands.
- [`MegaShot/MegaShotItem.cs`](MegaShot/MegaShotItem.cs)
  - `CreatePrefab` — re-add the `m_attackAnimation = "staff_rapidfire"` override here.
- [`MegaShot/MegaShotPlugin.cs`](MegaShot/MegaShotPlugin.cs)
  - `PluginVersion` const — **bump on every release**, not just `<Version>` in csproj. (Memory: `feedback-pluginversion-sync.md`.)
  - `Awake` — `Config.Reload()` is now properly guarded with `File.Exists`. Don't unguard it. (Memory: `feedback-bepinex-config-reload.md`.)

---

## Memories That Apply

- `feedback-bepinex-config-reload.md` — guard `Config.Reload()` with `File.Exists`. We already fixed it in MegaShot v2.6.38; don't regress.
- `feedback-pluginversion-sync.md` — bump the `PluginVersion` const alongside the csproj `<Version>` on every release.
- `feedback-megabugs-workflow.md` — open / update a MegaBugs ticket if this is being driven from a bug report. Use ticket `20260430-004951-964a1526` if continuing the existing animation thread.

---

## Acceptance Criteria

The next animation attempt is shippable when, with DebugMode on:

- Tests A-E above all pass.
- Log shows `ANIM: pulse` and `ANIM: skipped` lines proportional to the user's actions (one pulse per shot in Normal, one pulse per non-rapidfire frame in Armageddon, lots of skips between).
- No `Loading [Mega Shot 2.6.X]` ↔ `Mega Shot v2.6.X loaded!` mismatch in `LogOutput.log`.
- `BepInEx/config/com.rikal.megashot.cfg` regenerates correctly when deleted (the v2.6.38 fix stays).

Once those pass, ship as v2.6.40 (or whatever's next). If the visual beam comes back, that's a separate ship after the animation is locked in.

---

## What NOT To Do (Dragons Already Slain)

- Do NOT call `Animator.Play(currentState, 0, 0f)` after `SetTrigger` — cancels the just-set trigger transition. (v2.6.27.)
- Do NOT scale `Animator.speed` by fire rate — compresses the clip into invisibility. Keep at 1×. (v2.6.26.)
- Do NOT use `CrossFadeInFixedTime` per frame — restarts the state at frame 0 every call, freezes the animator on frame 0, clobbers other weapons sharing the same animator. (v2.6.28.)
- Do NOT call vanilla `Humanoid.StartAttack` to drive the animation — it plays `Attack.m_startEffect` (Dundr's "trinket" SFX) on every call without actually producing the cast animation, because the animator state machine evidently rejects the transition without additional gating. (v2.6.29-30.)
- Do NOT set `staff_charging = true` to "gate" the rapid-fire trigger — that BOOL is the CHARGE STATE itself, not a transition gate. Setting it puts the player in the slow draw pose. (v2.6.32.)
- Do NOT clear `Humanoid.m_currentAttack` via reflection on every call to bypass vanilla rate-limit — restarts the cast from frame 0 every call (this was actually the v2.6.30 lockup vector, mistakenly attributed to the throttle).
- Do NOT use a time-based throttle alone — even `clip_length + 30 ms` locks Milord. State-aware is the path.

---

**TL;DR:** Read the Recommended Approach section, implement the state-aware retrigger in `PulseFiringAnimation`, run tests A-E, ship. Everything else in this doc is the trail of breadcrumbs explaining why the obvious approaches don't work.

using HarmonyLib;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;

namespace MegaShot
{
    // =========================================================================
    // DIAGNOSTIC — writes ALT-fire / Armageddon hit info to the BepInEx
    // LogOutput.log (v2.6.19+). Previously wrote to a separate
    // MegaShot_Diagnostic.txt; merged into the main log so dumps go via the
    // standard MegaLoad LogOutput export and don't need separate copy-paste.
    // Controlled by config: 8. Debug > Enabled (default: off).
    // =========================================================================
    public static class DiagnosticHelper
    {
        private static BepInEx.Logging.ManualLogSource _logSource;
        private static BepInEx.Logging.ManualLogSource LogSource
        {
            get
            {
                if (_logSource == null)
                    _logSource = BepInEx.Logging.Logger.CreateLogSource("MegaShot.Diag");
                return _logSource;
            }
        }

        public static void Log(string message)
        {
            try
            {
                if (!MegaShotPlugin.DebugMode.Value) return;
                LogSource.LogInfo(message);
            }
            catch { }
        }

        /// <summary>Debug-only exception logging for catch blocks.</summary>
        public static void LogException(string context, Exception ex)
        {
            try
            {
                if (!MegaShotPlugin.DebugMode.Value) return;
                LogSource.LogWarning("[EX] " + context + ": " + ex.Message);
            }
            catch { }
        }
    }

    // =========================================================================
    // CROSSBOW DETECTION
    // =========================================================================
    public static class CrossbowHelper
    {
        public static bool IsCrossbow(ItemDrop.ItemData item)
        {
            return MegaShotItem.IsMegaShot(item);
        }

        public static bool IsBolt(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null) return false;
            return item.m_shared.m_name.ToLower().Contains("bolt");
        }
    }

    // =========================================================================
    // PER-PLAYER STATE
    // =========================================================================
    public class CrossbowState
    {
        public int magazineAmmo;
        public bool isReloading = false;
        public float reloadStartTime = 0f;
    }

    // =========================================================================
    // HUD (MonoBehaviour.OnGUI - VERIFIED WORKING)
    // =========================================================================
    public class CrossbowHUD : MonoBehaviour
    {
        public static bool showHUD = false;
        public static bool showScope = false;
        public static float scopeZoomLevel = 1f;
        public static string ammoText = "";
        public static string distanceText = "";
        public static string levelText = "";

        private GUIStyle ammoStyle;
        private GUIStyle distanceStyle;
        private GUIStyle levelStyle;
        private GUIStyle scopeZoomStyle;

        // Scope overlay texture (generated once, recreated on resolution change)
        private Texture2D scopeOverlay;
        private int scopeTexW;
        private int scopeTexH;

        void OnGUI()
        {
            // Scope overlay (rendered underneath HUD elements)
            if (showScope)
            {
                try
                {
                    EnsureScopeTexture();
                    if (scopeOverlay != null)
                        DrawScopeOverlay();
                }
                catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            }

            if (!showHUD) return;

            if (ammoStyle == null)
            {
                ammoStyle = new GUIStyle();
                ammoStyle.fontSize = 18;
                ammoStyle.fontStyle = FontStyle.Bold;
                ammoStyle.normal.textColor = Color.white;
                ammoStyle.alignment = TextAnchor.MiddleRight;

                distanceStyle = new GUIStyle();
                distanceStyle.fontSize = 16;
                distanceStyle.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
                distanceStyle.alignment = TextAnchor.MiddleCenter;

                levelStyle = new GUIStyle();
                levelStyle.fontSize = 16;
                levelStyle.fontStyle = FontStyle.Bold;
                levelStyle.normal.textColor = new Color(1f, 0.85f, 0.3f, 0.9f);
                levelStyle.alignment = TextAnchor.MiddleRight;

                scopeZoomStyle = new GUIStyle();
                scopeZoomStyle.fontSize = 14;
                scopeZoomStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 0.5f);
                scopeZoomStyle.alignment = TextAnchor.MiddleCenter;
            }

            float w = Screen.width;
            float h = Screen.height;

            // Weapon level - above ammo counter
            if (!string.IsNullOrEmpty(levelText))
            {
                GUI.Label(new Rect(w - 250, h - 130, 240, 30), levelText, levelStyle);
            }

            // Ammo counter - bottom right
            GUI.Label(new Rect(w - 250, h - 100, 240, 40), ammoText, ammoStyle);

            // Distance - below crosshair
            if (!string.IsNullOrEmpty(distanceText))
            {
                GUI.Label(new Rect(w / 2f - 100, h / 2f + 40, 200, 30), distanceText, distanceStyle);
            }
        }

        void OnDestroy()
        {
            if (scopeOverlay != null)
                Destroy(scopeOverlay);
        }

        private void EnsureScopeTexture()
        {
            int tw = Screen.width / 4;
            int th = Screen.height / 4;
            if (tw < 64) tw = 64;
            if (th < 64) th = 64;

            if (scopeOverlay != null && scopeTexW == tw && scopeTexH == th)
                return;

            scopeTexW = tw;
            scopeTexH = th;

            if (scopeOverlay != null)
                Destroy(scopeOverlay);

            scopeOverlay = new Texture2D(tw, th, TextureFormat.RGBA32, false);

            float cx = tw / 2f;
            float cy = th / 2f;
            float radius = th / 2f - 2f;
            float ringW = 1.5f;
            float edgeW = 3f;

            Color32[] pixels = new Color32[tw * th];
            Color32 black = new Color32(0, 0, 0, 255);
            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 ring = new Color32(30, 30, 30, 220);

            for (int y = 0; y < th; y++)
            {
                for (int x = 0; x < tw; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist > radius + edgeW)
                        pixels[y * tw + x] = black;
                    else if (dist > radius)
                    {
                        byte a = (byte)(255f * Mathf.Clamp01((dist - radius) / edgeW));
                        pixels[y * tw + x] = new Color32(0, 0, 0, a);
                    }
                    else if (dist > radius - ringW)
                        pixels[y * tw + x] = ring;
                    else
                        pixels[y * tw + x] = clear;
                }
            }

            scopeOverlay.SetPixels32(pixels);
            scopeOverlay.Apply();
        }

        private void DrawScopeOverlay()
        {
            float w = Screen.width;
            float h = Screen.height;

            // Black overlay with transparent circle (matches screen aspect ratio)
            GUI.DrawTexture(new Rect(0, 0, w, h), scopeOverlay, ScaleMode.StretchToFill);

            float cx = w / 2f;
            float cy = h / 2f;
            float scopeR = h / 2f - 8f;
            float gap = 15f;
            float lineW = 1.5f;
            float lineLen = scopeR * 0.85f;

            // Crosshair lines (dark, semi-transparent)
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(cx - lineLen, cy - lineW / 2, lineLen - gap, lineW), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + gap, cy - lineW / 2, lineLen - gap, lineW), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - lineW / 2, cy - lineLen, lineW, lineLen - gap), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - lineW / 2, cy + gap, lineW, lineLen - gap), Texture2D.whiteTexture);

            // Center dot (red)
            GUI.color = new Color(1f, 0.15f, 0.15f, 0.9f);
            float dotSize = 4f;
            GUI.DrawTexture(new Rect(cx - dotSize / 2, cy - dotSize / 2, dotSize, dotSize), Texture2D.whiteTexture);

            // Mil-dots along crosshair lines
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            float milSpacing = scopeR * 0.18f;
            float milSize = 3f;
            for (int i = 1; i <= 4; i++)
            {
                float off = i * milSpacing;
                GUI.DrawTexture(new Rect(cx - off - milSize / 2, cy - milSize / 2, milSize, milSize), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx + off - milSize / 2, cy - milSize / 2, milSize, milSize), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - milSize / 2, cy - off - milSize / 2, milSize, milSize), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - milSize / 2, cy + off - milSize / 2, milSize, milSize), Texture2D.whiteTexture);
            }

            GUI.color = Color.white;

            // Zoom magnification below scope circle
            if (scopeZoomStyle != null)
                GUI.Label(new Rect(cx - 50, cy + scopeR * 0.85f, 100, 20), $"{scopeZoomLevel:F1}x", scopeZoomStyle);
        }
    }

    // =========================================================================
    // HARMONY PATCHES - ONLY verified methods (see VALHEIM_API_VERIFIED.md)
    // =========================================================================

    // Block stamina drain for crossbows (VERIFIED: Player.UseStamina)
    [HarmonyPatch(typeof(Player), "UseStamina")]
    public static class PatchBlockStamina
    {
        public static bool Prefix(Player __instance, float v)
        {
            if (!MegaShotPlugin.ModEnabled.Value) return true;
            if (__instance != Player.m_localPlayer) return true;
            var weapon = __instance.GetCurrentWeapon();
            if (weapon != null && CrossbowHelper.IsCrossbow(weapon) && v > 0f)
                return false;
            return true;
        }
    }

    // Block vanilla attack - we handle firing ourselves (Humanoid.StartAttack - try-catch)
    // EXCEPTION: when we're explicitly invoking vanilla for animation purposes
    // (PulseFiringAnimation flips `_vanillaStartAttackAllowed` to true), let it
    // through so vanilla does the proper animator setup.
    [HarmonyPatch(typeof(Humanoid), "StartAttack")]
    public static class PatchBlockVanillaAttack
    {
        public static bool Prefix(Humanoid __instance)
        {
            try
            {
                if (!MegaShotPlugin.ModEnabled.Value) return true;
                if (__instance != Player.m_localPlayer) return true;
                var weapon = __instance.GetCurrentWeapon();
                if (weapon != null && CrossbowHelper.IsCrossbow(weapon))
                {
                    // Allow our induced StartAttack call through.
                    if (PatchPlayerUpdate._vanillaStartAttackAllowed) return true;
                    return false;
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return true;
        }
    }

    // Suppress vanilla's projectile spawn that fires off the cast animation's
    // event. We want vanilla's animator setup (StartAttack) but NOT vanilla's
    // Dundr bolt — our FireBolt handles the actual projectile with the right
    // velocity / damage / AOE. The flag is set by PulseFiringAnimation only
    // during the StartAttack call window; legitimate vanilla attack triggers
    // (which never reach a crossbow weapon anyway since Block returns false)
    // pass through unaffected.
    // (PatchSuppressVanillaProjectile removed in v2.6.30 — `m_attackProjectile`
    // null on the cloned Attack instance already prevents the spawn, and the
    // OnAttackTrigger Prefix was blocking vanilla's own state cleanup that
    // resets `m_currentAttack` when the cast finishes. Without that cleanup
    // our throttle saw "vanilla cast still running" forever and the second
    // animation never started.)

    // Block Eitr drain for MegaShot (Dundr clone normally uses Eitr)
    // Manually patched in Class1.cs (not attribute-based) — safe if UseEitr doesn't exist
    public static class PatchBlockEitr
    {
        public static bool Prefix(Player __instance, float v)
        {
            try
            {
                if (!MegaShotPlugin.ModEnabled.Value) return true;
                if (__instance != Player.m_localPlayer) return true;
                var weapon = __instance.GetCurrentWeapon();
                if (weapon != null && CrossbowHelper.IsCrossbow(weapon) && v > 0f)
                    return false;
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return true;
        }
    }

    // Block blocking stance - right-click is zoom, not block (try-catch)
    [HarmonyPatch(typeof(Humanoid))]
    public static class PatchBlockBlocking
    {
        [HarmonyPrefix]
        [HarmonyPatch("BlockAttack")]
        public static bool BlockAttack_Prefix(Humanoid __instance, ref bool __result)
        {
            try
            {
                if (!MegaShotPlugin.ModEnabled.Value) return true;
                if (__instance != Player.m_localPlayer) return true;
                var weapon = __instance.GetCurrentWeapon();
                if (weapon != null && CrossbowHelper.IsCrossbow(weapon))
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("IsBlocking")]
        public static bool IsBlocking_Prefix(Humanoid __instance, ref bool __result)
        {
            try
            {
                if (!MegaShotPlugin.ModEnabled.Value) return true;
                if (__instance != Player.m_localPlayer) return true;
                var weapon = __instance.GetCurrentWeapon();
                if (weapon != null && CrossbowHelper.IsCrossbow(weapon))
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return true;
        }
    }

    // Make crossbows indestructible (NEEDS VERIFICATION - try-catch)
    [HarmonyPatch(typeof(ItemDrop.ItemData))]
    public static class PatchDurability
    {
        [HarmonyPrefix]
        [HarmonyPatch("GetMaxDurability", new Type[] { })]
        public static bool MaxDur_Prefix(ItemDrop.ItemData __instance, ref float __result)
        {
            try
            {
                if (!MegaShotPlugin.ModEnabled.Value) return true;
                if (CrossbowHelper.IsCrossbow(__instance))
                {
                    __result = 9999999f;
                    return false;
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch("GetDurabilityPercentage")]
        public static bool DurPct_Prefix(ItemDrop.ItemData __instance, ref float __result)
        {
            try
            {
                if (!MegaShotPlugin.ModEnabled.Value) return true;
                if (CrossbowHelper.IsCrossbow(__instance))
                {
                    __result = 1f;
                    return false;
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return true;
        }
    }

    // =========================================================================
    // SUPPRESS FLOATING DAMAGE TEXT while crossbow is equipped.
    // At high fire rates the vanilla DamageText system floods the screen
    // with thousands of numbers, causing massive visual/perf issues.
    // =========================================================================
    [HarmonyPatch(typeof(DamageText), "AddInworldText")]
    public static class PatchSuppressDamageText
    {
        public static bool Prefix()
        {
            try
            {
                if (!MegaShotPlugin.ModEnabled.Value) return true;
                var player = Player.m_localPlayer;
                if (player == null) return true;
                var weapon = player.GetCurrentWeapon();
                if (weapon != null && CrossbowHelper.IsCrossbow(weapon))
                    return false;
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return true;
        }
    }

    // =========================================================================
    // SUPPRESS ZLog.Log SPAM from game code (SE_Poison, Fire, etc).
    // At high fire rates with all damage types enabled, Valheim's own
    // ZLog.Log calls flood the log with string allocations causing crashes.
    // =========================================================================
    [HarmonyPatch(typeof(ZLog), "Log")]
    public static class PatchSuppressPoisonLog
    {
        public static bool Prefix(object o)
        {
            try
            {
                if (!MegaShotPlugin.ModEnabled.Value) return true;
                var player = Player.m_localPlayer;
                if (player == null) return true;
                var weapon = player.GetCurrentWeapon();
                if (weapon != null && CrossbowHelper.IsCrossbow(weapon))
                    return false; // suppress ALL ZLog.Log while crossbow is equipped
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return true;
        }
    }

    // =========================================================================
    // MAIN MOD LOGIC - Player.Update postfix (VERIFIED)
    // =========================================================================
    [HarmonyPatch(typeof(Player), "Update")]
    public static class PatchPlayerUpdate
    {
        // State
        private static Dictionary<long, CrossbowState> states = new Dictionary<long, CrossbowState>();
        private static CrossbowHUD hudComponent;

        // Zoom / Scope
        private static bool zooming = false;
        private static float zoomLevel = 2f;
        private static float savedFOV = 65f;
        private static float savedDistance = 4f;
        private static float savedMinDistance = 1f;
        private static float savedMaxDistance = 6f;

        // Cached reflection for GameCamera distance fields
        private static FieldInfo camDistField;
        private static FieldInfo camMinDistField;
        private static FieldInfo camMaxDistField;
        private static bool camFieldsCached = false;

        // Fire timing
        private static float lastFireTime = 0f;

        // Cached animator for speed control / idle reset.
        private static Animator cachedAnimator;

        // Cached audio for reliable sound per shot
        private static AudioSource cachedAudioSource;
        private static AudioClip cachedFireClip;
        private static bool fireClipSearched = false;

        // Adaptive sound system for high fire rates
        private static float lastSoundTime = 0f;
        private const float SOUND_THROTTLE_RATE = 12f;  // Max PlayOneShot events/sec for high fire rates

        // v2.6.45: Armageddon now reuses the standard FireBolt path — gated
        // by ArmageddonFireRate (default 100 rps) instead of a parallel beam
        // pipeline. All custom beam machinery (LineRenderer, particles,
        // looping laser hum, per-frame raycast, splash) deleted. Damage flows
        // through the bolt projectile's normal hit pipeline carrying the
        // existing Armageddon sentinels (m_chop=999998, m_backstabBonus=-7777).

        // HUD throttle
        private static float lastHudUpdate = 0f;

        // Player model visibility for scope view
        private static bool playerModelHidden = false;
        private static Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode> savedShadowModes;

        private static CrossbowState GetState(Player player)
        {
            long id = player.GetPlayerID();
            if (!states.ContainsKey(id))
            {
                var s = new CrossbowState();
                s.magazineAmmo = MegaShotPlugin.GetEffectiveMagazineCapacity();
                states[id] = s;
            }
            return states[id];
        }

        // ---- Shared aim helpers (used by BOTH FireBolt and HUD) ----

        private static bool GetAimRay(out Camera cam, out Ray aimRay)
        {
            cam = null;
            aimRay = default(Ray);
            if (GameCamera.instance == null) return false;
            cam = GameCamera.instance.GetComponent<Camera>();
            if (cam == null) cam = Camera.main;
            if (cam == null) return false;
            aimRay = cam.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0f));
            return true;
        }

        /// <summary>
        /// Raycast along the crosshair direction, skipping the local player's own colliders
        /// and non-solid layers (water volumes, triggers, UI).
        /// Uses RaycastAll to find all hits, then filters out invalid targets.
        /// </summary>
        private static bool RaycastCrosshair(Ray aimRay, Player player, out RaycastHit hit, out Vector3 targetPoint)
        {
            // Exclude non-solid/invisible layers that should never block aiming
            int layerMask = ~(LayerMask.GetMask("UI", "character_trigger", "viewblock", "WaterVolume", "Water", "smoke"));

            // Get ALL hits along the ray
            RaycastHit[] hits = Physics.RaycastAll(aimRay.origin, aimRay.direction, 1000f, layerMask);

            // Sort by distance (RaycastAll doesn't guarantee order)
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            // Find first hit that is NOT the local player and NOT a trigger collider
            Transform playerRoot = player.transform.root;
            for (int i = 0; i < hits.Length; i++)
            {
                Transform hitRoot = hits[i].collider.transform.root;
                if (hitRoot == playerRoot) continue;
                if (hits[i].collider.isTrigger) continue;

                hit = hits[i];
                targetPoint = hit.point;
                return true;
            }

            // Nothing hit past player
            hit = default(RaycastHit);
            targetPoint = aimRay.origin + aimRay.direction * 500f;
            return false;
        }

        // ---- Main Update ----

        public static void Postfix(Player __instance)
        {
            if (!MegaShotPlugin.ModEnabled.Value) return;
            if (__instance != Player.m_localPlayer) return;

            // Update MegaShot recipe (needs to run even when not holding crossbow)
            MegaShotItem.UpdateRecipeForPlayer(__instance);

            // Ensure HUD component
            if (hudComponent == null)
            {
                hudComponent = __instance.gameObject.GetComponent<CrossbowHUD>();
                if (hudComponent == null)
                {
                    hudComponent = __instance.gameObject.AddComponent<CrossbowHUD>();
                }
            }

            var weapon = __instance.GetCurrentWeapon();
            if (weapon == null || !CrossbowHelper.IsCrossbow(weapon))
            {
                CrossbowHUD.showHUD = false;
                CrossbowHUD.showScope = false;
                if (zooming) ResetZoom();
                if (playerModelHidden) ShowPlayerModel();
                // Reset audio cache when leaving crossbow
                fireClipSearched = false;
                cachedFireClip = null;
                ReleaseFiringAnimation(__instance);
                // Belt-and-braces: clear bow_aim if we set it via PulseFiringAnimation
                // and the player has since switched to a non-MegaShot weapon.
                try { if (cachedAnimator != null) cachedAnimator.SetBool("bow_aim", false); } catch { }
                return;
            }

            var state = GetState(__instance);

            // === Block all input when UI is open (menu, inventory, chat, map, console, etc.) ===
            bool uiOpen = false;
            try
            {
                uiOpen = InventoryGui.IsVisible()
                    || Menu.IsVisible()
                    || TextInput.IsVisible()
                    || Minimap.IsOpen()
                    || Console.IsVisible()
                    || StoreGui.IsVisible();
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

            if (uiOpen)
            {
                if (zooming) ResetZoom();
                CrossbowHUD.showScope = false;
                ReleaseFiringAnimation(__instance);
                try { UpdateHUD(__instance, state); } catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                return;
            }

            // === ZOOM (Right Mouse) ===
            try { HandleZoom(); } catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            CrossbowHUD.showScope = zooming;
            CrossbowHUD.scopeZoomLevel = zoomLevel;

            // === RELOAD ===
            if (state.isReloading)
            {
                if (Time.time - state.reloadStartTime >= 2f)
                {
                    state.isReloading = false;
                    state.magazineAmmo = MegaShotPlugin.GetEffectiveMagazineCapacity();
                    __instance.Message(MessageHud.MessageType.Center, "<color=green>RELOADED</color>");
                }
                try { UpdateHUD(__instance, state); } catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                return;
            }

            // === FIRE ===
            // v2.6.45: single fire path. Armageddon, Destroy, and Normal all
            // route through FireBolt. Each bolt carries its own per-mode tags
            // (set inside FireBolt via the same IsArmageddonActive() / DestroyObjects
            // checks). Difference between modes:
            //   - Normal:     full-auto at FireRate, decrements magazine, reloads at 0.
            //   - Destroy:    semi-auto (one bolt per click), no rate limit.
            //   - Armageddon: full-auto at ArmageddonFireRate (default 100), unlimited ammo.
            bool armageddon = MegaShotPlugin.IsArmageddonActive();
            bool destroyMode = !armageddon
                && MegaShotPlugin.DestroyObjects.Value
                && Input.GetKey(MegaShotPlugin.DestroyObjectsKey.Value);

            // Stamp Armageddon-active timestamp for the drop suppressor (covers
            // cascading AOE chains and MineRock5 sub-area fractures whose drops
            // scatter past the raw bolt impact).
            if (armageddon && Input.GetMouseButton(0))
                ArmageddonSuppression.MarkBeamActive();

            bool fireInput = destroyMode ? Input.GetMouseButtonDown(0) : Input.GetMouseButton(0);

            if (fireInput)
            {
                if (!armageddon && state.magazineAmmo <= 0)
                {
                    state.isReloading = true;
                    state.reloadStartTime = Time.time;
                    __instance.Message(MessageHud.MessageType.Center, "<color=yellow>RELOADING</color>");
                }
                else if (destroyMode)
                {
                    // Semi-auto: one shot per click, no rate limiting.
                    state.magazineAmmo--;
                    FireBolt(__instance, weapon);
                }
                else
                {
                    // Full-auto: gated by GetEffectiveFireRate (Armageddon
                    // mode returns ArmageddonFireRate; otherwise FireRate).
                    float interval = 1f / MegaShotPlugin.GetEffectiveFireRate();
                    if (Time.time - lastFireTime >= interval)
                    {
                        lastFireTime = Mathf.Max(lastFireTime + interval, Time.time - interval);
                        if (!armageddon) state.magazineAmmo--; // Armageddon = unlimited.
                        FireBolt(__instance, weapon);
                    }
                }
            }
            else
            {
                // Idle path — release bow_aim so the player isn't permanently in
                // aim stance after MegaShot fires (PulseFiringAnimation sets it
                // true to gate the crossbow_fire transition).
                try
                {
                    if (cachedAnimator == null)
                        cachedAnimator = __instance.GetComponentInChildren<Animator>();
                    if (cachedAnimator != null)
                    {
                        if (Mathf.Abs(cachedAnimator.speed - 1f) > 0.01f)
                            cachedAnimator.speed = 1f;
                        try { cachedAnimator.SetBool("bow_aim", false); } catch { }
                    }
                }
                catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                ReleaseFiringAnimation(__instance);
            }

            try { UpdateHUD(__instance, state); } catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }

        // ---- Zoom ----

        private static void CacheCamFields()
        {
            if (camFieldsCached) return;
            camFieldsCached = true;
            var gcType = typeof(GameCamera);
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // Find distance field (could be m_distance, m_dist, etc.)
            string[] distNames = { "m_distance", "m_dist", "m_cameraDistance", "m_zoomDistance" };
            string[] minNames = { "m_minDistance", "m_minDist", "m_nearClipDist" };
            string[] maxNames = { "m_maxDistance", "m_maxDist", "m_farClipDist" };

            foreach (var n in distNames)
            {
                camDistField = gcType.GetField(n, flags);
                if (camDistField != null && camDistField.FieldType == typeof(float)) break;
                camDistField = null;
            }
            foreach (var n in minNames)
            {
                camMinDistField = gcType.GetField(n, flags);
                if (camMinDistField != null && camMinDistField.FieldType == typeof(float)) break;
                camMinDistField = null;
            }
            foreach (var n in maxNames)
            {
                camMaxDistField = gcType.GetField(n, flags);
                if (camMaxDistField != null && camMaxDistField.FieldType == typeof(float)) break;
                camMaxDistField = null;
            }
        }

        private static float GetCamFloat(FieldInfo field, float fallback)
        {
            if (field == null || GameCamera.instance == null) return fallback;
            try { return (float)field.GetValue(GameCamera.instance); } catch { return fallback; }
        }

        private static void SetCamFloat(FieldInfo field, float value)
        {
            if (field == null || GameCamera.instance == null) return;
            try { field.SetValue(GameCamera.instance, value); } catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }

        private static void HandleZoom()
        {
            if (Input.GetMouseButton(1))
            {
                if (!zooming && GameCamera.instance != null)
                {
                    CacheCamFields();
                    // Save current camera state
                    savedFOV = GameCamera.instance.m_fov;
                    savedDistance = GetCamFloat(camDistField, 4f);
                    savedMinDistance = GetCamFloat(camMinDistField, 1f);
                    savedMaxDistance = GetCamFloat(camMaxDistField, 6f);
                    zooming = true;
                    zoomLevel = MegaShotPlugin.ZoomMin.Value;
                    HidePlayerModel();
                }

                // Scroll adjusts zoom magnification, not camera distance
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    zoomLevel = Mathf.Clamp(
                        zoomLevel + scroll * 3f,
                        MegaShotPlugin.ZoomMin.Value,
                        MegaShotPlugin.ZoomMax.Value
                    );
                }

                // Lock camera to first-person scope view every frame
                if (GameCamera.instance != null)
                {
                    GameCamera.instance.m_fov = savedFOV / zoomLevel;
                    SetCamFloat(camDistField, 0f);
                    SetCamFloat(camMinDistField, 0f);
                    SetCamFloat(camMaxDistField, 0f);
                }
            }
            else if (zooming)
            {
                ResetZoom();
            }
        }

        private static void ResetZoom()
        {
            zooming = false;
            ShowPlayerModel();
            if (GameCamera.instance != null)
            {
                GameCamera.instance.m_fov = savedFOV;
                SetCamFloat(camDistField, savedDistance);
                SetCamFloat(camMinDistField, savedMinDistance);
                SetCamFloat(camMaxDistField, savedMaxDistance);
            }
        }

        // ---- Player Model Visibility (scope view) ----

        /// <summary>
        /// Hides the local player model by setting all renderers to ShadowsOnly.
        /// The character becomes invisible to the camera but still casts shadows.
        /// </summary>
        private static void HidePlayerModel()
        {
            if (playerModelHidden) return;
            try
            {
                var player = Player.m_localPlayer;
                if (player == null) return;
                var renderers = player.GetComponentsInChildren<Renderer>();
                savedShadowModes = new Dictionary<Renderer, UnityEngine.Rendering.ShadowCastingMode>();
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    if (r.GetType().Name.Contains("Particle")) continue;
                    savedShadowModes[r] = r.shadowCastingMode;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                }
                playerModelHidden = true;
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }

        /// <summary>
        /// Restores the local player model to its original rendering mode.
        /// </summary>
        private static void ShowPlayerModel()
        {
            if (!playerModelHidden) return;
            try
            {
                if (savedShadowModes != null)
                {
                    foreach (var kvp in savedShadowModes)
                    {
                        if (kvp.Key != null)
                            kvp.Key.shadowCastingMode = kvp.Value;
                    }
                    savedShadowModes = null;
                }
                playerModelHidden = false;
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }

        // ---- Fire ----

        private static void FireBolt(Player player, ItemDrop.ItemData weapon)
        {
            try
            {
            // 1. Find projectile prefab from weapon (no ammo — Dundr-based, uses weapon's own projectile)
            GameObject prefab = null;
            if (weapon.m_shared?.m_attack?.m_attackProjectile != null)
                prefab = weapon.m_shared.m_attack.m_attackProjectile;
            else if (weapon.m_shared?.m_secondaryAttack?.m_attackProjectile != null)
                prefab = weapon.m_shared.m_secondaryAttack.m_attackProjectile;

            if (prefab == null)
            {
                return;
            }

            // 2. Aim: raycast from CAMERA (crosshair origin) to find target point,
            //    then aim bolt from player chest to that exact point.
            //    This ensures bolt hits exactly where the crosshair points.
            Camera cam;
            Ray aimRay;
            if (!GetAimRay(out cam, out aimRay)) return;

            Vector3 spawnPos = player.transform.position + Vector3.up * 1.5f;
            Vector3 targetPoint;
            RaycastHit hit;
            RaycastCrosshair(aimRay, player, out hit, out targetPoint);

            Vector3 aimDir = (targetPoint - spawnPos).normalized;

            // 3. Spawn projectile
            GameObject proj = UnityEngine.Object.Instantiate(prefab, spawnPos, Quaternion.LookRotation(aimDir));
            if (proj == null) return;

            Projectile projectile = proj.GetComponent<Projectile>();
            if (projectile == null)
            {
                UnityEngine.Object.Destroy(proj);
                return;
            }

            // 4. Velocity
            var attack = weapon.m_shared?.m_attack;
            if (attack == null) { UnityEngine.Object.Destroy(proj); return; }
            float speed = attack.m_projectileVel * (MegaShotPlugin.GetEffectiveVelocity() / 100f);
            Vector3 velocity = aimDir * speed;

            // 5. Damage — Split system (no ammo, weapon-only)
            // Total damage from per-level values, split evenly across enabled types,
            // scaled by DamageMultiplier. e.g. Level 1 (240), all 8 types, mult 2: 2*(240/8) = 60 per type.
            HitData hitData = new HitData();
            float totalDamage = MegaShotItem.GetTotalDamage(weapon.m_quality);
            float overallMult = MegaShotPlugin.DamageMultiplier.Value;

            // ALT-fire (destroy mode) or Armageddon (Shift) quadruples damage
            bool armageddon = MegaShotPlugin.IsArmageddonActive();
            bool destroyMode = armageddon || (MegaShotPlugin.DestroyObjects.Value &&
                Input.GetKey(MegaShotPlugin.DestroyObjectsKey.Value));
            if (destroyMode)
                overallMult *= 4f;

            // Count enabled damage types
            bool pierce = MegaShotPlugin.DamagePierce.Value;
            bool blunt = MegaShotPlugin.DamageBlunt.Value;
            bool slash = MegaShotPlugin.DamageSlash.Value;
            bool fire = MegaShotPlugin.DamageFire.Value;
            bool frost = MegaShotPlugin.DamageFrost.Value;
            bool lightning = MegaShotPlugin.DamageLightning.Value;
            bool poison = MegaShotPlugin.DamagePoison.Value;
            bool spirit = MegaShotPlugin.DamageSpirit.Value;

            int typeCount = 0;
            if (pierce) typeCount++;
            if (blunt) typeCount++;
            if (slash) typeCount++;
            if (fire) typeCount++;
            if (frost) typeCount++;
            if (lightning) typeCount++;
            if (poison) typeCount++;
            if (spirit) typeCount++;
            if (typeCount == 0) { typeCount = 1; lightning = true; } // fallback: at least lightning (Dundr native)

            float perType = totalDamage * overallMult / typeCount;

            hitData.m_damage.m_damage = 0f;
            // Split damage across enabled types
            hitData.m_damage.m_pierce = pierce ? perType : 0f;
            hitData.m_damage.m_blunt = blunt ? perType : 0f;
            hitData.m_damage.m_slash = slash ? perType : 0f;
            hitData.m_damage.m_fire = fire ? perType : 0f;
            hitData.m_damage.m_frost = frost ? perType : 0f;
            hitData.m_damage.m_lightning = lightning ? perType : 0f;
            hitData.m_damage.m_poison = poison ? perType : 0f;
            hitData.m_damage.m_spirit = spirit ? perType : 0f;

            hitData.m_damage.m_chop = 0f;
            hitData.m_damage.m_pickaxe = 0f;
            if (weapon.m_shared != null)
                hitData.m_skill = weapon.m_shared.m_skillType;

            // Tag bolt for object destruction if ALT-fire or Armageddon mode.
            // Armageddon uses 999998 marker (vs 999999 normal) so downstream code
            // can spare trees/logs while still destroying everything else.
            if (destroyMode)
            {
                float marker = armageddon ? 999998f : 999999f;
                hitData.m_damage.m_chop = marker;
                hitData.m_damage.m_pickaxe = marker;
                if (armageddon)
                    hitData.m_backstabBonus = -7777f; // sentinel: armageddon — preserved through TryApplyDestroyDamage
            }

            // v2.6.46: Armageddon mode = guaranteed-kill damage on direct hit
            // AND on AOE splash. Mirrors the old ApplyBeamHit damage payload
            // (999999 per type) so a single bolt obliterates the target plus
            // every creature within ArmageddonAoeRadius. PatchCrossbowAOE
            // copies hit.m_damage onto each splash hit, so just setting the
            // bolt's damage massively also handles AOE.
            if (armageddon)
            {
                hitData.m_damage.m_damage    = 999999f;
                hitData.m_damage.m_blunt     = 999999f;
                hitData.m_damage.m_slash     = 999999f;
                hitData.m_damage.m_pierce    = 999999f;
                hitData.m_damage.m_fire      = 999999f;
                hitData.m_damage.m_frost     = 999999f;
                hitData.m_damage.m_lightning = 999999f;
                hitData.m_damage.m_poison    = 999999f;
                hitData.m_damage.m_spirit    = 999999f;
                hitData.m_toolTier           = 9999;
            }

            // Stagger / knockback
            float staggerMult = MegaShotPlugin.Stagger.Value;
            try { hitData.m_pushForce = (weapon.m_shared?.m_attackForce ?? 0f) * staggerMult; } catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            try { hitData.m_staggerMultiplier = staggerMult; } catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

            // 6. Setup projectile (VERIFIED: 6-parameter overload, no ammo)
            // Ensure rigidbody is non-kinematic before Setup sets velocity
            try
            {
                Rigidbody rbSetup = proj.GetComponent<Rigidbody>();
                if (rbSetup != null && rbSetup.isKinematic)
                    rbSetup.isKinematic = false;
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            projectile.Setup(player, velocity, 0f, hitData, weapon, weapon);

            // Set tool tier AFTER Setup via reflection (Setup may overwrite from item data)
            if (destroyMode)
            {
                try
                {
                    var tierField = typeof(Projectile).GetField("m_toolTier", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (tierField != null) tierField.SetValue(projectile, (short)9999);
                }
                catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

                // Spawn HouseFire at impact point on ANY hit (if enabled).
                // Armageddon skips HouseFire — too easy to torch the whole base.
                if (!armageddon && MegaShotPlugin.HouseFireEnabled.Value)
                {
                    try
                    {
                        projectile.m_onHit = (OnProjectileHit)System.Delegate.Combine(
                            projectile.m_onHit,
                            new OnProjectileHit((Collider col, Vector3 hitPoint, bool water) =>
                            {
                                if (!water)
                                {
                                    HouseFireHelper.SpawnFire(hitPoint);
                                }
                            }));
                    }
                    catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                }

                // Diagnostic: log component types on ALT-fire hit target to file
                try
                {
                    projectile.m_onHit = (OnProjectileHit)System.Delegate.Combine(
                        projectile.m_onHit,
                        new OnProjectileHit((Collider col, Vector3 hitPoint, bool water) =>
                        {
                            try
                            {
                                if (col == null || water) return;
                                var go = col.gameObject;
                                if (go == null) return;
                                string info = "HIT: " + go.name;
                                var root = go.transform.root != null ? go.transform.root.gameObject : go;
                                if (root != go) info += " root:" + root.name;
                                if (go.GetComponentInParent<WearNTear>() != null) info += " [WNT]";
                                if (go.GetComponentInParent<Destructible>() != null) info += " [Destr]";
                                if (go.GetComponentInParent<Door>() != null) info += " [Door]";
                                var piece = go.GetComponentInParent<Piece>();
                                if (piece != null)
                                {
                                    info += " [Piece:" + piece.m_name + "]";
                                    try { info += piece.IsPlacedByPlayer() ? " PLAYER" : " WORLD"; }
                                    catch { info += " unk"; }
                                }
                                else
                                {
                                    info += " [NoPiece]";
                                }
                                DiagnosticHelper.Log(info);
                            }
                            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                        }));
                }
                catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

                // Fish catching: ALT-fire on a fish catches it at level 5, grants fishing skill
                if (MegaShotPlugin.FishCatching.Value)
                {
                    try
                    {
                        projectile.m_onHit = (OnProjectileHit)System.Delegate.Combine(
                            projectile.m_onHit,
                            new OnProjectileHit((Collider col, Vector3 hitPoint, bool water) =>
                            {
                                try
                                {
                                    if (col == null) return;

                                    Fish fish = col.GetComponentInParent<Fish>();
                                    if (fish == null) return;

                                    Player localPlayer = Player.m_localPlayer;
                                    if (localPlayer == null) return;

                                    // Fish objects carry an ItemDrop component with the pickup item
                                    ItemDrop itemDrop = fish.gameObject.GetComponent<ItemDrop>();
                                    if (itemDrop?.m_itemData?.m_shared == null) return;

                                    // Set quality to creature level 5
                                    int maxQuality = itemDrop.m_itemData.m_shared.m_maxQuality > 0
                                        ? itemDrop.m_itemData.m_shared.m_maxQuality
                                        : 5;
                                    itemDrop.m_itemData.m_quality = Mathf.Min(5, maxQuality);

                                    // Add to player inventory
                                    Inventory inv = localPlayer.GetInventory();
                                    if (inv == null) return;

                                    if (!inv.AddItem(itemDrop.m_itemData))
                                    {
                                        localPlayer.Message(MessageHud.MessageType.Center, "$inventory_full");
                                        return;
                                    }

                                    // Raise fishing skill by 1 point (no-op if already maxed)
                                    try { localPlayer.RaiseSkill(Skills.SkillType.Fishing, 1f); }
                                    catch { }

                                    // Standard pickup message (top-left with icon)
                                    localPlayer.Message(MessageHud.MessageType.TopLeft,
                                        "$msg_added " + itemDrop.m_itemData.m_shared.m_name,
                                        1, itemDrop.m_itemData.GetIcon());

                                    // Remove the fish from the world
                                    ZNetView nview = fish.GetComponent<ZNetView>();
                                    if (nview == null) nview = fish.GetComponentInParent<ZNetView>();
                                    if (nview != null && nview.IsValid())
                                    {
                                        nview.ClaimOwnership();
                                        nview.Destroy();
                                    }
                                    else
                                    {
                                        UnityEngine.Object.Destroy(fish.gameObject);
                                    }
                                }
                                catch (Exception ex) { DiagnosticHelper.LogException("FishCatch", ex); }
                            }));
                    }
                    catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                }
            }

            // 7. AOE � we handle AOE ourselves in PatchCrossbowAOE (Character.Damage postfix)
            // so that splash emanates from the actual IMPACT POINT, not the bolt's transform.position.
            // Valheim's built-in m_aoe uses transform.position which at 940 m/s can be far past the hit.
            // Destroy mode AOE is handled separately by DestroyMineRock5Areas.
            try { projectile.m_aoe = 0f; } catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

            // 7b. Extend ZDO range � Valheim's zone system destroys network objects
            // beyond ~64-100m from the player. m_distant=true extends sync range,
            // m_persistent keeps it from being cleaned up, and setting the ZDO type
            // to Prioritized gives it maximum active range. Applies to ALL bolts.
            try
            {
                var nview = proj.GetComponent<ZNetView>();
                if (nview != null)
                {
                    nview.m_distant = true;
                    nview.m_persistent = true;
                    // Set ZDO type for extended range (must be done after ZDO is created)
                    if (nview.GetZDO() != null)
                    {
                        nview.GetZDO().SetType(ZDO.ObjectType.Prioritized);
                    }
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

            // 9. Physics - gravity and collision detection
            if (MegaShotPlugin.NoGravity.Value)
            {
                // Projectile component gravity (Valheim's custom gravity system)
                try { projectile.m_gravity = 0f; } catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            }

            // Always set CCD and Rigidbody for high-speed bolts
            try
            {
                Rigidbody rb = proj.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    if (MegaShotPlugin.NoGravity.Value)
                    {
                        rb.useGravity = false;
                        rb.linearDamping = 0f;
                    }
                    // CCD prevents fast bolts from tunneling through thin colliders
                    // At 940 m/s the bolt moves ~15m per frame - without CCD it phases through objects
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

            // 10. Animation — fire Dundr's cast trigger via ZSyncAnimation
            // (vanilla pipeline). Per-shot retrigger plus natural 1× speed.
            PulseFiringAnimation(player, attack);

            // 11. Adaptive sound system — two tiers based on fire rate:
            //     ≤12 rps: per-shot effects (all 3 Valheim fallback attempts, like vanilla)
            //     >12 rps: throttled overlapping PlayOneShot (~12/sec on one AudioSource)
            //     Overlapping one-shots naturally blend into continuous "brrrrt" fire sound.
            //     12 PlayOneShot/sec with a ~0.3-0.5s clip = ~4-6 overlapping voices = smooth.
            //
            // v2.6.45: Armageddon's laser-hum loop is gone — it now uses the
            // throttled bolt-fire SFX (which at 100 rps blends into a near-
            // continuous brrrrt without needing a separate looping clip).
            try
            {
                float fireRate = MegaShotPlugin.GetEffectiveFireRate();

                // Ensure fire clip is always cached
                if (!fireClipSearched)
                {
                    fireClipSearched = true;
                    cachedFireClip = FindFireClip(attack, weapon);
                }

                // v2.6.47: in Armageddon, the throttle uses ArmageddonSoundRate
                // (default 24, range 1-60) for a denser thunder-roar; otherwise
                // the constant 12/sec keeps Normal mode tame.
                float effectiveSoundRate = armageddon
                    ? (float)MegaShotPlugin.ArmageddonSoundRate.Value
                    : SOUND_THROTTLE_RATE;

                if (fireRate > effectiveSoundRate)
                {
                    // --- THROTTLED MODE: high fire rates (>effectiveSoundRate rps) ---
                    // PlayOneShot capped at effectiveSoundRate. Overlapping tails
                    // create a continuous sound naturally — no looping needed.
                    float soundInterval = 1f / effectiveSoundRate;
                    if (Time.time - lastSoundTime >= soundInterval)
                    {
                        lastSoundTime = Time.time;
                        if (cachedFireClip != null)
                        {
                            EnsureCachedAudioSource(player);
                            if (cachedAudioSource != null)
                                cachedAudioSource.PlayOneShot(cachedFireClip);
                        }
                    }
                }
                else
                {
                    // --- DISCRETE MODE: low fire rates (≤12 rps) ---
                    // Full per-shot sound with all fallback attempts (current behaviour)
                    bool soundPlayed = false;

                    // Attempt 1: EffectList.Create (Valheim's native effect system)
                    if (!soundPlayed) soundPlayed = TryPlayEffect(attack, "m_triggerEffect", spawnPos, aimDir, player.transform);
                    if (!soundPlayed) soundPlayed = TryPlayEffect(attack, "m_startEffect", spawnPos, aimDir, player.transform);
                    if (!soundPlayed) soundPlayed = TryPlayEffect(attack, "m_hitEffect", spawnPos, aimDir, player.transform);

                    // Attempt 2: Direct AudioSource.PlayOneShot
                    if (!soundPlayed && cachedFireClip != null)
                    {
                        EnsureCachedAudioSource(player);
                        if (cachedAudioSource != null)
                        {
                            cachedAudioSource.PlayOneShot(cachedFireClip);
                            soundPlayed = true;
                        }
                    }

                    // Attempt 3: Instantiate effect prefabs directly
                    if (!soundPlayed)
                    {
                        soundPlayed = TryInstantiateEffectPrefabs(attack, spawnPos, aimDir);
                    }
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

            } // end top-level try
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }

        // ---- Animation + Audio Helpers ----

        private static void EnsureCachedAudioSource(Player player)
        {
            if (cachedAudioSource != null) return;
            cachedAudioSource = player.gameObject.GetComponent<AudioSource>();
            if (cachedAudioSource == null)
            {
                cachedAudioSource = player.gameObject.AddComponent<AudioSource>();
                cachedAudioSource.spatialBlend = 1f;
                cachedAudioSource.maxDistance = 50f;
                cachedAudioSource.rolloffMode = AudioRolloffMode.Linear;
            }
        }

        // Flags retained so PatchBlockVanillaAttack still compiles.
        // Both unused by current PulseFiringAnimation (which goes via
        // ZSyncAnimation.SetTrigger only — see v2.6.31 history in the
        // Animation Fix plan).
        internal static bool _vanillaStartAttackAllowed = false;
        internal static bool _suppressVanillaProjectile = false;
        private static FieldInfo _zanimField;
        private static bool _zanimFieldCached = false;
        private static FieldInfo _zanimAnimatorField;
        private static bool _zanimAnimatorFieldCached = false;
        private static bool _animDiagDumped = false;
        private static int _animProbeCounter = 0;

        private static string ProbeFirstClipName(Animator a, int layer)
        {
            try
            {
                var clips = a.GetCurrentAnimatorClipInfo(layer);
                if (clips == null || clips.Length == 0) return "(empty)";
                return clips[0].clip != null ? clips[0].clip.name : "?";
            }
            catch { return "(err)"; }
        }

        // PulseFiringAnimation — fires the weapon's m_attackAnimation trigger
        // through ZSyncAnimation. v2.6.44 confirmed this puts layer 0 into
        // the "Fire Crossbow" state when bow_aim=true is set first.
        private static void PulseFiringAnimation(Player player, Attack attack)
        {
            if (player == null || attack == null) return;
            try
            {
                if (!_zanimFieldCached)
                {
                    _zanimField = typeof(Character).GetField("m_zanim", BindingFlags.NonPublic | BindingFlags.Instance);
                    _zanimFieldCached = true;
                }
                if (_zanimField == null) return;
                var zanim = _zanimField.GetValue(player) as ZSyncAnimation;
                if (zanim == null) return;

                if (!_zanimAnimatorFieldCached)
                {
                    _zanimAnimatorField = typeof(ZSyncAnimation).GetField("m_animator", BindingFlags.NonPublic | BindingFlags.Instance)
                                       ?? typeof(ZSyncAnimation).GetField("m_animator", BindingFlags.Public | BindingFlags.Instance);
                    _zanimAnimatorFieldCached = true;
                }
                Animator anim = (_zanimAnimatorField != null) ? (_zanimAnimatorField.GetValue(zanim) as Animator) : null;
                if (anim == null) anim = player.GetComponentInChildren<Animator>();
                cachedAnimator = anim;

                if (cachedAnimator != null && Mathf.Abs(cachedAnimator.speed - 1f) > 0.01f)
                    cachedAnimator.speed = 1f;

                if (cachedAnimator != null && cachedAnimator.layerCount > 1)
                    cachedAnimator.SetLayerWeight(1, 1f);

                string trig = attack.m_attackAnimation;
                if (string.IsNullOrEmpty(trig)) return;

                // Vanilla crossbow_fire transitions are gated on bow_aim=true.
                if (cachedAnimator != null)
                {
                    try { cachedAnimator.SetBool("bow_aim", true); } catch { }
                }

                zanim.SetTrigger(trig);

                if (MegaShotPlugin.DebugMode != null && MegaShotPlugin.DebugMode.Value)
                {
                    DiagnosticHelper.Log("ANIM: SetTrigger('" + trig + "') bow_aim=true");
                    if (!_animDiagDumped && cachedAnimator != null)
                    {
                        _animDiagDumped = true;
                        try { AnimatorDeepDiag(cachedAnimator, trig); }
                        catch (Exception ex2) { DiagnosticHelper.LogException("MegaShot", ex2); }
                    }
                    _animProbeCounter++;
                    if (_animProbeCounter >= 30 && cachedAnimator != null)
                    {
                        _animProbeCounter = 0;
                        try
                        {
                            string l0 = ProbeFirstClipName(cachedAnimator, 0);
                            string l1 = cachedAnimator.layerCount > 1 ? ProbeFirstClipName(cachedAnimator, 1) : "(no L1)";
                            DiagnosticHelper.Log("ANIM-PROBE: L0=" + l0 + " L1=" + l1 + " bow_aim=" + cachedAnimator.GetBool("bow_aim"));
                        }
                        catch (Exception ex2) { DiagnosticHelper.LogException("MegaShot", ex2); }
                    }
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }

        // Stub — kept for call-site compatibility.
        private static void ReleaseFiringAnimation(Player player) { }

        // One-shot deep diagnostic — fires once on first DebugMode pulse.
        private static void AnimatorDeepDiag(Animator a, string ourTrigger)
        {
            DiagnosticHelper.Log("ANIM-DIAG: animator=" + a.name + " layers=" + a.layerCount + " parameterCount=" + a.parameterCount);
            for (int i = 0; i < a.layerCount; i++)
            {
                var s = a.GetCurrentAnimatorStateInfo(i);
                string clipNames = "(none)";
                try
                {
                    var clips = a.GetCurrentAnimatorClipInfo(i);
                    if (clips != null && clips.Length > 0)
                    {
                        var sb = new System.Text.StringBuilder();
                        for (int k = 0; k < clips.Length; k++)
                        {
                            if (k > 0) sb.Append(",");
                            sb.Append(clips[k].clip != null ? clips[k].clip.name : "?");
                            sb.Append("@").Append(clips[k].weight.ToString("0.00"));
                        }
                        clipNames = sb.ToString();
                    }
                }
                catch { }
                DiagnosticHelper.Log("ANIM-DIAG: layer=" + i
                    + " name=" + a.GetLayerName(i)
                    + " weight=" + a.GetLayerWeight(i)
                    + " stateHash=" + s.fullPathHash
                    + " shortHash=" + s.shortNameHash
                    + " norm=" + s.normalizedTime
                    + " len=" + s.length
                    + " inTransition=" + a.IsInTransition(i)
                    + " clips=[" + clipNames + "]");
            }
            for (int p = 0; p < a.parameterCount; p++)
            {
                var par = a.GetParameter(p);
                string val = "?";
                try
                {
                    switch (par.type)
                    {
                        case AnimatorControllerParameterType.Bool:    val = a.GetBool(par.nameHash) ? "true" : "false"; break;
                        case AnimatorControllerParameterType.Float:   val = a.GetFloat(par.nameHash).ToString("0.000"); break;
                        case AnimatorControllerParameterType.Int:     val = a.GetInteger(par.nameHash).ToString(); break;
                        case AnimatorControllerParameterType.Trigger: val = "(trig)"; break;
                    }
                }
                catch { }
                DiagnosticHelper.Log("ANIM-DIAG: param[" + p + "] " + par.type + " '" + par.name + "'=" + val);
            }
        }

        // ---- Sound Helpers ----


        private static readonly Dictionary<string, FieldInfo> _fieldCache = new Dictionary<string, FieldInfo>();
        private static FieldInfo GetCachedField(Type type, string fieldName)
        {
            var key = type.FullName + "." + fieldName;
            if (!_fieldCache.TryGetValue(key, out var fi))
            {
                fi = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
                _fieldCache[key] = fi;
            }
            return fi;
        }

        private static bool TryPlayEffect(object source, string fieldName, Vector3 pos, Vector3 dir, Transform parent)
        {
            try
            {
                var field = GetCachedField(source.GetType(), fieldName);
                if (field == null) return false;
                var el = field.GetValue(source) as EffectList;
                if (el?.m_effectPrefabs == null || el.m_effectPrefabs.Length == 0) return false;
                bool hasValid = false;
                foreach (var ep in el.m_effectPrefabs)
                    if (ep.m_prefab != null) { hasValid = true; break; }
                if (!hasValid) return false;
                var created = el.Create(pos, Quaternion.LookRotation(dir), parent, 1f);
                if (created != null && created.Length > 0)
                {
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// Searches all EffectList fields on Attack, SharedData, and ammo for AudioClips.
        /// Checks ZSFX (Valheim custom audio) and AudioSource components.
        /// </summary>
        private static AudioClip FindFireClip(Attack attack, ItemDrop.ItemData weapon)
        {
            // Sources to search: attack, weapon shared data (no ammo)
            object[] sources = new object[] { attack, weapon.m_shared };

            foreach (var source in sources)
            {
                try
                {
                    foreach (var f in source.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (f.FieldType != typeof(EffectList)) continue;
                        var el = f.GetValue(source) as EffectList;
                        if (el?.m_effectPrefabs == null) continue;
                        foreach (var ep in el.m_effectPrefabs)
                        {
                            if (ep.m_prefab == null) continue;

                            // Try ZSFX (Valheim's randomized audio component)
                            try
                            {
                                var zsfx = ep.m_prefab.GetComponent<ZSFX>();
                                if (zsfx != null)
                                {
                                    // ZSFX stores clips in m_audioClips
                                    var clipsField = GetCachedField(typeof(ZSFX), "m_audioClips");
                                    if (clipsField != null)
                                    {
                                        var clips = clipsField.GetValue(zsfx) as AudioClip[];
                                        if (clips != null && clips.Length > 0 && clips[0] != null)
                                        {
                                            return clips[0];
                                        }
                                    }
                                }
                            }
                            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

                            // Try regular AudioSource
                            try
                            {
                                var audioSrc = ep.m_prefab.GetComponentInChildren<AudioSource>();
                                if (audioSrc != null && audioSrc.clip != null)
                                {
                                    return audioSrc.clip;
                                }
                            }
                            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                        }
                    }
                }
                catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            }
            return null;
        }

        /// <summary>
        /// Last resort: directly instantiate effect prefabs (they may auto-play sounds via ZSFX).
        /// </summary>
        private static bool TryInstantiateEffectPrefabs(Attack attack, Vector3 pos, Vector3 dir)
        {
            try
            {
                foreach (var f in typeof(Attack).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (f.FieldType != typeof(EffectList)) continue;
                    var el = f.GetValue(attack) as EffectList;
                    if (el?.m_effectPrefabs == null) continue;
                    foreach (var ep in el.m_effectPrefabs)
                    {
                        if (ep.m_prefab == null) continue;
                        var go = UnityEngine.Object.Instantiate(ep.m_prefab, pos, Quaternion.LookRotation(dir));
                        if (go != null)
                        {
                            UnityEngine.Object.Destroy(go, 3f);
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return false;
        }

        // ---- HUD ----

        private static void UpdateHUD(Player player, CrossbowState state)
        {
            if (Time.time - lastHudUpdate < 0.1f)
            {
                CrossbowHUD.showHUD = true;
                return;
            }
            lastHudUpdate = Time.time;

            // Distance: raycast from CAMERA (crosshair), measure from player to hit point.
            float range = -1f;
            Camera cam;
            Ray aimRay;
            if (GetAimRay(out cam, out aimRay))
            {
                RaycastHit hit;
                Vector3 targetPoint;
                if (RaycastCrosshair(aimRay, player, out hit, out targetPoint))
                {
                    Vector3 playerPos = player.transform.position + Vector3.up * 1.5f;
                    range = Vector3.Distance(playerPos, targetPoint);
                }
            }

            // Format HUD text
            string zoomStr = zooming ? $" | {zoomLevel:F1}x" : "";

            // Show weapon quality level (Valheim's star UI caps at 4, actual quality can be 8)
            var weapon = player.GetCurrentWeapon();
            if (weapon != null && MegaShotItem.IsMegaShot(weapon))
            {
                int quality = weapon.m_quality;
                int maxQuality = weapon.m_shared != null ? weapon.m_shared.m_maxQuality : 4;
                CrossbowHUD.levelText = $"MegaShot Lv.{quality}/{maxQuality}";
            }
            else
            {
                CrossbowHUD.levelText = "";
            }

            if (state.isReloading)
            {
                CrossbowHUD.ammoText = "RELOADING...";
                CrossbowHUD.distanceText = "";
            }
            else
            {
                CrossbowHUD.ammoText = $"{state.magazineAmmo}/{MegaShotPlugin.GetEffectiveMagazineCapacity()}{zoomStr}";
                CrossbowHUD.distanceText = range > 0 ? $"{range:F0}m" : "";
            }
            CrossbowHUD.showHUD = true;
        }
    }

    // =========================================================================
    // BUILDING DAMAGE - WearNTear patch for crossbow bolts
    // Buildings are EXCLUDED from destroy mode — ALT-mode spawns HouseFire instead.
    // =========================================================================
    [HarmonyPatch(typeof(WearNTear), "Damage")]
    public static class PatchBuildingDamage
    {
        private static bool isApplyingSpread = false;

        // Track whether the current hit was destroy-tagged so Postfix can spawn fire
        private static bool wasDestroyTagged = false;
        private static Vector3 savedHitPoint;

        // Track destroyable world-piece destruction (fortress doors, stairs, etc.)
        private static bool wasWorldPieceDestroy = false;
        private static HitData.DamageModifiers savedDamageModifiers;

        // Prefab name substrings that are destroyable in ALT mode.
        // Only specific fortress pieces — NOT floors or main structural elements.
        // Add new substrings here to expand what ALT-fire can destroy.
        // NOTE: order matters — longer/more-specific patterns must come BEFORE shorter ones
        // to avoid substring false positives (e.g. "Wall_2x2_top" before "Wall_2x2").
        private static readonly string[] DestroyablePrefabPatterns = new string[]
        {
            "Gate_Door",             // Ashlands_Fortress_Gate_Door — fortress entrance doors
            "Ashland_Stair",         // Ashland_Stair — fortress stairs (note: no trailing 's')
            "Ashlands_Wall_2x2_top", // Ashlands_Wall_2x2_top — upper wall section (stair foundation)
        };

        /// <summary>
        /// Checks if a WearNTear piece is a specific destroyable world piece
        /// (fortress doors, stairs, etc.) rather than a generic wall/floor.
        /// Only pieces whose prefab name contains one of the allowed patterns are destroyable.
        /// Player-built pieces are always protected.
        /// </summary>
        public static bool IsDestroyableWorldPiece(WearNTear wnt)
        {
            try
            {
                // Player-built pieces are never destroyable
                var piece = wnt.GetComponent<Piece>();
                if (piece != null && piece.IsPlacedByPlayer())
                    return false;

                string goName = wnt.gameObject.name;
                for (int i = 0; i < DestroyablePrefabPatterns.Length; i++)
                {
                    if (goName.IndexOf(DestroyablePrefabPatterns[i], StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return false;
        }

        public static bool Prefix(WearNTear __instance, HitData hit)
        {
            try
            {
                if (!MegaShotPlugin.ModEnabled.Value) return true;
                if (hit == null) return true;

                // --- Armageddon hard-block (v2.6.15): zero WearNTear damage ---
                // Per Milord's spec, Armageddon never damages structures —
                // fortresses, dungeon walls, player builds. The previous
                // IsDestroyableWorldPiece allowlist (Gate_Door / Ashland_Stair /
                // Ashlands_Wall_2x2_top) cascaded structural-support failures
                // through the rest of the fortress, so the whole thing fell.
                // Block at the source: if it's an Armageddon hit, refuse.
                if (DestroyObjectsHelper.IsArmageddonHit(hit))
                {
                    return false;
                }

                // --- Player-built buildings are EXCLUDED from destroy mode ---
                // World-generated structures (Charred Fortress, dungeons, etc.)
                // ARE destroyable via ALT-fire bolts (kept unchanged from
                // pre-2.6.15 behaviour).
                if (DestroyObjectsHelper.IsDestroyTagged(hit))
                {
                    bool canDestroy = IsDestroyableWorldPiece(__instance);

                    // Diagnostic: log WearNTear decision to file
                    try
                    {
                        string diag = "WNT: " + __instance.gameObject.name + (canDestroy ? " =DESTROY" : " =SKIP");
                        diag += " hp=" + __instance.m_health;
                        diag += " tier=" + __instance.m_minToolTier;
                        DiagnosticHelper.Log(diag);
                    }
                    catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

                    if (canDestroy)
                    {
                        // World structure: clear damage modifiers (bypass Immune/VeryResistant),
                        // save them for restoration in Postfix, then apply full destroy damage.
                        wasWorldPieceDestroy = true;
                        savedHitPoint = hit.m_point;
                        savedDamageModifiers = __instance.m_damages;
                        __instance.m_damages = new HitData.DamageModifiers();
                        DestroyObjectsHelper.TryApplyDestroyDamage(hit);
                        return true;
                    }

                    // Alt-fire on player-built / non-whitelisted: strip destroy tags
                    // so vanilla damage goes through with the bolt's per-type values
                    // (small/moderate). HouseFire spawn happens in Postfix.
                    wasDestroyTagged = true;
                    savedHitPoint = hit.m_point;
                    // Strip destroy-level damage so the building takes normal hit
                    hit.m_damage.m_chop = 0f;
                    hit.m_damage.m_pickaxe = 0f;
                    return true;
                }

                if (hit.m_skill != Skills.SkillType.Crossbows) return true;

                // --- Building damage multiplier ---
                float buildMult = MegaShotPlugin.BuildingDamage.Value;
                if (buildMult > 1f)
                {
                    hit.m_damage.m_damage *= buildMult;
                    hit.m_damage.m_blunt *= buildMult;
                    hit.m_damage.m_slash *= buildMult;
                    hit.m_damage.m_pierce *= buildMult;
                    hit.m_damage.m_chop *= buildMult;
                    hit.m_damage.m_pickaxe *= buildMult;
                    hit.m_damage.m_fire *= buildMult;
                    hit.m_damage.m_frost *= buildMult;
                    hit.m_damage.m_lightning *= buildMult;
                    hit.m_damage.m_poison *= buildMult;
                    hit.m_damage.m_spirit *= buildMult;
                }

                // --- Fire damage to buildings (Ashlands fire behavior) ---
                float fireMult = MegaShotPlugin.BuildingFireDamage.Value;
                if (fireMult > 0f)
                {
                    float fireDmg = 10f * fireMult;
                    hit.m_damage.m_fire = Mathf.Max(hit.m_damage.m_fire, fireDmg);
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return true;
        }

        public static void Postfix(WearNTear __instance, HitData hit)
        {
            try
            {
                if (!MegaShotPlugin.ModEnabled.Value) return;
                if (hit == null) return;

                // --- World-generated structure destroyed in ALT mode ---
                if (wasWorldPieceDestroy)
                {
                    wasWorldPieceDestroy = false;
                    try { __instance.m_damages = savedDamageModifiers; }
                    catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                    try { DestroyObjectsHelper.ForceDestroyObject(__instance, "WearNTear(World)"); }
                    catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                    try { DestroyObjectsHelper.TryAOEDestroy(hit, savedHitPoint); }
                    catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                    return;
                }

                // --- Destroy-tagged bolt hit a building: handled by Projectile.m_onHit ---
                if (wasDestroyTagged)
                {
                    wasDestroyTagged = false;
                    return;
                }

                if (hit.m_skill != Skills.SkillType.Crossbows) return;
                if (isApplyingSpread) return;

                float fireMult = MegaShotPlugin.BuildingFireDamage.Value;
                if (fireMult <= 0f) return;

                TryApplyAshlandsFire(__instance);

                if (fireMult >= 2f)
                {
                    float spreadRadius = fireMult;
                    ApplyFireSpread(__instance, spreadRadius, fireMult);
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }

        /// <summary>
        /// Try to trigger Ashlands fire behavior on a WearNTear piece via reflection.
        /// Ashlands added fire fields to WearNTear - we try to find and activate them.
        /// Caches discovered method/field on first hit to avoid per-call reflection.
        /// </summary>
        private static MethodInfo _cachedIgniteMethod;
        private static bool _cachedIgniteIsBoolean;
        private static bool _igniteMethodSearched;
        private static FieldInfo _cachedDurationField;
        private static bool _durationFieldSearched;

        private static void TryApplyAshlandsFire(WearNTear wnt)
        {
            try
            {
                float durationMult = MegaShotPlugin.BuildingFireDuration.Value;
                var wntType = typeof(WearNTear);

                // Discover ignite method once, cache for future calls
                if (!_igniteMethodSearched)
                {
                    _igniteMethodSearched = true;
                    string[] igniteMethods = { "Ignite", "SetFire", "StartFire", "RPC_Ignite" };
                    foreach (var methodName in igniteMethods)
                    {
                        var method = wntType.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (method != null)
                        {
                            var parms = method.GetParameters();
                            if (parms.Length == 0 || (parms.Length == 1 && parms[0].ParameterType == typeof(bool)))
                            {
                                _cachedIgniteMethod = method;
                                _cachedIgniteIsBoolean = parms.Length == 1;
                                break;
                            }
                        }
                    }
                }

                // Invoke cached ignite method
                if (_cachedIgniteMethod != null)
                {
                    try
                    {
                        if (_cachedIgniteIsBoolean)
                            _cachedIgniteMethod.Invoke(wnt, new object[] { true });
                        else
                            _cachedIgniteMethod.Invoke(wnt, null);
                    }
                    catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                }

                // Discover duration field once, cache for future calls
                if (!_durationFieldSearched)
                {
                    _durationFieldSearched = true;
                    string[] durationFields = { "m_burnTime", "m_fireDuration", "m_burnDuration", "m_ashDamageTimer" };
                    foreach (var fieldName in durationFields)
                    {
                        var field = wntType.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (field != null && field.FieldType == typeof(float))
                        {
                            _cachedDurationField = field;
                            break;
                        }
                    }
                }

                // Apply duration multiplier via cached field
                if (_cachedDurationField != null)
                {
                    try
                    {
                        float current = (float)_cachedDurationField.GetValue(wnt);
                        _cachedDurationField.SetValue(wnt, current * durationMult);
                    }
                    catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }

        /// <summary>
        /// Spread fire damage to nearby building pieces within radius.
        /// </summary>
        private static void ApplyFireSpread(WearNTear source, float radius, float fireMult)
        {
            try
            {
                isApplyingSpread = true;

                int pieceMask = LayerMask.GetMask("piece", "piece_nonsolid");
                Collider[] nearby = Physics.OverlapSphere(source.transform.position, radius, pieceMask);

                float spreadFireDmg = 5f * fireMult; // Spread does less than direct hit
                int spreadCount = 0;

                foreach (var col in nearby)
                {
                    if (col == null) continue;
                    var wnt = col.GetComponentInParent<WearNTear>();
                    if (wnt == null || wnt == source) continue;

                    // Apply fire damage to nearby piece
                    HitData fireHit = new HitData();
                    fireHit.m_damage.m_fire = spreadFireDmg;
                    wnt.Damage(fireHit);

                    TryApplyAshlandsFire(wnt);
                    spreadCount++;

                    // Cap spread per shot to avoid performance issues
                    if (spreadCount >= 10) break;
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            finally
            {
                isApplyingSpread = false;
            }
        }
    }

    // =========================================================================
    // CROSSBOW AOE - Apply splash damage from the bolt's IMPACT POINT.
    // Valheim's built-in Projectile.m_aoe applies AOE from the bolt's
    // transform.position, which at high velocities (940 m/s = ~15m/frame)
    // is far past the actual collision point. This patch uses hit.m_point
    // (the real impact location) as the AOE center instead.
    // =========================================================================
    [HarmonyPatch(typeof(Character), "Damage")]
    public static class PatchCrossbowAOE
    {
        private static bool isApplyingAOE = false;

        public static void Postfix(Character __instance, HitData hit)
        {
            try
            {
                if (isApplyingAOE) return;
                if (!MegaShotPlugin.ModEnabled.Value) return;
                if (hit == null) return;
                if (hit.m_skill != Skills.SkillType.Crossbows) return;
                if (!DestroyObjectsHelper.IsDestroyTagged(hit)) return;

                float radius = DestroyObjectsHelper.GetAoeRadiusForHit(hit);
                if (radius <= 0f) return;

                Vector3 impactPoint = hit.m_point;
                if (impactPoint == Vector3.zero) return;

                isApplyingAOE = true;

                // OverlapSphere at the IMPACT POINT — only check character layers
                int layerMask = LayerMask.GetMask("character", "character_net", "character_ghost", "character_noenv");
                Collider[] nearby = Physics.OverlapSphere(impactPoint, radius, layerMask);

                Character attacker = hit.GetAttacker();
                HashSet<int> alreadyHit = new HashSet<int>();
                alreadyHit.Add(__instance.GetInstanceID()); // skip the direct-hit target
                if (attacker != null) alreadyHit.Add(attacker.GetInstanceID()); // skip the player

                foreach (var col in nearby)
                {
                    if (col == null) continue;
                    var character = col.GetComponentInParent<Character>();
                    if (character == null) continue;
                    if (!alreadyHit.Add(character.GetInstanceID())) continue;
                    // Spare tames, other players, player-raised undead, and Dvergr —
                    // same rule Valheim uses for AI aggro (attacker.IsEnemy(target)).
                    if (ArmageddonSuppression.IsFriendlyToBeam(character, attacker)) continue;

                    // Build splash damage from the original hit — use a different skill
                    // to prevent the AOE/DoT postfixes from running again on splash targets
                    HitData splashHit = new HitData();
                    splashHit.m_damage = hit.m_damage;
                    splashHit.m_point = character.GetCenterPoint();
                    splashHit.m_dir = (character.transform.position - impactPoint).normalized;
                    splashHit.m_skill = Skills.SkillType.None; // NOT Crossbows — prevents re-triggering AOE/DoT
                    try { splashHit.m_pushForce = hit.m_pushForce; } catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                    try { splashHit.m_staggerMultiplier = hit.m_staggerMultiplier; } catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                    try { splashHit.SetAttacker(attacker); } catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

                    character.Damage(splashHit);
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            finally
            {
                isApplyingAOE = false;
            }
        }
    }

    // =========================================================================
    // ELEMENTAL DoT - Patch Character.Damage to scale status effect duration
    // when a crossbow bolt hits. This is the SOLE source of DoT scaling.
    // The elemental damage on HitData uses only the config multiplier (no DoT).
    // This patch scales the resulting status effects (TTL + damage pool).
    // DoT=0: no modification (default Valheim behavior)
    // DoT=1+: multiply burn/poison duration AND per-tick damage
    // =========================================================================
    [HarmonyPatch(typeof(Character), "Damage")]
    public static class PatchCharacterDamageDoT
    {
        private static bool IsElementalEffect(StatusEffect se)
        {
            if (se == null) return false;
            string name = (se.m_name ?? "").ToLower();
            return name.Contains("burn") || name.Contains("fire") ||
                   name.Contains("poison") || name.Contains("frost") ||
                   name.Contains("lightning") || name.Contains("spirit");
        }

        public static void Postfix(Character __instance, HitData hit)
        {
            try
            {
                if (!MegaShotPlugin.ModEnabled.Value) return;
                if (hit == null) return;
                if (hit.m_skill != Skills.SkillType.Crossbows) return;

                float dotMult = MegaShotPlugin.ElementalDoT.Value;
                if (dotMult <= 0f) return;

                // Check if there's any elemental damage on this hit
                bool hasElemental = hit.m_damage.m_fire > 0 || hit.m_damage.m_frost > 0 ||
                                    hit.m_damage.m_lightning > 0 || hit.m_damage.m_poison > 0 ||
                                    hit.m_damage.m_spirit > 0;
                if (!hasElemental) return;

                var seman = __instance.GetSEMan();
                if (seman == null) return;

                var effects = seman.GetStatusEffects();
                if (effects == null) return;

                foreach (var se in effects)
                {
                    if (!IsElementalEffect(se)) continue;

                    // Scale TTL (duration)
                    se.m_ttl *= dotMult;

                    // Scale damage pool (SE_Burning.m_damage is HitData.DamageTypes, not float)
                    try
                    {
                        var dmgField = se.GetType().GetField("m_damage", BindingFlags.Public | BindingFlags.Instance);
                        if (dmgField != null && dmgField.FieldType == typeof(HitData.DamageTypes))
                        {
                            var dmg = (HitData.DamageTypes)dmgField.GetValue(se);
                            dmg.m_fire *= dotMult;
                            dmg.m_frost *= dotMult;
                            dmg.m_lightning *= dotMult;
                            dmg.m_poison *= dotMult;
                            dmg.m_spirit *= dotMult;
                            dmgField.SetValue(se, dmg);
                        }
                    }
                    catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

                    // Also try float damage fields (other SE types)
                    string[] floatDmgFields = { "m_damagePerHit", "m_damagePerTick", "m_fireDamage", "m_poisonDamage" };
                    foreach (var fieldName in floatDmgFields)
                    {
                        try
                        {
                            var field = se.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (field != null && field.FieldType == typeof(float))
                            {
                                float val = (float)field.GetValue(se);
                                if (val > 0)
                                    field.SetValue(se, val * dotMult);
                            }
                        }
                        catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                    }
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }
    }

    // =========================================================================
    // BOLT STACK SIZE - Set all bolt items to stack up to 1000
    // =========================================================================
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    public static class PatchBoltStackSize
    {
        public static void Postfix(ObjectDB __instance)
        {
            try
            {
                if (!MegaShotPlugin.ModEnabled.Value) return;
                if (__instance.m_items == null) return;

                foreach (var prefab in __instance.m_items)
                {
                    if (prefab == null) continue;
                    var itemDrop = prefab.GetComponent<ItemDrop>();
                    if (itemDrop == null) continue;
                    if (CrossbowHelper.IsBolt(itemDrop.m_itemData))
                    {
                        itemDrop.m_itemData.m_shared.m_maxStackSize = 1000;
                    }
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }
    }

    // =========================================================================
    // DEFERRED MINE ROCK DESTRUCTION
    // Calling rock.Damage() from within a Harmony Postfix doesn't work �
    // Valheim's RPCs get lost in the re-entrant call context. This component
    // waits 2 frames then calls Damage() on each sub-area from a clean stack,
    // so RPCs process normally and drops/effects spawn as expected.
    // Works for both MineRock5 (large deposits) and MineRock (small deposits).
    // =========================================================================
    public class DeferredMineRockDestroy : MonoBehaviour
    {
        private float aoeRadius;
        private Vector3 impactPoint;
        private int frameDelay = 2;

        public void Setup(float radius, Vector3 impact)
        {
            aoeRadius = radius;
            impactPoint = impact;
        }

        void Update()
        {
            if (frameDelay-- > 0) return;

            try
            {
                DestroyObjectsHelper.isDestroyingAreas = true;

                // --- MineRock5 (large multi-area deposits: copper, silver, etc.) ---
                var rock5 = GetComponent<MineRock5>();
                if (rock5 != null)
                {
                    DestroyRock5(rock5);
                    Destroy(this);
                    return;
                }

                // --- MineRock (small single deposits: tin, obsidian, etc.) ---
                var rock = GetComponent<MineRock>();
                if (rock != null)
                {
                    HitData hit = CreateDestroyHit(rock.transform.position);
                    DestroyObjectsHelper.isDeferredDamage = true;
                    rock.Damage(hit);
                    DestroyObjectsHelper.isDeferredDamage = false;
                    Destroy(this);
                    return;
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            finally
            {
                DestroyObjectsHelper.isDestroyingAreas = false;
            }

            Destroy(this);
        }

        private void DestroyRock5(MineRock5 rock)
        {
            // Claim ownership so we have authority
            try
            {
                var nview = rock.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid() && !nview.IsOwner())
                    nview.ClaimOwnership();
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

            // Find DamageArea(int hitAreaIndex, HitData hit) � the internal method
            // that directly damages a specific area by index, synchronously.
            // Calling rock.Damage() sends RPCs that never resolve; DamageArea bypasses that.
            var damageAreaMethod = typeof(MineRock5).GetMethod("DamageArea",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (damageAreaMethod == null)
            {
                DestroyObjectsHelper.ForceDestroyObject(rock, "MineRock5(no-DamageArea)");
                return;
            }

            // Access m_hitAreas to find each sub-area's collider
            var hitAreasField = typeof(MineRock5).GetField("m_hitAreas",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (hitAreasField == null)
            {
                DestroyObjectsHelper.ForceDestroyObject(rock, "MineRock5(deferred-fallback)");
                return;
            }

            var hitAreas = hitAreasField.GetValue(rock) as System.Collections.IList;
            if (hitAreas == null || hitAreas.Count == 0)
            {
                DestroyObjectsHelper.ForceDestroyObject(rock, "MineRock5(deferred-empty)");
                return;
            }

            Type hitAreaType = hitAreas[0].GetType();
            var colField = hitAreaType.GetField("m_collider",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            // Strip damage modifiers to bypass Immune / VeryResistant on
            // Mistlands MineRock5s — the chunked-but-not-destroyed bug
            // (v2.6.24). DamageArea applies the area's resists *before*
            // health subtraction; if the area is Immune, even 999999 turns
            // into 0. Save and restore so other code paths see normal modifiers.
            HitData.DamageModifiers savedMods = default;
            bool modsSaved = false;
            try
            {
                savedMods = rock.m_damageModifiers;
                rock.m_damageModifiers = new HitData.DamageModifiers();
                modsSaved = true;
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

            int areasHit = 0;
            try
            {
                for (int i = 0; i < hitAreas.Count; i++)
                {
                    var area = hitAreas[i];
                    if (area == null) continue;

                    Collider col = null;
                    if (colField != null)
                        col = colField.GetValue(area) as Collider;

                    if (col == null || !col.enabled) continue;

                    if (aoeRadius <= 1f)
                    {
                        float dist = Vector3.Distance(impactPoint, col.bounds.center);
                        if (dist > aoeRadius) continue;
                    }

                    HitData areaHit = CreateDestroyHit(col.bounds.center);
                    try
                    {
                        damageAreaMethod.Invoke(rock, new object[] { i, areaHit });
                        areasHit++;
                    }
                    catch { }
                }
            }
            finally
            {
                if (modsSaved)
                {
                    try { rock.m_damageModifiers = savedMods; }
                    catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                }
            }

            // Cleanup pass — if anything visible survived (areas with disabled
            // colliders, areas the reflection missed, sub-meshes Valheim hides
            // post-DamageArea but doesn't despawn), nuke the whole prefab via
            // ZNetView.Destroy. Loot already spawned during the DamageArea
            // calls so we're not stealing drops.
            try
            {
                var nv = rock.GetComponent<ZNetView>();
                if (nv != null && nv.IsValid() && rock.gameObject != null)
                {
                    DestroyObjectsHelper.ForceDestroyObject(rock, "MineRock5(deferred-cleanup)");
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }

        private static HitData CreateDestroyHit(Vector3 point)
        {
            HitData hit = new HitData();
            hit.m_point = point;
            hit.m_damage.m_damage = 999999f;
            hit.m_damage.m_blunt = 999999f;
            hit.m_damage.m_slash = 999999f;
            hit.m_damage.m_pierce = 999999f;
            hit.m_damage.m_chop = 999999f;
            hit.m_damage.m_pickaxe = 999999f;
            hit.m_damage.m_fire = 999999f;
            hit.m_damage.m_frost = 999999f;
            hit.m_damage.m_lightning = 999999f;
            hit.m_damage.m_poison = 999999f;
            hit.m_damage.m_spirit = 999999f;
            hit.m_toolTier = 9999;
            return hit;
        }
    }

    // =========================================================================
    // DESTROY OBJECTS - Crossbow bolts instantly destroy resource objects
    // Covers: trees, logs, rocks, copper, tin, silver, obsidian, flametal,
    //         and any other mineable/choppable world objects.
    // Requires DestroyObjects=true AND modifier key held when bolt was fired.
    // Bolts are tagged at fire time with chop/pickaxe=999999 on HitData.m_damage
    // (reliably preserved by Projectile) and projectile.m_toolTier=9999.
    // Also applies AOE destruction using the configured AOE radius.
    // =========================================================================
    public static class DestroyObjectsHelper
    {
        private static bool isApplyingAOE = false;
        internal static bool isDestroyingAreas = false;
        internal static bool isDeferredDamage = false;

        // v2.6.24: per-AOE-call cache of "is this prefab spared?" keyed by
        // ZDO prefab hash. With AOE=100 a single sweep can hit hundreds of
        // colliders for the same handful of prefabs; without the cache the
        // multi-candidate name walk runs on each one. Cleared at the top of
        // each TryAOEDestroy invocation.
        private static readonly Dictionary<int, bool> _aoeSpareCache = new Dictionary<int, bool>();

        private static bool IsSparedAOECached(GameObject go)
        {
            int hash = 0;
            try
            {
                var nv = go.GetComponentInParent<ZNetView>();
                if (nv != null)
                {
                    var zdo = nv.GetZDO();
                    if (zdo != null) hash = zdo.GetPrefab();
                }
            }
            catch { }

            if (hash != 0 && _aoeSpareCache.TryGetValue(hash, out bool cached))
                return cached;

            bool spared = ArmageddonTargetFilter.IsSparedByArmageddon(go);
            if (hash != 0) _aoeSpareCache[hash] = spared;
            return spared;
        }

        // Throttle: prevent DestroyMineRock5Areas from running multiple times on the same rock
        private static int lastProcessedRockId = 0;
        private static float lastProcessedRockTime = 0f;

        // Cached reflection fields for damage modifiers
        private static readonly string[] ModifierFieldNames = { "m_damageModifiers", "m_damages", "m_damageModifier" };

        /// <summary>
        /// Clears damage modifier list on any destructible via reflection.
        /// Ashlands fortress pieces use these to set Immune/VeryResistant on damage types.
        /// Returns opaque saved state for RestoreDamageModifiers(), or null if no modifiers found.
        /// </summary>
        public static object ClearDamageModifiers(Component target)
        {
            if (target == null) return null;
            try
            {
                foreach (var fname in ModifierFieldNames)
                {
                    var field = target.GetType().GetField(fname, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field == null) continue;
                    var val = field.GetValue(target);
                    if (val is System.Collections.IList list && list.Count > 0)
                    {
                        var saved = new object[list.Count];
                        list.CopyTo(saved, 0);
                        list.Clear();
                        return new KeyValuePair<FieldInfo, object[]>(field, saved);
                    }
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return null;
        }

        /// <summary>
        /// Restores damage modifiers previously saved by ClearDamageModifiers.
        /// </summary>
        public static void RestoreDamageModifiers(Component target, object savedData)
        {
            if (savedData == null || target == null) return;
            try
            {
                var pair = (KeyValuePair<FieldInfo, object[]>)savedData;
                var list = pair.Key.GetValue(target) as System.Collections.IList;
                if (list != null)
                {
                    foreach (var item in pair.Value) list.Add(item);
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }

        /// <summary>
        /// Detects destroy-tagged bolts by their massive chop/pickaxe damage values
        /// (set in FireBolt, carried by Projectile.m_damage). Boosts all damage types
        /// to 999999 and sets toolTier to pass all tier checks.
        /// </summary>
        public static bool TryApplyDestroyDamage(HitData hit)
        {
            if (!MegaShotPlugin.ModEnabled.Value) return false;
            if (hit == null) return false;
            if (!MegaShotPlugin.DestroyObjects.Value && !IsArmageddonHit(hit)) return false;

            // Detect destroy-tagged bolts by their chop/pickaxe values
            // (these are reliably preserved in Projectile.m_damage)
            if (hit.m_damage.m_chop < 999000f && hit.m_damage.m_pickaxe < 999000f)
                return false;

            // Preserve the armageddon marker (999998) so the no-trees rule survives
            // through to ForceDestroyIfNeeded / TryAOEDestroy.
            float marker = IsArmageddonHit(hit) ? 999998f : 999999f;
            hit.m_damage.m_damage = 999999f;
            hit.m_damage.m_blunt = 999999f;
            hit.m_damage.m_slash = 999999f;
            hit.m_damage.m_pierce = 999999f;
            hit.m_damage.m_chop = marker;
            hit.m_damage.m_pickaxe = marker;
            hit.m_damage.m_fire = 999999f;
            hit.m_damage.m_frost = 999999f;
            hit.m_damage.m_lightning = 999999f;
            hit.m_damage.m_poison = 999999f;
            hit.m_damage.m_spirit = 999999f;
            hit.m_toolTier = 9999;
            return true;
        }

        /// <summary>
        /// Checks if a HitData is tagged for object destruction.
        /// </summary>
        public static bool IsDestroyTagged(HitData hit)
        {
            if (hit == null) return false;
            return hit.m_damage.m_chop >= 999000f || hit.m_damage.m_pickaxe >= 999000f;
        }

        /// <summary>
        /// Armageddon Mode bolts carry a sentinel marker on m_backstabBonus
        /// (set in FireBolt). Preserved across destroy paths so trees/logs
        /// can be spared from destruction.
        /// </summary>
        public static bool IsArmageddonHit(HitData hit)
        {
            if (hit == null) return false;
            return hit.m_backstabBonus < -7776.5f && hit.m_backstabBonus > -7777.5f;
        }

        /// <summary>
        /// Returns the configured AOE radius for the given hit — Armageddon
        /// hits use the larger ArmageddonAoeRadius, otherwise standard AoeRadius.
        /// </summary>
        public static float GetAoeRadiusForHit(HitData hit) =>
            IsArmageddonHit(hit) ? (float)MegaShotPlugin.ArmageddonAoeRadius.Value : MegaShotPlugin.AoeRadius.Value;

        /// <summary>
        /// Force-destroys an object that survived our 999999 damage due to immunity/resistance.
        /// Bypasses all damage checks by directly destroying via ZNetView or setting health to 0.
        /// Skips MineRock5/MineRock � these use DeferredMineRockDestroy for proper drops.
        /// </summary>
        public static void ForceDestroyIfNeeded(Component target, HitData hit, string typeName)
        {
            if (!IsDestroyTagged(hit)) return;
            if (target == null || target.gameObject == null) return;

            // MineRock5/MineRock use deferred destruction so drops spawn properly
            if (target is MineRock5 || target is MineRock) return;

            // Buildings are excluded from destroy mode — they get HouseFire instead
            if (target is WearNTear) return;

            // Trees/logs must die through Valheim's normal path so they fall,
            // spawn logs, split into pieces, and drop wood properly
            if (target is TreeBase || target is TreeLog) return;

            // Destructibles (MuddyScrapPile, beehives, guck sacks, plain crates,
            // etc.) MUST go through Destructible.Destroy() so the event chain
            // fires: m_destroyedEffect, m_dropWhenDestroyed, m_spawnWhenDestroyed,
            // m_onDestroyed → DropOnDestroyed.OnDestroyed (the iron/leather/withered-
            // bone drops). Bare nview.Destroy() rips the GameObject out without
            // firing any of those, so dungeon scrap piles vanish without loot.
            if (target is Destructible dest)
            {
                try
                {
                    var nv = dest.GetComponent<ZNetView>();
                    if (nv == null) nv = dest.GetComponentInParent<ZNetView>();
                    if (nv != null && nv.IsValid())
                    {
                        if (!nv.IsOwner()) nv.ClaimOwnership();
                        if (nv.IsOwner())
                        {
                            // Drive health to 0 so any Destructible.Destroy guard
                            // that re-checks the field passes cleanly.
                            try { dest.m_health = 0f; }
                            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                            dest.Destroy();
                            return;
                        }
                    }
                }
                catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                // Fall through to the generic ZNetView destroy as last resort.
            }

            try
            {
                // Try setting health fields to 0 via reflection (many types use m_health)
                var healthField = target.GetType().GetField("m_health", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (healthField != null && healthField.FieldType == typeof(float))
                {
                    float hp = (float)healthField.GetValue(target);
                    if (hp > 0)
                    {
                        healthField.SetValue(target, 0f);
                    }
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

            try
            {
                // Force-destroy via ZNetView (network-safe destruction)
                var nview = target.GetComponent<ZNetView>();
                if (nview == null) nview = target.GetComponentInParent<ZNetView>();
                if (nview != null && nview.IsValid())
                {
                    // Claim ownership so we have authority to destroy
                    if (!nview.IsOwner())
                    {
                        nview.ClaimOwnership();
                    }
                    nview.Destroy();
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }

        /// <summary>
        /// Creates a destroy-level HitData for AOE spread hits.
        /// </summary>
        private static HitData CreateDestroyHitData(Vector3 hitPoint)
        {
            HitData aoeHit = new HitData();
            aoeHit.m_point = hitPoint;
            aoeHit.m_damage.m_damage = 999999f;
            aoeHit.m_damage.m_blunt = 999999f;
            aoeHit.m_damage.m_slash = 999999f;
            aoeHit.m_damage.m_pierce = 999999f;
            aoeHit.m_damage.m_chop = 999999f;
            aoeHit.m_damage.m_pickaxe = 999999f;
            aoeHit.m_damage.m_fire = 999999f;
            aoeHit.m_damage.m_frost = 999999f;
            aoeHit.m_damage.m_lightning = 999999f;
            aoeHit.m_damage.m_poison = 999999f;
            aoeHit.m_damage.m_spirit = 999999f;
            aoeHit.m_toolTier = 9999;
            return aoeHit;
        }

        /// <summary>
        /// Destroys a MineRock5 object by deferring per-area Damage() calls to
        /// the next frame. Calling rock.Damage() from within a Harmony Postfix
        /// does NOT work � Valheim's RPCs get lost in the re-entrant call context.
        /// Deferring to the next frame runs the calls on a clean stack where
        /// RPCs process normally, so drops/effects spawn as expected.
        /// </summary>
        public static void DestroyMineRock5Areas(MineRock5 rock, HitData hit, Vector3 impactPoint)
        {
            if (isDestroyingAreas) return;
            if (!IsDestroyTagged(hit)) return;
            if (rock == null) return;

            // Throttle: only process each rock once per second
            int rockId = rock.GetInstanceID();
            if (rockId == lastProcessedRockId && Time.time - lastProcessedRockTime < 1f)
                return;
            lastProcessedRockId = rockId;
            lastProcessedRockTime = Time.time;

            float radius = IsArmageddonHit(hit)
                ? 9999f // Armageddon: vaporise the entire rock, no sub-area filter
                : MegaShotPlugin.AoeRadius.Value;
            // AoeRadius.Value is float; ArmageddonAoeRadius.Value is int — both handled above.
            if (radius <= 0f) radius = 1f;

            if (impactPoint == Vector3.zero) impactPoint = hit.m_point;
            if (impactPoint == Vector3.zero) return;


            // Attach a deferred destruction component � it will damage all areas
            // on the next frame, outside the Harmony Postfix stack
            if (rock.GetComponent<DeferredMineRockDestroy>() == null)
            {
                var deferred = rock.gameObject.AddComponent<DeferredMineRockDestroy>();
                deferred.Setup(radius, impactPoint);
            }
        }

        /// <summary>
        /// Directly destroys any MonoBehaviour's GameObject via ZNetView.
        /// Works for MineRock5, MineRock, TreeBase, or any networked object.
        /// Bypasses Damage() entirely � no RPCs, no Harmony recursion issues.
        /// </summary>
        public static void ForceDestroyObject(Component target, string typeName)
        {
            if (target == null || target.gameObject == null) return;
            try
            {
                var nview = target.GetComponent<ZNetView>();
                if (nview == null) nview = target.GetComponentInParent<ZNetView>();
                if (nview != null && nview.IsValid())
                {
                    if (!nview.IsOwner())
                        nview.ClaimOwnership();
                    nview.Destroy();
                }
                else
                {
                    UnityEngine.Object.Destroy(target.gameObject);
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }

        /// <summary>
        /// Destroys all resource objects within the AOE radius around the hit point.
        /// Uses the same radius as the configured AOE for elemental/combat damage.
        /// </summary>
        public static void TryAOEDestroy(HitData hit)
        {
            TryAOEDestroy(hit, Vector3.zero);
        }

        /// <summary>
        /// Overload: use an explicit impact point instead of hit.m_point.
        /// Some Damage() methods (e.g. MineRock5) modify hit.m_point to the
        /// matched sub-area center, so callers should pass the original bolt
        /// impact position saved in their Prefix.
        /// </summary>
        public static void TryAOEDestroy(HitData hit, Vector3 overrideImpactPoint)
        {
            if (isApplyingAOE) return;
            if (isDestroyingAreas) return; // don't AOE blast while fracturing sub-areas
            if (!MegaShotPlugin.ModEnabled.Value) return;
            if (hit == null) return;
            bool isArmageddon = IsArmageddonHit(hit);
            if (!MegaShotPlugin.DestroyObjects.Value && !isArmageddon) return;
            if (hit.m_damage.m_chop < 999000f && hit.m_damage.m_pickaxe < 999000f) return;

            float radius = GetAoeRadiusForHit(hit);
            if (radius <= 0f)
                return;

            // Use explicit impact point if provided, otherwise fall back to hit.m_point
            Vector3 hitPoint = (overrideImpactPoint != Vector3.zero) ? overrideImpactPoint : hit.m_point;
            if (hitPoint == Vector3.zero)
                return;

            try
            {
                isApplyingAOE = true;

                Collider[] nearby = Physics.OverlapSphere(hitPoint, radius);
                // Track root objects already processed to avoid hitting the same
                // MineRock5/TreeBase/etc. multiple times (they have many colliders)
                HashSet<int> processedRoots = new HashSet<int>();

                // v2.6.24 perf: cache the spare-check result by ZDO prefab
                // hash for the duration of this AOE call. With AOE=100 and
                // a forest full of identical Rock_3 / Bush01 colliders, the
                // first instance pays the multi-candidate name walk and the
                // rest hit O(1) cache lookups. Cleared per call so changes
                // to the allow/block lists between sweeps still take effect.
                _aoeSpareCache.Clear();

                foreach (var col in nearby)
                {
                    if (col == null) continue;
                    GameObject go = col.gameObject;
                    if (go == null) continue;

                    // Armageddon target gate: skip spared targets. Cached by
                    // ZDO prefab hash for the duration of this AOE call —
                    // identical prefabs (every Rock_3 in the sphere) share
                    // the cached decision. Falls back to per-GO check when
                    // the prefab hash isn't resolvable.
                    if (isArmageddon
                        && go.GetComponentInParent<Character>() == null)
                    {
                        if (IsSparedAOECached(go)) continue;
                    }

                    HitData aoeHit = CreateDestroyHitData(go.transform.position);

                    // --- MineRock5: defer destruction to next frame so RPCs work and drops spawn ---
                    try
                    {
                        var rock5 = go.GetComponentInParent<MineRock5>();
                        if (rock5 != null)
                        {
                            if (processedRoots.Add(rock5.GetInstanceID()))
                            {
                                // Drop-suppression window is Armageddon-only. ALT-fire
                                // kills must drop normally — never poison the gate.
                                if (isArmageddon) ArmageddonSuppression.MarkMineRockDestroyed();
                                if (rock5.GetComponent<DeferredMineRockDestroy>() == null)
                                {
                                    var deferred = rock5.gameObject.AddComponent<DeferredMineRockDestroy>();
                                    deferred.Setup(9999f, hitPoint); // 9999 = destroy all areas
                                }
                            }
                            continue;
                        }
                    }
                    catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

                    // --- MineRock: defer destruction to next frame ---
                    try
                    {
                        var rock = go.GetComponentInParent<MineRock>();
                        if (rock != null)
                        {
                            if (processedRoots.Add(rock.GetInstanceID()))
                            {
                                if (isArmageddon) ArmageddonSuppression.MarkMineRockDestroyed();
                                if (rock.GetComponent<DeferredMineRockDestroy>() == null)
                                {
                                    var deferred = rock.gameObject.AddComponent<DeferredMineRockDestroy>();
                                    deferred.Setup(9999f, hitPoint);
                                }
                            }
                            continue;
                        }
                    }
                    catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

                    // --- Trees ---
                    // v2.6.21: damage TreeBase in Armageddon too. Full-grown
                    // trees are already filtered upstream by the spare gate
                    // (TreeBase + no small/sapling/dead marker → spare). By
                    // the time we reach this branch in Armageddon, the spare
                    // gate has explicitly cleared it (small / sapling / dead
                    // tree per Milord's spec). Drops (seeds, etc.) fire via
                    // vanilla `m_dropWhenDestroyed` — only `Wood` itself gets
                    // junk-suppressed; seeds are preserved.
                    try
                    {
                        var tree = go.GetComponentInParent<TreeBase>();
                        if (tree != null)
                        {
                            if (processedRoots.Add(tree.GetInstanceID()))
                            {
                                tree.Damage(aoeHit);
                                ForceDestroyIfNeeded(tree, aoeHit, "TreeBase(AOE)");
                            }
                            continue;
                        }
                    }
                    catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                    try
                    {
                        var log = go.GetComponentInParent<TreeLog>();
                        if (log != null)
                        {
                            // v2.6.15: fallen logs (chopped tree segments) are
                            // explicitly destroyable per Milord's allowlist —
                            // both ALT-fire and Armageddon damage them.
                            if (processedRoots.Add(log.GetInstanceID()))
                            {
                                log.Damage(aoeHit);
                                ForceDestroyIfNeeded(log, aoeHit, "TreeLog(AOE)");
                            }
                            continue;
                        }
                    }
                    catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                    try
                    {
                        var dest = go.GetComponentInParent<Destructible>();
                        if (dest != null)
                        {
                            if (processedRoots.Add(dest.GetInstanceID()))
                            {
                                dest.Damage(aoeHit);
                                ForceDestroyIfNeeded(dest, aoeHit, "Destructible(AOE)");
                            }
                            continue;
                        }
                    }
                    catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                    // Buildings: only destroy specific world pieces (doors, stairs) in AOE.
                    // v2.6.15: Armageddon never damages WearNTear (no fortress / dungeon
                    // / player build hits) — even nominally-allowlisted pieces cascaded
                    // through structural support and flattened the rest.
                    try
                    {
                        var wnt = go.GetComponentInParent<WearNTear>();
                        if (wnt != null)
                        {
                            if (processedRoots.Add(wnt.GetInstanceID()))
                            {
                                if (!isArmageddon && PatchBuildingDamage.IsDestroyableWorldPiece(wnt))
                                    wnt.Damage(aoeHit);
                            }
                            continue;
                        }
                    }
                    catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            finally
            {
                isApplyingAOE = false;
            }
        }
    }

    // =========================================================================
    // HOUSE FIRE - Spawns Valheim's native fire on ALT-mode bolt impacts.
    // Searches ZNetScene prefabs at runtime for one with a Fire component,
    // caches the result, then instantiates at hit point.
    // Also forces m_burnable=true on nearby WearNTear pieces so stone,
    // black marble, and grausten buildings can burn.
    // =========================================================================
    public static class HouseFireHelper
    {
        private static GameObject cachedFirePrefab;
        private static bool searchDone = false;

        public static void SpawnFire(Vector3 position)
        {
            try
            {
                if (position == Vector3.zero) return;
                if (!searchDone) FindFirePrefab();
                if (cachedFirePrefab == null) return;

                var go = UnityEngine.Object.Instantiate(cachedFirePrefab, position + Vector3.up * 0.1f, Quaternion.identity);

                // Force m_burnable=true on nearby building pieces so Fire.Dot()
                // damages stone, black marble, grausten, etc.
                ForceBurnableNearby(position);
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }

        private static void ForceBurnableNearby(Vector3 position)
        {
            try
            {
                float radius = MegaShotPlugin.AoeRadius.Value;
                int pieceMask = LayerMask.GetMask("piece", "piece_nonsolid");
                Collider[] nearby = Physics.OverlapSphere(position, radius, pieceMask);

                HashSet<int> processed = new HashSet<int>();
                foreach (var col in nearby)
                {
                    if (col == null) continue;
                    var wnt = col.GetComponentInParent<WearNTear>();
                    if (wnt == null) continue;
                    if (!processed.Add(wnt.GetInstanceID())) continue;
                    wnt.m_burnable = true;
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }

        private static void FindFirePrefab()
        {
            searchDone = true;
            try
            {
                if (ZNetScene.instance == null) return;

                // Try known prefab names first (fastest path)
                string[] knownNames = { "fire_house", "HouseFire", "houseFire", "fx_fire_house" };
                foreach (var name in knownNames)
                {
                    var prefab = ZNetScene.instance.GetPrefab(name);
                    if (prefab != null)
                    {
                        CacheFirePrefab(prefab);
                        return;
                    }
                }

                // Fallback: find any Cinder prefab and grab its m_houseFirePrefab field
                var cinderField = typeof(Cinder).GetField("m_houseFirePrefab",
                    BindingFlags.Public | BindingFlags.Instance);

                if (cinderField != null)
                {
                    foreach (var prefab in ZNetScene.instance.m_prefabs)
                    {
                        if (prefab == null) continue;
                        var cinder = prefab.GetComponent<Cinder>();
                        if (cinder == null) continue;
                        var hfPrefab = cinderField.GetValue(cinder) as GameObject;
                        if (hfPrefab != null)
                        {
                            CacheFirePrefab(hfPrefab);
                            return;
                        }
                    }
                }

                // Last resort: find any prefab with a Fire component
                foreach (var prefab in ZNetScene.instance.m_prefabs)
                {
                    if (prefab == null) continue;
                    if (prefab.GetComponent<Fire>() != null)
                    {
                        CacheFirePrefab(prefab);
                        return;
                    }
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }

        private static void CacheFirePrefab(GameObject prefab)
        {
            cachedFirePrefab = prefab;
        }
    }

    // Trees (standing) — Armageddon Mode skips Damage entirely so trees survive
    [HarmonyPatch(typeof(TreeBase), "Damage")]
    public static class PatchDestroyTree
    {
        private static Vector3 savedImpactPoint;

        public static bool Prefix(HitData hit)
        {
            try
            {
                if (DestroyObjectsHelper.IsArmageddonHit(hit))
                    return false; // Armageddon spares trees

                if (DestroyObjectsHelper.IsDestroyTagged(hit))
                    savedImpactPoint = hit.m_point;
                DestroyObjectsHelper.TryApplyDestroyDamage(hit);
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return true;
        }
        public static void Postfix(TreeBase __instance, HitData hit)
        {
            try { DestroyObjectsHelper.ForceDestroyIfNeeded(__instance, hit, "TreeBase"); }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            try { DestroyObjectsHelper.TryAOEDestroy(hit, savedImpactPoint); }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }
    }

    // Logs (fallen trees) — Armageddon Mode skips Damage entirely so logs survive
    [HarmonyPatch(typeof(TreeLog), "Damage")]
    public static class PatchDestroyLog
    {
        private static Vector3 savedImpactPoint;

        public static bool Prefix(HitData hit)
        {
            try
            {
                if (DestroyObjectsHelper.IsArmageddonHit(hit))
                    return false; // Armageddon spares logs

                if (DestroyObjectsHelper.IsDestroyTagged(hit))
                    savedImpactPoint = hit.m_point;
                DestroyObjectsHelper.TryApplyDestroyDamage(hit);
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return true;
        }
        public static void Postfix(TreeLog __instance, HitData hit)
        {
            try { DestroyObjectsHelper.ForceDestroyIfNeeded(__instance, hit, "TreeLog"); }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            try { DestroyObjectsHelper.TryAOEDestroy(hit, savedImpactPoint); }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }
    }

    // Generic destructibles (small rocks, stumps, fortress barricades, etc.)
    [HarmonyPatch(typeof(Destructible), "Damage")]
    public static class PatchDestroyDestructible
    {
        private static bool destroyModeActive = false;
        private static HitData.DamageModifiers savedModifiers;
        private static Vector3 savedImpactPoint;

        public static bool Prefix(Destructible __instance, HitData hit)
        {
            try
            {
                // Armageddon target gate (defence in depth): if this hit is
                // Armageddon-tagged and the target is on the spare list, skip
                // vanilla Damage() entirely so the destructible survives.
                if (DestroyObjectsHelper.IsArmageddonHit(hit)
                    && __instance != null
                    && ArmageddonTargetFilter.IsSparedByArmageddon(__instance.gameObject))
                {
                    return false;
                }

                // Bypass damage modifiers (Immune/VeryResistant) for destroy-tagged bolts
                if (DestroyObjectsHelper.IsDestroyTagged(hit))
                {
                    destroyModeActive = true;
                    savedImpactPoint = hit.m_point;
                    savedModifiers = __instance.m_damages;
                    __instance.m_damages = new HitData.DamageModifiers();

                    // Claim ZNetView ownership so vanilla Damage runs locally and the
                    // full destruction path fires (m_destroyedEffect, m_dropWhenDestroyed,
                    // m_spawnWhenDestroyed, m_onDestroyed → DropOnDestroyed). Without
                    // this, dungeon-resident destructibles like MuddyScrapPile see the
                    // damage RPC routed to the remote owner — locally Damage is a no-op,
                    // and our Postfix's ForceDestroyIfNeeded then nukes the GameObject
                    // via nview.Destroy() without ever firing DropOnDestroyed.
                    try
                    {
                        var nv = __instance.GetComponent<ZNetView>();
                        if (nv == null) nv = __instance.GetComponentInParent<ZNetView>();
                        if (nv != null && nv.IsValid() && !nv.IsOwner())
                            nv.ClaimOwnership();
                    }
                    catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

                    // Diagnostic: log Destructible hit info to file
                    try
                    {
                        string diag = "DESTR: " + __instance.gameObject.name;
                        diag += " hp=" + __instance.m_health;
                        diag += " tier=" + __instance.m_minToolTier;
                        DiagnosticHelper.Log(diag);
                    }
                    catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
                }
                DestroyObjectsHelper.TryApplyDestroyDamage(hit);
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return true;
        }
        public static void Postfix(Destructible __instance, HitData hit)
        {
            try
            {
                if (destroyModeActive)
                {
                    destroyModeActive = false;
                    __instance.m_damages = savedModifiers;
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            try { DestroyObjectsHelper.ForceDestroyIfNeeded(__instance, hit, "Destructible"); }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            try { DestroyObjectsHelper.TryAOEDestroy(hit, savedImpactPoint); }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }
    }

    // Small mineral deposits (tin, obsidian, flametal, etc.)
    [HarmonyPatch(typeof(MineRock), "Damage")]
    public static class PatchDestroyMineRock
    {
        private static Vector3 savedImpactPoint;

        public static bool Prefix(MineRock __instance, HitData hit)
        {
            try
            {
                if (DestroyObjectsHelper.isDeferredDamage) return true;

                // Armageddon target gate (defence in depth): spare ore veins
                // and any other named-out targets even if a hit slips through.
                if (DestroyObjectsHelper.IsArmageddonHit(hit)
                    && __instance != null
                    && ArmageddonTargetFilter.IsSparedByArmageddon(__instance.gameObject))
                {
                    return false;
                }

                if (DestroyObjectsHelper.IsDestroyTagged(hit))
                    savedImpactPoint = hit.m_point;
                DestroyObjectsHelper.TryApplyDestroyDamage(hit);
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return true;
        }
        public static void Postfix(MineRock __instance, HitData hit)
        {
            try
            {
                if (DestroyObjectsHelper.isDeferredDamage) return;
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            try { DestroyObjectsHelper.ForceDestroyIfNeeded(__instance, hit, "MineRock"); }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            try { DestroyObjectsHelper.TryAOEDestroy(hit, savedImpactPoint); }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }
    }

    // Large mineral deposits (copper, silver, Grausten, etc. - multi-area fracture)
    [HarmonyPatch(typeof(MineRock5), "Damage")]
    public static class PatchDestroyMineRock5
    {
        private static bool destroyModeActive = false;
        private static HitData.DamageModifiers savedModifiers;
        // MineRock5.Damage() modifies hit.m_point to the matched area's position.
        // Save the bolt's actual impact point BEFORE Damage() runs.
        private static Vector3 savedImpactPoint;

        public static bool Prefix(MineRock5 __instance, HitData hit)
        {
            try
            {
                // Deferred damage from DeferredMineRockDestroy — let vanilla handle it clean
                if (DestroyObjectsHelper.isDeferredDamage) return true;

                // Armageddon target gate (defence in depth): spare silver
                // veins, copper deposits, drake nests, etc. that slip through
                // the upstream beam / AOE filters.
                if (DestroyObjectsHelper.IsArmageddonHit(hit)
                    && __instance != null
                    && ArmageddonTargetFilter.IsSparedByArmageddon(__instance.gameObject))
                {
                    return false;
                }

                if (DestroyObjectsHelper.IsDestroyTagged(hit))
                {
                    destroyModeActive = true;
                    savedImpactPoint = hit.m_point; // save BEFORE MineRock5.Damage() modifies it
                    savedModifiers = __instance.m_damageModifiers;
                    __instance.m_damageModifiers = new HitData.DamageModifiers();
                }
                DestroyObjectsHelper.TryApplyDestroyDamage(hit);
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return true;
        }
        public static void Postfix(MineRock5 __instance, HitData hit)
        {
            try
            {
                // Deferred damage from DeferredMineRockDestroy — skip all post-processing
                if (DestroyObjectsHelper.isDeferredDamage) return;

                if (destroyModeActive)
                {
                    destroyModeActive = false;
                    __instance.m_damageModifiers = savedModifiers;
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            // Fracture sub-areas using the ORIGINAL bolt impact point, not the modified hit.m_point.
            try { DestroyObjectsHelper.DestroyMineRock5Areas(__instance, hit, savedImpactPoint); }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            // AOE destroy adjacent objects (trees, other rocks, etc.)
            try { DestroyObjectsHelper.TryAOEDestroy(hit, savedImpactPoint); }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }
    }

    // =========================================================================
    // ARMAGEDDON TARGET FILTER
    // Per Milord's spec (v2.6.15): Armageddon destroys ground clutter ONLY —
    // shrubs, saplings, generic rocks, mountain stone, Grausten mounds, and
    // fallen logs. Default = SPARE everything else.
    //
    // Earlier versions used a sprawling blocklist (spare these specific
    // things, destroy the rest). That kept springing leaks — fortresses,
    // skeletal remains, soft tissue, black marble all fell through. Flipped
    // to an allowlist: a target must explicitly match a "destroyable" rule
    // to be hit; anything unmatched survives.
    //
    // Pipeline: TreeLog allow → component spare (Pickable/Container/WearNTear/
    // ItemStand/TreeBase) → block-name override (ores, biomass, marble) →
    // allow-name match → default SPARE.
    // =========================================================================
    public static class ArmageddonTargetFilter
    {
        // Prefab name substrings (case-insensitive). Hitting ANY of these
        // makes the target destroyable in Armageddon. Order doesn't matter —
        // any match passes, no match = spared.
        private static readonly string[] AllowedSubstrings = new string[]
        {
            // ── Foliage shrubs (cosmetic destructibles, no Pickable) ──
            "bush", "shrub",
            // ── Generic rocks (Destructible + small MineRock that drops Stone) ──
            "rock_destructible",
            "rock_3", "rock_4",
            "rock1_", "rock2_", "rock3_", "rock4_", "rock5_",
            // ── Big-rock cells and Mistlands rock formations
            //    (revealed by v2.6.19 ZDO-prefab dump) ──
            "bigrock",
            "mistlandrockformation",
            "heathrockpillar",
            // ── Mistlands / Ashlands cliff faces (mineable cliff_X_frac
            //    plus the unfractured visual cliffs) ──
            "cliff_mistlands", "cliff_ashlands",
            // ── Mountain / Mistlands / Ashlands stone (MineRock + MineRock5) ──
            "minerock_meadows",
            "minerock_blackforest",
            "minerock_mountain",
            "minerock_swamp",
            "minerock_plains",
            "minerock_mistlands",
            "minerock_ashlands",
            "minerock_stone",
            // ── Mistlands rock prefabs (rock_mistlands1, rock_mistlands2, …) ──
            "rock_mistlands",
            // ── Grausten mounds / pillars / Ashlands stone variants ──
            "grausten",
            "ashlands_stone",
            // ── Tree stumps & fallen logs ──
            "stub_", "stubbe",
            "_log", "log_", "oldlog", "fallenlog",
            // ── Ashlands fallen branches (drop Ashwood) ──
            "ashlandsbranch",
            // ── Small / sapling / dead trees (some have no TreeBase
            //    component, so the TreeBase branch misses them — caught
            //    here as a name-pattern fallback) ──
            "_small", "_dead", "sapling",
        };

        // BLOCK overrides allow. Anything matching here is spared even if a
        // (more general) allow pattern would have matched it. Covers ore
        // veins, Ashlands biomass sources, black marble, and named-out
        // dungeon / world specials.
        private static readonly string[] BlockedSubstrings = new string[]
        {
            // ── Ore veins (drop valuable mats) ──
            "copper", "_tin", "tin_", "silver", "iron",
            "meteor", "obsidian", "flametal",
            // (v2.6.22: yggashoot / yggdrasil REMOVED from block —
            //  Milord wants Yggdrasil shoots destroyed; YggdrasilWood
            //  drops survive the `wood` prefix junk-suppression because
            //  the prefab name starts with "Yggdrasil", not "Wood".)
            // ── Ashlands skeletal remains / soft tissue / charred sources ──
            "bone", "softtissue", "soft_tissue",
            "ashlandsbone", "ashlandstorment",
            "hairstrand", "charred_",
            // ── Black marble (Mistlands building material) ──
            "marble", "blackmarble", "black_marble",
            // ── Dragon eggs, drake nests, leviathans, ymir remains ──
            "dragonegg", "dragon_egg",
            "drake_nest", "drakenest",
            "leviathan",
            "ymir", "ymirremains",
            // ── Dungeon / world specials ──
            "guck",
            "shrine", "altar",
            "rune_", "runestone",
            "trader",
            "spawner",
            // ── Frost-cave / crypt decor (drops nothing useful but
            //    spawns inside dungeons we don't want gutted) ──
            "stalagmite", "stalactite", "icicle",
            "ice_", "iceblock", "iceblocker", "icerock", "icefloor", "icewall",
            "frostcave", "froststone",
            "crypt", "burialchamber",
            // ── Mistlands special ──
            "dvergrtower", "dvergrprops", "dvergr_",
            "giant_helmet", "giant_brain", "giantcorpse", "giant_skull",
            // ── Item stands / chests (component check below also catches them) ──
            "stand_", "_stand",
            "chest_", "_chest", "container",
            // ── Pickable_* prefabs (defense in depth — some are missing
            //    the actual Pickable component on the parent we walk) ──
            "pickable_",
        };

        /// <summary>
        /// Returns true if Armageddon must SKIP damaging this target.
        /// Allowlist-based: the default is to spare. Only explicit
        /// ground-clutter matches (shrubs, rocks, grausten, fallen logs,
        /// stumps, mountain stone) pass through to the destroy pipeline.
        ///
        /// v2.6.17: name match runs against MULTIPLE candidate strings
        /// (collider GO + each component owner's GO + transform root),
        /// not a single resolved name. Mistlands MineRock5s in particular
        /// rename their internal mesh-filter GO to "___MineRock5 m_meshFilter"
        /// and ZNetView lives on it, so any single resolution path would
        /// miss the actual prefab name (which only appears on the collider
        /// GO, e.g. "Rock_3_Untitled_31_cell.020").
        /// </summary>
        public static bool IsSparedByArmageddon(GameObject go)
        {
            if (go == null) return true;
            try
            {
                // ── Component spare: Pickable / Container / ItemStand ──
                if (go.GetComponentInParent<Pickable>() != null) return true;
                if (go.GetComponentInParent<Container>() != null) return true;
                if (go.GetComponentInParent<ItemStand>() != null) return true;

                // ── WearNTear: spare structures (fortresses, dungeons,
                //    player builds) UNLESS the same root also has a
                //    MineRock / MineRock5 / Destructible — that means it's
                //    worldgen ground clutter that just happens to carry
                //    WearNTear too (Mistlands hybrid prefabs do this).
                var wnt = go.GetComponentInParent<WearNTear>();
                if (wnt != null)
                {
                    bool alsoClutter = wnt.GetComponent<MineRock>() != null
                                    || wnt.GetComponent<MineRock5>() != null
                                    || wnt.GetComponent<Destructible>() != null;
                    if (!alsoClutter) return true;
                }

                // ── Build the unified name-candidate list once ──
                // Sources: collider GO + each parent component owner +
                // ZNetView root + transform.parent + transform.root.
                // Mistlands MineRock5 in particular renames its internal
                // mesh-filter GO and parks ZNetView there, so the prefab
                // signal can sit on the collider GO instead of the root.
                var candidates = CollectNameCandidates(go);

                // ── Explicit allow: chopped fallen tree logs ──
                if (go.GetComponentInParent<TreeLog>() != null) return false;

                // ── TreeBase: spare full-grown trees only. Small/sapling/
                //    dead variants are explicitly destroyable per Milord's
                //    spec ("small trees / saplings"), so we MUST return
                //    false (allow) — falling through to name match dropped
                //    FirTree_small etc. into spare-by-default in v2.6.17.
                var tb = go.GetComponentInParent<TreeBase>();
                if (tb != null)
                {
                    if (ContainsAny(candidates, SmallTreeMarkers)) return false;
                    return true;
                }

                if (candidates.Count == 0) return true;

                if (ContainsAny(candidates, BlockedSubstrings)) return true;
                if (ContainsAny(candidates, AllowedSubstrings)) return false;

                // Diagnostic — log unique unmatched candidate sets.
                LogUnmatchedOnce(candidates);
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return true;
        }

        private static bool ContainsAny(List<string> candidates, string[] markers)
        {
            for (int c = 0; c < candidates.Count; c++)
            {
                string n = candidates[c];
                for (int m = 0; m < markers.Length; m++)
                {
                    if (n.IndexOf(markers[m], StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }

        private static readonly string[] SmallTreeMarkers = new string[]
        {
            "small", "sapling", "_dead",
        };

        // Per-prefab-name dedupe for the unmatched diagnostic — same key
        // logged once per session keeps the log readable when the beam
        // sweeps over hundreds of identical objects.
        private static readonly HashSet<string> _loggedUnmatched =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static void LogUnmatchedOnce(List<string> candidates)
        {
            try
            {
                string key = string.Join(" | ", candidates);
                if (_loggedUnmatched.Add(key))
                    DiagnosticHelper.Log("ARMAG-SPARE(unmatched): " + key);
            }
            catch { }
        }

        /// <summary>
        /// Builds the list of name strings to substring-match against:
        /// the GO's own name, each interesting parent component's GO name,
        /// the ZNetView root's GO name, transform.parent / .root, AND the
        /// canonical prefab name resolved via ZDO → ZNetScene.GetPrefab(hash).
        /// Mistlands MineRock5 in particular renames its internal mesh-filter
        /// GO to "___MineRock5 m_meshFilter" and parks ZNetView there, so the
        /// only way to recover the real prefab name (e.g. "rock_mistlands1")
        /// is the ZDO lookup. Each stripped of "(Clone)" and de-duplicated.
        /// </summary>
        private static List<string> CollectNameCandidates(GameObject go)
        {
            var list = new List<string>(10);
            if (go == null) return list;
            try
            {
                AddName(list, go);

                var nv = go.GetComponentInParent<ZNetView>();
                if (nv != null)
                {
                    AddName(list, nv.gameObject);
                    AddRawName(list, ResolvePrefabNameFromZDO(nv));
                }

                var rock5 = go.GetComponentInParent<MineRock5>();
                if (rock5 != null) AddName(list, rock5.gameObject);

                var rock = go.GetComponentInParent<MineRock>();
                if (rock != null) AddName(list, rock.gameObject);

                var dest = go.GetComponentInParent<Destructible>();
                if (dest != null) AddName(list, dest.gameObject);

                var tree = go.GetComponentInParent<TreeBase>();
                if (tree != null) AddName(list, tree.gameObject);

                if (go.transform != null && go.transform.parent != null)
                    AddName(list, go.transform.parent.gameObject);

                if (go.transform != null && go.transform.root != null)
                    AddName(list, go.transform.root.gameObject);
            }
            catch { }
            return list;
        }

        /// <summary>
        /// Asks Valheim's prefab registry for the canonical prefab name
        /// behind this ZNetView's ZDO. Returns null if the ZDO / scene /
        /// prefab can't be resolved (e.g. during world load). The name is
        /// the prefab's authoring name with no "(Clone)" suffix.
        /// </summary>
        private static string ResolvePrefabNameFromZDO(ZNetView nv)
        {
            try
            {
                if (nv == null) return null;
                var zdo = nv.GetZDO();
                if (zdo == null) return null;
                int hash = zdo.GetPrefab();
                var scene = ZNetScene.instance;
                if (scene == null) return null;
                var prefab = scene.GetPrefab(hash);
                return prefab != null ? prefab.name : null;
            }
            catch { return null; }
        }

        private static void AddName(List<string> list, GameObject g)
        {
            if (g == null) return;
            AddRawName(list, g.name);
        }

        private static void AddRawName(List<string> list, string raw)
        {
            if (string.IsNullOrEmpty(raw)) return;
            int paren = raw.IndexOf('(');
            string n = (paren > 0) ? raw.Substring(0, paren).TrimEnd() : raw;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], n, StringComparison.OrdinalIgnoreCase))
                    return;
            list.Add(n);
        }
    }

    // =========================================================================
    // ARMAGEDDON DROP SUPPRESSION
    // While firing the beam, any resource-junk ItemDrop (stone, wood, flint,
    // resin, etc.) that spawns within the AOE radius of a recent impact gets
    // destroyed before it hits the ground. Keeps the world tidy at 100 ticks/sec.
    // =========================================================================
    public static class ArmageddonSuppression
    {
        // Time-based gate replaces the old proximity-based impact ring buffer.
        // Proximity was too narrow: big Mistlands / Plains rocks and MineRock5
        // ore veins scatter drops well past any AOE+bounds radius we can register
        // when destruction cascades. Gating on "beam fired recently" catches
        // every junk drop regardless of where in the vein it spawned.
        //
        // Window bumped 5s → 15s in v2.6.13: MineRock5 fractures keep dropping
        // items for many seconds after the beam stops, especially with the
        // huge Armageddon AOE radius. The old 5s gate let late drops through.
        private const float ActiveWindowSec = 15f;
        private static float lastBeamActiveTime = -999f;

        // Tier 1 — bulk drops always swallowed while the beam is recently
        // active OR a MineRock was destroyed recently. These are the
        // overwhelming bulk drops from generic rocks / trees / stumps /
        // Ashlands Grausten mounds and are useless at the scale Armageddon
        // produces them.
        //
        // Grausten was promoted to Tier 1 in v2.6.13 — the old "only when
        // MineRock window open" gate was unreliable because deferred MineRock5
        // sub-area damage spawns drops on a delayed RPC and the window was
        // missing them. Plus there are multiple Ashlands sources (mounds,
        // pillars, scattered rocks) and we want them all suppressed during
        // Armageddon regardless of which sub-prefab dropped the item.
        private static readonly HashSet<string> junkItemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "$item_stone",
            "$item_wood",
            "$item_grausten",
        };

        // Prefix matches (case-insensitive) on the dropped GameObject's
        // name (stripped of "(Clone)"). Match if the GO name STARTS WITH
        // the prefix — catches stacked / variant prefabs like "Grausten_Stack"
        // and "Wood_pile" while leaving Ashwood, FineWood, Roundlog,
        // ElderBark, AshlandsWood, etc. alone (they don't start with
        // "wood"). v2.6.18 used substring match which ate Ashwood drops.
        private static readonly string[] junkPrefabPrefixes = new string[]
        {
            "stone",
            "wood",
            "grausten",
        };

        // Independent secondary gate: if a MineRock/MineRock5 was destroyed
        // within the last 15 s, junk-drop suppression stays active even if
        // the beam itself stopped firing >15 s ago. Catches cascading drops
        // from deferred sub-area destruction.
        private const float MineRockWindowSec = 15f;
        private static float lastMineRockKillTime = -999f;

        public static void MarkMineRockDestroyed()
        {
            lastMineRockKillTime = UnityEngine.Time.time;
        }

        public static bool IsMineRockWindowOpen()
        {
            return UnityEngine.Time.time - lastMineRockKillTime <= MineRockWindowSec;
        }

        public static void MarkBeamActive()
        {
            lastBeamActiveTime = UnityEngine.Time.time;
        }

        public static bool IsBeamRecentlyActive()
        {
            return UnityEngine.Time.time - lastBeamActiveTime <= ActiveWindowSec;
        }

        // FX suppression window. v2.6.20 extended from 0.3s → 5s so the
        // MineRock5 deferred sub-area destruction (which can keep playing
        // shatter / dust / sound effects for several seconds after the
        // beam stops) is also covered. Big rocks at high AOE radius were
        // spawning hundreds of cosmetic effects per second post-beam,
        // freezing the game. Loot effects (m_destroyedEffect with an
        // ItemDrop-bearing prefab) are still preserved via the cached
        // EffectListHasItemDrop check downstream.
        public static bool IsBeamFiringNow()
        {
            return UnityEngine.Time.time - lastBeamActiveTime <= 5f;
        }

        // FX suppression safety gate: some destructibles (Guck sacks, beehives,
        // ornaments, etc.) spawn their LOOT through `m_destroyedEffect.Create`
        // rather than a separate drop path — the effect prefab list contains an
        // ItemDrop-bearing prefab that Create instantiates. If we blanket-skip
        // Create we also delete the loot. Walk the EffectList once per instance
        // and cache whether any prefab has an ItemDrop anywhere in its
        // hierarchy; if yes, let vanilla run so drops still spawn.
        private static readonly Dictionary<EffectList, bool> effectListDropCache =
            new Dictionary<EffectList, bool>();

        /// <summary>
        /// Returns true if the beam/AOE must NOT damage the given character.
        /// Covers: tamed creatures, player-raised undead (PlayerSubjects faction),
        /// other players, and neutral Dvergr. Uses `attacker.IsEnemy(target)`
        /// as the canonical faction check — same rule Valheim uses for AI aggro.
        /// </summary>
        public static bool IsFriendlyToBeam(Character character, Character attacker)
        {
            if (character == null) return false;
            try
            {
                if (character.IsPlayer()) return true;
                if (character.IsTamed()) return true;
                // Canonical faction check — same rule vanilla AI uses for aggro.
                if (attacker != null && !BaseAI.IsEnemy(attacker, character)) return true;
            }
            catch { /* defensive — on any reflection oddity, default to hostile (don't accidentally protect everything) */ }
            return false;
        }

        public static bool EffectListHasItemDrop(EffectList list)
        {
            if (list == null) return false;
            if (effectListDropCache.TryGetValue(list, out bool cached)) return cached;

            bool hasDrop = false;
            try
            {
                var arr = list.m_effectPrefabs;
                if (arr != null)
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var data = arr[i];
                        if (data == null) continue;
                        var prefab = data.m_prefab;
                        if (prefab == null) continue;
                        if (prefab.GetComponentInChildren<ItemDrop>(true) != null)
                        {
                            hasDrop = true;
                            break;
                        }
                    }
                }
            }
            catch { hasDrop = true; } // fail safe → let vanilla run so drops aren't lost
            effectListDropCache[list] = hasDrop;
            return hasDrop;
        }

        public static bool IsJunkItem(ItemDrop drop)
        {
            try
            {
                if (drop == null) return false;
                var name = drop.m_itemData?.m_shared?.m_name;
                string goName = drop.gameObject != null ? drop.gameObject.name : null;
                if (!string.IsNullOrEmpty(goName))
                {
                    int paren = goName.IndexOf('(');
                    if (paren > 0) goName = goName.Substring(0, paren).TrimEnd();
                }

                // Exact-match on the localised item name token (most reliable
                // — same item can have multiple prefab variants but one
                // shared.m_name).
                if (!string.IsNullOrEmpty(name) && junkItemNames.Contains(name))
                    return true;

                // Prefix match on prefab GameObject name (v2.6.19: was
                // substring match, which ate Ashwood / FineWood / Roundlog
                // because all three contain "wood"). Now requires the GO
                // name to START WITH "wood" / "stone" / "grausten" so only
                // standalone Wood drops (and stack variants like Wood_pile,
                // Grausten_Stack, Stone_Heavy) get suppressed.
                if (!string.IsNullOrEmpty(goName))
                {
                    for (int i = 0; i < junkPrefabPrefixes.Length; i++)
                    {
                        if (goName.StartsWith(junkPrefabPrefixes[i], StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }

                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// Combined gate for drop suppression: true if either the beam was
        /// fired recently OR a MineRock was destroyed recently. Either is
        /// strong enough evidence that junk drops should be eaten.
        /// </summary>
        public static bool ShouldSuppressDropsNow()
        {
            return IsBeamRecentlyActive() || IsMineRockWindowOpen();
        }

        public static void TryDestroyDrop(ItemDrop drop)
        {
            try
            {
                if (drop == null || drop.gameObject == null) return;
                var nview = drop.GetComponent<ZNetView>();
                if (nview == null) nview = drop.GetComponentInParent<ZNetView>();
                if (nview != null && nview.IsValid())
                {
                    if (!nview.IsOwner()) nview.ClaimOwnership();
                    nview.Destroy();
                }
                else
                {
                    UnityEngine.Object.Destroy(drop.gameObject);
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }
    }

    [HarmonyPatch(typeof(ItemDrop), "Awake")]
    public static class PatchArmageddonSuppressDrops
    {
        public static void Postfix(ItemDrop __instance)
        {
            try
            {
                if (!MegaShotPlugin.ModEnabled.Value) return;
                if (!MegaShotPlugin.ArmageddonEnabled.Value) return;
                if (!MegaShotPlugin.ArmageddonSuppressDrops.Value) return;
                if (__instance == null || __instance.gameObject == null) return;

                // Combined gate: beam recent OR MineRock recent. Either side
                // catches drops; together they cover the full window of
                // cascading deferred-destroy spawns.
                if (!ArmageddonSuppression.ShouldSuppressDropsNow()) return;

                bool isJunk = ArmageddonSuppression.IsJunkItem(__instance);

                // Diagnostic: log every drop we see while the gate is open
                // (regardless of suppression decision) so unexpected drops can
                // be identified and added to the junk list. Only writes when
                // DebugMode is on.
                try
                {
                    string itemName = __instance.m_itemData?.m_shared?.m_name ?? "?";
                    string goName = __instance.gameObject != null ? __instance.gameObject.name : "?";
                    DiagnosticHelper.Log("ARMAG-DROP" + (isJunk ? "(suppress)" : "(keep)")
                        + " name=" + itemName + " go=" + goName);
                }
                catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

                if (!isJunk) return;
                ArmageddonSuppression.TryDestroyDrop(__instance);
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }
    }

    // =========================================================================
    // ARMAGEDDON FX SUPPRESSION
    // Dense rock clusters under a large AOE spawn hundreds of m_hitEffect /
    // m_destroyedEffect prefabs per second (dust puffs, splinters, sounds,
    // lights). At 30 Hz damage ticks this can freeze the game. While the beam
    // is actively firing (or within the 5 s grace window), short-circuit
    // EffectList.Create so those prefabs never instantiate. The beam's own
    // bloom + particles + impact flash carry the visual load.
    // =========================================================================
    [HarmonyPatch(typeof(EffectList), "Create")]
    public static class PatchArmageddonSuppressFx
    {
        public static bool Prefix(EffectList __instance, ref GameObject[] __result)
        {
            try
            {
                if (!MegaShotPlugin.ModEnabled.Value) return true;
                if (!MegaShotPlugin.ArmageddonEnabled.Value) return true;
                if (!MegaShotPlugin.ArmageddonSuppressFx.Value) return true;
                if (!ArmageddonSuppression.IsBeamFiringNow()) return true;

                // Safety: if this effect list instantiates any ItemDrop-bearing
                // prefab (Guck sacks, beehives, etc. spawn loot via the destroy
                // effect), let vanilla run so loot still drops.
                if (ArmageddonSuppression.EffectListHasItemDrop(__instance)) return true;

                __result = System.Array.Empty<GameObject>();
                return false;
            }
            catch (Exception ex)
            {
                DiagnosticHelper.LogException("MegaShot", ex);
                return true;
            }
        }
    }
}

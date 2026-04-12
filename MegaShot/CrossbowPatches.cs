using HarmonyLib;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;

namespace MegaShot
{
    // =========================================================================
    // DIAGNOSTIC — writes ALT-fire hit info to BepInEx/MegaShot_Diagnostic.txt.
    // Controlled by config: 8. Debug > Enabled (default: off).
    // Shows prefab names, component types, HP, tier for identifying fortress pieces.
    // =========================================================================
    public static class DiagnosticHelper
    {
        private static string _logPath;

        private static string LogPath
        {
            get
            {
                if (_logPath == null)
                    _logPath = Path.Combine(BepInEx.Paths.BepInExRootPath, "MegaShot_Diagnostic.txt");
                return _logPath;
            }
        }

        public static void Log(string message)
        {
            try
            {
                if (!MegaShotPlugin.DebugMode.Value) return;
                File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + " " + message + "\n");
            }
            catch { }
        }

        /// <summary>Debug-only exception logging for catch blocks.</summary>
        public static void LogException(string context, Exception ex)
        {
            try
            {
                if (!MegaShotPlugin.DebugMode.Value) return;
                File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss") + " [EX] " + context + ": " + ex.Message + "\n");
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
                    return false;
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            return true;
        }
    }

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

        // Cached reflection for animation (VERIFIED: m_zanim + SetTrigger)
        private static FieldInfo zanimField;
        private static bool zanimFieldCached = false;

        // Cached animator for speed control
        private static Animator cachedAnimator;

        // Cached audio for reliable sound per shot
        private static AudioSource cachedAudioSource;
        private static AudioClip cachedFireClip;
        private static bool fireClipSearched = false;

        // Adaptive sound system for high fire rates
        private static float lastSoundTime = 0f;
        private const float SOUND_THROTTLE_RATE = 12f;  // Max PlayOneShot events/sec for high fire rates

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
            // Destroy mode (Alt held): semi-auto, one bolt per click
            // Normal mode: full-auto while held at configured fire rate
            bool destroyMode = MegaShotPlugin.DestroyObjects.Value &&
                Input.GetKey(MegaShotPlugin.DestroyObjectsKey.Value);
            bool fireInput = destroyMode ? Input.GetMouseButtonDown(0) : Input.GetMouseButton(0);

            if (fireInput)
            {
                if (state.magazineAmmo <= 0)
                {
                    state.isReloading = true;
                    state.reloadStartTime = Time.time;
                    __instance.Message(MessageHud.MessageType.Center, "<color=yellow>RELOADING</color>");
                }
                else if (destroyMode)
                {
                    // Semi-auto: one shot per click, no rate limiting
                    state.magazineAmmo--;
                    FireBolt(__instance, weapon);
                }
                else
                {
                    float interval = 1f / MegaShotPlugin.GetEffectiveFireRate();
                    if (Time.time - lastFireTime >= interval)
                    {
                        // Additive timing to prevent drift, cap to prevent burst after pause
                        lastFireTime = Mathf.Max(lastFireTime + interval, Time.time - interval);
                        state.magazineAmmo--;
                        FireBolt(__instance, weapon);
                    }
                }
            }
            else
            {
                // Reset animator speed when not firing
                try
                {
                    if (cachedAnimator == null)
                        cachedAnimator = __instance.GetComponentInChildren<Animator>();
                    if (cachedAnimator != null && cachedAnimator.speed > 1f)
                        cachedAnimator.speed = 1f;
                }
                catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
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

            // ALT-fire (destroy mode) quadruples damage
            bool destroyMode = MegaShotPlugin.DestroyObjects.Value &&
                Input.GetKey(MegaShotPlugin.DestroyObjectsKey.Value);
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

            // Tag bolt for object destruction if ALT-fire mode
            if (destroyMode)
            {
                hitData.m_damage.m_chop = 999999f;
                hitData.m_damage.m_pickaxe = 999999f;
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

                // Spawn HouseFire at impact point on ANY hit (if enabled)
                if (MegaShotPlugin.HouseFireEnabled.Value)
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

            // 10. Animation - force restart at fire-rate speed so every shot shows
            try
            {
                if (!zanimFieldCached)
                {
                    zanimField = typeof(Character).GetField("m_zanim", BindingFlags.NonPublic | BindingFlags.Instance);
                    zanimFieldCached = true;
                }

                if (cachedAnimator == null)
                    cachedAnimator = player.GetComponentInChildren<Animator>();

                float fireRate = MegaShotPlugin.GetEffectiveFireRate();

                if (cachedAnimator != null)
                {
                    // Scale animator speed so full attack animation fits within one fire interval
                    // A typical crossbow attack anim is ~1s, so at rate 10 we need 10x speed
                    cachedAnimator.speed = Mathf.Max(1f, fireRate);

                    // Force restart the current animation state from the beginning.
                    // This is critical: SetTrigger alone won't replay if we're already
                    // in the attack state. Play(hash, 0, 0f) restarts it every shot.
                    AnimatorStateInfo stateInfo = cachedAnimator.GetCurrentAnimatorStateInfo(0);
                    cachedAnimator.Play(stateInfo.fullPathHash, 0, 0f);
                }

                // Also fire the trigger via ZSyncAnimation for network sync
                if (zanimField != null)
                {
                    var zanim = zanimField.GetValue(player) as ZSyncAnimation;
                    if (zanim != null && !string.IsNullOrEmpty(attack.m_attackAnimation))
                        zanim.SetTrigger(attack.m_attackAnimation);
                }
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }

            // 11. Adaptive sound system — two tiers based on fire rate:
            //     ≤15 rps: per-shot effects (all 3 Valheim fallback attempts, like vanilla)
            //     >15 rps: throttled overlapping PlayOneShot (~12/sec on one AudioSource)
            //     Overlapping one-shots naturally blend into continuous "brrrrt" fire sound.
            //     12 PlayOneShot/sec with a ~0.3-0.5s clip = ~4-6 overlapping voices = smooth.
            try
            {
                float fireRate = MegaShotPlugin.GetEffectiveFireRate();

                // Ensure fire clip is always cached
                if (!fireClipSearched)
                {
                    fireClipSearched = true;
                    cachedFireClip = FindFireClip(attack, weapon);
                }

                if (fireRate > SOUND_THROTTLE_RATE)
                {
                    // --- THROTTLED MODE: high fire rates (>12 rps) ---
                    // PlayOneShot capped at ~12/sec. Overlapping tails create
                    // a continuous sound naturally — no looping needed.
                    float soundInterval = 1f / SOUND_THROTTLE_RATE;
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

        // ---- Sound Helpers ----

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

        public static void Prefix(WearNTear __instance, HitData hit)
        {
            try
            {
                if (!MegaShotPlugin.ModEnabled.Value) return;
                if (hit == null) return;

                // --- Player-built buildings are EXCLUDED from destroy mode ---
                // World-generated structures (Charred Fortress, dungeons, etc.) ARE destroyable.
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
                        return;
                    }

                    // Player-built buildings: strip destroy tags so vanilla damage
                    // goes through normally, and mark for HouseFire spawn in Postfix.
                    wasDestroyTagged = true;
                    savedHitPoint = hit.m_point;
                    // Strip destroy-level damage so the building takes normal hit
                    hit.m_damage.m_chop = 0f;
                    hit.m_damage.m_pickaxe = 0f;
                    return;
                }

                if (hit.m_skill != Skills.SkillType.Crossbows) return;

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

                float radius = MegaShotPlugin.AoeRadius.Value;
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

                // Call DamageArea(index, hit) directly � synchronous, no RPCs
                HitData areaHit = CreateDestroyHit(col.bounds.center);
                try
                {
                    damageAreaMethod.Invoke(rock, new object[] { i, areaHit });
                }
                catch
                {
                }
            }
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
            if (!MegaShotPlugin.DestroyObjects.Value) return false;
            if (hit == null) return false;

            // Detect destroy-tagged bolts by their chop/pickaxe values
            // (these are reliably preserved in Projectile.m_damage)
            if (hit.m_damage.m_chop < 999000f && hit.m_damage.m_pickaxe < 999000f)
                return false;

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

            float radius = MegaShotPlugin.AoeRadius.Value;
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
            if (!MegaShotPlugin.DestroyObjects.Value) return;
            if (hit == null) return;
            if (hit.m_damage.m_chop < 999000f && hit.m_damage.m_pickaxe < 999000f) return;

            float radius = MegaShotPlugin.AoeRadius.Value;
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

                foreach (var col in nearby)
                {
                    if (col == null) continue;
                    GameObject go = col.gameObject;
                    if (go == null) continue;

                    HitData aoeHit = CreateDestroyHitData(go.transform.position);

                    // --- MineRock5: defer destruction to next frame so RPCs work and drops spawn ---
                    try
                    {
                        var rock5 = go.GetComponentInParent<MineRock5>();
                        if (rock5 != null)
                        {
                            if (processedRoots.Add(rock5.GetInstanceID()))
                            {
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
                    // Buildings: only destroy specific world pieces (doors, stairs) in AOE
                    try
                    {
                        var wnt = go.GetComponentInParent<WearNTear>();
                        if (wnt != null)
                        {
                            if (processedRoots.Add(wnt.GetInstanceID()))
                            {
                                if (PatchBuildingDamage.IsDestroyableWorldPiece(wnt))
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

    // Trees (standing)
    [HarmonyPatch(typeof(TreeBase), "Damage")]
    public static class PatchDestroyTree
    {
        private static Vector3 savedImpactPoint;

        public static void Prefix(HitData hit)
        {
            try
            {
                if (DestroyObjectsHelper.IsDestroyTagged(hit))
                    savedImpactPoint = hit.m_point;
                DestroyObjectsHelper.TryApplyDestroyDamage(hit);
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }
        public static void Postfix(TreeBase __instance, HitData hit)
        {
            try { DestroyObjectsHelper.ForceDestroyIfNeeded(__instance, hit, "TreeBase"); }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
            try { DestroyObjectsHelper.TryAOEDestroy(hit, savedImpactPoint); }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
        }
    }

    // Logs (fallen trees)
    [HarmonyPatch(typeof(TreeLog), "Damage")]
    public static class PatchDestroyLog
    {
        private static Vector3 savedImpactPoint;

        public static void Prefix(HitData hit)
        {
            try
            {
                if (DestroyObjectsHelper.IsDestroyTagged(hit))
                    savedImpactPoint = hit.m_point;
                DestroyObjectsHelper.TryApplyDestroyDamage(hit);
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
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

        public static void Prefix(Destructible __instance, HitData hit)
        {
            try
            {
                // Bypass damage modifiers (Immune/VeryResistant) for destroy-tagged bolts
                if (DestroyObjectsHelper.IsDestroyTagged(hit))
                {
                    destroyModeActive = true;
                    savedImpactPoint = hit.m_point;
                    savedModifiers = __instance.m_damages;
                    __instance.m_damages = new HitData.DamageModifiers();

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

        public static void Prefix(HitData hit)
        {
            try
            {
                if (DestroyObjectsHelper.isDeferredDamage) return;

                if (DestroyObjectsHelper.IsDestroyTagged(hit))
                    savedImpactPoint = hit.m_point;
                DestroyObjectsHelper.TryApplyDestroyDamage(hit);
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShot", ex); }
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

        public static void Prefix(MineRock5 __instance, HitData hit)
        {
            try
            {
                // Deferred damage from DeferredMineRockDestroy — let vanilla handle it clean
                if (DestroyObjectsHelper.isDeferredDamage) return;

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
}

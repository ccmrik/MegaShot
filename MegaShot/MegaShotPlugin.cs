using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using System;
using System.IO;
using System.Reflection;

namespace MegaShot
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class MegaShotPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.rikal.megashot";
        public const string PluginName = "Mega Shot";
        public const string PluginVersion = "2.6.43";

        // General
        public static ConfigEntry<bool> ModEnabled;
        public static ConfigEntry<bool> DestroyObjects;
        public static ConfigEntry<KeyCode> DestroyObjectsKey;
        public static ConfigEntry<int> FireRate;
        public static ConfigEntry<int> MagazineCapacity;
        
        // Zoom
        public static ConfigEntry<float> ZoomMin;
        public static ConfigEntry<float> ZoomMax;
        
        // Projectile
        public static ConfigEntry<float> Velocity;
        public static ConfigEntry<bool> NoGravity;
        
        // Damage
        public static ConfigEntry<float> DamageMultiplier;
        public static ConfigEntry<bool> DamagePierce;
        public static ConfigEntry<bool> DamageBlunt;
        public static ConfigEntry<bool> DamageSlash;
        public static ConfigEntry<float> Stagger;
        
        // Damage - Elemental
        public static ConfigEntry<bool> DamageFire;
        public static ConfigEntry<bool> DamageFrost;
        public static ConfigEntry<bool> DamageLightning;
        public static ConfigEntry<bool> DamagePoison;
        public static ConfigEntry<bool> DamageSpirit;
        public static ConfigEntry<float> ElementalDoT;
        
        // AOE
        public static ConfigEntry<float> AoeRadius;

        // Building Damage
        public static ConfigEntry<float> BuildingDamage;
        public static ConfigEntry<float> BuildingFireDamage;
        public static ConfigEntry<float> BuildingFireDuration;

        // Fish Catching
        public static ConfigEntry<bool> FishCatching;

        // HouseFire
        public static ConfigEntry<bool> HouseFireEnabled;

        // Armageddon Mode
        public static ConfigEntry<bool> ArmageddonEnabled;
        public static ConfigEntry<KeyCode> ArmageddonKey;
        public static ConfigEntry<int> ArmageddonAoeRadius;
        public static ConfigEntry<int> ArmageddonRange;
        public static ConfigEntry<bool> ArmageddonLaserSound;
        public static ConfigEntry<int> ArmageddonLaserVolume;
        public static ConfigEntry<bool> ArmageddonSuppressDrops;
        public static ConfigEntry<bool> ArmageddonSuppressFx;

        // Debug
        public static ConfigEntry<bool> DebugMode;

        private Harmony _harmony;
        private FileSystemWatcher _configWatcher;


        private void Awake()
        {
            MigrateConfig(Config.ConfigFilePath);
            // Guard Config.Reload with File.Exists. Calling Reload on a missing
            // cfg file throws and aborts Awake silently — no Bind calls, no cfg
            // regen, no Harmony patches. Bit Milord on a fresh-profile / cfg-
            // delete scenario in v2.6.37: deleted cfg → MegaShot silently
            // failed to load → no cfg ever appeared again. (See
            // feedback-bepinex-config-reload memory note.)
            if (File.Exists(Config.ConfigFilePath)) Config.Reload();

            // General
            ModEnabled = Config.Bind("1. General", "Enabled", true, "Enable or disable the mod");
            DestroyObjects = Config.Bind("1. General", "DestroyObjects", true,
                "Bolts instantly destroy resource objects: trees, logs, rocks, copper, tin, silver, obsidian, flametal, and other mineable/choppable objects (must hold modifier key while firing)");
            DestroyObjectsKey = Config.Bind("1. General", "DestroyObjectsKey", KeyCode.LeftAlt,
                "Hold this key while firing to destroy objects (only when DestroyObjects is enabled)");
            FireRate = Config.Bind("1. General", "FireRate", 10, "Fire rate per second");
            MagazineCapacity = Config.Bind("1. General", "MagazineCapacity", 1000, "Magazine capacity before reload");
            
            // Zoom
            ZoomMin = Config.Bind("2. Zoom", "ZoomMin", 2f, "Minimum zoom level");
            ZoomMax = Config.Bind("2. Zoom", "ZoomMax", 10f, "Maximum zoom level");
            
            // Projectile
            Velocity = Config.Bind("3. Projectile", "Velocity", 470f, "Bolt velocity %");
            NoGravity = Config.Bind("3. Projectile", "NoGravity", true, "Disable gravity for bolts (default: true for accuracy)");
            
            // Damage � Split system: per-level total damage is split evenly across all enabled types.
            // e.g. Level 1 (240 total), all 8 types: 240/8 = 30 per type.
            // DamageMultiplier scales the total: mult 2 ? 2*(240/8) = 60 per type.
            DamageMultiplier = Config.Bind("4. Damage", "BaseMultiplier", 1f, 
                new ConfigDescription("Overall damage multiplier (1 = normal, 2 = double total damage, 10 = 10x)", new AcceptableValueRange<float>(0f, 10f)));
            DamagePierce = Config.Bind("4. Damage", "Pierce", true,
                "Enable pierce damage (splits total damage across enabled types)");
            DamageBlunt = Config.Bind("4. Damage", "Blunt", true,
                "Enable blunt damage (splits total damage across enabled types)");
            DamageSlash = Config.Bind("4. Damage", "Slash", true,
                "Enable slash damage (splits total damage across enabled types)");
            DamageFire = Config.Bind("4. Damage", "Fire", true,
                "Enable fire damage (splits total damage across enabled types)");
            DamageFrost = Config.Bind("4. Damage", "Frost", true,
                "Enable frost damage (splits total damage across enabled types)");
            DamageLightning = Config.Bind("4. Damage", "Lightning", true,
                "Enable lightning damage (splits total damage across enabled types)");
            DamagePoison = Config.Bind("4. Damage", "Poison", true,
                "Enable poison damage (splits total damage across enabled types)");
            DamageSpirit = Config.Bind("4. Damage", "Spirit", true,
                "Enable spirit damage (splits total damage across enabled types)");
            Stagger = Config.Bind("4. Damage", "Stagger", 0f, 
                new ConfigDescription("Stagger/knockback multiplier (0 = none, 1 = normal, 10 = 10x)", new AcceptableValueRange<float>(0f, 10f)));
            ElementalDoT = Config.Bind("4. Damage", "ElementalDoT", 1f, 
                new ConfigDescription("Elemental damage over time multiplier (0 = none, 1 = normal, 10 = 10x stronger DoT)", new AcceptableValueRange<float>(0f, 10f)));
            
            // AOE
            AoeRadius = Config.Bind("5. AOE", "Radius", 1f, 
                new ConfigDescription("Area of Effect radius (0 = disabled, 1 = default)", new AcceptableValueRange<float>(0f, 10f)));
            
            // Building Damage
            BuildingDamage = Config.Bind("6. Building Damage", "BuildingDamageMultiplier", 1f, 
                new ConfigDescription("Building damage multiplier (1 = normal, 10 = 10x)", new AcceptableValueRange<float>(1f, 10f)));
            BuildingFireDamage = Config.Bind("6. Building Damage", "BuildingFireDamage", 0f, 
                new ConfigDescription("Fire damage to buildings - Ashlands fire behavior (0 = none, 1 = normal, 10 = 10x)", new AcceptableValueRange<float>(0f, 10f)));
            BuildingFireDuration = Config.Bind("6. Building Damage", "BuildingFireDuration", 1f, 
                new ConfigDescription("How long buildings burn (1 = normal Ashlands duration, 10 = 10x duration)", new AcceptableValueRange<float>(1f, 10f)));

            // Fish Catching (ALT-mode fish auto-loot)
            FishCatching = Config.Bind("7. Fish Catching", "Enabled", true,
                "ALT-fire catches fish on hit: adds to inventory as level 5, grants fishing skill point");

            // HouseFire (ALT-mode fire spawned on impact)
            HouseFireEnabled = Config.Bind("8. HouseFire", "Enabled", false,
                "Enable HouseFire spawning in ALT mode (set to false to disable fire on impact)");

            // Armageddon Mode — full-auto destruction modifier (Shift by default).
            // While the modifier is held: 100 rps fire rate, unlimited ammo, AOE 10,
            // destroys rocks/saplings/ores/destructibles but spares trees and logs.
            ArmageddonEnabled = Config.Bind("9. Armageddon Mode", "Enabled", false,
                "Enable Armageddon Mode. Hold the modifier key while firing for unlimited full-auto destruction (skips trees/logs)");
            ArmageddonKey = Config.Bind("9. Armageddon Mode", "ArmageddonKey", KeyCode.LeftControl,
                "Hold this key while firing to engage Armageddon Mode (only when Enabled). Default: LeftControl (crouch — keeps the upper-body cast overlay visible). LeftShift triggers vanilla sprint which suppresses the cast pose; LeftControl avoids that conflict.");
            ArmageddonAoeRadius = Config.Bind("9. Armageddon Mode", "AoeRadius", 10,
                new ConfigDescription("Armageddon AOE radius in metres (overrides AOE Radius while modifier held)", new AcceptableValueRange<int>(0, 100)));
            ArmageddonRange = Config.Bind("9. Armageddon Mode", "Range", 500,
                new ConfigDescription("Maximum Armageddon beam reach in metres", new AcceptableValueRange<int>(50, 1000)));
            ArmageddonLaserSound = Config.Bind("9. Armageddon Mode", "LaserSound", true,
                "Play a continuous laser-beam hum while firing in Armageddon Mode (replaces the per-shot bolt sound)");
            ArmageddonLaserVolume = Config.Bind("9. Armageddon Mode", "LaserVolume", 60,
                new ConfigDescription("Volume of the Armageddon laser hum as a percentage (0 = silent, 100 = max)", new AcceptableValueRange<int>(0, 100)));
            ArmageddonSuppressDrops = Config.Bind("9. Armageddon Mode", "SuppressDrops", true,
                "Delete resource drops (stone, wood, flint, resin, etc.) spawned by objects destroyed in Armageddon Mode. They never hit the ground.");
            ArmageddonSuppressFx = Config.Bind("9. Armageddon Mode", "SuppressFx", true,
                "Skip vanilla destroy/hit VFX while the beam is firing. Huge perf win in dense rock clusters with large AOE — the beam's own glow + impact flash do the visual work. Disable if you want individual per-rock dust puffs.");

            // 99. Debug — standardised section + key across all Mega mods (v2.4.0+)
            DebugMode = Config.Bind("99. Debug", "DebugMode", false,
                "Write ALT-fire hit diagnostics to Desktop\\MegaShot_Diagnostic.txt (prefab names, component types, HP, tier)");

            // Watch config file for live reload on save
            SetupConfigWatcher();

            if (ModEnabled.Value)
            {
                _harmony = new Harmony(PluginGUID);
                _harmony.PatchAll();

                // Manual patching for GetDamage (not attribute-based � safe if method doesn't exist)
                try
                {
                    var getDamageMethod = typeof(ItemDrop.ItemData).GetMethod("GetDamage",
                        BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    if (getDamageMethod != null)
                    {
                        var postfix = typeof(PatchMegaShotDamage).GetMethod("Postfix",
                            BindingFlags.Static | BindingFlags.Public);
                        if (postfix != null)
                            _harmony.Patch(getDamageMethod, postfix: new HarmonyMethod(postfix));
                    }
                }
                catch (Exception ex) { DiagnosticHelper.LogException("MegaShotPlugin", ex); }

                // Also patch GetDamage(int, float) overload � tooltip/UI calls this for per-level display
                try
                {
                    var getDamageQualityMethod = typeof(ItemDrop.ItemData).GetMethod("GetDamage",
                        BindingFlags.Public | BindingFlags.Instance, null,
                        new Type[] { typeof(int), typeof(float) }, null);
                    if (getDamageQualityMethod != null)
                    {
                        var postfixQuality = typeof(PatchMegaShotDamage).GetMethod("PostfixQuality",
                            BindingFlags.Static | BindingFlags.Public);
                        if (postfixQuality != null)
                            _harmony.Patch(getDamageQualityMethod, postfix: new HarmonyMethod(postfixQuality));
                    }
                }
                catch (Exception ex) { DiagnosticHelper.LogException("MegaShotPlugin", ex); }

                // Manual patch for UseEitr � blocks Eitr drain when holding MegaShot
                // (Dundr clone normally consumes Eitr; safe if UseEitr doesn't exist)
                try
                {
                    var useEitrMethod = typeof(Player).GetMethod("UseEitr",
                        BindingFlags.Public | BindingFlags.Instance, null,
                        new Type[] { typeof(float) }, null);
                    if (useEitrMethod != null)
                    {
                        var eitrPrefix = typeof(PatchBlockEitr).GetMethod("Prefix",
                            BindingFlags.Static | BindingFlags.Public);
                        if (eitrPrefix != null)
                            _harmony.Patch(useEitrMethod, prefix: new HarmonyMethod(eitrPrefix));
                    }
                }
                catch (Exception ex) { DiagnosticHelper.LogException("MegaShotPlugin", ex); }
            }

            Logger.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _configWatcher?.Dispose();
        }

        private void SetupConfigWatcher()
        {
            try
            {
                var configFile = Config.ConfigFilePath;
                var configDir = Path.GetDirectoryName(configFile);
                var configFileName = Path.GetFileName(configFile);

                _configWatcher = new FileSystemWatcher(configDir, configFileName);
                _configWatcher.Changed += OnConfigFileChanged;
                _configWatcher.NotifyFilter = NotifyFilters.LastWrite;
                _configWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShotPlugin", ex); }
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                Config.Reload();
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShotPlugin", ex); }
        }

        public static float GetEffectiveFireRate() => (float)FireRate.Value;
        public static float GetEffectiveVelocity() => Velocity.Value;
        public static int GetEffectiveMagazineCapacity() => MagazineCapacity.Value;

        public static bool IsArmageddonActive()
        {
            if (ArmageddonEnabled == null || !ArmageddonEnabled.Value) return false;
            try { return Input.GetKey(ArmageddonKey.Value); }
            catch { return false; }
        }

        public static float GetEffectiveAoeRadius() =>
            IsArmageddonActive() ? (float)ArmageddonAoeRadius.Value : AoeRadius.Value;

        private static void MigrateConfig(string configPath)
        {
            try
            {
                if (!File.Exists(configPath)) return;
                string text = File.ReadAllText(configPath);
                bool changed = false;

                changed |= MigrateCfgSection(ref text, "0. Profile", null);
                changed |= MigrateCfgSection(ref text, "Profile", null);
                changed |= MigrateCfgSection(ref text, "8. Diagnostic", "8. Debug");
                changed |= MigrateCfgSection(ref text, "Diagnostic", "8. Debug");

                // Unnumbered → numbered
                changed |= MigrateCfgSection(ref text, "General", "1. General");
                changed |= MigrateCfgSection(ref text, "Zoom", "2. Zoom");
                changed |= MigrateCfgSection(ref text, "Projectile", "3. Projectile");
                changed |= MigrateCfgSection(ref text, "Damage", "4. Damage");
                changed |= MigrateCfgSection(ref text, "AOE", "5. AOE");
                changed |= MigrateCfgSection(ref text, "Building Damage", "6. Building Damage");
                changed |= MigrateCfgSection(ref text, "Fish Catching", "7. Fish Catching");
                changed |= MigrateCfgSection(ref text, "7. HouseFire", "8. HouseFire");
                changed |= MigrateCfgSection(ref text, "HouseFire", "8. HouseFire");
                changed |= MigrateCfgSection(ref text, "8. Debug", "99. Debug");
                changed |= MigrateCfgSection(ref text, "9. Debug", "99. Debug");
                changed |= MigrateCfgSection(ref text, "Debug", "99. Debug");
                // Rename legacy key `Enabled = X` → `DebugMode = X` inside the debug section.
                changed |= RenameKeyInSection(ref text, "99. Debug", "Enabled", "DebugMode");

                if (changed)
                    File.WriteAllText(configPath, text.TrimEnd() + "\n");
            }
            catch (Exception ex) { DiagnosticHelper.LogException("MegaShotPlugin", ex); }
        }

        private static bool MigrateCfgSection(ref string text, string oldName, string newName)
        {
            string oldHeader = "[" + oldName + "]";
            int idx = text.IndexOf(oldHeader, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;

            int sectionEnd = text.IndexOf("\n[", idx + oldHeader.Length, StringComparison.Ordinal);

            if (newName == null || text.IndexOf("[" + newName + "]", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (sectionEnd < 0)
                    text = text.Substring(0, idx).TrimEnd('\r', '\n');
                else
                    text = text.Substring(0, idx) + text.Substring(sectionEnd + 1);
            }
            else
            {
                text = text.Remove(idx, oldHeader.Length).Insert(idx, "[" + newName + "]");
            }
            return true;
        }

        /// <summary>
        /// Rename a key inside a specific section. Used to migrate `Enabled = X`
        /// under `[99. Debug]` to the new standard `DebugMode = X`.
        /// </summary>
        private static bool RenameKeyInSection(ref string text, string section, string oldKey, string newKey)
        {
            string header = "[" + section + "]";
            int sectionIdx = text.IndexOf(header, StringComparison.OrdinalIgnoreCase);
            if (sectionIdx < 0) return false;
            int sectionStart = sectionIdx + header.Length;
            int sectionEnd = text.IndexOf("\n[", sectionStart, StringComparison.Ordinal);
            if (sectionEnd < 0) sectionEnd = text.Length;

            string before = text.Substring(0, sectionStart);
            string body = text.Substring(sectionStart, sectionEnd - sectionStart);
            string after = text.Substring(sectionEnd);

            string newBody = System.Text.RegularExpressions.Regex.Replace(
                body,
                "(^|\\n)[ \\t]*" + System.Text.RegularExpressions.Regex.Escape(oldKey) + "[ \\t]*=",
                "$1" + newKey + " =",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
            if (newBody == body) return false;
            text = before + newBody + after;
            return true;
        }
    }
}



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
        public const string PluginName = "MegaShot";
        public const string PluginVersion = "2.2.2";

        // General
        public static ConfigEntry<bool> ModEnabled;
        public static ConfigEntry<string> ConfigProfile;
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

        // HouseFire
        public static ConfigEntry<bool> HouseFireEnabled;

        // Diagnostic
        public static ConfigEntry<bool> DiagnosticMode;

        private Harmony _harmony;
        private FileSystemWatcher _configWatcher;


        private void Awake()
        {
            // Profile (top of config file)
            ConfigProfile = Config.Bind("0. Profile", "ConfigProfile", "Default",
                new ConfigDescription(
                    "Quick config preset.\n" +
                    "Default = normal play settings.\n" +
                    "Development = enables DestroyObjects + AOE radius 10m for testing.",
                    new AcceptableValueList<string>("Default", "Development")));

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
            ElementalDoT = Config.Bind("4. Damage", "ElementalDoT", 0f, 
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

            // HouseFire (ALT-mode fire spawned on impact)
            HouseFireEnabled = Config.Bind("7. HouseFire", "Enabled", true,
                "Enable HouseFire spawning in ALT mode (set to false to disable fire on impact)");

            // Diagnostic
            DiagnosticMode = Config.Bind("8. Diagnostic", "Enabled", false,
                "Write ALT-fire hit diagnostics to Desktop\\MegaShot_Diagnostic.txt (prefab names, component types, HP, tier)");

            // Apply profile overrides
            ApplyProfileOverrides();

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
                catch { }

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
                catch { }

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
                catch { }
            }
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
            catch { }
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                Config.Reload();
                ApplyProfileOverrides();
            }
            catch { }
        }

        private static void ApplyProfileOverrides()
        {
            if (ConfigProfile.Value == "Development")
            {
                DestroyObjects.Value = true;
                AoeRadius.Value = 10f;
            }
        }

        public static float GetEffectiveFireRate() => (float)FireRate.Value;
        public static float GetEffectiveVelocity() => Velocity.Value;
        public static int GetEffectiveMagazineCapacity() => MagazineCapacity.Value;
    }
}



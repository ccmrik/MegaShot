using HarmonyLib;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace MegaCrossbows
{
    /// <summary>
    /// Custom MegaShot crossbow — cloned from StaffLightning (Dundr) with 4 quality levels,
    /// per-level damage, per-level upgrade recipes with different ingredients.
    /// </summary>
    public static class MegaShotItem
    {
        public const string ItemName = "MegaShot";
        public const string PrefabName = "MegaShot";
        public const string Description = "A legendary rapid-fire crossbow. Forged for destruction.";

        private static GameObject megaShotPrefab;
        private static GameObject prefabContainer;
        private static Recipe megaShotRecipe;
        private static int currentRecipeLevel = -1;

        // Per-level total damage (index 0 = level 1, index 3 = level 4)
        // Based on Dundr: 240, 276, 312, 348 — split evenly across all enabled damage types
        public static readonly float[] TotalDamagePerLevel = { 240f, 276f, 312f, 348f };

        // Per-level recipe ingredient prefab names (all 5 each)
        private static readonly string[][] IngredientNames = new string[][]
        {
            new[] { "Wood", "LeatherScraps", "Resin" },        // Level 1
            new[] { "RoundLog", "BjornHide", "GreydwarfEye" },  // Level 2
            new[] { "FineWood", "LoxPelt", "Tar" },             // Level 3
            new[] { "Ashwood", "AskHide", "MoltenCore" },       // Level 4
        };
        private const int IngredientAmount = 5;

        // Per-level crafting station: (stationKeyword, minStationLevel)
        // Keyword is matched against recipe crafting station names in ObjectDB
        private static readonly string[] StationKeywords = new string[]
        {
            "workbench",   // Level 1
            "workbench",   // Level 2
            "forge",       // Level 3
            "blackforge",  // Level 4
        };
        private static readonly int[] StationLevels = new int[]
        {
            1, 2, 1, 1
        };

        // Cached crafting station references (found from existing recipes)
        private static CraftingStation cachedWorkbench;
        private static CraftingStation cachedForge;
        private static CraftingStation cachedBlackForge;

        public static bool IsMegaShot(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null) return false;
            return item.m_shared.m_name == ItemName;
        }

        public static float GetTotalDamage(int quality)
        {
            int idx = Mathf.Clamp(quality - 1, 0, TotalDamagePerLevel.Length - 1);
            return TotalDamagePerLevel[idx];
        }

        public static void Register(ObjectDB objectDB)
        {
            if (objectDB == null || objectDB.m_items == null) return;

            // Check if prefab already exists (ObjectDB.Awake can be called multiple times)
            bool alreadyInObjectDB = false;
            foreach (var item in objectDB.m_items)
            {
                if (item != null && item.name == PrefabName)
                {
                    megaShotPrefab = item;
                    alreadyInObjectDB = true;
                    break;
                }
            }

            if (!alreadyInObjectDB)
            {
                CreatePrefab(objectDB);
                if (megaShotPrefab == null) return;

                // New ObjectDB instance — old recipe/stations are stale, must re-create
                megaShotRecipe = null;
                currentRecipeLevel = -1;
                cachedWorkbench = null;
                cachedForge = null;
                cachedBlackForge = null;
            }

            // Enforce m_maxQuality = 4 on prefab and recipe item
            try
            {
                var itemDrop = megaShotPrefab.GetComponent<ItemDrop>();
                if (itemDrop != null)
                    itemDrop.m_itemData.m_shared.m_maxQuality = 4;
                if (megaShotRecipe != null && megaShotRecipe.m_item != null)
                    megaShotRecipe.m_item.m_itemData.m_shared.m_maxQuality = 4;
            }
            catch { }

            // Always attempt ZNetScene registration (may only succeed on later calls
            // when ZNetScene.instance is available)
            TryRegisterZNetScene();

            // Cache crafting stations (may find more on later calls)
            CacheStations(objectDB);

            // Create recipe if not yet done, or re-create if stale (not in current ObjectDB)
            bool recipeInDB = false;
            if (megaShotRecipe != null && objectDB.m_recipes != null)
            {
                foreach (var r in objectDB.m_recipes)
                {
                    if (r == megaShotRecipe) { recipeInDB = true; break; }
                }
            }
            if (!recipeInDB)
            {
                megaShotRecipe = null;
                currentRecipeLevel = -1;
                CreateRecipe(objectDB);
            }

            // Retry resource initialization if previous call had an incomplete ObjectDB
            // (first ObjectDB.Awake at menu may not have all item prefabs loaded)
            // Also retry if resources are incomplete (some prefabs weren't found)
            if (megaShotRecipe != null &&
                (megaShotRecipe.m_resources == null || megaShotRecipe.m_resources.Length < IngredientNames[0].Length))
            {
                currentRecipeLevel = -1;
                SetRecipeResources(objectDB, 1);
            }
        }

        private static void CreatePrefab(ObjectDB objectDB)
        {
            try
            {
                // Find Dundr (StaffLightning) prefab
                GameObject dundrPrefab = null;
                foreach (var prefab in objectDB.m_items)
                {
                    if (prefab == null) continue;
                    if (prefab.name == "StaffLightning")
                    {
                        dundrPrefab = prefab;
                        break;
                    }
                }
                if (dundrPrefab == null) return;

                // Inactive container pattern: parent the clone under an inactive root GO.
                // The clone keeps activeSelf=true but activeInHierarchy=false,
                // so ZNetView.Awake() NEVER fires (no live ZDO, no ZNetScene registration).
                // When Valheim later calls Instantiate(megaShotPrefab), the copy is
                // root-level with no parent ? fully active ? ZNetView.Awake works normally.
                if (prefabContainer == null)
                {
                    prefabContainer = new GameObject("MegaCrossbowsPrefabs");
                    prefabContainer.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(prefabContainer);
                }

                megaShotPrefab = UnityEngine.Object.Instantiate(dundrPrefab, prefabContainer.transform);
                megaShotPrefab.name = PrefabName;

                // Modify item properties (GetComponent works on inactive-in-hierarchy objects)
                var itemDrop = megaShotPrefab.GetComponent<ItemDrop>();
                if (itemDrop == null) return;

                var shared = itemDrop.m_itemData.m_shared;
                shared.m_name = ItemName;
                shared.m_description = Description;
                shared.m_maxQuality = 4;
                shared.m_backstabBonus = 3f;

                // Clear DLC flag if any
                try { shared.m_dlc = ""; } catch { }

                // Remove ammo requirement (Dundr uses Eitr, MegaShot uses neither)
                shared.m_ammoType = "";

                // Force crossbow skill type so all Harmony patches (AOE, DoT, etc.) trigger correctly
                shared.m_skillType = Skills.SkillType.Crossbows;

                // Remove Eitr cost from attacks
                try { shared.m_attack.m_attackEitr = 0f; } catch { }
                try { shared.m_secondaryAttack.m_attackEitr = 0f; } catch { }
                try { shared.m_attack.m_attackStamina = 0f; } catch { }

                // Base damage (level 1) + linear per-level increment for native tooltip support
                // Dundr native type is lightning; our postfix overrides with split values
                var baseDmg = new HitData.DamageTypes();
                baseDmg.m_lightning = TotalDamagePerLevel[0];
                shared.m_damages = baseDmg;
                var perLevelDmg = new HitData.DamageTypes();
                perLevelDmg.m_lightning = 36f;
                shared.m_damagesPerLevel = perLevelDmg;

                // Register in ObjectDB
                objectDB.m_items.Add(megaShotPrefab);

                // Rebuild ObjectDB's internal hash maps (m_itemByHash + m_itemByData)
                // so inventory loading can resolve MegaShot items from save data.
                // UpdateRegisters() rebuilds both maps from m_items.
                try
                {
                    var updateMethod = typeof(ObjectDB).GetMethod("UpdateRegisters",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (updateMethod != null)
                        updateMethod.Invoke(objectDB, null);
                }
                catch { }
            }
            catch { }
        }

        private static void TryRegisterZNetScene()
        {
            if (megaShotPrefab == null) return;
            try
            {
                if (ZNetScene.instance == null) return;

                var hash = megaShotPrefab.name.GetStableHashCode();
                var namedField = typeof(ZNetScene).GetField("m_namedPrefabs",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (namedField != null)
                {
                    var dict = namedField.GetValue(ZNetScene.instance) as Dictionary<int, GameObject>;
                    if (dict != null && !dict.ContainsKey(hash))
                    {
                        dict[hash] = megaShotPrefab;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Scans existing recipes in ObjectDB to find crafting station references
        /// (Workbench, Forge, Black Forge) by matching station GameObject names.
        /// </summary>
        private static void CacheStations(ObjectDB objectDB)
        {
            // Retry until all three stations are found (may take multiple ObjectDB.Awake calls)
            if (cachedWorkbench != null && cachedForge != null && cachedBlackForge != null)
                return;

            try
            {
                if (objectDB.m_recipes == null) return;

                foreach (var r in objectDB.m_recipes)
                {
                    if (r == null || r.m_craftingStation == null) continue;
                    string stationName = r.m_craftingStation.gameObject.name.ToLower();

                    if (cachedWorkbench == null && stationName.Contains("workbench"))
                        cachedWorkbench = r.m_craftingStation;
                    if (cachedForge == null && stationName == "forge")
                        cachedForge = r.m_craftingStation;
                    if (cachedBlackForge == null && stationName.Contains("blackforge"))
                        cachedBlackForge = r.m_craftingStation;

                    if (cachedWorkbench != null && cachedForge != null && cachedBlackForge != null)
                        break;
                }
            }
            catch { }
        }

        private static CraftingStation GetStationForLevel(int level)
        {
            if (level < 1 || level > 4) return cachedWorkbench;
            string keyword = StationKeywords[level - 1];
            if (keyword == "blackforge" && cachedBlackForge != null) return cachedBlackForge;
            if (keyword == "forge" && cachedForge != null) return cachedForge;
            return cachedWorkbench;
        }

        private static void CreateRecipe(ObjectDB objectDB)
        {
            try
            {
                megaShotRecipe = ScriptableObject.CreateInstance<Recipe>();
                megaShotRecipe.name = "Recipe_MegaShot";
                megaShotRecipe.m_item = megaShotPrefab?.GetComponent<ItemDrop>();
                megaShotRecipe.m_amount = 1;
                megaShotRecipe.m_enabled = true;
                megaShotRecipe.m_craftingStation = GetStationForLevel(1);
                megaShotRecipe.m_repairStation = cachedWorkbench;
                megaShotRecipe.m_minStationLevel = StationLevels[0];

                // Set initial resources for level 1
                SetRecipeResources(objectDB, 1);

                objectDB.m_recipes.Add(megaShotRecipe);
            }
            catch { }
        }

        private static void SetRecipeResources(ObjectDB objectDB, int level)
        {
            if (objectDB == null || megaShotRecipe == null) return;
            if (level < 1 || level > 4) return;
            if (level == currentRecipeLevel) return;

            try
            {
                var names = IngredientNames[level - 1];
                var reqs = new List<Piece.Requirement>();

                foreach (var ingredientName in names)
                {
                    GameObject prefab = FindItemPrefab(objectDB, ingredientName);
                    if (prefab == null) continue;
                    var itemDrop = prefab.GetComponent<ItemDrop>();
                    if (itemDrop == null) continue;

                    var req = new Piece.Requirement();
                    req.m_resItem = itemDrop;
                    req.m_amount = IngredientAmount;
                    req.m_amountPerLevel = IngredientAmount;
                    req.m_recover = true;
                    reqs.Add(req);
                }

                megaShotRecipe.m_resources = reqs.ToArray();

                // Swap crafting station and min level for the target quality
                megaShotRecipe.m_craftingStation = GetStationForLevel(level);
                megaShotRecipe.m_minStationLevel = StationLevels[level - 1];

                // Only cache level when ALL ingredients were resolved.
                // If ObjectDB was incomplete (first Awake at menu), leave uncached
                // so the next Register() call retries with a full ObjectDB.
                if (reqs.Count == names.Length)
                    currentRecipeLevel = level;
            }
            catch { }
        }

        private static GameObject FindItemPrefab(ObjectDB objectDB, string name)
        {
            // Try ObjectDB.GetItemPrefab(string) via reflection (may or may not exist)
            try
            {
                var method = typeof(ObjectDB).GetMethod("GetItemPrefab",
                    new Type[] { typeof(string) });
                if (method != null)
                {
                    var result = method.Invoke(objectDB, new object[] { name }) as GameObject;
                    if (result != null) return result;
                }
            }
            catch { }

            // Fallback: search m_items by name
            try
            {
                foreach (var item in objectDB.m_items)
                {
                    if (item != null && item.name == name)
                        return item;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Called from Player.Update to keep the recipe resources in sync with the
        /// player's current MegaShot quality (so upgrades show correct ingredients).
        /// Dynamically swaps recipe ingredients based on target upgrade level.
        /// </summary>
        public static void UpdateRecipeForPlayer(Player player)
        {
            if (megaShotRecipe == null) return;

            // Force recipe discovery: add to known recipes when player knows any level 1 material
            // m_knownRecipes / m_knownMaterial may be private — access via reflection
            try
            {
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var knownRecipesField = typeof(Player).GetField("m_knownRecipes", flags);
                var knownMaterialField = typeof(Player).GetField("m_knownMaterial", flags);
                if (knownRecipesField != null && knownMaterialField != null)
                {
                    var knownRecipes = knownRecipesField.GetValue(player) as HashSet<string>;
                    var knownMaterial = knownMaterialField.GetValue(player) as HashSet<string>;
                    if (knownRecipes != null && !knownRecipes.Contains(megaShotRecipe.name))
                    {
                        if (megaShotRecipe.m_resources != null &&
                            megaShotRecipe.m_resources.Length > 0 &&
                            knownMaterial != null)
                        {
                            foreach (var req in megaShotRecipe.m_resources)
                            {
                                if (req != null && req.m_resItem != null &&
                                    knownMaterial.Contains(
                                        req.m_resItem.m_itemData.m_shared.m_name))
                                {
                                    knownRecipes.Add(megaShotRecipe.name);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // Enforce m_maxQuality = 4 on player's MegaShot items (in case shared data was reset)
            try
            {
                var inv = player.GetInventory();
                if (inv != null)
                {
                    foreach (var item in inv.GetAllItems())
                    {
                        if (IsMegaShot(item) && item.m_shared.m_maxQuality != 4)
                            item.m_shared.m_maxQuality = 4;
                    }
                }
            }
            catch { }

            try
            {
                bool craftingOpen = false;
                try { craftingOpen = InventoryGui.IsVisible(); } catch { }
                if (!craftingOpen)
                {
                    // Reset to level 1 when GUI is closed (also retry if resources are incomplete)
                    if (ObjectDB.instance != null &&
                        (currentRecipeLevel != 1 ||
                         megaShotRecipe.m_resources == null ||
                         megaShotRecipe.m_resources.Length < IngredientNames[0].Length))
                    {
                        currentRecipeLevel = -1;
                        SetRecipeResources(ObjectDB.instance, 1);
                    }
                    return;
                }

                // Find the player's MegaShot and determine target quality
                int targetLevel = 1;
                var inv = player.GetInventory();
                if (inv != null)
                {
                    foreach (var item in inv.GetAllItems())
                    {
                        if (IsMegaShot(item))
                        {
                            targetLevel = item.m_quality + 1;
                            break;
                        }
                    }
                }

                if (targetLevel > 4) return;

                if (ObjectDB.instance != null)
                    SetRecipeResources(ObjectDB.instance, targetLevel);
            }
            catch { }
        }
    }

    // Register MegaShot item in ObjectDB.Awake
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    public static class PatchRegisterMegaShot
    {
        [HarmonyPriority(Priority.High)]
        public static void Postfix(ObjectDB __instance)
        {
            try
            {
                if (!MegaCrossbowsPlugin.ModEnabled.Value) return;
                MegaShotItem.Register(__instance);
            }
            catch { }
        }
    }

    // Manually patched in Class1.cs (not attribute-based) — safe if GetDamage doesn't exist
    public static class PatchMegaShotDamage
    {
        // Postfix for GetDamage() (no params)
        public static void Postfix(ItemDrop.ItemData __instance, ref HitData.DamageTypes __result)
        {
            try
            {
                if (!MegaCrossbowsPlugin.ModEnabled.Value) return;
                if (!MegaShotItem.IsMegaShot(__instance)) return;
                float totalDmg = MegaShotItem.GetTotalDamage(__instance.m_quality);
                ApplyDamageSplit(ref __result, totalDmg);
            }
            catch { }
        }

        // Postfix for GetDamage(int quality, float worldLevel) — used by tooltip/UI for per-level display
        public static void PostfixQuality(ItemDrop.ItemData __instance, int quality, ref HitData.DamageTypes __result)
        {
            try
            {
                if (!MegaCrossbowsPlugin.ModEnabled.Value) return;
                if (!MegaShotItem.IsMegaShot(__instance)) return;
                float totalDmg = MegaShotItem.GetTotalDamage(quality);
                ApplyDamageSplit(ref __result, totalDmg);
            }
            catch { }
        }

        /// <summary>
        /// Splits total damage evenly across all enabled damage types.
        /// Used for tooltip display (base stats without DamageMultiplier).
        /// </summary>
        public static void ApplyDamageSplit(ref HitData.DamageTypes result, float totalDamage)
        {
            bool pierce = MegaCrossbowsPlugin.DamagePierce.Value;
            bool blunt = MegaCrossbowsPlugin.DamageBlunt.Value;
            bool slash = MegaCrossbowsPlugin.DamageSlash.Value;
            bool fire = MegaCrossbowsPlugin.DamageFire.Value;
            bool frost = MegaCrossbowsPlugin.DamageFrost.Value;
            bool lightning = MegaCrossbowsPlugin.DamageLightning.Value;
            bool poison = MegaCrossbowsPlugin.DamagePoison.Value;
            bool spirit = MegaCrossbowsPlugin.DamageSpirit.Value;

            int typeCount = 0;
            if (pierce) typeCount++;
            if (blunt) typeCount++;
            if (slash) typeCount++;
            if (fire) typeCount++;
            if (frost) typeCount++;
            if (lightning) typeCount++;
            if (poison) typeCount++;
            if (spirit) typeCount++;
            if (typeCount == 0) { typeCount = 1; lightning = true; }

            float perType = totalDamage / typeCount;

            result.m_damage = 0f;
            result.m_pierce = pierce ? perType : 0f;
            result.m_blunt = blunt ? perType : 0f;
            result.m_slash = slash ? perType : 0f;
            result.m_fire = fire ? perType : 0f;
            result.m_frost = frost ? perType : 0f;
            result.m_lightning = lightning ? perType : 0f;
            result.m_poison = poison ? perType : 0f;
            result.m_spirit = spirit ? perType : 0f;
            result.m_chop = 0f;
            result.m_pickaxe = 0f;
        }
    }
}

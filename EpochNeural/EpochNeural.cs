using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SpaceCraft;
using UnityEngine;

namespace EpochNeural
{
    [HarmonyPatch(typeof(StaticDataHandler), "LoadStaticData")]
    internal static class EpochNeural
    {
        internal const int HubInventorySize = 240;
        internal const string HubId = "Epoch_Hub";

        // ============================================================
        // TIER SYSTEM DATA
        // ============================================================
        public class HubTierData
        {
            public int Tier { get; set; }
            public string Name { get; set; }
            public int Budget { get; set; }
            public int StackCap { get; set; }
            public double UnlockTi { get; set; }
            public string StageName { get; set; }
        }

        public static readonly List<HubTierData> HubTiers = new List<HubTierData>
        {
            new HubTierData { Tier = 1, Name = "Epoch Hub", Budget = 50, StackCap = 50, UnlockTi = 0, StageName = "Barren" },
            new HubTierData { Tier = 2, Name = "Neural Mesh", Budget = 100, StackCap = 200, UnlockTi = 350000, StageName = "Clouds" },
            new HubTierData { Tier = 3, Name = "Synaptic Drive", Budget = 150, StackCap = 400, UnlockTi = 3000000, StageName = "Liquid Water" },
            new HubTierData { Tier = 4, Name = "Quantum Core", Budget = 200, StackCap = 700, UnlockTi = 700000000, StageName = "Flora" },
            new HubTierData { Tier = 5, Name = "Epoch Singularity", Budget = 250, StackCap = 1000, UnlockTi = 120000000000, StageName = "Fish" }
        };

        private static int _currentTier = 1;
        private static int _hubWorldObjectId = -1;

        public static int CurrentTier => _currentTier;
        public static HubTierData CurrentTierData => GetTierData(_currentTier);
        public static int HubWorldObjectId => _hubWorldObjectId;

        // ============================================================
        // HUB RESTRICTION - 1 PER PLANET
        // ============================================================
        private static int _activeHubId = -1;
        private const string HubPlacementMessage = "EPOCH: A Hub already exists on this planet!";

        private static readonly FieldInfo GroupBackingIdField =
            typeof(Group).GetField("<id>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo LocalizationDictionaryField =
            typeof(Localization).GetField("localizationDictionary", BindingFlags.Static | BindingFlags.NonPublic);

        internal static bool IsInitialized;
        internal static Inventory EpochHubInventory;
        internal static Inventory PlayerInventory;

        public static HubTierData GetTierData(int tier)
        {
            if (tier < 1 || tier > HubTiers.Count)
                return HubTiers[0];
            return HubTiers[tier - 1];
        }

        public static HubTierData GetNextTierData()
        {
            if (_currentTier >= HubTiers.Count)
                return null;
            return HubTiers[_currentTier];
        }

        public static bool IsAtMaxTier()
        {
            return _currentTier >= HubTiers.Count;
        }

        public static void SetHubWorldObjectId(int id)
        {
            _hubWorldObjectId = id;
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            try
            {
                Plugin.Logger.LogInfo("[Epoch] Initializing Central Core...");

                RegisterEpochHub();
                RegisterLocalization();
                HideVanillaItems();

                IsInitialized = true;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[Epoch] System initialization failure: {ex}");
            }
        }

        // ============================================================
        // HUB PLACEMENT RESTRICTION PATCHES
        // ============================================================

        [HarmonyPatch(typeof(PlayerBuilder), "InputOnAction")]
        [HarmonyPrefix]
        private static bool PrefixPlaceHub(PlayerBuilder __instance)
        {
            var ghostGroup = __instance.GetType().GetField("_ghostGroupConstructible",
                BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(__instance) as Group;

            if (ghostGroup == null || ghostGroup.GetId() != HubId)
                return true;

            if (HubExistsInWorld())
            {
                var hud = Managers.GetManager<BaseHudHandler>();
                if (hud != null)
                {
                    hud.DisplayCursorText(HubPlacementMessage, 3f);
                }
                else
                {
                    Plugin.Logger.LogWarning(HubPlacementMessage);
                }
                return false;
            }

            return true;
        }

        [HarmonyPatch(typeof(PlayerBuilder), "OnConstructed")]
        [HarmonyPostfix]
        private static void PostfixPlaceHub(PlayerBuilder __instance)
        {
            var constructedObjects = WorldObjectsHandler.Instance?.GetConstructedWorldObjects();
            if (constructedObjects == null)
                return;

            WorldObject latestHub = null;
            foreach (var wo in constructedObjects)
            {
                if (wo?.GetGroup()?.GetId() == HubId)
                {
                    if (latestHub == null || wo.GetId() > latestHub.GetId())
                        latestHub = wo;
                }
            }

            if (latestHub != null)
            {
                _activeHubId = latestHub.GetId();
                _hubWorldObjectId = latestHub.GetId();
                _currentTier = 1;
                Plugin.Logger.LogInfo($"[Epoch] Hub placed and registered. ID: {_activeHubId}, Tier: 1");

                if (EpochHubInventory != null)
                {
                    EpochVacuumSystem.Initialize(EpochHubInventory);
                }
            }
        }

        [HarmonyPatch(typeof(WorldObjectsHandler), "DestroyWorldObject", new Type[] { typeof(int), typeof(bool) })]
        [HarmonyPrefix]
        private static void PrefixDestroyHub(WorldObjectsHandler __instance, int woId, bool destroyGameObject)
        {
            if (woId == _activeHubId)
            {
                _activeHubId = -1;
                _hubWorldObjectId = -1;
                Plugin.Logger.LogInfo($"[Epoch] Hub destroyed. New hub can now be placed.");
            }
        }

        private static bool HubExistsInWorld()
        {
            if (_activeHubId != -1)
            {
                var wo = WorldObjectsHandler.Instance?.GetWorldObjectViaId(_activeHubId);
                if (wo != null && wo.GetGroup()?.GetId() == HubId)
                    return true;
                else
                    _activeHubId = -1;
            }

            var constructedObjects = WorldObjectsHandler.Instance?.GetConstructedWorldObjects();
            if (constructedObjects == null)
                return false;

            foreach (var wo in constructedObjects)
            {
                if (wo?.GetGroup()?.GetId() == HubId)
                {
                    _activeHubId = wo.GetId();
                    _hubWorldObjectId = wo.GetId();
                    return true;
                }
            }

            return false;
        }

        // ============================================================
        // HIDE VANILLA ITEMS FROM CONSTRUCTION MENU
        // ============================================================

        private static void HideVanillaItems()
        {
            try
            {
                var groups = GroupsHandler.GetAllGroups();
                if (groups == null)
                {
                    Plugin.Logger.LogWarning("[Epoch] No groups found to hide items from.");
                    return;
                }

                int removedCount = 0;
                var itemsToRemove = new List<Group>();

                foreach (var group in groups)
                {
                    if (group == null || string.IsNullOrEmpty(group.GetId()))
                        continue;

                    string id = group.GetId();
                    bool shouldRemove = false;

                    // Skip our own items - keep them visible!
                    if (id == HubId || id == "Epoch_Node_Drill")
                        continue;

                    // Hide ONLY Ore Extractors (vanilla mining machines)
                    if (id.Contains("OreExtractor"))
                    {
                        shouldRemove = true;
                    }
                    // Hide Containers (but keep Epoch Hub)
                    else if (id.Contains("Container") && !id.Contains("Epoch_Hub"))
                    {
                        shouldRemove = true;
                    }
                    // Hide Drone items
                    else if (id.Contains("Drone"))
                    {
                        shouldRemove = true;
                    }
                    // Vanilla drills are NOT hidden - they remain available for players!

                    if (shouldRemove)
                    {
                        itemsToRemove.Add(group);
                        Plugin.Logger.LogInfo($"[Epoch] Removing item from construction: {id}");
                    }
                }

                foreach (var item in itemsToRemove)
                {
                    groups.Remove(item);
                    removedCount++;
                }

                GroupsHandler.SetAllGroups(groups);
                Plugin.Logger.LogInfo($"[Epoch] Removed {removedCount} vanilla items from construction menu.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[Epoch] Failed to hide vanilla items: {ex.Message}");
            }
        }

        // ============================================================
        // HUB GHOST COLOR CHANGE
        // ============================================================

        [HarmonyPatch(typeof(ConstructibleGhost), "InitGhost")]
        [HarmonyPostfix]
        private static void PostfixInitGhost(ConstructibleGhost __instance, Group ghostGroupConstructible, PlayerAimController playerAimController, WorldObject wo, Action closeGhost)
        {
            if (ghostGroupConstructible == null || ghostGroupConstructible.GetId() != HubId)
                return;

            if (HubExistsInWorld())
            {
                ChangeGhostColor(__instance, Color.red);
                Plugin.Logger.LogInfo("[Epoch] Hub ghost set to RED - hub already exists.");
            }
            else
            {
                ChangeGhostColor(__instance, Color.white);
            }
        }

        private static void ChangeGhostColor(ConstructibleGhost ghost, Color color)
        {
            if (ghost == null)
                return;

            try
            {
                var renderers = ghost.GetComponentsInChildren<Renderer>(true);

                foreach (var renderer in renderers)
                {
                    if (renderer == null) continue;

                    Material[] newMaterials = new Material[renderer.materials.Length];
                    for (int i = 0; i < renderer.materials.Length; i++)
                    {
                        if (renderer.materials[i] == null) continue;

                        Material newMat = new Material(renderer.materials[i]);

                        if (newMat.HasProperty("_Color"))
                            newMat.SetColor("_Color", color);
                        if (newMat.HasProperty("_BaseColor"))
                            newMat.SetColor("_BaseColor", color);
                        if (newMat.HasProperty("_EmissionColor"))
                            newMat.SetColor("_EmissionColor", color * 0.3f);

                        if (newMat.shader != null)
                        {
                            int mainColorId = Shader.PropertyToID("_MainColor");
                            if (newMat.HasProperty(mainColorId))
                                newMat.SetColor(mainColorId, color);

                            int unlitColorId = Shader.PropertyToID("_UnlitColor");
                            if (newMat.HasProperty(unlitColorId))
                                newMat.SetColor(unlitColorId, color);

                            int glowColorId = Shader.PropertyToID("_GlowColor");
                            if (newMat.HasProperty(glowColorId))
                                newMat.SetColor(glowColorId, color);
                        }

                        newMaterials[i] = newMat;
                    }

                    renderer.materials = newMaterials;
                }

                var ghostRenderer = ghost.GetComponent<Renderer>();
                if (ghostRenderer != null && ghostRenderer.material != null)
                {
                    Material overrideMat = new Material(ghostRenderer.material);
                    if (overrideMat.HasProperty("_Color"))
                        overrideMat.SetColor("_Color", color);
                    else if (overrideMat.HasProperty("_BaseColor"))
                        overrideMat.SetColor("_BaseColor", color);
                    ghostRenderer.material = overrideMat;
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[Epoch] Could not change ghost color: {ex.Message}");
            }
        }

        // ============================================================
        // HUB REGISTRATION
        // ============================================================

        private static void RegisterEpochHub()
        {
            List<Group> groups = GroupsHandler.GetAllGroups();
            if (groups == null || groups.Exists(g => g != null && g.GetId() == HubId))
                return;

            GroupConstructible container = GroupsHandler.GetGroupViaId("Container3") as GroupConstructible;
            GroupDataConstructible containerData = container?.GetGroupData() as GroupDataConstructible;

            if (containerData == null)
            {
                Plugin.Logger.LogError("[Epoch] Base blueprint data matching failed.");
                return;
            }

            GroupDataConstructible hubData = ScriptableObject.CreateInstance<GroupDataConstructible>();

            foreach (FieldInfo field in typeof(GroupDataConstructible).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy))
            {
                try { field.SetValue(hubData, field.GetValue(containerData)); } catch { }
            }

            hubData.id = HubId;
            hubData.name = "Epoch Hub";
            hubData.inventorySize = HubInventorySize;
            hubData.secondaryInventoriesSize = new List<int> { 240, 240, 240, 240, 240, 240, 240, 240 };
            hubData.unlockingWorldUnit = DataConfig.WorldUnitType.Terraformation;
            hubData.unlockingValue = 0f;

            foreach (FieldInfo field in typeof(GroupDataConstructible).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy))
            {
                if (field.FieldType == typeof(string) && field.Name.Contains("Constraint"))
                {
                    try { field.SetValue(hubData, ""); } catch { }
                }

                if (field.FieldType.IsEnum && (field.Name.Contains("Place") || field.Name.Contains("Constraint") || field.Name.Contains("Type")))
                {
                    try
                    {
                        object buildingEnumValue = Enum.Parse(field.FieldType, "AsBuilding", true);
                        if (buildingEnumValue != null)
                        {
                            field.SetValue(hubData, buildingEnumValue);
                            Plugin.Logger.LogInfo("[Epoch Core] Placement type escalated to global building parameters successfully.");
                        }
                    }
                    catch
                    {
                        try
                        {
                            object fallbackValue = Enum.Parse(field.FieldType, "Outside", true);
                            if (fallbackValue != null) field.SetValue(hubData, fallbackValue);
                        }
                        catch { }
                    }
                }
            }

            hubData.hideInCrafter = false;

            GroupConstructible epochHub = new GroupConstructible(hubData) { id = HubId };

            List<GroupDataItem> freeIngredientsList = new List<GroupDataItem>();
            epochHub.SetRecipe(new Recipe(freeIngredientsList));

            try { GroupBackingIdField?.SetValue(epochHub, HubId); } catch { }

            groups.Add(epochHub);
            GroupsHandler.SetAllGroups(groups);

            Plugin.Logger.LogInfo("[Epoch] Epoch Hub data registered securely as a zero-cost build asset.");
        }

        private static void RegisterLocalization()
        {
            AddLocalization("GROUP_NAME_Epoch_Hub", "Epoch Hub");
            AddLocalization("GROUP_DESC_Epoch_Hub", "Stores every obtainable resource.");

            AddLocalization("GROUP_NAME_Epoch_Node_Drill", "Epoch Node Extractor");
            AddLocalization("GROUP_DESC_Epoch_Node_Drill", "Transmits automated regional extraction telemetry back to the base hub.");
        }

        private static void AddLocalization(string key, string value)
        {
            try
            {
                Localization.GetLocalizedString("LANGUAGE");
                var dictionary = LocalizationDictionaryField?.GetValue(null) as Dictionary<string, Dictionary<string, string>>;
                if (dictionary == null) return;

                foreach (var language in dictionary.Values)
                {
                    language[key] = value;
                }
            }
            catch { }
        }

        // ============================================================
        // RESTORE HUB TIER FROM CURRENT TERRAFORMATION
        // ============================================================

        internal static void RestoreTierFromTerraformation()
        {
            try
            {
                var worldUnitsHandler =
                    Managers.GetManager<WorldUnitsHandler>();

                if (worldUnitsHandler == null)
                {
                    Plugin.Logger?.LogWarning(
                        "[Epoch] Could not restore Hub tier: WorldUnitsHandler unavailable.");

                    return;
                }

                var terraUnit =
                    worldUnitsHandler.GetUnit(
                        DataConfig.WorldUnitType.Terraformation);

                if (terraUnit == null)
                {
                    Plugin.Logger?.LogWarning(
                        "[Epoch] Could not restore Hub tier: Terraformation unit unavailable.");

                    return;
                }

                double currentTi = terraUnit.GetValue();

                int resolvedTier = 1;

                foreach (var tier in HubTiers)
                {
                    if (currentTi >= tier.UnlockTi)
                    {
                        resolvedTier = tier.Tier;
                    }
                    else
                    {
                        break;
                    }
                }

                if (_currentTier != resolvedTier)
                {
                    Plugin.Logger?.LogInfo(
                        $"[Epoch] Restoring Hub tier from Terraformation: " +
                        $"Tier {_currentTier} -> Tier {resolvedTier} " +
                        $"(TI: {currentTi:F0})");
                }

                _currentTier = resolvedTier;
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch] Failed to restore Hub tier: {ex.Message}");
            }
        }
    }
}
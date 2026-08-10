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

        // --- Preserving your custom Reflection Cache ---
        private static readonly FieldInfo GroupBackingIdField =
            typeof(Group).GetField("<id>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo LocalizationDictionaryField =
            typeof(Localization).GetField("localizationDictionary", BindingFlags.Static | BindingFlags.NonPublic);

        // --- Core Runtime Hook States ---
        internal static bool IsInitialized;
        internal static Inventory EpochHubInventory;
        internal static Inventory PlayerInventory;

        [HarmonyPostfix]
        private static void Postfix()
        {
            try
            {
                Plugin.Logger.LogInfo("[Epoch] Initializing Central Core...");

                RegisterEpochHub();
                RegisterLocalization();

                IsInitialized = true;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[Epoch] System initialization failure: {ex}");
            }
        }

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

            // Your exact field replication loop
            foreach (FieldInfo field in typeof(GroupDataConstructible).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy))
            {
                try { field.SetValue(hubData, field.GetValue(containerData)); } catch { }
            }

            // Your custom item identity adjustments
            hubData.id = HubId;
            hubData.name = "Epoch Hub";
            hubData.inventorySize = HubInventorySize;
            hubData.secondaryInventoriesSize = new List<int> { 240, 240, 240, 240, 240, 240, 240, 240 };
            hubData.unlockingWorldUnit = DataConfig.WorldUnitType.Terraformation;
            hubData.unlockingValue = 0f;

            // ==================================================================
            // --- LOGISTICAL INFRASTRUCTURE PLACEMENT ESCALATION ---
            // Loops through the cloned GroupData fields to identify placement rules.
            // Dynamically overrides constraints to match full external buildings.
            // ==================================================================
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
                        // Escalate the asset's placement profile to behave like a primary external machine/building
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
                            // Secondary fallback profile value matching rule for alternative game assembly setups
                            object fallbackValue = Enum.Parse(field.FieldType, "Outside", true);
                            if (fallbackValue != null) field.SetValue(hubData, fallbackValue);
                        }
                        catch { }
                    }
                }
            }
            // ==================================================================

            // FIX A: Set this back to false so the text engine maps your titles and unlocks your building clicks!
            hubData.hideInCrafter = false;

            GroupConstructible epochHub = new GroupConstructible(hubData) { id = HubId };

            // FIX B: Instantiate an actual recipe container but provide it with a completely blank list.
            // This safely satisfies the build menu layout engine checks while keeping construction 100% free!
            List<GroupDataItem> freeIngredientsList = new List<GroupDataItem>();
            epochHub.SetRecipe(new Recipe(freeIngredientsList));

            // Your compiler backing fields setter logic
            try { GroupBackingIdField?.SetValue(epochHub, HubId); } catch { }

            // Clean, original data registration routing
            groups.Add(epochHub);
            GroupsHandler.SetAllGroups(groups);
            Plugin.Logger.LogInfo("[Epoch] Epoch Hub data registered securely as a zero-cost build asset.");
        }


        private static void RegisterLocalization()
        {
            // Your precise localization mappings
            AddLocalization("GROUP_NAME_Epoch_Hub", "Epoch Hub");
            AddLocalization("GROUP_DESC_Epoch_Hub", "Stores every obtainable resource.");
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
    }
}
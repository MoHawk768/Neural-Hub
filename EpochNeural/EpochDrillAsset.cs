using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SpaceCraft;
using UnityEngine;

namespace EpochNeural
{
    [HarmonyPatch(typeof(StaticDataHandler), "LoadStaticData")]
    internal static class EpochDrillAsset
    {
        internal const string DrillId = "Epoch_Node_Drill";

        private static readonly FieldInfo GroupBackingIdField =
            typeof(Group).GetField("<id>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo LocalizationDictionaryField =
            typeof(Localization).GetField("localizationDictionary", BindingFlags.Static | BindingFlags.NonPublic);

        [HarmonyPostfix]
        private static void Postfix()
        {
            try
            {
                RegisterEpochDrill();
                RegisterLocalization();
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Drill] Asset assignment failure: {ex.Message}");
            }
        }

        private static void RegisterEpochDrill()
        {
            List<Group> groups = GroupsHandler.GetAllGroups();
            if (groups == null || groups.Exists(g => g != null && g.GetId() == DrillId))
                return;

            // BASE GAME MATCHING: Clones the exact model mesh properties from the native T3 Ore Extractor
            GroupConstructible template =
    GroupsHandler.GetGroupViaId("OreExtractor3") as GroupConstructible;

            GroupDataConstructible templateData =
                template?.GetGroupData() as GroupDataConstructible;

            if (templateData == null)
            {
                Plugin.Logger?.LogError(
                    "[Epoch Drill] T3 Ore Extractor baseline asset model lookup failed.");

                return;
            }

            // ============================================================
            // EPOCH UNLOCK OVERRIDE
            // Clone the T3 data so the vanilla OreExtractor3 is untouched.
            // Epoch Node Extractor unlocks at 1 mPa pressure.
            // ============================================================

            GroupDataConstructible epochDrillData =
                UnityEngine.Object.Instantiate(templateData);

            epochDrillData.unlockingWorldUnit =
                DataConfig.WorldUnitType.Pressure;

            epochDrillData.unlockingValue = 0.001f;

            // If the T3 extractor uses a TerraformStage unlock,
            // clear it so the Pressure requirement above is used.
            epochDrillData.terraformStageUnlock = null;

            Plugin.Logger?.LogInfo(
                "[Epoch Drill] Unlock requirement overridden: " +
                "1 mPa Pressure (0.001 Pa).");

            // Create Epoch Node Extractor from the modified copy.
            GroupConstructible epochDrill =
                new GroupConstructible(epochDrillData)
                {
                    id = DrillId
                };

            try { GroupBackingIdField?.SetValue(epochDrill, DrillId); } catch { }

            // Wipe out construction ingredients requirements to keep development link checks entirely free
            List<GroupDataItem> freeIngredientsList = new List<GroupDataItem>();
            epochDrill.SetRecipe(new Recipe(freeIngredientsList));

            groups.Add(epochDrill);
            GroupsHandler.SetAllGroups(groups);
            Plugin.Logger?.LogInfo("[Epoch Drill] Custom item assigned successfully using base game T3 Extractor properties.");
        }

        private static void RegisterLocalization()
        {
            AddLocalization("GROUP_NAME_Epoch_Node_Drill", "Epoch Node Drill");
            AddLocalization("GROUP_DESC_Epoch_Node_Drill", "Transmits automated regional extraction telemetry back to the base hub.");
        }

        private static void AddLocalization(string key, string value)
        {
            try
            {
                var dictionary = LocalizationDictionaryField?.GetValue(null) as Dictionary<string, Dictionary<string, string>>;
                if (dictionary == null) return;
                foreach (var language in dictionary.Values) { language[key] = value; }
            }
            catch { }
        }
    }
}

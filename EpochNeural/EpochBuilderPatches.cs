using System;
using System.Collections.Generic;
using HarmonyLib;
using SpaceCraft;
using UnityEngine;

namespace EpochNeural
{
    /// <summary>
    /// Harmony patches for PlayerBuilder to enable global Hub construction
    /// </summary>
    [HarmonyPatch]
    internal static class EpochBuilderPatches
    {
        /// <summary>
        /// Patch PlayerBuilder.OnConstructed to check Hub resources first
        /// </summary>
        [HarmonyPatch(typeof(PlayerBuilder), "OnConstructed")]
        [HarmonyPrefix]
        private static bool PrefixOnConstructed(PlayerBuilder __instance, GameObject result)
        {
            try
            {
                // Get the ghost group via reflection
                var ghostGroupField = typeof(PlayerBuilder).GetField("_ghostGroupConstructible",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (ghostGroupField == null) return true;

                var ghostGroup = ghostGroupField.GetValue(__instance) as Group;
                if (ghostGroup == null) return true;

                // Get the recipe requirements
                var recipe = ghostGroup.GetRecipe();
                if (recipe == null) return true;

                var requiredResources = recipe.GetIngredientsGroupInRecipe();
                if (requiredResources == null || requiredResources.Count == 0) return true;

                // Check if Free Craft is enabled
                bool freeCraft = Managers.GetManager<GameSettingsHandler>().GetCurrentGameSettings().GetFreeCraft();
                if (freeCraft) return true;

                // Check if source WorldObject exists (using a blueprint item)
                var sourceField = typeof(PlayerBuilder).GetField("_sourceWorldObject",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                var sourceWorldObject = sourceField?.GetValue(__instance) as WorldObject;
                if (sourceWorldObject != null) return true; // Using a blueprint item, skip Hub check

                // Try to build from Hub
                Inventory playerInventory = __instance.GetComponent<PlayerBackpack>().GetInventory();
                bool usedHub;
                bool success = EpochGlobalBuilder.TryBuildFromHub(requiredResources, playerInventory, out usedHub);

                if (success && usedHub)
                {
                    // Resources were taken from Hub, skip player inventory deduction
                    // Set source WorldObject to null so normal OnConstructed doesn't remove from player
                    sourceField?.SetValue(__instance, null);

                    Plugin.Logger?.LogDebug("[EpochBuilder] Built from Hub resources");

                    // Continue with construction (the original method will run but skip inventory deduction)
                    return true;
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning($"[EpochBuilder] Error in PrefixOnConstructed: {ex.Message}");
            }

            // Fallback to normal behavior
            return true;
        }
    }
}
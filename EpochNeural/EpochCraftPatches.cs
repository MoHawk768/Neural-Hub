using System;
using System.Collections.Generic;
using HarmonyLib;
using SpaceCraft;
using UnityEngine;

namespace EpochNeural
{
    /// <summary>
    /// Harmony patches for CraftManager to enable global Hub crafting
    /// </summary>
    [HarmonyPatch]
    internal static class EpochCraftPatches
    {
        /// <summary>
        /// Patch CraftManager.TryToCraftInInventory to check Hub resources first
        /// </summary>
        [HarmonyPatch(typeof(CraftManager), "TryToCraftInInventory")]
        [HarmonyPrefix]
        private static bool PrefixTryToCraftInInventory(
            ActionCrafter sourceCrafter,
            PlayerMainController playerController,
            GroupItem groupItem)
        {
            try
            {
                // Check if Hub has resources first
                var recipe = groupItem.GetRecipe();
                if (recipe == null) return true;

                var requiredResources = recipe.GetIngredientsGroupInRecipe();
                if (requiredResources == null || requiredResources.Count == 0) return true;

                // Check if Free Craft is enabled
                bool freeCraft = Managers.GetManager<GameSettingsHandler>().GetCurrentGameSettings().GetFreeCraft();
                if (freeCraft) return true;

                // Try to build from Hub
                Inventory playerInventory = playerController.GetPlayerBackpack().GetInventory();
                bool usedHub;
                bool success = EpochGlobalBuilder.TryBuildFromHub(requiredResources, playerInventory, out usedHub);

                if (success && usedHub)
                {
                    // Resources were taken from Hub, craft the item
                    sourceCrafter.CraftAnimation(groupItem);

                    // Add the crafted item to player inventory
                    InventoriesHandler.Instance.AddItemToInventory(groupItem, playerInventory, delegate (bool addSuccess, int woId)
                    {
                        if (addSuccess)
                        {
                            Plugin.Logger?.LogInfo($"[EpochCraft] Crafted {groupItem.GetId()} from Hub resources");
                        }
                    });

                    WorldObjectsHandler.Instance.AddOneToTotalCraft();
                    return false; // Skip original method
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning($"[EpochCraft] Error in PrefixTryToCraftInInventory: {ex.Message}");
            }

            // Fallback to normal behavior
            return true;
        }

        /// <summary>
        /// Patch CraftManager.TryToCraftInWorld to check Hub resources first
        /// </summary>
        [HarmonyPatch(typeof(CraftManager), "TryToCraftInWorld")]
        [HarmonyPrefix]
        private static bool PrefixTryToCraftInWorld(
            ActionCrafter sourceCrafter,
            PlayerMainController playerController,
            GroupItem groupItem,
            bool checkSpawnPosition)
        {
            try
            {
                // Check if Hub has resources first
                var recipe = groupItem.GetRecipe();
                if (recipe == null) return true;

                var requiredResources = recipe.GetIngredientsGroupInRecipe();
                if (requiredResources == null || requiredResources.Count == 0) return true;

                // Check if Free Craft is enabled
                bool freeCraft = Managers.GetManager<GameSettingsHandler>().GetCurrentGameSettings().GetFreeCraft();
                if (freeCraft) return true;

                // Try to build from Hub
                Inventory playerInventory = playerController.GetPlayerBackpack().GetInventory();
                bool usedHub;
                bool success = EpochGlobalBuilder.TryBuildFromHub(requiredResources, playerInventory, out usedHub);

                if (success && usedHub)
                {
                    // Resources were taken from Hub, spawn the item in the world
                    WorldObjectsHandler.Instance.CreateAndInstantiateWorldObject(groupItem,
                        sourceCrafter.GetSpawnPosition(),
                        sourceCrafter.GetSpawnRotation(),
                        disolve: true,
                        checkSpawnPosition,
                        save: true,
                        addDeconstructIcon: false,
                        delegate (GameObject newSpawnedObject)
                        {
                            if (newSpawnedObject != null)
                            {
                                sourceCrafter.PlayCraftSound();
                                Plugin.Logger?.LogInfo($"[EpochCraft] Crafted {groupItem.GetId()} in world from Hub resources");
                            }
                        });

                    WorldObjectsHandler.Instance.AddOneToTotalCraft();
                    return false; // Skip original method
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning($"[EpochCraft] Error in PrefixTryToCraftInWorld: {ex.Message}");
            }

            // Fallback to normal behavior
            return true;
        }
    }
}
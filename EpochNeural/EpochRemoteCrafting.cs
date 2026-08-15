using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SpaceCraft;
using UnityEngine;

namespace EpochNeural
{
    /// <summary>
    /// Enables remote crafting and building from the Epoch Hub inventory
    /// </summary>
    [HarmonyPatch]
    internal static class EpochRemoteCrafting
    {
        // ============================================================
        // CONFIGURATION
        // ============================================================

        private const string MissingResourcesMessage = "EPOCH: Resources not found in Hub!";

        // ============================================================
        // RECIPE HELPERS
        // ============================================================

        private static List<Group> GetIngredientGroups(Recipe recipe)
        {
            if (recipe == null)
                return new List<Group>();

            return recipe.GetIngredientsGroupInRecipe() ?? new List<Group>();
        }

        // ============================================================
        // INVENTORY CHECK - The core fix
        // ============================================================

        /// <summary>
        /// Intercepts Inventory.ContainsItems to also check Hub inventory
        /// This makes building and crafting work from Hub
        /// </summary>
        [HarmonyPatch(typeof(Inventory), "ContainsItems")]
        [HarmonyPrefix]
        private static bool PrefixContainsItems(Inventory __instance, ref bool __result, List<Group> groups)
        {
            try
            {
                // If the inventory being checked is the Hub inventory itself, use vanilla
                if (EpochNeural.EpochHubInventory != null && __instance.GetId() == EpochNeural.EpochHubInventory.GetId())
                    return true;

                if (groups == null || groups.Count == 0)
                    return true;

                var hubInventory = EpochNeural.EpochHubInventory;
                if (hubInventory == null)
                    return true;

                bool allInHub = true;
                foreach (Group group in groups)
                {
                    if (group == null) continue;
                    int hubCount = EpochHubLogistics.GetResourceCount(group.GetId());
                    if (hubCount < 1)
                    {
                        allInHub = false;
                        break;
                    }
                }

                if (allInHub)
                {
                    __result = true;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Remote] Error in PrefixContainsItems: {ex.Message}");
                return true;
            }
        }

        // ============================================================
        // VISUAL FIX - Fix greyed out recipe icons
        // ============================================================

        /// <summary>
        /// Intercepts GroupDisplayer.SetGreyedStatus to fix greyed out state
        /// </summary>
        [HarmonyPatch(typeof(GroupDisplayer), "SetGreyedStatus")]
        [HarmonyPrefix]
        private static bool PrefixSetGreyedStatus(GroupDisplayer __instance, bool greyed)
        {
            try
            {
                // If it's already not greyed, let it through
                if (!greyed)
                    return true;

                // Get the hub inventory
                var hubInventory = EpochNeural.EpochHubInventory;
                if (hubInventory == null)
                    return true; // Hub not built, use vanilla

                // Try to find what group this displayer is showing
                // We can check the image sprite
                if (__instance.image == null || __instance.image.sprite == null)
                    return true;

                // Find the group that has this sprite
                Group displayedGroup = null;
                var allGroups = GroupsHandler.GetAllGroups();
                if (allGroups != null)
                {
                    foreach (var group in allGroups)
                    {
                        if (group == null) continue;
                        var groupSprite = group.GetImage();
                        if (groupSprite != null && groupSprite == __instance.image.sprite)
                        {
                            displayedGroup = group;
                            break;
                        }
                    }
                }

                if (displayedGroup == null)
                    return true;

                // Get the recipe
                var recipe = displayedGroup.GetRecipe();
                if (recipe == null)
                    return true;

                var ingredients = GetIngredientGroups(recipe);
                if (ingredients == null || ingredients.Count == 0)
                    return true;

                // Check if ALL ingredients are available in the Hub (1 of each)
                bool allInHub = true;

                foreach (Group ingredientGroup in ingredients)
                {
                    if (ingredientGroup == null) continue;

                    int hubCount = EpochHubLogistics.GetResourceCount(ingredientGroup.GetId());

                    if (hubCount < 1)
                    {
                        allInHub = false;
                        break;
                    }
                }

                if (allInHub)
                {
                    // All resources are in the Hub! Skip the greyed status.
                    // We need to manually set the colors to non-greyed
                    __instance.image.color = new Color(1f, 1f, 1f, 1f);
                    if (__instance.background != null)
                    {
                        __instance.background.color = new Color(1f, 1f, 1f, 1f);
                    }
                    Plugin.Logger?.LogDebug($"[Epoch Remote] Un-greying {displayedGroup.GetId()} - Hub has resources");
                    return false; // Skip the original SetGreyedStatus
                }

                return true; // Let vanilla handle greyed state
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Remote] Error in PrefixSetGreyedStatus: {ex.Message}");
                return true;
            }
        }

        // ============================================================
        // BUILDING - OnConstructed (Consume from Hub)
        // ============================================================

        [HarmonyPatch(typeof(PlayerBuilder), "OnConstructed")]
        [HarmonyPostfix]
        private static void PostfixOnConstructed(PlayerBuilder __instance, GameObject result)
        {
            try
            {
                var ghostGroupField = typeof(PlayerBuilder).GetField("_ghostGroupConstructible", BindingFlags.Instance | BindingFlags.NonPublic);
                if (ghostGroupField == null)
                    return;

                var ghostGroup = ghostGroupField.GetValue(__instance) as Group;
                if (ghostGroup == null)
                    return;

                var hubInventory = EpochNeural.EpochHubInventory;
                if (hubInventory == null)
                    return;

                var recipe = ghostGroup.GetRecipe();
                if (recipe == null)
                    return;

                var ingredients = GetIngredientGroups(recipe);
                if (ingredients == null || ingredients.Count == 0)
                    return;

                // Check if Hub has the resources
                bool allInHub = true;
                List<Group> neededGroups = new List<Group>();
                foreach (Group ingredientGroup in ingredients)
                {
                    if (ingredientGroup == null) continue;
                    int hubCount = EpochHubLogistics.GetResourceCount(ingredientGroup.GetId());
                    if (hubCount < 1)
                    {
                        allInHub = false;
                        break;
                    }
                    neededGroups.Add(ingredientGroup);
                }

                if (!allInHub)
                    return;

                // Consume from Hub
                bool consumed = ConsumeFromHub(neededGroups);
                if (!consumed)
                {
                    Plugin.Logger?.LogWarning($"[Epoch Build] Failed to consume ingredients from Hub!");
                    ShowMessage("EPOCH: Failed to consume resources from Hub!");
                    return;
                }

                Plugin.Logger?.LogInfo($"[Epoch Build] Consumed from Hub for {ghostGroup.GetId()}");
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Build] Error in PostfixOnConstructed: {ex.Message}");
            }
        }

        // ============================================================
        // CRAFTING
        // ============================================================

        [HarmonyPatch(typeof(CraftManager), "TryToCraftInInventory")]
        [HarmonyPostfix]
        private static void PostfixTryToCraftInInventory(
            ActionCrafter sourceCrafter,
            PlayerMainController playerController,
            GroupItem groupItem,
            bool __result)
        {
            try
            {
                if (!__result)
                    return;

                var hubInventory = EpochNeural.EpochHubInventory;
                if (hubInventory == null)
                    return;

                var recipe = groupItem.GetRecipe();
                if (recipe == null)
                    return;

                var ingredients = GetIngredientGroups(recipe);
                if (ingredients == null || ingredients.Count == 0)
                    return;

                bool allInHub = true;
                List<Group> neededGroups = new List<Group>();
                foreach (Group ingredientGroup in ingredients)
                {
                    if (ingredientGroup == null) continue;
                    int hubCount = EpochHubLogistics.GetResourceCount(ingredientGroup.GetId());
                    if (hubCount < 1)
                    {
                        allInHub = false;
                        break;
                    }
                    neededGroups.Add(ingredientGroup);
                }

                if (!allInHub)
                    return;

                bool consumed = ConsumeFromHub(neededGroups);
                if (!consumed)
                {
                    Plugin.Logger?.LogWarning($"[Epoch Craft] Failed to consume ingredients from Hub!");
                    return;
                }

                Plugin.Logger?.LogInfo($"[Epoch Craft] Consumed from Hub for {groupItem.GetId()}");
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Craft] Error in PostfixTryToCraftInInventory: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(CraftManager), "TryToCraftInWorld")]
        [HarmonyPostfix]
        private static void PostfixTryToCraftInWorld(
            ActionCrafter sourceCrafter,
            PlayerMainController playerController,
            GroupItem groupItem,
            bool checkSpawnPosition,
            bool __result)
        {
            try
            {
                if (!__result)
                    return;

                var hubInventory = EpochNeural.EpochHubInventory;
                if (hubInventory == null)
                    return;

                var recipe = groupItem.GetRecipe();
                if (recipe == null)
                    return;

                var ingredients = GetIngredientGroups(recipe);
                if (ingredients == null || ingredients.Count == 0)
                    return;

                bool allInHub = true;
                List<Group> neededGroups = new List<Group>();
                foreach (Group ingredientGroup in ingredients)
                {
                    if (ingredientGroup == null) continue;
                    int hubCount = EpochHubLogistics.GetResourceCount(ingredientGroup.GetId());
                    if (hubCount < 1)
                    {
                        allInHub = false;
                        break;
                    }
                    neededGroups.Add(ingredientGroup);
                }

                if (!allInHub)
                    return;

                bool consumed = ConsumeFromHub(neededGroups);
                if (!consumed)
                {
                    Plugin.Logger?.LogWarning($"[Epoch Craft] Failed to consume ingredients from Hub!");
                    return;
                }

                Plugin.Logger?.LogInfo($"[Epoch Craft] Consumed from Hub for {groupItem.GetId()}");
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Craft] Error in PostfixTryToCraftInWorld: {ex.Message}");
            }
        }

        // ============================================================
        // CONSUMPTION HELPER
        // ============================================================

        private static bool ConsumeFromHub(List<Group> neededGroups)
        {
            var hubInventory = EpochNeural.EpochHubInventory;
            if (hubInventory == null)
                return false;

            List<WorldObject> itemsToRemove = new List<WorldObject>();

            foreach (Group group in neededGroups)
            {
                if (group == null) continue;

                var items = hubInventory.GetInsideWorldObjects();
                if (items == null)
                    continue;

                bool found = false;
                foreach (WorldObject wo in items)
                {
                    if (wo == null || wo.GetGroup() == null)
                        continue;

                    if (wo.GetGroup().GetId() == group.GetId() && !wo.GetIsLockedInInventory())
                    {
                        itemsToRemove.Add(wo);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    Plugin.Logger?.LogWarning($"[Epoch Remote] Not enough {group.GetId()} in Hub!");
                    return false;
                }
            }

            foreach (WorldObject wo in itemsToRemove)
            {
                if (hubInventory.ContainWorldObject(wo))
                {
                    hubInventory.RemoveItem(wo);
                    WorldObjectsHandler.Instance.DestroyWorldObject(wo, true);
                }
            }

            EpochHubLogistics.RefreshCompressedStacks();
            Plugin.Logger?.LogInfo($"[Epoch Remote] Consumed {itemsToRemove.Count} items from Hub.");
            return true;
        }

        private static void ShowMessage(string message)
        {
            try
            {
                var hud = Managers.GetManager<BaseHudHandler>();
                if (hud != null)
                {
                    hud.DisplayCursorText(message, 3f);
                }
                else
                {
                    Plugin.Logger?.LogWarning(message);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Remote] Error showing message: {ex.Message}");
            }
        }
    }
}
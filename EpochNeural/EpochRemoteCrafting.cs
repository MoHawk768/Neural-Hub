using HarmonyLib;
using SpaceCraft;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace EpochNeural
{
    /// <summary>
    /// Core remote resource framework that overrides resource verification 
    /// loops directly inside the base engine inventory data block.
    /// </summary>
    [HarmonyPatch]
    internal static class EpochRemoteCrafting
    {
        // ============================================================
        // MASTER UI & PLACEMENT CHECK - Un-greys and unlocks building
        // ============================================================

        /// <summary>
        /// Intercepts the master method the game uses to determine if a building 
        /// or crafting item can be constructed or highlighted in color.
        /// </summary>
        [HarmonyPatch(typeof(Inventory), "ContainsItems")]
        [HarmonyPrefix]
        private static bool PrefixContainsItems(Inventory __instance, ref bool __result, List<Group> groups)
        {
            try
            {
                // Fallback to native check loops if evaluating the Hub container itself
                if (EpochNeural.EpochHubInventory != null && __instance.GetId() == EpochNeural.EpochHubInventory.GetId())
                    return true;

                if (groups == null || groups.Count == 0)
                {
                    __result = true;
                    return false;
                }

                // Gather our base tracking inventories
                var hubInventory = EpochNeural.EpochHubInventory;
                var rawBackpackItems = __instance.GetInsideWorldObjects();

                // Create a unified tracking ledger to trace item quantities safely
                Dictionary<string, int> combinedResourceLedger = new Dictionary<string, int>();

                // 1. Map existing resources found inside the player's local backpack
                if (rawBackpackItems != null)
                {
                    foreach (WorldObject item in rawBackpackItems)
                    {
                        if (item == null || item.GetGroup() == null) continue;
                        string id = item.GetGroup().GetId();
                        if (combinedResourceLedger.TryGetValue(id, out int count))
                            combinedResourceLedger[id] = count + 1;
                        else
                            combinedResourceLedger.Add(id, 1);
                    }
                }

                // 2. Map existing resources found inside our remote Hub infrastructure
                if (hubInventory != null)
                {
                    var rawHubItems = hubInventory.GetInsideWorldObjects();
                    if (rawHubItems != null)
                    {
                        foreach (WorldObject item in rawHubItems)
                        {
                            if (item == null || item.GetGroup() == null || item.GetIsLockedInInventory()) continue;
                            string id = item.GetGroup().GetId();
                            if (combinedResourceLedger.TryGetValue(id, out int count))
                                combinedResourceLedger[id] = count + 1;
                            else
                                combinedResourceLedger.Add(id, 1);
                        }
                    }
                }

                // 3. Evaluate if the total cluster values satisfy the construction demands
                bool canAffordRecipe = true;
                foreach (Group requiredGroup in groups)
                {
                    if (requiredGroup == null) continue;
                    string reqId = requiredGroup.GetId();

                    if (combinedResourceLedger.ContainsKey(reqId) && combinedResourceLedger[reqId] >= 1)
                    {
                        combinedResourceLedger[reqId]--; // Mentally allocate resource
                    }
                    else
                    {
                        canAffordRecipe = false;
                        break;
                    }
                }

                // If resources exist across the shared storage bank, bypass the game's backpack check
                if (canAffordRecipe)
                {
                    __result = true;
                    return false; // Skip the engine's default backpack checks entirely
                }

                return true; // Fallback to vanilla if neither storage pools have the items
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Remote] Shared registry verification crash: {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// Secondary fallback handler matching singular sub-element layout checks.
        /// </summary>
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.ContainGroup))]
        [HarmonyPrefix]
        private static bool PrefixContainGroup(Inventory __instance, Group group, ref bool __result)
        {
            try
            {
                if (group == null || (EpochNeural.EpochHubInventory != null && __instance.GetId() == EpochNeural.EpochHubInventory.GetId()))
                    return true;

                // Check local inventory directly.
                // IMPORTANT: this patch targets Inventory.ContainGroup itself, so
                // calling __instance.ContainGroup(group) here would re-enter this
                // Harmony prefix recursively and can hard-crash the game when
                // machines such as AutoCrafter query their inventory repeatedly.
                var localItems = __instance.GetInsideWorldObjects();
                if (localItems != null)
                {
                    string requiredId = group.GetId();
                    foreach (WorldObject localItem in localItems)
                    {
                        if (localItem?.GetGroup()?.GetId() == requiredId)
                            return true;
                    }
                }

                // Check Hub database registry
                int hubCount = EpochHubLogistics.GetResourceCount(group.GetId());
                if (hubCount > 0)
                {
                    __result = true;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Remote] ContainGroup safety check mismatch: {ex.Message}");
                return true;
            }
        }
        // ============================================================
        // CONSUMPTION HOOKS - Handles items upon placement
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

                var recipe = ghostGroup.GetRecipe();
                if (recipe == null)
                    return;

                var ingredients = recipe.GetIngredientsGroupInRecipe();
                if (ingredients == null || ingredients.Count == 0)
                    return;

                // Process item consumption safely from shared storage blocks
                ConsumeSharedResources(ingredients);
                Plugin.Logger?.LogInfo($"[Epoch Build] Consumed shared ingredients for building: {ghostGroup.GetId()}");
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Build] Construction processing pipeline failure: {ex.Message}");
            }
        }

        // ============================================================
        // CRAFTING TABLE SYNC HOOKS
        // ============================================================

        [HarmonyPatch(typeof(CraftManager), "TryToCraftInInventory")]
        [HarmonyPostfix]
        private static void PostfixTryToCraftInInventory(ActionCrafter sourceCrafter, PlayerMainController playerController, GroupItem groupItem, bool __result)
        {
            try
            {
                if (!__result) return;

                // IMPORTANT:
                // AutoCrafter inputs are now supplied physically by Epoch's
                // machine-logistics pass.  Do NOT also consume those recipe
                // ingredients from the Hub here.  The vanilla AutoCrafter
                // must own the crafting transaction from this point onward.
                if (IsAutoCrafterCrafter(sourceCrafter))
                {
                    Plugin.Logger?.LogInfo($"[Epoch Craft] AutoCrafter craft detected; skipping remote ingredient consumption for {groupItem?.GetId()}.");
                    return;
                }

                var recipe = groupItem.GetRecipe();
                if (recipe == null) return;

                var ingredients = recipe.GetIngredientsGroupInRecipe();
                if (ingredients == null || ingredients.Count == 0) return;

                ConsumeSharedResources(ingredients);
                Plugin.Logger?.LogInfo($"[Epoch Craft] Consumed shared ingredients for craft slot: {groupItem.GetId()}");
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Craft] Craft execution database track error: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(CraftManager), "TryToCraftInWorld")]
        [HarmonyPostfix]
        private static void PostfixTryToCraftInWorld(ActionCrafter sourceCrafter, PlayerMainController playerController, GroupItem groupItem, bool checkSpawnPosition, bool __result)
        {
            try
            {
                if (!__result) return;

                // Same protection for world-instanced AutoCrafter crafting.
                // Epoch machine logistics supplies the physical ingredients;
                // vanilla crafting handles their consumption.
                if (IsAutoCrafterCrafter(sourceCrafter))
                {
                    Plugin.Logger?.LogInfo($"[Epoch Craft] AutoCrafter world craft detected; skipping remote ingredient consumption for {groupItem?.GetId()}.");
                    return;
                }

                var recipe = groupItem.GetRecipe();
                if (recipe == null) return;

                var ingredients = recipe.GetIngredientsGroupInRecipe();
                if (ingredients == null || ingredients.Count == 0) return;

                ConsumeSharedResources(ingredients);
                Plugin.Logger?.LogInfo($"[Epoch Craft] Consumed shared ingredients for world instanced item: {groupItem.GetId()}");
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Craft] Physical coordinate instancing sync error: {ex.Message}");
            }
        }

        // ============================================================
        // AUTO-CRAFTER SOURCE DETECTION
        // ============================================================
        // Keep normal player remote crafting intact.  Only bypass the old
        // shared-Hub consumption path when CraftManager is being driven by
        // an AutoCrafter.  Reflection is deliberately defensive because the
        // ActionCrafter internals differ between Planet Crafter builds.
        private static bool IsAutoCrafterCrafter(ActionCrafter sourceCrafter)
        {
            if (sourceCrafter == null)
                return false;

            try
            {
                // Fast path: Unity hierarchy/name information.
                var component = sourceCrafter as UnityEngine.Component;
                if (component != null)
                {
                    Transform current = component.transform;
                    while (current != null)
                    {
                        if (!string.IsNullOrEmpty(current.name) &&
                            current.name.IndexOf("AutoCrafter", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;

                        current = current.parent;
                    }
                }

                // Defensive reflection path: look for a WorldObject/Group
                // reference carried by the ActionCrafter implementation.
                Type type = sourceCrafter.GetType();
                const BindingFlags flags = BindingFlags.Instance |
                                           BindingFlags.Public |
                                           BindingFlags.NonPublic;

                foreach (FieldInfo field in type.GetFields(flags))
                {
                    object value;
                    try { value = field.GetValue(sourceCrafter); }
                    catch { continue; }

                    if (IsAutoCrafterReference(value))
                        return true;
                }

                foreach (PropertyInfo property in type.GetProperties(flags))
                {
                    if (!property.CanRead || property.GetIndexParameters().Length != 0)
                        continue;

                    object value;
                    try { value = property.GetValue(sourceCrafter, null); }
                    catch { continue; }

                    if (IsAutoCrafterReference(value))
                        return true;
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogDebug($"[Epoch Craft] AutoCrafter source detection skipped safely: {ex.Message}");
            }

            return false;
        }

        private static bool IsAutoCrafterReference(object value)
        {
            if (value == null)
                return false;

            try
            {
                if (value is WorldObject worldObject)
                {
                    string id = worldObject.GetGroup()?.GetId();
                    return !string.IsNullOrEmpty(id) &&
                           id.IndexOf("AutoCrafter", StringComparison.OrdinalIgnoreCase) >= 0;
                }

                if (value is Group group)
                {
                    string id = group.GetId();
                    return !string.IsNullOrEmpty(id) &&
                           id.IndexOf("AutoCrafter", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { }

            return false;
        }

        // ============================================================
        // SHARED RESOURCE DEDUCTION ENGINE
        // ============================================================

        private static void ConsumeSharedResources(List<Group> ingredients)
        {
            PlayerMainController player = Managers.GetManager<PlayersManager>()?.GetActivePlayerController();
            Inventory backpack = player?.GetPlayerBackpack()?.GetInventory();
            Inventory hub = EpochNeural.EpochHubInventory;

            foreach (Group ingredient in ingredients)
            {
                if (ingredient == null) continue;
                string reqId = ingredient.GetId();
                bool itemCleared = false;

                // Priority 1: Consume from player backpack first if available
                if (backpack != null)
                {
                    var backpackItems = backpack.GetInsideWorldObjects();
                    for (int i = backpackItems.Count - 1; i >= 0; i--)
                    {
                        WorldObject wo = backpackItems[i];
                        if (wo != null && wo.GetGroup()?.GetId() == reqId)
                        {
                            // MANDATORY FIX: Skip any object if it is a constructed structure type (CraftStation, Drill, etc.)
                            if (wo.GetGroup() is GroupItem || wo.GetLinkedInventoryId() > 0) continue;

                            backpack.RemoveItem(wo);
                            WorldObjectsHandler.Instance.DestroyWorldObject(wo, true);
                            itemCleared = true;
                            break;
                        }
                    }
                }

                if (itemCleared) continue;

                // Priority 2: Consume from remote Epoch Hub inventory
                if (hub != null)
                {
                    var hubItems = hub.GetInsideWorldObjects();
                    for (int i = hubItems.Count - 1; i >= 0; i--)
                    {
                        WorldObject wo = hubItems[i];
                        if (wo != null && wo.GetGroup()?.GetId() == reqId && !wo.GetIsLockedInInventory())
                        {
                            // MANDATORY FIX: Skip any object if it is a constructed structure type (CraftStation, Drill, etc.)
                            if (wo.GetGroup() is GroupItem || wo.GetLinkedInventoryId() > 0) continue;

                            hub.RemoveItem(wo);
                            WorldObjectsHandler.Instance.DestroyWorldObject(wo, true);
                            break;
                        }
                    }
                }
            }

            EpochHubLogistics.RefreshCompressedStacks();
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
            }
            catch { }
        }
    }

    // ============================================================
    // CONSTRUCTION RECIPE AVAILABILITY
    // Shows Hub resources as available in the construction recipe UI
    // ============================================================

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.ItemsContainsStatus))]
    internal static class EpochRecipeAvailabilityPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Inventory __instance,
            List<Group> groups,
            ref List<bool> __result)
        {
            try
            {
                if (__instance == null || groups == null || groups.Count == 0)
                    return;

                var player =
                    Managers.GetManager<PlayersManager>()?
                        .GetActivePlayerController();

                if (player == null)
                    return;

                Inventory playerBackpack =
                    player.GetPlayerBackpack()?.GetInventory();

                // Only augment the player's backpack availability.
                if (playerBackpack == null ||
                    __instance.GetId() != playerBackpack.GetId())
                    return;

                Inventory hubInventory = EpochNeural.EpochHubInventory;

                if (hubInventory == null)
                    return;

                var hubItems = hubInventory.GetInsideWorldObjects();

                if (hubItems == null || hubItems.Count == 0)
                    return;

                // Track which Hub objects have already been allocated
                // to recipe ingredients.
                HashSet<int> usedHubObjects = new HashSet<int>();

                for (int i = 0; i < groups.Count; i++)
                {
                    // Already satisfied by backpack.
                    if (i < __result.Count && __result[i])
                        continue;

                    Group requiredGroup = groups[i];

                    if (requiredGroup == null)
                        continue;

                    // Find one unused matching object in the Hub.
                    foreach (WorldObject hubObject in hubItems)
                    {
                        if (hubObject == null)
                            continue;

                        if (usedHubObjects.Contains(hubObject.GetId()))
                            continue;

                        Group hubGroup = hubObject.GetGroup();

                        if (hubGroup == null)
                            continue;

                        if (hubGroup.GetId() != requiredGroup.GetId())
                            continue;

                        if (hubObject.GetIsLockedInInventory())
                            continue;

                        usedHubObjects.Add(hubObject.GetId());

                        if (i < __result.Count)
                            __result[i] = true;

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Remote] Recipe availability patch failed: {ex}");
            }
        }
    }
}
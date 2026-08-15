using System;
using System.Collections.Generic;
using HarmonyLib;
using SpaceCraft;

namespace EpochNeural
{
    /// <summary>
    /// Epoch Hub inventory rules.
    ///
    /// This patch does NOT change the Epoch tier system or stack sizes.
    ///
    /// It enforces two rules:
    ///
    /// 1. A resource may occupy only ONE Epoch Hub slot.
    ///    Once that resource reaches the current tier's stack cap,
    ///    additional copies are rejected.
    ///
    /// 2. Vanilla Inventory.AutoSort() is allowed to determine the
    ///    resource ordering. Epoch's stable visual ordering cache is
    ///    cleared immediately after a Sort operation so the new order
    ///    becomes visible.
    /// </summary>
    [HarmonyPatch]
    internal static class EpochHubInventoryRules
    {
        // ============================================================
        // PREVENT SECOND STACK OF A FULL RESOURCE
        // ============================================================

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem))]
        [HarmonyPrefix]
        private static bool PrefixAddItem(
            Inventory __instance,
            WorldObject worldObject,
            ref bool __result)
        {
            try
            {
                // Only modify the Epoch Hub.
                if (!IsEpochHubInventory(__instance))
                    return true;

                if (worldObject == null ||
                    worldObject.GetGroup() == null)
                {
                    return true;
                }

                string groupId =
                    worldObject.GetGroup().GetId();

                if (string.IsNullOrEmpty(groupId))
                    return true;

                // ----------------------------------------------------
                // Check the CURRENT Epoch tier stack capacity.
                //
                // This uses the existing Epoch tier system.
                // Nothing about tier progression is changed here.
                // ----------------------------------------------------

                int stackCap =
                    EpochHubLogistics.GetCurrentStackCap();

                // Count the existing resource directly from the
                // actual Hub inventory.
                int existingCount =
                    CountResourceInHub(
                        __instance,
                        groupId);

                // ----------------------------------------------------
                // Resource already exists and is full.
                //
                // Reject this item completely.
                //
                // Returning false prevents vanilla AddItem() from
                // adding the WorldObject to the Hub.
                // ----------------------------------------------------

                if (existingCount >= stackCap)
                {
                    __result = false;

                    Plugin.Logger?.LogInfo(
                        $"[Epoch Hub] REJECTED {groupId}: " +
                        $"resource slot full ({existingCount}/{stackCap}).");

                    return false;
                }

                // ----------------------------------------------------
                // Resource exists but has room.
                //
                // Allow vanilla AddItem().
                //
                // Resource will join the existing compressed stack.
                // ----------------------------------------------------

                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Hub] AddItem rule error: {ex.Message}");

                // Never break normal inventory behaviour because
                // of the Epoch protection layer.
                return true;
            }
        }

        // ============================================================
        // VANILLA SORT
        // ============================================================

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AutoSort))]
        [HarmonyPostfix]
        private static void PostfixAutoSort(
            Inventory __instance)
        {
            try
            {
                if (!IsEpochHubInventory(__instance))
                    return;

                // ----------------------------------------------------
                // Vanilla has already sorted the underlying
                // WorldObjects at this point.
                //
                // Epoch's BuildCustomStacks() normally preserves
                // stable order using _stableGroupOrder.
                //
                // Reset that ordering so Epoch rebuilds the visual
                // stacks from the newly sorted raw inventory.
                // ----------------------------------------------------

                ResetEpochVisualOrder(__instance);

                EpochHubLogistics.RefreshCompressedStacks();

                Plugin.Logger?.LogInfo(
                    "[Epoch Hub] Sort applied. " +
                    "Epoch visual stack order refreshed.");
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Hub] Sort refresh error: {ex.Message}");
            }
        }

        // ============================================================
        // COUNT RESOURCE
        // ============================================================

        private static int CountResourceInHub(
            Inventory inventory,
            string groupId)
        {
            if (inventory == null ||
                string.IsNullOrEmpty(groupId))
            {
                return 0;
            }

            var items =
                inventory.GetInsideWorldObjects();

            if (items == null ||
                items.Count == 0)
            {
                return 0;
            }

            int count = 0;

            foreach (WorldObject wo in items)
            {
                if (wo == null ||
                    wo.GetGroup() == null)
                {
                    continue;
                }

                string existingGroupId =
                    wo.GetGroup().GetId();

                if (string.Equals(
                    existingGroupId,
                    groupId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }

        // ============================================================
        // EPOCH HUB IDENTIFICATION
        // ============================================================

        private static bool IsEpochHubInventory(
            Inventory inventory)
        {
            if (inventory == null)
                return false;

            Inventory hub =
                EpochNeural.EpochHubInventory;

            if (hub == null)
                return false;

            return inventory.GetId() ==
                   hub.GetId();
        }

        // ============================================================
        // RESET EPOCH VISUAL ORDER
        // ============================================================

        private static void ResetEpochVisualOrder(
            Inventory inventory)
        {
            try
            {
                // EpochHubLogistics owns the stable-order dictionary.
                //
                // We cannot access it directly because it is private,
                // so use reflection to clear only the Hub's entry.
                var field =
                    typeof(EpochHubLogistics).GetField(
                        "_stableGroupOrder",
                        System.Reflection.BindingFlags.Static |
                        System.Reflection.BindingFlags.NonPublic);

                if (field == null)
                {
                    Plugin.Logger?.LogWarning(
                        "[Epoch Hub] Could not access stable sort cache.");

                    return;
                }

                var dictionary =
                    field.GetValue(null)
                    as Dictionary<int, Dictionary<string, int>>;

                if (dictionary == null)
                    return;

                int inventoryId =
                    inventory.GetId();

                dictionary.Remove(inventoryId);
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Hub] Failed to reset visual order: {ex.Message}");
            }
        }
    }
}
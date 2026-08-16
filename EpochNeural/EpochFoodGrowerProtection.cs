using System;
using System.Collections.Generic;
using HarmonyLib;
using SpaceCraft;

namespace EpochNeural
{
    /// <summary>
    /// Prevents Epoch World Collection from harvesting
    /// unfinished food crops from vanilla vegetation growers.
    ///
    /// This intentionally only targets Vegetable*Growable
    /// WorldObjects. Other production systems are left untouched
    /// until an actual problem is confirmed.
    /// </summary>
    [HarmonyPatch]
    internal static class EpochFoodGrowerProtection
    {
        [HarmonyPatch(typeof(EpochVacuumDiscovery), "DiscoverCollectibles")]
        [HarmonyPostfix]
        private static void ProtectUnfinishedFoodGrowers(
            ref EpochVacuumDiscovery.DiscoveryResult __result)
        {
            try
            {
                if (__result == null ||
                    __result.Objects == null ||
                    __result.Objects.Count == 0)
                {
                    return;
                }

                int removed = 0;

                for (int i = __result.Objects.Count - 1; i >= 0; i--)
                {
                    WorldObject worldObject =
                        __result.Objects[i];

                    if (!IsUnfinishedFoodGrowable(worldObject))
                        continue;

                    string groupId =
                        worldObject.GetGroup()?.GetId();

                    float growth =
                        worldObject.GetGrowth();

                    __result.Objects.RemoveAt(i);

                    if (!string.IsNullOrEmpty(groupId) &&
                        __result.ResourceCounts.ContainsKey(groupId))
                    {
                        __result.ResourceCounts[groupId]--;

                        if (__result.ResourceCounts[groupId] <= 0)
                        {
                            __result.ResourceCounts.Remove(groupId);
                        }
                    }

                    removed++;

                    Plugin.Logger?.LogInfo(
                        $"[Epoch Food Protection] " +
                        $"Ignored unfinished {groupId} " +
                        $"({growth:F1}% grown).");
                }

                if (removed > 0)
                {
                    __result.ReturnedObjects =
                        __result.Objects.Count;
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Food Protection] " +
                    $"Protection check failed: {ex.Message}");
            }
        }

        private static bool IsUnfinishedFoodGrowable(
            WorldObject worldObject)
        {
            if (worldObject == null)
                return false;

            try
            {
                Group group = worldObject.GetGroup();

                if (group == null)
                    return false;

                string groupId = group.GetId();

                if (string.IsNullOrEmpty(groupId))
                    return false;

                // Only target vanilla food-grower outputs.
                //
                // Examples:
                // Vegetable0Growable
                // Vegetable1Growable
                // Vegetable2Growable
                // Vegetable3Growable
                //
                // Seeds and other objects are NOT affected.
                if (!groupId.StartsWith(
                        "Vegetable",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!groupId.EndsWith(
                        "Growable",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Vanilla considers the crop ready at 100%.
                return worldObject.GetGrowth() < 100f;
            }
            catch
            {
                return false;
            }
        }
    }
}
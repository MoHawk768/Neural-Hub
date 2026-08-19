using System.Collections.Generic;
using UnityEngine;
using SpaceCraft;

namespace EpochNeural
{
    /// <summary>
    /// Responsible ONLY for discovering collectible WorldObjects.
    /// No inventory logic belongs here.
    /// </summary>
    internal static class EpochVacuumDiscovery
    {
        private static float _lastLogTime = 0f;
        private const float LOG_INTERVAL = 60f; // Log full scan only once per minute
        private static int _scanCount = 0;

        internal sealed class DiscoveryResult
        {
            public readonly List<WorldObject> Objects = new List<WorldObject>();

            public readonly Dictionary<string, int> ResourceCounts =
                new Dictionary<string, int>();

            public int TotalAssociatedObjects;
            public int ReturnedObjects;
            public int InventoryLinkedObjects;
            public int MissingWorldObjects;
        }

        internal static DiscoveryResult DiscoverCollectibles()
        {
            DiscoveryResult result = new DiscoveryResult();

            HashSet<int> seenIds = new HashSet<int>();

            WorldObjectAssociated[] associatedObjects =
                Object.FindObjectsByType<WorldObjectAssociated>(
                    FindObjectsSortMode.None);

            result.TotalAssociatedObjects = associatedObjects.Length;

            foreach (WorldObjectAssociated assoc in associatedObjects)
            {
                if (assoc == null)
                    continue;

                WorldObject wo = assoc.GetWorldObject();

                if (wo == null)
                {
                    result.MissingWorldObjects++;
                    continue;
                }

                if (wo.HasLinkedInventory())
                {
                    result.InventoryLinkedObjects++;
                    continue;
                }

                if (wo.GetGameObject() == null)
                    continue;

                if (!wo.GetGameObject().activeInHierarchy)
                    continue;

                if (!seenIds.Add(wo.GetId()))
                    continue;

                result.Objects.Add(wo);

                try
                {
                    Group group = wo.GetGroup();

                    if (group != null)
                    {
                        string id = group.GetId();

                        if (!string.IsNullOrEmpty(id))
                        {
                            if (result.ResourceCounts.TryGetValue(id, out int count))
                                result.ResourceCounts[id] = count + 1;
                            else
                                result.ResourceCounts.Add(id, 1);
                        }
                    }
                }
                catch
                {
                    // Ignore malformed objects.
                }
            }

            result.ReturnedObjects = result.Objects.Count;

            // ============================================================
            // OPTIMIZED LOGGING - Only log full scan every 60 seconds
            // ============================================================
            _scanCount++;

            // Always log a summary line (minimal)
            Plugin.Logger?.LogInfo(
                $"[DISCOVERY] Scan #{_scanCount}: {result.ReturnedObjects} collectible objects, " +
                $"{result.ResourceCounts.Count} resource types");

            // Full detailed log only every 60 seconds (or if DebugLogging is enabled)
            bool shouldLogFull = Plugin.DebugLogging || (Time.time - _lastLogTime >= LOG_INTERVAL);

            if (shouldLogFull)
            {
                _lastLogTime = Time.time;

                Plugin.Logger?.LogInfo("");
                Plugin.Logger?.LogInfo("==================================================");
                Plugin.Logger?.LogInfo("[DISCOVERY] Epoch World Scan (FULL)");
                Plugin.Logger?.LogInfo("==================================================");
                Plugin.Logger?.LogInfo($"Associated Objects : {result.TotalAssociatedObjects}");
                Plugin.Logger?.LogInfo($"Collectible Objects: {result.ReturnedObjects}");
                Plugin.Logger?.LogInfo($"Linked Inventories : {result.InventoryLinkedObjects}");
                Plugin.Logger?.LogInfo($"Missing WorldObjs  : {result.MissingWorldObjects}");
                Plugin.Logger?.LogInfo("");

                foreach (var pair in result.ResourceCounts)
                {
                    Plugin.Logger.LogInfo(
                        $"{pair.Key.PadRight(18)} : {pair.Value}");
                }

                Plugin.Logger?.LogInfo("==================================================");
                Plugin.Logger?.LogInfo("");
            }

            return result;
        }
    }
}
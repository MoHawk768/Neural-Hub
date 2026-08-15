using System;
using System.Collections.Generic;
using SpaceCraft;
using UnityEngine;

namespace EpochNeural
{
    /// <summary>
    /// Global construction system - allows building anywhere using Hub resources
    /// </summary>
    public static class EpochGlobalBuilder
    {
        private static readonly Dictionary<string, int> _resourceCache = new Dictionary<string, int>();
        private static float _lastCacheTime = 0f;
        private const float CACHE_INTERVAL = 1f;

        /// <summary>
        /// Check if the Hub has all required resources
        /// </summary>
        public static bool HubHasResources(List<Group> requiredResources)
        {
            if (requiredResources == null || requiredResources.Count == 0)
                return true;

            var hubInventory = EpochNeural.EpochHubInventory;
            if (hubInventory == null)
                return false;

            RefreshResourceCache();

            foreach (Group group in requiredResources)
            {
                if (group == null) continue;
                string groupId = group.GetId();

                // Check if Hub has at least 1 of this resource
                int hubCount = EpochHubLogistics.GetResourceCount(groupId);
                if (hubCount <= 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Get the total count of a specific resource in the Hub
        /// </summary>
        public static int GetHubResourceCount(string groupId)
        {
            return EpochHubLogistics.GetResourceCount(groupId);
        }

        /// <summary>
        /// Deduct resources from the Hub inventory
        /// </summary>
        public static bool DeductFromHub(List<Group> requiredResources)
        {
            if (requiredResources == null || requiredResources.Count == 0)
                return true;

            var hubInventory = EpochNeural.EpochHubInventory;
            if (hubInventory == null)
                return false;

            bool allFound = true;

            foreach (Group group in requiredResources)
            {
                if (group == null) continue;

                // Find one WorldObject of this group in the Hub
                WorldObject itemToRemove = null;
                var items = hubInventory.GetInsideWorldObjects();
                foreach (WorldObject wo in items)
                {
                    if (wo != null && wo.GetGroup() != null && wo.GetGroup().GetId() == group.GetId())
                    {
                        if (!wo.GetIsLockedInInventory())
                        {
                            itemToRemove = wo;
                            break;
                        }
                    }
                }

                if (itemToRemove != null)
                {
                    // Remove from Hub
                    hubInventory.RemoveItem(itemToRemove);
                    // Destroy the WorldObject (it's consumed)
                    WorldObjectsHandler.Instance.DestroyWorldObject(itemToRemove);
                    Plugin.Logger?.LogDebug($"[GlobalBuilder] Deducted {group.GetId()} from Hub");
                }
                else
                {
                    allFound = false;
                    Plugin.Logger?.LogWarning($"[GlobalBuilder] Could not find {group.GetId()} in Hub");
                }
            }

            // Refresh stacks after deduction
            EpochHubLogistics.RefreshCompressedStacks();

            return allFound;
        }

        /// <summary>
        /// Try to build using Hub resources first, fallback to player inventory
        /// </summary>
        public static bool TryBuildFromHub(List<Group> requiredResources, Inventory playerInventory, out bool usedHub)
        {
            usedHub = false;

            if (requiredResources == null || requiredResources.Count == 0)
            {
                usedHub = false;
                return true;
            }

            // Check if Free Craft is enabled
            bool freeCraft = Managers.GetManager<GameSettingsHandler>().GetCurrentGameSettings().GetFreeCraft();
            if (freeCraft)
            {
                usedHub = false;
                return true;
            }

            // Check if Hub exists and has resources
            bool hubHasResources = HubHasResources(requiredResources);

            if (hubHasResources)
            {
                // Check if player has room for the crafted item
                if (playerInventory.IsFull())
                {
                    Plugin.Logger?.LogDebug("[GlobalBuilder] Player inventory full, cannot craft");
                    return false;
                }

                // Deduct from Hub
                bool success = DeductFromHub(requiredResources);
                if (success)
                {
                    usedHub = true;
                    Plugin.Logger?.LogInfo($"[GlobalBuilder] Built from Hub resources");

                    // Show notification
                    if (EpochHud.Instance != null)
                    {
                        EpochHud.Instance.ShowNotification("Built from Hub", true);
                    }

                    return true;
                }
            }

            // Fallback: Check player inventory
            if (playerInventory.ContainsItems(requiredResources))
            {
                usedHub = false;
                return true;
            }

            Plugin.Logger?.LogDebug("[GlobalBuilder] Not enough resources in Hub or inventory");
            return false;
        }

        /// <summary>
        /// Refresh the resource cache
        /// </summary>
        private static void RefreshResourceCache()
        {
            if (Time.time - _lastCacheTime < CACHE_INTERVAL)
                return;

            _resourceCache.Clear();
            _lastCacheTime = Time.time;

            var hubInventory = EpochNeural.EpochHubInventory;
            if (hubInventory == null)
                return;

            var items = hubInventory.GetInsideWorldObjects();
            if (items == null)
                return;

            foreach (WorldObject wo in items)
            {
                if (wo == null || wo.GetGroup() == null) continue;
                string groupId = wo.GetGroup().GetId();
                if (string.IsNullOrEmpty(groupId)) continue;

                if (_resourceCache.ContainsKey(groupId))
                    _resourceCache[groupId]++;
                else
                    _resourceCache[groupId] = 1;
            }
        }

        /// <summary>
        /// Check if building from Hub is enabled
        /// </summary>
        public static bool IsHubBuildingEnabled()
        {
            return EpochNeural.EpochHubInventory != null;
        }
    }
}
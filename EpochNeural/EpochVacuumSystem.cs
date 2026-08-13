using SpaceCraft;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace EpochNeural
{
    internal static class EpochVacuumSystem
    {
        // ============================================================
        // CONFIGURATION
        // ============================================================

        private const float VacuumInterval = 30f;
        private const int MaxPickupsPerCycle = 250;
        private const int MaxStackSize = EpochHubLogistics.StaticStackCap;

        // ============================================================
        // RUNTIME STATE
        // ============================================================

        private static Inventory _hubInventory;
        private static readonly HashSet<string> _learnedResources = new HashSet<string>();
        private static readonly Dictionary<string, int> _resourceCounts = new Dictionary<string, int>();
        private static bool _scanRunning;
        private static float _nextScanTime;
        private static int _totalVacuumed;
        private static int _scanNumber;
        private static int _lastObjectsSeen;
        private static string _hudState = "WAITING";
        private static bool _initialized;

        // ============================================================
        // INITIALIZATION
        // ============================================================

        internal static void Initialize(Inventory inventory)
        {
            if (inventory == null) return;

            _hubInventory = inventory;
            EpochNeural.EpochHubInventory = inventory;

            if (!_initialized)
            {
                _initialized = true;
                _nextScanTime = Time.time + VacuumInterval;
                Plugin.Logger.LogInfo("[Epoch Hub] Logistics Engine started.");
            }

            LearnInventoryResources();
            RefreshDevelopmentHud();
        }

        // ============================================================
        // UPDATE
        // ============================================================

        internal static void UpdateVacuum()
        {
            if (!_initialized || _hubInventory == null || _scanRunning || _learnedResources.Count == 0)
                return;

            if (Time.time < _nextScanTime) return;

            _scanRunning = true;

            try
            {
                RunLogisticsCycle();
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError("[Epoch Hub] Logistics cycle failed\n" + ex);
            }
            finally
            {
                _scanRunning = false;
                _nextScanTime = Time.time + VacuumInterval;
            }
        }

        // ============================================================
        // MAIN CYCLE
        // ============================================================

        private static void RunLogisticsCycle()
        {
            _scanNumber++;
            _hudState = "SCANNING";

            Plugin.Logger.LogInfo($"[Epoch Hub] Logistics Cycle {_scanNumber}");

            LearnInventoryResources();
            BuildCurrentResourceCounts();

            int collectedFromWorld = CollectKnownResourcesFromWorld();
            int collectedFromExtractors = CollectKnownResourcesFromExtractors();
            int totalCollected = collectedFromWorld + collectedFromExtractors;

            BuildCurrentResourceCounts();

            _totalVacuumed += totalCollected;
            _hudState = "ONLINE";

            Plugin.Logger.LogInfo($"[Epoch Hub] Collected {totalCollected} resource(s) this cycle (World: {collectedFromWorld}, Extractors: {collectedFromExtractors}). Lifetime Total: {_totalVacuumed}");

            RefreshDevelopmentHud();
        }

        // ============================================================
        // LEARNING
        // ============================================================

        private static void LearnInventoryResources()
        {
            if (_hubInventory == null) return;

            var items = _hubInventory.GetInsideWorldObjects();
            if (items == null) return;

            foreach (WorldObject wo in items)
            {
                if (wo == null) continue;
                string id = GetGroupId(wo);
                if (string.IsNullOrEmpty(id)) continue;
                _learnedResources.Add(id);
            }
        }

        // ============================================================
        // INVENTORY COUNTING
        // ============================================================

        private static void BuildCurrentResourceCounts()
        {
            _resourceCounts.Clear();

            var items = _hubInventory.GetInsideWorldObjects();
            if (items == null) return;

            foreach (WorldObject wo in items)
            {
                if (wo == null) continue;
                string id = GetGroupId(wo);
                if (string.IsNullOrEmpty(id)) continue;
                int amount = wo.GetCount().x;
                if (_resourceCounts.ContainsKey(id))
                    _resourceCounts[id] += amount;
                else
                    _resourceCounts.Add(id, amount);
            }
        }

        // ============================================================
        // COLLECTION ENGINE - WORLD
        // ============================================================

        private static int CollectKnownResourcesFromWorld()
        {
            EpochVacuumDiscovery.DiscoveryResult discovery = EpochVacuumDiscovery.DiscoverCollectibles();
            _lastObjectsSeen = discovery.ReturnedObjects;

            Plugin.Logger.LogInfo("========== Epoch Discovery ==========");
            Plugin.Logger.LogInfo($"Associated Objects : {discovery.TotalAssociatedObjects}");
            Plugin.Logger.LogInfo($"Returned Objects   : {discovery.ReturnedObjects}");
            Plugin.Logger.LogInfo($"Linked Inventories : {discovery.InventoryLinkedObjects}");
            Plugin.Logger.LogInfo($"Missing WorldObjs  : {discovery.MissingWorldObjects}");
            Plugin.Logger.LogInfo("=====================================");

            if (discovery.Objects == null || discovery.Objects.Count == 0) return 0;

            Dictionary<string, List<WorldObject>> groups = BuildResourceGroups(discovery.Objects);
            if (groups.Count == 0) return 0;

            List<string> activeResources = GetActiveResources(groups);
            if (activeResources.Count == 0) return 0;

            int remainingBudget = MaxPickupsPerCycle;
            int collected = 0;

            Plugin.Logger.LogInfo($"[Epoch Hub] Learned Resources : {_learnedResources.Count}");
            Plugin.Logger.LogInfo($"[Epoch Hub] Active Resources : {activeResources.Count}");
            Plugin.Logger.LogInfo($"[Epoch Hub] Work Budget : {remainingBudget}");

            while (remainingBudget > 0 && activeResources.Count > 0)
            {
                int allocation = Mathf.Max(1, remainingBudget / activeResources.Count);
                bool collectedAnythingThisPass = false;

                for (int i = activeResources.Count - 1; i >= 0; i--)
                {
                    string resourceId = activeResources[i];

                    EpochHubLogistics.RefreshCompressedStacks();

                    if (EpochHubLogistics.IsResourceSlotFull(resourceId))
                    {
                        activeResources.RemoveAt(i);
                        continue;
                    }

                    int currentAmount = EpochHubLogistics.GetResourceCount(resourceId);
                    int remainingCapacity = MaxStackSize - currentAmount;

                    if (remainingCapacity <= 0)
                    {
                        activeResources.RemoveAt(i);
                        continue;
                    }

                    int actualAllocation = Mathf.Min(allocation, remainingCapacity);
                    int collectedForGroup = CollectResourceGroup(resourceId, groups[resourceId], actualAllocation);

                    if (collectedForGroup > 0)
                    {
                        collected += collectedForGroup;
                        remainingBudget -= collectedForGroup;
                        collectedAnythingThisPass = true;
                    }

                    EpochHubLogistics.RefreshCompressedStacks();

                    currentAmount = EpochHubLogistics.GetResourceCount(resourceId);
                    remainingCapacity = MaxStackSize - currentAmount;

                    if (groups[resourceId].Count == 0 || remainingCapacity <= 0)
                    {
                        activeResources.RemoveAt(i);
                    }

                    if (remainingBudget <= 0) break;
                }

                if (!collectedAnythingThisPass) break;
            }

            if (collected > 0) EpochHubLogistics.RefreshCompressedStacks();

            Plugin.Logger.LogInfo($"[Epoch Hub] World Collection | Collected: {collected} | Remaining Budget: {remainingBudget}");

            return collected;
        }

        // ============================================================
        // COLLECTION ENGINE - NODE EXTRACTORS (FIXED - Uses TransferItem)
        // ============================================================

        private static int CollectKnownResourcesFromExtractors()
        {
            if (_hubInventory == null)
            {
                Plugin.Logger?.LogDebug("[Epoch Hub] No hub inventory for extractor collection.");
                return 0;
            }

            var constructedObjects = WorldObjectsHandler.Instance?.GetConstructedWorldObjects();
            if (constructedObjects == null)
            {
                Plugin.Logger?.LogDebug("[Epoch Hub] No constructed objects found.");
                return 0;
            }

            int totalCollected = 0;
            int remainingBudget = MaxPickupsPerCycle;

            // Build list of extractor inventories
            List<Inventory> extractorInventories = new List<Inventory>();
            List<WorldObject> extractorWorldObjects = new List<WorldObject>();
            List<int> extractorInventoryIds = new List<int>();

            foreach (var wo in constructedObjects)
            {
                if (wo == null || wo.GetGroup() == null) continue;
                if (wo.GetGroup().GetId() != "Epoch_Node_Drill") continue;

                int inventoryId = wo.GetLinkedInventoryId();
                if (inventoryId == 0)
                {
                    Plugin.Logger?.LogDebug($"[Epoch Hub] Node Extractor {wo.GetId()} has no linked inventory.");
                    continue;
                }

                Inventory extractorInventory = InventoriesHandler.Instance.GetInventoryById(inventoryId);
                if (extractorInventory == null)
                {
                    Plugin.Logger?.LogDebug($"[Epoch Hub] Node Extractor {wo.GetId()} inventory not found.");
                    continue;
                }

                extractorInventories.Add(extractorInventory);
                extractorWorldObjects.Add(wo);
                extractorInventoryIds.Add(inventoryId);
            }

            if (extractorInventories.Count == 0)
            {
                Plugin.Logger?.LogDebug("[Epoch Hub] No Node Extractors with inventories found.");
                return 0;
            }

            Plugin.Logger?.LogInfo($"[Epoch Hub] Found {extractorInventories.Count} Node Extractors with inventories.");

            // For each learned resource, check extractors
            foreach (string resourceId in _learnedResources)
            {
                if (remainingBudget <= 0) break;

                if (EpochHubLogistics.IsResourceSlotFull(resourceId)) continue;

                int currentAmount = EpochHubLogistics.GetResourceCount(resourceId);
                int remainingCapacity = MaxStackSize - currentAmount;
                if (remainingCapacity <= 0) continue;

                int allocation = Mathf.Min(remainingBudget / _learnedResources.Count, remainingCapacity);
                if (allocation <= 0) allocation = Mathf.Min(1, remainingCapacity);

                int collectedForResource = 0;

                // Check each extractor for this resource
                for (int e = 0; e < extractorInventories.Count; e++)
                {
                    if (allocation <= 0) break;

                    Inventory extractorInventory = extractorInventories[e];
                    WorldObject extractorWO = extractorWorldObjects[e];
                    int extractorId = extractorInventoryIds[e];

                    var items = extractorInventory.GetInsideWorldObjects();
                    if (items == null) continue;

                    for (int i = items.Count - 1; i >= 0; i--)
                    {
                        if (allocation <= 0) break;

                        WorldObject item = items[i];
                        if (item == null || item.GetGroup() == null) continue;
                        if (item.GetGroup().GetId() != resourceId) continue;

                        try
                        {
                            int itemId = item.GetId();
                            bool transferSuccess = false;

                            // ============================================================
                            // FIX: Use TransferItem from InventoriesHandler
                            // ============================================================
                            // This properly moves the item between inventories without destroying it
                            InventoriesHandler.Instance.TransferItem(
                                extractorInventory,
                                _hubInventory,
                                item,
                                delegate (bool success)
                                {
                                    transferSuccess = success;
                                }
                            );

                            // Wait a frame for the transfer to complete
                            // The transfer is async, so we need to check if it succeeded
                            // After transfer, the item should be in the hub inventory
                            if (transferSuccess)
                            {
                                // Verify the item was moved to the Hub
                                var hubItems = _hubInventory.GetInsideWorldObjects();
                                bool addedToHub = false;
                                if (hubItems != null)
                                {
                                    foreach (var hubItem in hubItems)
                                    {
                                        if (hubItem != null && hubItem.GetId() == itemId)
                                        {
                                            addedToHub = true;
                                            break;
                                        }
                                    }
                                }

                                if (addedToHub)
                                {
                                    collectedForResource++;
                                    totalCollected++;
                                    remainingBudget--;
                                    allocation--;
                                    Plugin.Logger?.LogDebug($"[Epoch Hub] Transferred {resourceId} (ID: {itemId}) from Node Extractor {extractorWO.GetId()} to Hub using TransferItem.");
                                }
                                else
                                {
                                    // If it didn't work, try the manual approach as fallback
                                    Plugin.Logger?.LogWarning($"[Epoch Hub] TransferItem failed for {resourceId} (ID: {itemId}), trying manual fallback.");

                                    // Manual fallback: remove and add
                                    if (extractorInventory.ContainWorldObject(item))
                                    {
                                        extractorInventory.RemoveItem(item);
                                        if (_hubInventory.AddItem(item))
                                        {
                                            collectedForResource++;
                                            totalCollected++;
                                            remainingBudget--;
                                            allocation--;
                                            Plugin.Logger?.LogDebug($"[Epoch Hub] Manual fallback succeeded for {resourceId} (ID: {itemId}).");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Transfer didn't succeed, try manual approach
                                if (extractorInventory.ContainWorldObject(item))
                                {
                                    extractorInventory.RemoveItem(item);
                                    if (_hubInventory.AddItem(item))
                                    {
                                        collectedForResource++;
                                        totalCollected++;
                                        remainingBudget--;
                                        allocation--;
                                        Plugin.Logger?.LogDebug($"[Epoch Hub] Manual fallback succeeded for {resourceId} (ID: {itemId}).");
                                    }
                                }
                            }

                            EpochHubLogistics.RefreshCompressedStacks();
                        }
                        catch (Exception ex)
                        {
                            Plugin.Logger?.LogWarning($"[Epoch Hub] Error moving {resourceId} from extractor: {ex.Message}");
                        }
                    }
                }

                if (collectedForResource > 0)
                {
                    Plugin.Logger?.LogInfo($"[Epoch Hub] Collected {collectedForResource} {resourceId} from Node Extractors.");
                }

                EpochHubLogistics.RefreshCompressedStacks();
            }

            if (totalCollected > 0)
            {
                EpochHubLogistics.RefreshCompressedStacks();
                Plugin.Logger?.LogInfo($"[Epoch Hub] Extractor Collection | Total Collected: {totalCollected} | Remaining Budget: {remainingBudget}");
            }

            return totalCollected;
        }

        // ============================================================
        // RESOURCE LOOKUP HELPERS
        // ============================================================

        private static Dictionary<string, List<WorldObject>> BuildResourceGroups(List<WorldObject> objects)
        {
            Dictionary<string, List<WorldObject>> groups = new Dictionary<string, List<WorldObject>>();
            if (objects == null) return groups;

            foreach (WorldObject wo in objects)
            {
                if (wo == null) continue;
                string resourceId = GetGroupId(wo);
                if (string.IsNullOrEmpty(resourceId)) continue;

                if (!groups.TryGetValue(resourceId, out List<WorldObject> list))
                {
                    list = new List<WorldObject>();
                    groups.Add(resourceId, list);
                }
                list.Add(wo);
            }

            return groups;
        }

        private static List<string> GetActiveResources(Dictionary<string, List<WorldObject>> groups)
        {
            List<string> activeResources = new List<string>();

            foreach (var pair in groups)
            {
                if (!_learnedResources.Contains(pair.Key)) continue;
                if (EpochHubLogistics.IsResourceSlotFull(pair.Key)) continue;
                if (pair.Value == null || pair.Value.Count == 0) continue;
                activeResources.Add(pair.Key);
            }

            return activeResources;
        }

        private static int CollectResourceGroup(string resourceId, List<WorldObject> objects, int allocation)
        {
            if (_hubInventory == null) return 0;
            if (objects == null || objects.Count == 0) return 0;

            int targetPickup = allocation;
            int collected = 0;

            for (int i = objects.Count - 1; i >= 0 && collected < targetPickup; i--)
            {
                WorldObject worldObject = objects[i];
                if (worldObject == null) continue;

                try
                {
                    InventoriesHandler.Instance.AddWorldObjectToInventory(worldObject, _hubInventory);
                    objects.RemoveAt(i);
                    collected++;

                    EpochHubLogistics.RefreshCompressedStacks();

                    if (EpochHubLogistics.IsResourceSlotFull(resourceId)) break;
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"[Epoch Hub] Failed to collect {resourceId}: {ex.Message}");
                }
            }

            return collected;
        }

        private static string GetGroupId(WorldObject worldObject)
        {
            if (worldObject == null) return null;
            try
            {
                Group group = worldObject.GetGroup();
                if (group == null) return null;
                return group.GetId();
            }
            catch { return null; }
        }

        // ============================================================
        // HUD HELPERS
        // ============================================================

        internal static void LearnFromInventoryContent() { LearnInventoryResources(); }
        internal static string GetHudState() { return _hudState; }
        internal static int GetHudObjects() { return _lastObjectsSeen; }
        internal static int GetHudCollected() { return _totalVacuumed; }
        internal static bool HudVisible() { return _initialized; }
        internal static bool IsInitialized() { return _initialized; }

        internal static string GetStatus()
        {
            if (!_initialized) return "Offline";
            return $"Known: {_learnedResources.Count} | Collected: {_totalVacuumed} | Next Scan: {Mathf.Max(0f, _nextScanTime - Time.time):F0}s";
        }

        private static void RefreshDevelopmentHud()
        {
            if (EpochHud.Instance == null) return;
            EpochHud.Instance.UpdateHud(_initialized, _lastObjectsSeen, EpochHubLogistics.GetTotalItemCount());
        }

        internal static void ResetSystem()
        {
            if (!_initialized && _hubInventory == null) return;

            _hubInventory = null;
            EpochNeural.EpochHubInventory = null;
            EpochNeural.PlayerInventory = null;

            _learnedResources.Clear();
            _resourceCounts.Clear();
            EpochHubLogistics.Reset();

            _scanRunning = false;
            _initialized = false;
            _nextScanTime = 0f;
            _scanNumber = 0;
            _totalVacuumed = 0;
            _lastObjectsSeen = 0;
            _hudState = "WAITING";

            if (EpochHud.Instance != null)
            {
                EpochHud.Instance.UpdateHud(false, 0, 0);
            }

            EpochDrillManager.ResetNetwork();

            Plugin.Logger.LogInfo("[Epoch Hub] Runtime state reset.");
        }
    }
}
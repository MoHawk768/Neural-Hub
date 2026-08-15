using SpaceCraft;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        private static int _cachedBudget = 50;
        private static int _cachedStackCap = 25;

        // ============================================================
        // MACHINE TYPES FOR AUTOMATION
        // ============================================================

        // Machine group IDs that have inventories we can collect from
        private static readonly HashSet<string> _collectableMachineIds = new HashSet<string>
        {
            "AlgaeGenerator",
            "WaterCollector",
            "Furnace",
            "BioLab",
            "DNAExtractor",
            "OreExtractor",
            "Epoch_Node_Drill"
        };

        // Machine group IDs that we can supply resources to
        private static readonly HashSet<string> _suppliableMachineIds = new HashSet<string>
        {
            "AutoCrafter",
            "RocketPlatform",
            "Furnace",
            "BioLab",
            "DNAExtractor",
            "VegetationGrower"
        };

        // ============================================================
        // INITIALIZATION
        // ============================================================

        internal static void Initialize(Inventory inventory)
        {
            if (inventory == null) return;

            _hubInventory = inventory;
            EpochNeural.EpochHubInventory = inventory;

            // Get initial tier data
            UpdateTierData();

            if (!_initialized)
            {
                _initialized = true;
                _nextScanTime = Time.time + VacuumInterval;
                Plugin.Logger.LogInfo("[Epoch Hub] Logistics Engine started.");
            }

            LearnInventoryResources();
            RefreshDevelopmentHud();
        }

        internal static void UpdateTierData()
        {
            var tierData = EpochNeural.CurrentTierData;
            if (tierData != null)
            {
                _cachedBudget = tierData.Budget;
                _cachedStackCap = tierData.StackCap;
            }
            else
            {
                _cachedBudget = 50;
                _cachedStackCap = 25;
            }
        }

        public static int GetCurrentBudget()
        {
            UpdateTierData();
            return _cachedBudget;
        }

        public static int GetCurrentStackCap()
        {
            UpdateTierData();
            return _cachedStackCap;
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
                UpdateTierData();
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

            Plugin.Logger.LogInfo($"[Epoch Hub] Logistics Cycle {_scanNumber} (Budget: {_cachedBudget}, StackCap: {_cachedStackCap})");

            LearnInventoryResources();
            BuildCurrentResourceCounts();

            int remainingBudget = _cachedBudget;

            // Priority 1: Collect from world (ground items)
            int collectedFromWorld = CollectKnownResourcesFromWorld(ref remainingBudget);

            // Priority 2: Collect from Node Extractors
            int collectedFromExtractors = CollectKnownResourcesFromExtractors(ref remainingBudget);

            // Priority 3: Collect from other machines
            int collectedFromMachines = CollectFromAllMachines(ref remainingBudget);

            // Priority 4: Supply to machines (uses remaining budget as well)
            int suppliedToMachines = SupplyToAllMachines(ref remainingBudget);

            int totalCollected = collectedFromWorld + collectedFromExtractors + collectedFromMachines;

            BuildCurrentResourceCounts();

            _totalVacuumed += totalCollected;
            _hudState = "ONLINE";

            Plugin.Logger.LogInfo($"[Epoch Hub] Collected: {totalCollected} (World: {collectedFromWorld}, Extractors: {collectedFromExtractors}, Machines: {collectedFromMachines})");
            Plugin.Logger.LogInfo($"[Epoch Hub] Supplied: {suppliedToMachines} items to machines. Budget remaining: {remainingBudget}");

            RefreshDevelopmentHud();
        }

        // ============================================================
        // LEARNING
        // ============================================================

        internal static void LearnFromInventoryContent()
        {
            LearnInventoryResources();
        }

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
                int amount = wo.GetCount().x > 0 ? wo.GetCount().x : 1;
                if (_resourceCounts.ContainsKey(id))
                    _resourceCounts[id] += amount;
                else
                    _resourceCounts.Add(id, amount);
            }
        }

        // ============================================================
        // COLLECTION ENGINE - WORLD
        // ============================================================

        private static int CollectKnownResourcesFromWorld(ref int remainingBudget)
        {
            if (remainingBudget <= 0) return 0;

            EpochVacuumDiscovery.DiscoveryResult discovery = EpochVacuumDiscovery.DiscoverCollectibles();
            _lastObjectsSeen = discovery.ReturnedObjects;

            if (discovery.Objects == null || discovery.Objects.Count == 0) return 0;

            Dictionary<string, List<WorldObject>> groups = BuildResourceGroups(discovery.Objects);
            if (groups.Count == 0) return 0;

            List<string> activeResources = GetActiveResources(groups);
            if (activeResources.Count == 0) return 0;

            int collected = 0;
            int budgetUsed = 0;

            while (remainingBudget > 0 && activeResources.Count > 0)
            {
                int allocation = Mathf.Max(1, remainingBudget / activeResources.Count);
                bool collectedAnythingThisPass = false;

                for (int i = activeResources.Count - 1; i >= 0; i--)
                {
                    if (remainingBudget <= 0) break;

                    string resourceId = activeResources[i];

                    EpochHubLogistics.RefreshCompressedStacks();

                    if (EpochHubLogistics.IsResourceSlotFull(resourceId))
                    {
                        activeResources.RemoveAt(i);
                        continue;
                    }

                    int currentAmount = EpochHubLogistics.GetResourceCount(resourceId);
                    int remainingCapacity = _cachedStackCap - currentAmount;

                    if (remainingCapacity <= 0)
                    {
                        activeResources.RemoveAt(i);
                        continue;
                    }

                    int actualAllocation = Mathf.Min(allocation, remainingCapacity, remainingBudget);
                    int collectedForGroup = CollectResourceGroup(resourceId, groups[resourceId], actualAllocation);

                    if (collectedForGroup > 0)
                    {
                        collected += collectedForGroup;
                        remainingBudget -= collectedForGroup;
                        budgetUsed += collectedForGroup;
                        collectedAnythingThisPass = true;
                    }

                    EpochHubLogistics.RefreshCompressedStacks();

                    currentAmount = EpochHubLogistics.GetResourceCount(resourceId);
                    remainingCapacity = _cachedStackCap - currentAmount;

                    if (groups[resourceId].Count == 0 || remainingCapacity <= 0)
                    {
                        activeResources.RemoveAt(i);
                    }
                }

                if (!collectedAnythingThisPass) break;
            }

            if (collected > 0) EpochHubLogistics.RefreshCompressedStacks();

            Plugin.Logger.LogInfo($"[Epoch Hub] World Collection | Collected: {collected} | Budget Used: {budgetUsed} | Remaining: {remainingBudget}");

            return collected;
        }

        // ============================================================
        // COLLECTION ENGINE - NODE EXTRACTORS
        // ============================================================

        private static int CollectKnownResourcesFromExtractors(ref int remainingBudget)
        {
            if (remainingBudget <= 0 || _hubInventory == null)
                return 0;

            var constructedObjects = WorldObjectsHandler.Instance?.GetConstructedWorldObjects();
            if (constructedObjects == null) return 0;

            int totalCollected = 0;
            int budgetUsed = 0;

            List<Inventory> extractorInventories = new List<Inventory>();

            foreach (var wo in constructedObjects)
            {
                if (wo == null || wo.GetGroup() == null) continue;
                string groupId = wo.GetGroup().GetId();
                if (groupId != "Epoch_Node_Drill" && groupId != "OreExtractor" && !groupId.Contains("OreExtractor"))
                    continue;

                int inventoryId = wo.GetLinkedInventoryId();
                if (inventoryId == 0) continue;

                Inventory extractorInventory = InventoriesHandler.Instance.GetInventoryById(inventoryId);
                if (extractorInventory == null) continue;

                extractorInventories.Add(extractorInventory);
            }

            if (extractorInventories.Count == 0) return 0;

            Plugin.Logger?.LogInfo($"[Epoch Hub] Found {extractorInventories.Count} extractors with inventories.");

            foreach (string resourceId in _learnedResources)
            {
                if (remainingBudget <= 0) break;

                if (EpochHubLogistics.IsResourceSlotFull(resourceId)) continue;

                int currentAmount = EpochHubLogistics.GetResourceCount(resourceId);
                int remainingCapacity = _cachedStackCap - currentAmount;
                if (remainingCapacity <= 0) continue;

                int allocation = Mathf.Min(remainingBudget, remainingCapacity);
                int collectedForResource = 0;

                foreach (var extractorInventory in extractorInventories)
                {
                    if (allocation <= 0) break;

                    var items = extractorInventory.GetInsideWorldObjects();
                    if (items == null) continue;

                    for (int i = items.Count - 1; i >= 0; i--)
                    {
                        if (allocation <= 0 || remainingBudget <= 0) break;

                        WorldObject item = items[i];
                        if (item == null || item.GetGroup() == null) continue;
                        if (item.GetGroup().GetId() != resourceId) continue;

                        try
                        {
                            bool transferSuccess = false;
                            InventoriesHandler.Instance.TransferItem(
                                extractorInventory,
                                _hubInventory,
                                item,
                                delegate (bool success)
                                {
                                    transferSuccess = success;
                                }
                            );

                            if (transferSuccess)
                            {
                                collectedForResource++;
                                totalCollected++;
                                remainingBudget--;
                                allocation--;
                                budgetUsed++;
                            }
                            else
                            {
                                if (extractorInventory.ContainWorldObject(item))
                                {
                                    extractorInventory.RemoveItem(item);
                                    if (_hubInventory.AddItem(item))
                                    {
                                        collectedForResource++;
                                        totalCollected++;
                                        remainingBudget--;
                                        allocation--;
                                        budgetUsed++;
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
                    Plugin.Logger?.LogInfo($"[Epoch Hub] Collected {collectedForResource} {resourceId} from extractors.");
                }

                EpochHubLogistics.RefreshCompressedStacks();
            }

            if (totalCollected > 0)
            {
                EpochHubLogistics.RefreshCompressedStacks();
                Plugin.Logger?.LogInfo($"[Epoch Hub] Extractor Collection | Total: {totalCollected} | Budget Used: {budgetUsed} | Remaining: {remainingBudget}");
            }

            return totalCollected;
        }

        // ============================================================
        // COLLECTION ENGINE - ALL MACHINES
        // ============================================================

        private static int CollectFromAllMachines(ref int remainingBudget)
        {
            if (remainingBudget <= 0 || _hubInventory == null)
                return 0;

            var constructedObjects = WorldObjectsHandler.Instance?.GetConstructedWorldObjects();
            if (constructedObjects == null) return 0;

            int totalCollected = 0;
            int budgetUsed = 0;

            foreach (var wo in constructedObjects)
            {
                if (remainingBudget <= 0) break;
                if (wo == null || wo.GetGroup() == null) continue;

                string groupId = wo.GetGroup().GetId();

                if (!_collectableMachineIds.Contains(groupId)) continue;

                if (groupId == "Epoch_Node_Drill" || groupId == "OreExtractor" || groupId.Contains("OreExtractor"))
                    continue;

                int inventoryId = wo.GetLinkedInventoryId();
                if (inventoryId == 0) continue;

                Inventory machineInventory = InventoriesHandler.Instance.GetInventoryById(inventoryId);
                if (machineInventory == null) continue;

                var items = machineInventory.GetInsideWorldObjects();
                if (items == null || items.Count == 0) continue;

                foreach (WorldObject item in items)
                {
                    if (remainingBudget <= 0) break;
                    if (item == null || item.GetGroup() == null) continue;

                    string resourceId = item.GetGroup().GetId();
                    if (!_learnedResources.Contains(resourceId)) continue;

                    if (EpochHubLogistics.IsResourceSlotFull(resourceId)) continue;

                    try
                    {
                        bool transferSuccess = false;
                        InventoriesHandler.Instance.TransferItem(
                            machineInventory,
                            _hubInventory,
                            item,
                            delegate (bool success)
                            {
                                transferSuccess = success;
                            }
                        );

                        if (transferSuccess)
                        {
                            totalCollected++;
                            remainingBudget--;
                            budgetUsed++;
                        }
                        else
                        {
                            if (machineInventory.ContainWorldObject(item))
                            {
                                machineInventory.RemoveItem(item);
                                if (_hubInventory.AddItem(item))
                                {
                                    totalCollected++;
                                    remainingBudget--;
                                    budgetUsed++;
                                }
                            }
                        }

                        EpochHubLogistics.RefreshCompressedStacks();
                    }
                    catch (Exception ex)
                    {
                        Plugin.Logger?.LogWarning($"[Epoch Hub] Error collecting {resourceId} from {groupId}: {ex.Message}");
                    }
                }
            }

            if (totalCollected > 0)
            {
                EpochHubLogistics.RefreshCompressedStacks();
                Plugin.Logger?.LogInfo($"[Epoch Hub] Machine Collection | Total: {totalCollected} | Budget Used: {budgetUsed} | Remaining: {remainingBudget}");
            }

            return totalCollected;
        }

        // ============================================================
        // SUPPLY ENGINE - ALL MACHINES
        // ============================================================

        private static int SupplyToAllMachines(ref int remainingBudget)
        {
            if (remainingBudget <= 0 || _hubInventory == null)
                return 0;

            var constructedObjects = WorldObjectsHandler.Instance?.GetConstructedWorldObjects();
            if (constructedObjects == null) return 0;

            int totalSupplied = 0;
            int budgetUsed = 0;

            foreach (var wo in constructedObjects)
            {
                if (remainingBudget <= 0) break;
                if (wo == null || wo.GetGroup() == null) continue;

                string groupId = wo.GetGroup().GetId();

                if (!_suppliableMachineIds.Contains(groupId)) continue;

                int inventoryId = wo.GetLinkedInventoryId();
                if (inventoryId == 0) continue;

                Inventory machineInventory = InventoriesHandler.Instance.GetInventoryById(inventoryId);
                if (machineInventory == null || machineInventory.IsFull()) continue;

                List<Group> neededResources = GetMachineNeeds(wo, machineInventory);
                if (neededResources == null || neededResources.Count == 0) continue;

                foreach (Group neededGroup in neededResources)
                {
                    if (remainingBudget <= 0) break;
                    if (neededGroup == null) continue;

                    string resourceId = neededGroup.GetId();
                    if (!_learnedResources.Contains(resourceId)) continue;

                    int hubCount = EpochHubLogistics.GetResourceCount(resourceId);
                    if (hubCount <= 0) continue;

                    WorldObject itemToSupply = GetWorldObjectFromHub(resourceId);
                    if (itemToSupply == null) continue;

                    try
                    {
                        if (!machineInventory.GetIsAuthorized(itemToSupply))
                            continue;

                        bool transferSuccess = false;
                        InventoriesHandler.Instance.TransferItem(
                            _hubInventory,
                            machineInventory,
                            itemToSupply,
                            delegate (bool success)
                            {
                                transferSuccess = success;
                            }
                        );

                        if (transferSuccess)
                        {
                            totalSupplied++;
                            remainingBudget--;
                            budgetUsed++;
                            Plugin.Logger?.LogDebug($"[Epoch Hub] Supplied {resourceId} to {groupId}");
                        }
                        else
                        {
                            if (_hubInventory.ContainWorldObject(itemToSupply))
                            {
                                _hubInventory.RemoveItem(itemToSupply);
                                if (machineInventory.AddItem(itemToSupply))
                                {
                                    totalSupplied++;
                                    remainingBudget--;
                                    budgetUsed++;
                                    Plugin.Logger?.LogDebug($"[Epoch Hub] Supplied {resourceId} to {groupId} (fallback)");
                                }
                            }
                        }

                        EpochHubLogistics.RefreshCompressedStacks();
                    }
                    catch (Exception ex)
                    {
                        Plugin.Logger?.LogWarning($"[Epoch Hub] Error supplying {resourceId} to {groupId}: {ex.Message}");
                    }
                }
            }

            if (totalSupplied > 0)
            {
                EpochHubLogistics.RefreshCompressedStacks();
                Plugin.Logger?.LogInfo($"[Epoch Hub] Machine Supply | Total: {totalSupplied} | Budget Used: {budgetUsed} | Remaining: {remainingBudget}");
            }

            return totalSupplied;
        }

        // ============================================================
        // MACHINE NEEDS DETECTION
        // ============================================================

        private static List<Group> GetMachineNeeds(WorldObject machine, Inventory machineInventory)
        {
            List<Group> needs = new List<Group>();

            try
            {
                string groupId = machine.GetGroup().GetId();

                var linkedGroups = machine.GetLinkedGroups();
                if (linkedGroups != null && linkedGroups.Count > 0)
                {
                    if (groupId == "AutoCrafter")
                    {
                        foreach (Group group in linkedGroups)
                        {
                            var recipe = group.GetRecipe();
                            if (recipe != null)
                            {
                                var ingredients = recipe.GetIngredientsGroupInRecipe();
                                if (ingredients != null)
                                {
                                    foreach (Group ingredient in ingredients)
                                    {
                                        if (!machineInventory.ContainGroup(ingredient))
                                        {
                                            needs.Add(ingredient);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (Group group in linkedGroups)
                        {
                            if (!machineInventory.ContainGroup(group))
                            {
                                needs.Add(group);
                            }
                        }
                    }
                }

                var logisticEntity = machineInventory.GetLogisticEntity();
                if (logisticEntity != null)
                {
                    var demandGroups = logisticEntity.GetDemandGroups();
                    if (demandGroups != null)
                    {
                        foreach (Group demandGroup in demandGroups)
                        {
                            if (!machineInventory.ContainGroup(demandGroup) && !needs.Contains(demandGroup))
                            {
                                needs.Add(demandGroup);
                            }
                        }
                    }
                }

                if (groupId == "Furnace")
                {
                    var items = machineInventory.GetInsideWorldObjects();
                    if (items != null && items.Count == 0)
                    {
                        foreach (string resource in _learnedResources)
                        {
                            var group = GroupsHandler.GetGroupViaId(resource);
                            if (group is GroupItem item &&
                                (item.GetItemCategory() == DataConfig.ItemCategory.FusionEnergy ||
                                 resource.Contains("Fuel") ||
                                 resource.Contains("Energy")))
                            {
                                if (!needs.Contains(group))
                                    needs.Add(group);
                                break;
                            }
                        }
                    }
                }

                if (groupId == "VegetationGrower")
                {
                    var items = machineInventory.GetInsideWorldObjects();
                    if (items != null && items.Count == 0)
                    {
                        foreach (string resource in _learnedResources)
                        {
                            var group = GroupsHandler.GetGroupViaId(resource);
                            if (group is GroupItem item &&
                                (item.GetItemCategory() == DataConfig.ItemCategory.SeedPlant ||
                                 item.GetItemCategory() == DataConfig.ItemCategory.SeedTree ||
                                 item.GetItemCategory() == DataConfig.ItemCategory.SeedVegetable))
                            {
                                if (!needs.Contains(group))
                                    needs.Add(group);
                                break;
                            }
                        }
                    }
                }

                if (groupId == "BioLab")
                {
                    var items = machineInventory.GetInsideWorldObjects();
                    if (items != null && items.Count == 0)
                    {
                        foreach (string resource in _learnedResources)
                        {
                            var group = GroupsHandler.GetGroupViaId(resource);
                            if (group is GroupItem item &&
                                (item.GetItemCategory() == DataConfig.ItemCategory.DNASequence ||
                                 item.GetItemCategory() == DataConfig.ItemCategory.GeneticTrait))
                            {
                                if (!needs.Contains(group))
                                    needs.Add(group);
                                break;
                            }
                        }
                    }
                }

                if (groupId == "DNAExtractor")
                {
                    var items = machineInventory.GetInsideWorldObjects();
                    if (items != null && items.Count == 0)
                    {
                        foreach (string resource in _learnedResources)
                        {
                            var group = GroupsHandler.GetGroupViaId(resource);
                            if (group is GroupItem item &&
                                item.GetItemCategory() == DataConfig.ItemCategory.Larvae)
                            {
                                if (!needs.Contains(group))
                                    needs.Add(group);
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning($"[Epoch Hub] Error getting machine needs: {ex.Message}");
            }

            return needs;
        }

        // ============================================================
        // HELPER METHODS
        // ============================================================

        private static WorldObject GetWorldObjectFromHub(string resourceId)
        {
            if (_hubInventory == null) return null;

            var items = _hubInventory.GetInsideWorldObjects();
            if (items == null) return null;

            foreach (WorldObject wo in items)
            {
                if (wo == null || wo.GetGroup() == null) continue;
                if (wo.GetGroup().GetId() == resourceId)
                {
                    if (!wo.GetIsLockedInInventory())
                        return wo;
                }
            }

            return null;
        }

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
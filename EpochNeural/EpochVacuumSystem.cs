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

        private const float VacuumInterval = 5f;

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

            // Step 1: Collect from world (ground items)
            int collectedFromWorld = CollectKnownResourcesFromWorld(ref remainingBudget);

            // Step 2: Collect from ALL machines with inventories
            int collectedFromMachines = CollectFromAllMachines(ref remainingBudget);

            // Step 3: Supply to ALL machines that need items (AutoCrafters, Furnaces, etc.)
            int suppliedToMachines = SupplyToAllMachines(ref remainingBudget);

            int totalCollected = collectedFromWorld + collectedFromMachines;

            BuildCurrentResourceCounts();

            _totalVacuumed += totalCollected;
            _hudState = "ONLINE";

            Plugin.Logger.LogInfo($"[Epoch Hub] Collected: {totalCollected} (World: {collectedFromWorld}, Machines: {collectedFromMachines})");
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
        // COLLECTION ENGINE - ALL MACHINES
        // ============================================================

        private static int CollectFromAllMachines(ref int remainingBudget)
        {
            if (remainingBudget <= 0 || _hubInventory == null)
                return 0;

            var constructedObjects = WorldObjectsHandler.Instance?.GetConstructedWorldObjects();
            if (constructedObjects == null) return 0;

            // Snapshot the collection to avoid modification during enumeration
            var machineObjects = new List<WorldObject>(constructedObjects);

            int totalCollected = 0;
            int budgetUsed = 0;

            foreach (var wo in machineObjects)
            {
                if (remainingBudget <= 0) break;
                if (wo == null || wo.GetGroup() == null) continue;

                string groupId = wo.GetGroup().GetId();

                // Skip Epoch Hub and Epoch Node Drills (they're handled separately)
                if (groupId == EpochNeural.HubId || groupId == EpochDrillAsset.DrillId)
                    continue;

                // Skip vanilla Ore Extractors (they're hidden anyway)
                if (groupId.Contains("OreExtractor"))
                    continue;

                // ============================================================
                // SKIP VEGETUBES - They need time to grow seeds
                // ============================================================
                if (groupId == "Vegetube1" ||
                    groupId == "Vegetube2" ||
                    groupId == "Vegetube3" ||
                    groupId.Contains("Vegetube") ||
                    groupId.Contains("Grower") ||
                    groupId.Contains("Vegetable") ||
                    groupId.Contains("Vegetation") ||
                    groupId.Contains("Farm") ||
                    groupId == "VegetableGrower1" ||
                    groupId == "VegetableGrower2" ||
                    groupId == "VegetableGrower3" ||
                    groupId == "VegetationGrower1" ||
                    groupId == "VegetationGrower2" ||
                    groupId == "VegetationGrower3" ||
                    groupId == "Vegetable0Growable" ||
                    groupId == "Vegetable1Growable" ||
                    groupId == "Vegetable2Growable" ||
                    groupId == "Vegetable3Growable")
                {
                    Plugin.Logger?.LogDebug($"[Epoch Hub] Skipping grower machine: {groupId}");
                    continue;
                }

                // Check if this machine has an inventory
                int inventoryId = wo.GetLinkedInventoryId();
                if (inventoryId == 0) continue;

                Inventory machineInventory = InventoriesHandler.Instance.GetInventoryById(inventoryId);
                if (machineInventory == null) continue;

                var items = machineInventory.GetInsideWorldObjects();
                if (items == null || items.Count == 0) continue;

                // Snapshot items to avoid modification during enumeration
                var machineItems = new List<WorldObject>(items);

                foreach (WorldObject item in machineItems)
                {
                    if (remainingBudget <= 0) break;
                    if (item == null || item.GetGroup() == null) continue;

                    string resourceId = item.GetGroup().GetId();

                    // Check if this is a learned resource (already in Hub)
                    if (!_learnedResources.Contains(resourceId))
                        continue;

                    // Check if the Hub has room for this resource
                    if (EpochHubLogistics.IsResourceSlotFull(resourceId))
                        continue;

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
                            Plugin.Logger?.LogDebug($"[Epoch Hub] Collected {resourceId} from {groupId}");
                        }
                        else
                        {
                            // Fallback: manual transfer if TransferItem fails
                            if (machineInventory.ContainWorldObject(item))
                            {
                                machineInventory.RemoveItem(item);
                                if (_hubInventory.AddItem(item))
                                {
                                    totalCollected++;
                                    remainingBudget--;
                                    budgetUsed++;
                                    Plugin.Logger?.LogDebug($"[Epoch Hub] Manual collected {resourceId} from {groupId}");
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

            // Snapshot the collection
            var machineObjects = new List<WorldObject>(constructedObjects);

            foreach (var wo in machineObjects)
            {
                if (remainingBudget <= 0) break;
                if (wo == null || wo.GetGroup() == null) continue;

                string groupId = wo.GetGroup().GetId();

                // Skip Epoch Hub and Node Drills
                if (groupId == EpochNeural.HubId || groupId == EpochDrillAsset.DrillId)
                    continue;

                int inventoryId = wo.GetLinkedInventoryId();
                if (inventoryId == 0) continue;

                Inventory machineInventory = InventoriesHandler.Instance.GetInventoryById(inventoryId);
                if (machineInventory == null) continue;

                // Determine what this machine needs
                List<(Group neededGroup, int requiredCount)> needs = GetMachineNeeds(wo, machineInventory);

                if (needs == null || needs.Count == 0)
                    continue;

                foreach (var need in needs)
                {
                    if (remainingBudget <= 0) break;
                    if (need.neededGroup == null) continue;

                    string resourceId = need.neededGroup.GetId();
                    int requiredCount = need.requiredCount;

                    // Check how many of this resource we have in the Hub
                    int hubCount = EpochHubLogistics.GetResourceCount(resourceId);
                    if (hubCount < requiredCount) continue;

                    // We need to supply 'requiredCount' items
                    int suppliedCount = 0;

                    for (int i = 0; i < requiredCount && remainingBudget > 0; i++)
                    {
                        WorldObject itemToSupply = GetWorldObjectFromHub(resourceId);
                        if (itemToSupply == null) break;

                        try
                        {
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
                                suppliedCount++;
                                totalSupplied++;
                                remainingBudget--;
                                budgetUsed++;
                                Plugin.Logger?.LogDebug($"[Epoch Hub] Supplied {resourceId} to {groupId} ({suppliedCount}/{requiredCount})");
                            }
                            else
                            {
                                // Fallback: manual transfer
                                if (_hubInventory.ContainWorldObject(itemToSupply))
                                {
                                    _hubInventory.RemoveItem(itemToSupply);
                                    if (machineInventory.AddItem(itemToSupply))
                                    {
                                        suppliedCount++;
                                        totalSupplied++;
                                        remainingBudget--;
                                        budgetUsed++;
                                        Plugin.Logger?.LogDebug($"[Epoch Hub] Manual supplied {resourceId} to {groupId}");
                                    }
                                }
                            }

                            EpochHubLogistics.RefreshCompressedStacks();
                        }
                        catch (Exception ex)
                        {
                            Plugin.Logger?.LogWarning($"[Epoch Hub] Error supplying {resourceId} to {groupId}: {ex.Message}");
                            break;
                        }
                    }

                    if (suppliedCount > 0)
                    {
                        Plugin.Logger?.LogInfo($"[Epoch Hub] Supplied {suppliedCount}/{requiredCount} {resourceId} to {groupId}");
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

        private static List<(Group neededGroup, int requiredCount)> GetMachineNeeds(WorldObject machine, Inventory machineInventory)
        {
            var needs = new List<(Group, int)>();
            if (machine == null || machineInventory == null)
                return needs;

            try
            {
                string groupId = machine.GetGroup().GetId();
                bool isAutoCrafter = groupId.Contains("AutoCrafter");

                // ============================================================
                // AUTO-CRAFTER: Read the recipe and supply missing ingredients
                // ============================================================
                if (isAutoCrafter)
                {
                    var autoCrafterLinkedGroups = machine.GetLinkedGroups();
                    if (autoCrafterLinkedGroups == null || autoCrafterLinkedGroups.Count == 0)
                        return needs;

                    Group selectedRecipeGroup = autoCrafterLinkedGroups[0];
                    Recipe recipe = selectedRecipeGroup?.GetRecipe();
                    List<Group> ingredients = recipe?.GetIngredientsGroupInRecipe();

                    if (ingredients == null || ingredients.Count == 0)
                        return needs;

                    // Check if the AutoCrafter has room for outputs
                    if (machineInventory.IsFull())
                    {
                        Plugin.Logger?.LogDebug($"[Epoch Supply] AutoCrafter {groupId} inventory is full, skipping supply.");
                        return needs;
                    }

                    // Count how many of each ingredient is already in the machine
                    var currentItems = machineInventory.GetInsideWorldObjects();
                    Dictionary<string, int> currentCounts = new Dictionary<string, int>();

                    if (currentItems != null)
                    {
                        foreach (WorldObject item in currentItems)
                        {
                            if (item == null || item.GetGroup() == null) continue;
                            string id = item.GetGroup().GetId();
                            if (currentCounts.ContainsKey(id))
                                currentCounts[id]++;
                            else
                                currentCounts[id] = 1;
                        }
                    }

                    // For each ingredient, calculate how many are missing
                    foreach (Group ingredient in ingredients)
                    {
                        if (ingredient == null) continue;
                        string ingredientId = ingredient.GetId();

                        // Each ingredient is required once per craft (usually)
                        int requiredCount = 1;

                        // Check if the ingredient is already present
                        int presentCount = currentCounts.ContainsKey(ingredientId) ? currentCounts[ingredientId] : 0;

                        // If we have less than required, we need to supply the difference
                        int missingCount = requiredCount - presentCount;

                        if (missingCount > 0)
                        {
                            // Check if the machine has space for more items
                            int availableSpace = machineInventory.GetSize() - machineInventory.GetInsideWorldObjects().Count;
                            int actualMissing = Math.Min(missingCount, availableSpace);

                            if (actualMissing > 0)
                            {
                                needs.Add((ingredient, actualMissing));
                                Plugin.Logger?.LogDebug($"[Epoch Supply] AutoCrafter {groupId} needs {actualMissing} {ingredientId} (has {presentCount}, needs {requiredCount})");
                            }
                        }
                    }

                    return needs;
                }

                // ============================================================
                // OTHER MACHINES: Check demand groups and linked groups
                // ============================================================
                var otherLinkedGroups = machine.GetLinkedGroups();
                if (otherLinkedGroups != null && otherLinkedGroups.Count > 0)
                {
                    var currentItems = machineInventory.GetInsideWorldObjects();
                    Dictionary<string, int> currentCounts = new Dictionary<string, int>();

                    if (currentItems != null)
                    {
                        foreach (WorldObject item in currentItems)
                        {
                            if (item == null || item.GetGroup() == null) continue;
                            string id = item.GetGroup().GetId();
                            if (currentCounts.ContainsKey(id))
                                currentCounts[id]++;
                            else
                                currentCounts[id] = 1;
                        }
                    }

                    foreach (Group group in otherLinkedGroups)
                    {
                        if (group == null) continue;
                        string groupId2 = group.GetId();

                        // Check if we have any of this group
                        if (!currentCounts.ContainsKey(groupId2) || currentCounts[groupId2] == 0)
                        {
                            // Supply 1 of this item
                            int availableSpace = machineInventory.GetSize() - machineInventory.GetInsideWorldObjects().Count;
                            if (availableSpace > 0)
                            {
                                needs.Add((group, 1));
                                Plugin.Logger?.LogDebug($"[Epoch Supply] Machine {groupId} needs 1 {groupId2}");
                            }
                        }
                    }
                }

                // Check logistic entity demand groups
                try
                {
                    var logisticEntity = machineInventory.GetLogisticEntity();
                    if (logisticEntity != null)
                    {
                        var demandGroups = logisticEntity.GetDemandGroups();
                        if (demandGroups != null)
                        {
                            var currentItems = machineInventory.GetInsideWorldObjects();
                            Dictionary<string, int> currentCounts = new Dictionary<string, int>();

                            if (currentItems != null)
                            {
                                foreach (WorldObject item in currentItems)
                                {
                                    if (item == null || item.GetGroup() == null) continue;
                                    string id = item.GetGroup().GetId();
                                    if (currentCounts.ContainsKey(id))
                                        currentCounts[id]++;
                                    else
                                        currentCounts[id] = 1;
                                }
                            }

                            foreach (Group demandGroup in demandGroups)
                            {
                                if (demandGroup == null) continue;
                                string groupId2 = demandGroup.GetId();

                                if (!currentCounts.ContainsKey(groupId2) || currentCounts[groupId2] == 0)
                                {
                                    int availableSpace = machineInventory.GetSize() - machineInventory.GetInsideWorldObjects().Count;
                                    if (availableSpace > 0)
                                    {
                                        needs.Add((demandGroup, 1));
                                        Plugin.Logger?.LogDebug($"[Epoch Supply] Machine {groupId} demand group needs 1 {groupId2}");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger?.LogDebug($"[Epoch Supply] Could not check logistic entity for {groupId}: {ex.Message}");
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
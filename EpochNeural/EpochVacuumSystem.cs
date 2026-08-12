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

        /// <summary>
        /// Seconds between complete logistics cycles.
        /// </summary>
        private const float VacuumInterval = 30f;

        /// <summary>
        /// Maximum number of world pickups collected each cycle.
        /// </summary>
        private const int MaxPickupsPerCycle = 250;

        /// <summary>
        /// Maximum quantity of one resource allowed inside the Epoch Hub.
        /// Never create a second stack.
        /// </summary>
        private const int MaxStackSize = EpochHubLogistics.StaticStackCap;

        // ============================================================
        // RUNTIME STATE
        // ============================================================

        /// <summary>
        /// Inventory currently registered as the active Epoch Hub.
        /// </summary>
        private static Inventory _hubInventory;

        /// <summary>
        /// Resource IDs permanently learned by this Epoch Hub.
        /// Example:
        /// iron
        /// cobalt
        /// uranium
        /// etc.
        /// </summary>
        private static readonly HashSet<string> _learnedResources =
    new HashSet<string>();

        /// <summary>
        /// Current quantity of every learned resource inside the hub.
        /// Rebuilt once every scan.
        /// </summary>
        private static readonly Dictionary<string, int> _resourceCounts =
    new Dictionary<string, int>();

        /// <summary>
        /// Prevents overlapping scans.
        /// </summary>
        private static bool _scanRunning;

        /// <summary>
        /// Next scheduled scan time.
        /// </summary>
        private static float _nextScanTime;

        /// <summary>
        /// Statistics only.
        /// </summary>
        private static int _totalVacuumed;
        private static int _scanNumber;
        private static int _lastObjectsSeen;
        private static string _hudState = "WAITING";

        /// <summary>
        /// Initialization state.
        /// </summary>
        private static bool _initialized;

        // ============================================================
        // INITIALIZATION
        // ============================================================

        internal static void Initialize(Inventory inventory)
        {
            if (inventory == null)
                return;

            // We NEVER wipe learned resources anymore.
            // The Epoch Hub remembers forever.

            _hubInventory = inventory;
            EpochNeural.EpochHubInventory = inventory;

            if (!_initialized)
            {
                _initialized = true;
                _nextScanTime = Time.time + VacuumInterval;

                Plugin.Logger.LogInfo(
                    "[Epoch Hub] Logistics Engine started.");
            }

            LearnInventoryResources();

            RefreshDevelopmentHud();
        }

        // ============================================================
        // UPDATE
        // ============================================================

        internal static void UpdateVacuum()
        {

            if (!_initialized)
                return;

            if (_hubInventory == null)
                return;

            if (_scanRunning)
                return;

            if (_learnedResources.Count == 0)
                return;

            if (Time.time < _nextScanTime)
                return;

            _scanRunning = true;

            try
            {
                RunLogisticsCycle();
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError(
                    "[Epoch Hub] Logistics cycle failed\n" + ex);
            }
            finally
            {
                _scanRunning = false;
                _nextScanTime = Time.time + VacuumInterval;
            }
        }

        // ============================================================
        // DISCOVERY
        // ============================================================

        private static List<WorldObject> FindCollectibleWorldObjects()
        {
            List<WorldObject> results = new List<WorldObject>();
            HashSet<int> seenIds = new HashSet<int>();

            // --------------------------------------------------------
            // Player dropped items
            // --------------------------------------------------------

            ActionGrabable[] grabables =
                UnityEngine.Object.FindObjectsByType<ActionGrabable>(
                    FindObjectsSortMode.None);

            foreach (ActionGrabable g in grabables)
            {
                if (g == null)
                    continue;

                WorldObjectAssociated assoc =
                    g.GetComponentInParent<WorldObjectAssociated>();

                if (assoc == null)
                    continue;

                WorldObject wo = assoc.GetWorldObject();

                if (wo == null)
                    continue;

                results.Add(wo);
            }

            // --------------------------------------------------------
            // Natural world resources
            // --------------------------------------------------------

            ActionMinable[] minables =
                UnityEngine.Object.FindObjectsByType<ActionMinable>(
                    FindObjectsSortMode.None);

            foreach (ActionMinable m in minables)
            {
                if (m == null)
                    continue;

                WorldObjectAssociated assoc =
                    m.GetComponent<WorldObjectAssociated>();

                if (assoc == null)
                    continue;

                WorldObject wo = assoc.GetWorldObject();

                if (wo == null)
                    continue;

                results.Add(wo);
            }

            return results;
        }

        // ============================================================
        // MAIN CYCLE
        // ============================================================

        private static void RunLogisticsCycle()
        {
            _scanNumber++;
            _hudState = "SCANNING";

            Plugin.Logger.LogInfo(
                $"[Epoch Hub] Logistics Cycle {_scanNumber}");

            LearnInventoryResources();

            BuildCurrentResourceCounts();

            int collected = CollectKnownResources();

            // Rebuild inventory after the vacuum has deposited items
            BuildCurrentResourceCounts();

            _totalVacuumed += collected;
            _hudState = "ONLINE";

            Plugin.Logger.LogInfo(
    $"[Epoch Hub] Collected {collected} resource(s). " +
    $"Lifetime Total: {_totalVacuumed}");

            RefreshDevelopmentHud();
        }

        // ============================================================
        // LEARNING
        // ============================================================

        private static void LearnInventoryResources()
        {
            if (_hubInventory == null)
                return;

            var items = _hubInventory.GetInsideWorldObjects();

            if (items == null)
                return;

            foreach (WorldObject wo in items)
            {
                if (wo == null)
                    continue;

                string id = GetGroupId(wo);

                if (string.IsNullOrEmpty(id))
                    continue;

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

            if (items == null)
                return;

            foreach (WorldObject wo in items)
            {
                if (wo == null)
                    continue;

                string id = GetGroupId(wo);

                if (string.IsNullOrEmpty(id))
                    continue;

                int amount = wo.GetCount().x;

                if (_resourceCounts.ContainsKey(id))
                    _resourceCounts[id] += amount;
                else
                    _resourceCounts.Add(id, amount);
            }
        }

        // ============================================================
        // COLLECTION ENGINE
        // ============================================================

        private static int CollectKnownResources()
        {
            EpochVacuumDiscovery.DiscoveryResult discovery =
                EpochVacuumDiscovery.DiscoverCollectibles();
            _lastObjectsSeen = discovery.ReturnedObjects;

            Plugin.Logger.LogInfo("========== Epoch Discovery ==========");
            Plugin.Logger.LogInfo($"Associated Objects : {discovery.TotalAssociatedObjects}");
            Plugin.Logger.LogInfo($"Returned Objects   : {discovery.ReturnedObjects}");
            Plugin.Logger.LogInfo($"Linked Inventories : {discovery.InventoryLinkedObjects}");
            Plugin.Logger.LogInfo($"Missing WorldObjs  : {discovery.MissingWorldObjects}");
            Plugin.Logger.LogInfo("=====================================");

            if (discovery.Objects == null || discovery.Objects.Count == 0)
                return 0;

            Dictionary<string, List<WorldObject>> groups =
                BuildResourceGroups(discovery.Objects);

            if (groups.Count == 0)
                return 0;

            List<string> activeResources = new List<string>();

            foreach (var pair in groups)
            {
                if (!_learnedResources.Contains(pair.Key))
                    continue;

                if (GetCurrentAmount(pair.Key) >= MaxStackSize)
                    continue;

                if (pair.Value == null || pair.Value.Count == 0)
                    continue;

                activeResources.Add(pair.Key);
            }

            if (activeResources.Count == 0)
                return 0;

            int remainingBudget = MaxPickupsPerCycle;
            int collected = 0;

            Plugin.Logger.LogInfo(
                $"[Epoch Hub] Learned Resources : {_learnedResources.Count}");

            Plugin.Logger.LogInfo(
                $"[Epoch Hub] Active Resources : {activeResources.Count}");

            Plugin.Logger.LogInfo(
                $"[Epoch Hub] Work Budget : {remainingBudget}");

            while (remainingBudget > 0 && activeResources.Count > 0)
            {
                int allocation =
                    Mathf.Max(1, remainingBudget / activeResources.Count);

                bool collectedAnythingThisPass = false;

                for (int i = activeResources.Count - 1; i >= 0; i--)
                {
                    string resourceId = activeResources[i];

                    // Always check the live inventory before allocating work.
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

                    int collectedForGroup =
                        CollectResourceGroup(
                            resourceId,
                            groups[resourceId],
                            actualAllocation);

                    if (collectedForGroup > 0)
                    {
                        collected += collectedForGroup;
                        remainingBudget -= collectedForGroup;
                        collectedAnythingThisPass = true;
                    }

                    EpochHubLogistics.RefreshCompressedStacks();

                    // Refresh after this resource has been processed.
                    EpochHubLogistics.RefreshCompressedStacks();

                    currentAmount = EpochHubLogistics.GetResourceCount(resourceId);

                    remainingCapacity = MaxStackSize - currentAmount;

                    if (groups[resourceId].Count == 0 || remainingCapacity <= 0)
                    {
                        activeResources.RemoveAt(i);
                    }

                    if (remainingBudget <= 0)
                        break;
                }

                if (!collectedAnythingThisPass)
                    break;
            }

            if (collected > 0)
                EpochHubLogistics.RefreshCompressedStacks();

            Plugin.Logger.LogInfo(
                $"[Epoch Hub] Cycle Summary | Collected: {collected} | Remaining Budget: {remainingBudget}");

            return collected;
        }

        // ============================================================
        // RESOURCE LOOKUP
        // ============================================================

        private static Dictionary<string, List<WorldObject>> BuildResourceGroups(
            List<WorldObject> objects)
        {
            Dictionary<string, List<WorldObject>> groups =
                new Dictionary<string, List<WorldObject>>();

            if (objects == null)
                return groups;

            foreach (WorldObject wo in objects)
            {
                if (wo == null)
                    continue;

                string resourceId = GetGroupId(wo);

                if (string.IsNullOrEmpty(resourceId))
                    continue;

                if (!groups.TryGetValue(resourceId, out List<WorldObject> list))
                {
                    list = new List<WorldObject>();
                    groups.Add(resourceId, list);
                }

                list.Add(wo);
            }

            return groups;
        }

        private static int GetCurrentAmount(string resourceId)
        {
            return EpochHubLogistics.GetResourceCount(resourceId);
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private static int CollectResourceGroup(
    string resourceId,
    List<WorldObject> objects,
    int allocation)
        {
            if (_hubInventory == null)
                return 0;

            if (objects == null || objects.Count == 0)
                return 0;

            // The scheduler already calculated exactly how many
            // objects this worker is allowed to collect.
            int targetPickup = allocation;

            int collected = 0;

            for (int i = objects.Count - 1;
                 i >= 0 && collected < targetPickup;
                 i--)
            {
                WorldObject worldObject = objects[i];

                if (worldObject == null)
                {
                    objects.RemoveAt(i);
                    continue;
                }

                try
                {
                    InventoriesHandler.Instance.AddWorldObjectToInventory(
                        worldObject,
                        _hubInventory);

                    objects.RemoveAt(i);

                    collected++;

                    // Refresh after EVERY insert.
                    // The inventory is now the single source of truth.
                    EpochHubLogistics.RefreshCompressedStacks();

                    if (EpochHubLogistics.IsResourceSlotFull(resourceId))
                        break;
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning(
                        $"[Epoch Hub] Failed to collect {resourceId}: {ex.Message}");
                }
            }

            return collected;
        }
        private static string GetGroupId(WorldObject worldObject)
        {
            if (worldObject == null)
                return null;

            try
            {
                Group group = worldObject.GetGroup();

                if (group == null)
                    return null;

                return group.GetId();
            }
            catch
            {
                return null;
            }
        }

        internal static void LearnFromInventoryContent()
        {
            LearnInventoryResources();
        }

        internal static string GetHudState()
        {
            return _hudState;
        }

        internal static int GetHudObjects()
        {
            return _lastObjectsSeen;
        }

        internal static int GetHudCollected()
        {
            return _totalVacuumed;
        }

        internal static bool HudVisible()
        {
            return _initialized;
        }

        internal static bool IsInitialized()
        {
            return _initialized;
        }

        internal static string GetStatus()
        {
            if (!_initialized)
                return "Offline";

            return
                $"Known: {_learnedResources.Count} | " +
                $"Collected: {_totalVacuumed} | " +
                $"Next Scan: {Mathf.Max(0f, _nextScanTime - Time.time):F0}s";
        }

        private static void RefreshDevelopmentHud()
        {
            if (EpochDevHud.Instance == null)
                return;

            EpochDevHud.Instance.UpdateHud(
                _initialized,
                _lastObjectsSeen,
                EpochHubLogistics.GetTotalItemCount());
        }

        internal static void ResetSystem()
        {
            if (!_initialized && _hubInventory == null)
                return;

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

            if (EpochDevHud.Instance != null)
            {
                EpochDevHud.Instance.UpdateHud(false, 0, 0);
            }

            // ADDED HERE: Clears out all active linked drill memory when exiting to the main menu
            EpochDrillManager.ResetNetwork();

            Plugin.Logger.LogInfo("[Epoch Hub] Runtime state reset.");
        }
    }
}

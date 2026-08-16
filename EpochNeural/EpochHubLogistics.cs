using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SpaceCraft;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace EpochNeural
{
    [HarmonyPatch]
    internal static class EpochHubLogistics
    {
        private static int _cachedStackCap = 25;
        private static readonly Dictionary<int, Dictionary<string, int>> _stableGroupOrder = new Dictionary<int, Dictionary<string, int>>();

        internal static List<(WorldObject wo, int count, List<WorldObject> items)> ActiveFrameCompressedStacks = new List<(WorldObject, int, List<WorldObject>)>();

        private static FieldInfo _inventoryDisplayerInventoryField;
        private static bool _hasPerformedCleanup = false;

        static EpochHubLogistics()
        {
            try
            {
                _inventoryDisplayerInventoryField = typeof(InventoryDisplayer).GetField("_inventory", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            catch { }
        }

        public static int GetCurrentStackCap()
        {
            var tierData = EpochNeural.CurrentTierData;
            return tierData?.StackCap ?? 25;
        }

        public static void RefreshStackCap()
        {
            _cachedStackCap = GetCurrentStackCap();
            _hasPerformedCleanup = false; // Reset cleanup flag so it runs again on next open
            RefreshCompressedStacks();
            Plugin.Logger?.LogInfo($"[Epoch Hub] Stack cap updated to {_cachedStackCap}");
        }

        private static string StackKey(WorldObject wo)
        {
            if (wo == null || wo.GetGroup() == null) return "UnknownOrNull";
            return wo.GetGroup().GetId();
        }

        // ============================================================
        // CLEANUP FUNCTION - Removes overflow items
        // ============================================================

        internal static void CleanupOverflowItems()
        {
            if (_hasPerformedCleanup) return;
            if (EpochNeural.EpochHubInventory == null) return;

            int stackCap = GetCurrentStackCap();
            var hubInventory = EpochNeural.EpochHubInventory;
            var items = hubInventory.GetInsideWorldObjects();

            if (items == null || items.Count == 0)
            {
                _hasPerformedCleanup = true;
                return;
            }

            Plugin.Logger?.LogInfo($"[Epoch Hub] Running overflow cleanup. Stack cap: {stackCap}");

            // Group items by resource type
            Dictionary<string, List<WorldObject>> resourceGroups = new Dictionary<string, List<WorldObject>>();

            foreach (WorldObject wo in items)
            {
                if (wo == null || wo.GetGroup() == null) continue;
                string groupId = wo.GetGroup().GetId();
                if (string.IsNullOrEmpty(groupId)) continue;

                if (!resourceGroups.TryGetValue(groupId, out List<WorldObject> group))
                {
                    group = new List<WorldObject>();
                    resourceGroups[groupId] = group;
                }
                group.Add(wo);
            }

            int totalEjected = 0;
            List<WorldObject> itemsToEject = new List<WorldObject>();

            // For each resource group, check if it exceeds the stack cap
            foreach (var kvp in resourceGroups)
            {
                string resourceId = kvp.Key;
                List<WorldObject> resourceItems = kvp.Value;

                // Count total items of this resource
                int totalCount = resourceItems.Count;

                if (totalCount > stackCap)
                {
                    // We need to eject the excess items
                    int excessCount = totalCount - stackCap;
                    Plugin.Logger?.LogInfo($"[Epoch Hub] Resource {resourceId} has {totalCount} items, cap is {stackCap}. Ejecting {excessCount}.");

                    // Eject items from the end of the list (newest first)
                    for (int i = resourceItems.Count - 1; i >= 0 && excessCount > 0; i--)
                    {
                        WorldObject wo = resourceItems[i];
                        if (wo != null && !wo.GetIsLockedInInventory())
                        {
                            itemsToEject.Add(wo);
                            excessCount--;
                        }
                    }
                }
            }

            // Actually eject the items
            if (itemsToEject.Count > 0)
            {
                // Get a drop position near the hub
                Vector3 dropPosition = Vector3.zero;
                try
                {
                    var hubWO = WorldObjectsHandler.Instance?.GetWorldObjectViaId(EpochNeural.HubWorldObjectId);
                    if (hubWO != null)
                    {
                        dropPosition = hubWO.GetPosition();
                        // Random offset so items don't pile exactly on top of each other
                        dropPosition += new Vector3(
                            UnityEngine.Random.Range(-2f, 2f),
                            0.5f,
                            UnityEngine.Random.Range(-2f, 2f)
                        );
                    }
                    else
                    {
                        // Fallback - use player position
                        var player = Managers.GetManager<PlayersManager>()?.GetActivePlayerController();
                        if (player != null)
                        {
                            dropPosition = player.transform.position;
                        }
                    }
                }
                catch { }

                // Eject each item
                foreach (WorldObject wo in itemsToEject)
                {
                    try
                    {
                        // Remove from inventory
                        if (hubInventory.ContainWorldObject(wo))
                        {
                            hubInventory.RemoveItem(wo);
                            // Drop on floor
                            if (dropPosition != Vector3.zero)
                            {
                                WorldObjectsHandler.Instance.DropOnFloor(wo, dropPosition);
                                Plugin.Logger?.LogDebug($"[Epoch Hub] Ejected {wo.GetGroup()?.GetId() ?? "unknown"} to floor");
                            }
                            else
                            {
                                // Destroy if we can't drop
                                WorldObjectsHandler.Instance.DestroyWorldObject(wo);
                                Plugin.Logger?.LogDebug($"[Epoch Hub] Destroyed excess {wo.GetGroup()?.GetId() ?? "unknown"}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Logger?.LogWarning($"[Epoch Hub] Failed to eject item: {ex.Message}");
                    }
                }

                totalEjected = itemsToEject.Count;

                // Show notification
                if (EpochHud.Instance != null && totalEjected > 0)
                {
                    EpochHud.Instance.ShowNotification($"Ejected {totalEjected} overflow items from Hub", false);
                }

                Plugin.Logger?.LogInfo($"[Epoch Hub] Cleanup complete: {totalEjected} items ejected.");
            }
            else
            {
                Plugin.Logger?.LogInfo("[Epoch Hub] No overflow items found.");
            }

            _hasPerformedCleanup = true;

            // Refresh the stacks after cleanup
            RefreshCompressedStacks();
        }

        // ============================================================
        // STACK BUILDING
        // ============================================================

        private static List<(WorldObject wo, int count, List<WorldObject> items)> BuildCustomStacks(
            Inventory inventory,
            IEnumerable<WorldObject> items,
            int maxStack)
        {
            if (items == null) return new List<(WorldObject, int, List<WorldObject>)>();

            int id = inventory.GetId();
            if (!_stableGroupOrder.TryGetValue(id, out var value))
            {
                value = new Dictionary<string, int>();
                _stableGroupOrder[id] = value;
            }

            // Group items by their group ID
            Dictionary<string, List<WorldObject>> dictionary = new Dictionary<string, List<WorldObject>>();
            List<string> groupOrder = new List<string>();

            foreach (WorldObject item in items)
            {
                if (item == null) continue;

                string text = StackKey(item);
                if (string.IsNullOrEmpty(text)) continue;

                if (!dictionary.TryGetValue(text, out var value2))
                {
                    value2 = dictionary[text] = new List<WorldObject>();
                    groupOrder.Add(text);
                }

                value2.Add(item);
            }

            if (groupOrder.Count == 0)
            {
                _stableGroupOrder.Remove(id);
                return new List<(WorldObject, int, List<WorldObject>)>();
            }

            // Maintain stable order
            int maxRank = value.Count > 0 ? value.Values.Max() : -1;
            int nextRank = maxRank + 1;

            List<(string, int)> sortedGroups = new List<(string, int)>(groupOrder.Count);

            foreach (string groupId in groupOrder)
            {
                int rank;
                if (value.TryGetValue(groupId, out int existingRank))
                {
                    rank = existingRank;
                }
                else
                {
                    rank = nextRank++;
                }
                sortedGroups.Add((groupId, rank));
            }

            sortedGroups.Sort((a, b) => a.Item2.CompareTo(b.Item2));

            var compressedList = new List<(WorldObject, int, List<WorldObject>)>();
            Dictionary<string, int> updatedOrder = new Dictionary<string, int>();

            foreach (var sortedGroup in sortedGroups)
            {
                string groupId = sortedGroup.Item1;
                updatedOrder[groupId] = updatedOrder.Count;

                List<WorldObject> groupItems = dictionary[groupId];

                // Split into stacks of maxStack size
                for (int i = 0; i < groupItems.Count; i += maxStack)
                {
                    int stackSize = Math.Min(maxStack, groupItems.Count - i);
                    List<WorldObject> stackItems = groupItems.GetRange(i, stackSize);
                    compressedList.Add((stackItems[0], stackSize, stackItems));
                }
            }

            if (updatedOrder.Count == 0)
            {
                _stableGroupOrder.Remove(id);
            }
            else
            {
                _stableGroupOrder[id] = updatedOrder;
            }

            return compressedList;
        }

        internal static void RefreshCompressedStacks()
        {
            int stackCap = GetCurrentStackCap();

            if (EpochNeural.EpochHubInventory == null)
            {
                ActiveFrameCompressedStacks = new List<(WorldObject, int, List<WorldObject>)>();
                return;
            }

            var rawItems = EpochNeural.EpochHubInventory.GetInsideWorldObjects();
            if (rawItems == null || rawItems.Count == 0)
            {
                ActiveFrameCompressedStacks = new List<(WorldObject, int, List<WorldObject>)>();
                return;
            }

            ActiveFrameCompressedStacks = BuildCustomStacks(
                EpochNeural.EpochHubInventory,
                rawItems,
                stackCap);
        }

        internal static void RefreshStacksOnLoad()
        {
            if (EpochNeural.EpochHubInventory == null)
                return;

            // Run cleanup on load to fix any overflow from saved games
            CleanupOverflowItems();

            RefreshStackCap();
            RefreshCompressedStacks();
            Plugin.Logger?.LogInfo("[Epoch Hub] Stacks refreshed on load.");
        }

        /// <summary>
        /// Gets the total number of items in the Hub inventory
        /// </summary>
        internal static int GetTotalItemCount()
        {
            int total = 0;

            if (ActiveFrameCompressedStacks == null)
                return 0;

            foreach (var stack in ActiveFrameCompressedStacks)
            {
                total += stack.count;
            }

            return total;
        }

        /// <summary>
        /// Gets the count of a specific resource in the Hub inventory
        /// Used by remote crafting system to check if resources are available
        /// </summary>
        internal static int GetResourceCount(string groupId)
        {
            if (string.IsNullOrEmpty(groupId))
                return 0;

            if (ActiveFrameCompressedStacks == null)
                return 0;

            int total = 0;

            foreach (var stack in ActiveFrameCompressedStacks)
            {
                if (stack.wo == null)
                    continue;

                Group group = stack.wo.GetGroup();

                if (group == null)
                    continue;

                if (string.Equals(group.GetId(), groupId, StringComparison.OrdinalIgnoreCase))
                    total += stack.count;
            }

            return total;
        }

        /// <summary>
        /// Checks if a resource slot is full (at stack cap)
        /// Used by extraction system to prevent overflow
        /// </summary>
        internal static bool IsResourceSlotFull(string groupId)
        {
            if (string.IsNullOrEmpty(groupId))
                return false;

            if (ActiveFrameCompressedStacks == null)
                return false;

            int stackCap = GetCurrentStackCap();

            foreach (var stack in ActiveFrameCompressedStacks)
            {
                if (stack.wo == null)
                    continue;

                Group group = stack.wo.GetGroup();

                if (group == null)
                    continue;

                if (string.Equals(group.GetId(), groupId, StringComparison.OrdinalIgnoreCase))
                {
                    if (stack.count >= stackCap)
                        return true;
                }
            }

            return false;
        }

        internal static void Reset()
        {
            ActiveFrameCompressedStacks.Clear();
            _stableGroupOrder.Clear();
            _hasPerformedCleanup = false;
        }

        private static Inventory GetInventoryFromDisplayer(InventoryDisplayer displayer)
        {
            if (displayer == null) return null;
            if (_inventoryDisplayerInventoryField == null) return null;
            return _inventoryDisplayerInventoryField.GetValue(displayer) as Inventory;
        }

        private static bool IsEpochHubInventory(Inventory inventory)
        {
            if (inventory == null) return false;
            if (EpochNeural.EpochHubInventory == null) return false;
            return inventory.GetId() == EpochNeural.EpochHubInventory.GetId();
        }

        [HarmonyPatch(typeof(InventoryDisplayer), "SetInventoryBlocks")]
        [HarmonyPrefix]
        private static bool PrefixSetInventoryBlocks(InventoryDisplayer __instance, ref ReadOnlyCollection<WorldObject> inventoryWorldObjects)
        {
            var inventory = GetInventoryFromDisplayer(__instance);
            if (!IsEpochHubInventory(inventory))
                return true;

            if (inventoryWorldObjects == null || inventoryWorldObjects.Count == 0)
                return true;

            var hubItems = EpochNeural.EpochHubInventory.GetInsideWorldObjects();
            if (hubItems == null || hubItems.Count == 0)
                return true;

            int stackCap = GetCurrentStackCap();
            var compressed = BuildCustomStacks(EpochNeural.EpochHubInventory, hubItems, stackCap);

            List<WorldObject> representationalObjects = new List<WorldObject>();
            foreach (var stack in compressed)
            {
                representationalObjects.Add(stack.wo);
            }

            inventoryWorldObjects = new ReadOnlyCollection<WorldObject>(representationalObjects);
            return true;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.IsFull))]
        [HarmonyPrefix]
        private static bool PrefixIsFull(Inventory __instance, ref bool __result)
        {
            if (!IsEpochHubInventory(__instance))
                return true;

            var items = __instance.GetInsideWorldObjects();
            if (items == null || items.Count < __instance.GetSize())
            {
                __result = false;
                return false;
            }

            int stackCap = GetCurrentStackCap();
            var compressed = BuildCustomStacks(__instance, items, stackCap);
            __result = compressed.Count >= __instance.GetSize();
            return false;
        }

        [HarmonyPatch(typeof(InventoryDisplayer), "TrueRefreshContent")]
        [HarmonyPostfix]
        private static void PostfixCacheCounts(InventoryDisplayer __instance)
        {
            var inventory = GetInventoryFromDisplayer(__instance);
            if (!IsEpochHubInventory(inventory))
                return;

            if (__instance.gameObject.GetComponent<EpochUI.DirectInventoryScroller>() == null)
                return;

            RefreshCompressedStacks();
            EpochVacuumSystem.LearnFromInventoryContent();
        }
    }

    // ============================================================
    // STACK COUNTER
    // ============================================================

    internal class EpochStackCounter : MonoBehaviour
    {
        private TextMeshProUGUI _label;
        private int _currentDisplayedCount = -1;
        private static TMP_FontAsset _fallbackFontAsset;

        public void SetCount(int count)
        {
            if (_currentDisplayedCount == count && _label != null) return;
            _currentDisplayedCount = count;

            EnsureLabelExists();

            if (_label != null)
            {
                if (_currentDisplayedCount <= 1)
                {
                    _label.text = "";
                    _label.gameObject.SetActive(false);
                }
                else
                {
                    _label.gameObject.SetActive(true);
                    _label.fontSize = (_currentDisplayedCount >= 100) ? 13f : 15f;
                    _label.text = $"{_currentDisplayedCount}";
                    _label.transform.SetAsLastSibling();
                }
            }
        }

        private void EnsureLabelExists()
        {
            if (_label != null) return;

            if (_fallbackFontAsset == null)
            {
                var globalUiMesh = UnityEngine.Object.FindFirstObjectByType<TextMeshProUGUI>();
                if (globalUiMesh != null)
                {
                    _fallbackFontAsset = globalUiMesh.font;
                }
            }

            GameObject labelGo = new GameObject("EpochStackCountLabelTMPro");
            labelGo.transform.SetParent(transform, false);

            _label = labelGo.AddComponent<TextMeshProUGUI>();
            if (_fallbackFontAsset != null)
            {
                _label.font = _fallbackFontAsset;
            }

            _label.fontStyle = FontStyles.Bold;
            _label.alignment = TextAlignmentOptions.BottomRight;
            _label.color = Color.white;

            var shadow = labelGo.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.90f);
            shadow.effectDistance = new Vector2(1.25f, -1.25f);

            RectTransform rect = _label.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(1f, 0f);

                rect.offsetMin = new Vector2(10f, 4f);
                rect.offsetMax = new Vector2(-6f, -10f);
            }

            labelGo.transform.SetAsLastSibling();
        }

        private void OnDestroy()
        {
            _label = null;
        }
    }

    // ============================================================
    // SCROLLER COORDINATOR
    // ============================================================

    [HarmonyPatch]
    internal static class EpochHubScrollerCoordinator
    {
        [HarmonyPatch(typeof(EpochUI.DirectInventoryScroller), "LateUpdate")]
        [HarmonyPostfix]
        private static void PostfixLateUpdateSync(EpochUI.DirectInventoryScroller __instance)
        {
            if (EpochNeural.EpochHubInventory == null) return;

            GridLayoutGroup grid =
                __instance.GetComponentInChildren<GridLayoutGroup>(true);

            if (grid == null) return;

            int slotCount = grid.transform.childCount;
            var compressedData =
                EpochHubLogistics.ActiveFrameCompressedStacks;

            // Also refresh compressed stacks if they're empty but the inventory has items
            if ((compressedData == null || compressedData.Count == 0) &&
                EpochNeural.EpochHubInventory.GetInsideWorldObjects().Count > 0)
            {
                EpochHubLogistics.RefreshCompressedStacks();
                compressedData =
                    EpochHubLogistics.ActiveFrameCompressedStacks;
            }

            for (int i = 0; i < slotCount; i++)
            {
                GameObject slotGo =
                    grid.transform.GetChild(i).gameObject;

                EpochStackCounter counter =
                    slotGo.GetComponent<EpochStackCounter>();

                if (counter == null)
                {
                    counter = slotGo.AddComponent<EpochStackCounter>();
                }

                if (i < compressedData.Count)
                {
                    counter.SetCount(compressedData[i].count);
                }
                else
                {
                    counter.SetCount(0);
                }
            }
        }
    }
}
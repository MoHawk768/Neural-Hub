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
    /// <summary>
    /// Detached Logistical Stacking Engine for the Epoch Hub.
    /// One-Frame Slot Delay Mask Edition built to eliminate opening flashes without canvas leaks.
    /// </summary>
    [HarmonyPatch]
    internal static class EpochHubLogistics
    {
        internal const int StaticStackCap = 999;
        private static readonly Dictionary<int, Dictionary<string, int>> _stableGroupOrder = new Dictionary<int, Dictionary<string, int>>();

        // Cache to store the results of our stack compression across the frame lifecycle safely
        internal static List<(WorldObject wo, int count, List<WorldObject> items)> ActiveFrameCompressedStacks = new List<(WorldObject, int, List<WorldObject>)>();

        // Reflection cache for InventoryDisplayer._inventory
        private static FieldInfo _inventoryDisplayerInventoryField;

        static EpochHubLogistics()
        {
            try
            {
                _inventoryDisplayerInventoryField = typeof(InventoryDisplayer).GetField("_inventory", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            catch { }
        }

        // ============================================================
        // DATA PROCESSING & LOGISTICS LAYER
        // ============================================================

        private static string StackKey(WorldObject wo)
        {
            if (wo == null || wo.GetGroup() == null) return "UnknownOrNull";
            return wo.GetGroup().GetId();
        }

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

            Dictionary<string, List<WorldObject>> dictionary = new Dictionary<string, List<WorldObject>>();
            List<string> list = new List<string>();

            foreach (WorldObject item in items)
            {
                if (item == null) continue;

                string text = StackKey(item);
                if (string.IsNullOrEmpty(text)) continue;

                if (!dictionary.TryGetValue(text, out var value2))
                {
                    value2 = dictionary[text] = new List<WorldObject>();
                    list.Add(text);
                }

                value2.Add(item);
            }

            if (list.Count == 0)
            {
                _stableGroupOrder.Remove(id);
                return new List<(WorldObject, int, List<WorldObject>)>();
            }

            int num = value.Count > 0 ? value.Values.Max() : -1;
            int num2 = num + 1;

            List<(string, int)> list3 = new List<(string, int)>(list.Count);

            foreach (string item4 in list)
            {
                int value3;
                int itemRank = value.TryGetValue(item4, out value3) ? value3 : num2++;

                list3.Add((item4, itemRank));
            }

            list3.Sort((a, b) => a.Item2.CompareTo(b.Item2));

            var compressedList = new List<(WorldObject, int, List<WorldObject>)>();

            Dictionary<string, int> dictionary2 = new Dictionary<string, int>();

            foreach (var item5 in list3)
            {
                string item2 = item5.Item1;

                dictionary2[item2] = _stableGroupOrder[id].Count;

                List<WorldObject> list5 = dictionary[item2];

                for (int num4 = 0; num4 < list5.Count; num4 += maxStack)
                {
                    int num5 = Math.Min(maxStack, list5.Count - num4);

                    List<WorldObject> range = list5.GetRange(num4, num5);

                    compressedList.Add((list5[num4], num5, range));
                }
            }

            if (dictionary2.Count == 0)
            {
                _stableGroupOrder.Remove(id);
            }
            else
            {
                _stableGroupOrder[id] = dictionary2;
            }

            return compressedList;
        }

        internal static void RefreshCompressedStacks()
        {
            if (EpochNeural.EpochHubInventory == null)
            {
                ActiveFrameCompressedStacks = new List<(WorldObject, int, List<WorldObject>)>();
                return;
            }

            var rawItems = EpochNeural.EpochHubInventory.GetInsideWorldObjects();
            if (rawItems == null)
            {
                ActiveFrameCompressedStacks = new List<(WorldObject, int, List<WorldObject>)>();
                return;
            }

            ActiveFrameCompressedStacks = BuildCustomStacks(
                EpochNeural.EpochHubInventory,
                rawItems,
                StaticStackCap);
        }

        // ============================================================
        // NEW: Refresh stacks on game load
        // ============================================================
        internal static void RefreshStacksOnLoad()
        {
            if (EpochNeural.EpochHubInventory == null)
                return;

            var rawItems = EpochNeural.EpochHubInventory.GetInsideWorldObjects();
            if (rawItems == null)
                return;

            ActiveFrameCompressedStacks = BuildCustomStacks(
                EpochNeural.EpochHubInventory,
                rawItems,
                StaticStackCap);

            Plugin.Logger?.LogInfo("[Epoch Hub] Stacks refreshed on load.");
        }

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

        internal static bool IsResourceSlotFull(string groupId)
        {
            if (string.IsNullOrEmpty(groupId))
                return false;

            if (ActiveFrameCompressedStacks == null)
                return false;

            foreach (var stack in ActiveFrameCompressedStacks)
            {
                if (stack.wo == null)
                    continue;

                Group group = stack.wo.GetGroup();

                if (group == null)
                    continue;

                if (string.Equals(group.GetId(), groupId, StringComparison.OrdinalIgnoreCase))
                {
                    if (stack.count >= StaticStackCap)
                        return true;
                }
            }

            return false;
        }

        internal static void Reset()
        {
            ActiveFrameCompressedStacks.Clear();
            _stableGroupOrder.Clear();
        }

        /// <summary>
        /// Get the inventory from an InventoryDisplayer via reflection
        /// </summary>
        private static Inventory GetInventoryFromDisplayer(InventoryDisplayer displayer)
        {
            if (displayer == null) return null;
            if (_inventoryDisplayerInventoryField == null) return null;
            return _inventoryDisplayerInventoryField.GetValue(displayer) as Inventory;
        }

        /// <summary>
        /// Check if the given inventory is our Epoch Hub
        /// </summary>
        private static bool IsEpochHubInventory(Inventory inventory)
        {
            if (inventory == null) return false;
            if (EpochNeural.EpochHubInventory == null) return false;
            return inventory.GetId() == EpochNeural.EpochHubInventory.GetId();
        }

        // ==================================================================
        // HOOKS: PARAMETER INTERCEPTION
        // ==================================================================

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

            var compressed = BuildCustomStacks(EpochNeural.EpochHubInventory, hubItems, StaticStackCap);

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

            var compressed = BuildCustomStacks(__instance, items, StaticStackCap);
            __result = compressed.Count >= __instance.GetSize();
            return false;
        }

        // ==================================================================
        // CACHING STAGE: UPDATE BUFFER DATA
        // ==================================================================

        [HarmonyPatch(typeof(InventoryDisplayer), "TrueRefreshContent")]
        [HarmonyPostfix]
        private static void PostfixCacheCounts(InventoryDisplayer __instance)
        {
            var inventory = GetInventoryFromDisplayer(__instance);
            if (!IsEpochHubInventory(inventory))
                return;

            if (__instance.gameObject.GetComponent<DirectInventoryScroller>() == null)
                return;

            var rawItems = EpochNeural.EpochHubInventory.GetInsideWorldObjects();
            if (rawItems == null) return;

            ActiveFrameCompressedStacks = BuildCustomStacks(EpochNeural.EpochHubInventory, rawItems, StaticStackCap);

            EpochVacuumSystem.LearnFromInventoryContent();
        }
    }

    // ==================================================================
    // PERSISTENT UI CONTROLLER BEHAVIOUR
    // ==================================================================

    /// <summary>
    /// Lightweight component attached directly onto physical slot objects.
    /// Monitors layout calculations during LateUpdate to draw stack numbers.
    /// </summary>
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

    // ==================================================================
    // INTEGRATION LAYER: SYSTEM COORDINATOR HOOK
    // ==================================================================

    [HarmonyPatch]
    internal static class EpochHubScrollerCoordinator
    {
        [HarmonyPatch(typeof(DirectInventoryScroller), "LateUpdate")]
        [HarmonyPostfix]
        private static void PostfixLateUpdateSync(DirectInventoryScroller __instance)
        {
            if (EpochNeural.EpochHubInventory == null) return;

            GridLayoutGroup grid = __instance.GetComponentInChildren<GridLayoutGroup>(true);
            if (grid == null) return;

            int slotCount = grid.transform.childCount;
            var compressedData = EpochHubLogistics.ActiveFrameCompressedStacks;

            for (int i = 0; i < slotCount; i++)
            {
                GameObject slotGo = grid.transform.GetChild(i).gameObject;

                EpochStackCounter counter = slotGo.GetComponent<EpochStackCounter>();
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

            EpochUI.LiftMasterCanvasVeil(__instance);
        }
    }
}
using HarmonyLib;
using SpaceCraft;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using System.Collections;
using System.Collections.Generic;

namespace EpochNeural
{
    [HarmonyPatch(typeof(UiWindowContainer))]
    internal static class EpochUI
    {
        private static Canvas _rootCanvas;
        private static bool _upgradeCheckStarted = false;

        // Prevent the transient one-slot vanilla inventory state from being visible.
        private static Coroutine _startupStabilizeCoroutine;

        [HarmonyPatch("SetInventories")]
        [HarmonyPostfix]
        private static void PostfixOpen(UiWindowContainer __instance, Inventory inventoryLeft, Inventory inventoryRight)
        {
            if (inventoryRight == null || inventoryRight.GetSize() != EpochNeural.HubInventorySize)
                return;

            EpochNeural.PlayerInventory = inventoryLeft;
            EpochNeural.EpochHubInventory = inventoryRight;

            EpochVacuumSystem.Initialize(inventoryRight);
            EpochVacuumRunner.StartRunner();

            if (!_upgradeCheckStarted)
            {
                _upgradeCheckStarted = true;
                var runner = new GameObject("EpochUpgradeMonitor");
                runner.AddComponent<EpochUpgradeMonitor>();
                UnityEngine.Object.DontDestroyOnLoad(runner);
                Plugin.Logger.LogInfo("[Epoch UI] Upgrade monitor started.");
            }

            _rootCanvas = __instance.GetComponent<Canvas>()
    ?? __instance.GetComponentInParent<Canvas>();

            // Hide the transient one-slot initialization state until both inventory
            // grids have been populated by the vanilla UI.
            if (_startupStabilizeCoroutine != null)
            {
                try
                {
                    __instance.StopCoroutine(_startupStabilizeCoroutine);
                }
                catch { }
            }

            _startupStabilizeCoroutine = __instance.StartCoroutine(StabilizeHubWindow(__instance));

            __instance.SetContainerName("Epoch Hub");

            Plugin.Logger.LogInfo("[UI] Epoch Hub open session initialized successfully.");
        }

        private static IEnumerator StabilizeHubWindow(UiWindowContainer instance)
        {
            if (instance == null)
                yield break;

            CanvasGroup group = instance.GetComponent<CanvasGroup>();
            if (group == null)
                group = instance.gameObject.AddComponent<CanvasGroup>();

            // Hide only during the short Unity inventory construction phase.
            group.alpha = 0f;

            float deadline = Time.unscaledTime + 0.75f;
            bool stable = false;

            while (Time.unscaledTime < deadline)
            {
                yield return null;

                GridLayoutGroup[] grids =
                    instance.GetComponentsInChildren<GridLayoutGroup>(true);

                int readyGrids = 0;

                if (grids != null)
                {
                    foreach (GridLayoutGroup grid in grids)
                    {
                        if (grid == null) continue;

                        if (grid.transform.childCount >= EpochNeural.HubInventorySize)
                            readyGrids++;
                    }
                }

                if (readyGrids >= 2)
                {
                    stable = true;
                    break;
                }
            }

            group.alpha = 1f;

            Plugin.Logger.LogInfo(
                stable
                    ? "[UI] Hub inventory UI stabilized before display."
                    : "[UI] Hub inventory UI stabilization timeout; displaying normally.");

            _startupStabilizeCoroutine = null;
        }

        // ============================================================
        // UPGRADE MONITOR
        // ============================================================

        public class EpochUpgradeMonitor : MonoBehaviour
        {
            private float _checkInterval = 5f;
            private float _nextCheckTime;
            private int _lastCheckedTier = 1;

            private void Start()
            {
                _nextCheckTime = Time.time + _checkInterval;
                _lastCheckedTier = EpochNeural.CurrentTier;
                Plugin.Logger.LogInfo("[Epoch Upgrade] Monitor started. Current tier: " + _lastCheckedTier);
            }

            private void Update()
            {
                if (Time.time < _nextCheckTime)
                    return;

                _nextCheckTime = Time.time + _checkInterval;

                try
                {
                    CheckForUpgrade();
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"[Epoch Upgrade] Check error: {ex.Message}");
                }
            }

            private void CheckForUpgrade()
            {
                if (EpochNeural.EpochHubInventory == null)
                    return;

                var worldUnitsHandler = Managers.GetManager<WorldUnitsHandler>();
                if (worldUnitsHandler == null)
                    return;

                var terraUnit = worldUnitsHandler.GetUnit(DataConfig.WorldUnitType.Terraformation);
                if (terraUnit == null)
                    return;

                double currentTi = terraUnit.GetValue();

                var nextTier = EpochNeural.GetNextTierData();
                if (nextTier == null)
                    return;

                if (currentTi >= nextTier.UnlockTi)
                {
                    PerformUpgrade(nextTier);
                }
            }

            private void PerformUpgrade(EpochNeural.HubTierData newTier)
            {
                var tierField = typeof(EpochNeural).GetField("_currentTier", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (tierField != null)
                {
                    tierField.SetValue(null, newTier.Tier);
                }

                EpochHubLogistics.RefreshStackCap();
                EpochVacuumSystem.UpdateTierData();

                if (EpochHud.Instance != null)
                {
                    EpochHud.Instance.ShowUpgradeNotification(newTier.Name);
                }

                Plugin.Logger.LogInfo($"[Epoch Upgrade] Hub upgraded to Tier {newTier.Tier}: {newTier.Name}!");
                Plugin.Logger.LogInfo($"[Epoch Upgrade] New Budget: {newTier.Budget}, StackCap: {newTier.StackCap}");

                if (EpochNeural.IsAtMaxTier())
                {
                    Plugin.Logger.LogInfo("[Epoch Upgrade] MAX TIER REACHED!");
                    if (EpochHud.Instance != null)
                    {
                        EpochHud.Instance.ShowNotification("HUB MAXED - COMPLETE!", true);
                    }
                }
            }
        }

        // ============================================================
        // SCROLL RELAY
        // ============================================================

        internal class ScrollRelay : MonoBehaviour, IScrollHandler
        {
            public void OnScroll(PointerEventData eventData)
            {
                var parent = GetComponentInParent<DirectInventoryScroller>();
                if (parent != null)
                {
                    parent.OnScroll(eventData);
                }
            }
        }

        // ============================================================
        // DIRECT INVENTORY SCROLLER (FIXED)
        // ============================================================

        internal class DirectInventoryScroller : MonoBehaviour, IScrollHandler
        {
            public RectTransform Content;
            public float ScrollSpeed = 45f;
            public float MaxScroll = 1602f;

            public static float SavedY = 0f;
            private float _lastScrollTime = 0f;
            private const int TotalColumnsLocal = 8;
            private const float cell = 76f;
            private const float spacing = 3f;
            private const int visibleRows = 10;
            private const int TotalSlots = 240;

            public void OnScroll(PointerEventData eventData)
            {
                float y = SavedY;

                y -= eventData.scrollDelta.y * ScrollSpeed;
                y = Mathf.Clamp(y, 0f, MaxScroll);

                _lastScrollTime = Time.time;
                SavedY = y;

                if (Content != null)
                {
                    Vector2 p = Content.anchoredPosition;
                    p.y = y;
                    Content.anchoredPosition = p;
                }
            }

            void LateUpdate()
            {
                if (Content == null)
                    return;

                // Force position lock
                Vector2 p = Content.anchoredPosition;
                if (Mathf.Abs(p.y - SavedY) > 0.01f)
                {
                    p.y = SavedY;
                    Content.anchoredPosition = p;
                }

                // Auto-snap after scroll stops
                if (Time.time - _lastScrollTime > 0.15f)
                {
                    int highestFilledIndex = -1;

                    // Check the actual hub inventory for filled slots
                    if (EpochNeural.EpochHubInventory != null)
                    {
                        var items = EpochNeural.EpochHubInventory.GetInsideWorldObjects();
                        if (items != null)
                        {
                            // Count non-empty slots based on our compressed stacks
                            var compressedData = EpochHubLogistics.ActiveFrameCompressedStacks;
                            if (compressedData != null)
                            {
                                // The highest filled index is the number of occupied stacks - 1
                                int occupiedStacks = compressedData.Count;
                                if (occupiedStacks > 0)
                                {
                                    // Map occupied stacks to grid position
                                    // Each stack occupies one slot, but we need to account for the grid layout
                                    // The grid has 8 columns, so we need to calculate the row
                                    int lastRow = Mathf.FloorToInt((float)(occupiedStacks - 1) / TotalColumnsLocal);
                                    highestFilledIndex = occupiedStacks - 1;

                                    // Also check the actual items to be safe
                                    int actualItemCount = 0;
                                    foreach (var stack in compressedData)
                                    {
                                        if (stack.count > 0) actualItemCount++;
                                    }
                                    if (actualItemCount > 0)
                                    {
                                        int actualLastRow = Mathf.FloorToInt((float)(actualItemCount - 1) / TotalColumnsLocal);
                                        if (actualLastRow > lastRow)
                                        {
                                            lastRow = actualLastRow;
                                            highestFilledIndex = actualItemCount - 1;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    float trueTarget = 0f;
                    if (highestFilledIndex >= 0)
                    {
                        int rowsFilled = Mathf.FloorToInt((float)highestFilledIndex / TotalColumnsLocal) + 1;
                        if (rowsFilled > visibleRows)
                        {
                            int extraRows = rowsFilled - visibleRows;
                            trueTarget = extraRows * (cell + spacing);
                            trueTarget = Mathf.Clamp(trueTarget, 0f, MaxScroll);
                        }
                        else
                        {
                            trueTarget = 0f;
                        }
                    }

                    // Only snap if we're significantly off target
                    if (Mathf.Abs(SavedY - trueTarget) > 1f)
                    {
                        SavedY = trueTarget;
                        Vector2 np = Content.anchoredPosition;
                        np.y = SavedY;
                        Content.anchoredPosition = np;
                    }
                }
            }
        }

        // ============================================================
        // GRID FORMATTER
        // ============================================================

        [HarmonyPatch(typeof(InventoryDisplayer), "TrueRefreshContent")]
        internal static class EpochGridFormatter
        {
            private const int TotalColumns = 8;
            private const float TopMaskTrim = 65f;

            [HarmonyPostfix]
            private static void PostfixLayout(InventoryDisplayer __instance)
            {
                if (EpochNeural.EpochHubInventory == null)
                    return;

                GridLayoutGroup grid = __instance.GetComponentInChildren<GridLayoutGroup>(true);
                if (grid == null || grid.transform.childCount < 200)
                    return;

                ContentSizeFitter fitter = __instance.GetComponent<ContentSizeFitter>();
                if (fitter != null) fitter.enabled = false;

                ContentSizeFitter gridFitter = grid.GetComponent<ContentSizeFitter>();
                if (gridFitter != null) gridFitter.enabled = false;

                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = TotalColumns;

                const float cell = 76f;
                const float spacing = 3f;

                grid.cellSize = new Vector2(cell, cell);
                grid.spacing = new Vector2(spacing, spacing);
                grid.childAlignment = TextAnchor.UpperLeft;

                grid.padding = new RectOffset(0, 0, Mathf.RoundToInt(TopMaskTrim), 0);

                RectTransform gridRect = grid.GetComponent<RectTransform>();
                if (gridRect != null)
                {
                    gridRect.anchorMin = new Vector2(0.5f, 1f);
                    gridRect.anchorMax = new Vector2(0.5f, 1f);
                    gridRect.pivot = new Vector2(0.5f, 1f);

                    int rows = Mathf.CeilToInt((float)grid.transform.childCount / TotalColumns);
                    float totalGridContentHeight = (rows * cell) + ((rows - 1) * spacing) + (TopMaskTrim + 20f);

                    gridRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 630f);
                    gridRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalGridContentHeight);
                    gridRect.anchoredPosition = new Vector2(0f, gridRect.anchoredPosition.y);
                }

                RectTransform displayerRect = __instance.GetComponent<RectTransform>();
                if (displayerRect != null)
                {
                    displayerRect.anchorMin = new Vector2(0.5f, 0.5f);
                    displayerRect.anchorMax = new Vector2(0.5f, 0.5f);
                    displayerRect.pivot = new Vector2(0.5f, 0.5f);

                    displayerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 650f);
                    displayerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 850f);

                    displayerRect.anchoredPosition = new Vector2(displayerRect.anchoredPosition.x, -72.5f);
                }

                ScrollRect nativeScroll = __instance.GetComponent<ScrollRect>();
                if (nativeScroll != null)
                {
                    nativeScroll.enabled = false;
                }

                RectMask2D mask = __instance.gameObject.GetComponent<RectMask2D>();
                if (mask == null)
                {
                    mask = __instance.gameObject.AddComponent<RectMask2D>();
                }
                mask.padding = new Vector4(0f, 0f, 0f, TopMaskTrim);

                DirectInventoryScroller customScroller = __instance.gameObject.GetComponent<DirectInventoryScroller>();
                if (customScroller == null)
                {
                    customScroller = __instance.gameObject.AddComponent<DirectInventoryScroller>();
                    Plugin.Logger.LogInfo("[UI] DirectInventoryScroller engine attached.");
                }

                if (customScroller != null)
                {
                    customScroller.Content = gridRect;
                    customScroller.MaxScroll = 1602f;
                    customScroller.ScrollSpeed = 45f;

                    Vector2 p = gridRect.anchoredPosition;
                    p.x = 0f;
                    p.y = DirectInventoryScroller.SavedY;
                    gridRect.anchoredPosition = p;
                }

                if (gridRect != null)
                {
                    for (int si = 0; si < gridRect.childCount; si++)
                    {
                        var slot = gridRect.GetChild(si);
                        if (slot == null)
                            continue;

                        if (slot.GetComponent<ScrollRelay>() == null)
                            slot.gameObject.AddComponent<ScrollRelay>();

                        for (int c = 0; c < slot.childCount; c++)
                        {
                            var ch = slot.GetChild(c);
                            if (ch == null)
                                continue;

                            if (ch.GetComponent<Image>() != null || ch.GetComponent<RawImage>() != null || ch.childCount > 0)
                            {
                                if (ch.GetComponent<ScrollRelay>() == null)
                                    ch.gameObject.AddComponent<ScrollRelay>();
                            }
                        }
                    }
                }

                Transform iconsContainer = __instance.transform.Find("IconsContainer");
                Transform masterParentWindow = __instance.transform.parent;

                if (iconsContainer != null && masterParentWindow != null)
                {
                    if (iconsContainer.parent != masterParentWindow)
                    {
                        iconsContainer.SetParent(masterParentWindow, true);
                    }

                    RectTransform buttonRect = iconsContainer.GetComponent<RectTransform>();
                    if (buttonRect != null)
                    {
                        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
                        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
                        buttonRect.pivot = new Vector2(0.5f, 0.5f);

                        buttonRect.anchoredPosition = new Vector2(displayerRect.anchoredPosition.x, displayerRect.anchoredPosition.y + 409.5f);
                        LayoutRebuilder.ForceRebuildLayoutImmediate(buttonRect);
                    }
                }
            }
        }
    }
}

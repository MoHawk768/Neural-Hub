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
        private static Image _hubPreviewImage;
        private static Coroutine _previewCoroutine;
        private static int _currentPreviewIndex = 0;
        private static bool _hasEverHadItems = false;
        private static Sprite _lastValidSprite = null;

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

            // Reset the "has ever had items" flag when opening a fresh container
            // Check if the inventory has any items
            var items = inventoryRight.GetInsideWorldObjects();
            _hasEverHadItems = (items != null && items.Count > 0);
            if (!_hasEverHadItems)
            {
                _lastValidSprite = null;
            }

            // Find and setup the preview image
            SetupHubPreview(__instance);

            _rootCanvas = __instance.GetComponent<Canvas>() ?? __instance.GetComponentInParent<Canvas>();
            if (_rootCanvas != null)
            {
                _rootCanvas.enabled = false;
            }

            Plugin.Logger.LogInfo("[UI] Epoch Hub open session initialized successfully.");
        }

        private static void SetupHubPreview(UiWindowContainer __instance)
        {
            try
            {
                // Find the hub preview image - typically the large icon in the container UI
                Transform containerTransform = __instance.transform;

                // Try common names for the preview image
                string[] previewNames = { "PreviewImage", "IconImage", "ItemIcon", "ContainerIcon", "Preview" };
                Transform previewTransform = null;

                foreach (string name in previewNames)
                {
                    previewTransform = containerTransform.Find(name);
                    if (previewTransform != null) break;
                }

                // If not found by name, look for a large Image component
                if (previewTransform == null)
                {
                    // Look for any Image that's a direct child and has a non-default size
                    foreach (Transform child in containerTransform)
                    {
                        Image img = child.GetComponent<Image>();
                        if (img != null && img.sprite != null)
                        {
                            RectTransform rect = child as RectTransform;
                            if (rect != null && rect.sizeDelta.x > 50 && rect.sizeDelta.y > 50)
                            {
                                previewTransform = child;
                                break;
                            }
                        }
                    }
                }

                if (previewTransform != null)
                {
                    _hubPreviewImage = previewTransform.GetComponent<Image>();
                    if (_hubPreviewImage != null)
                    {
                        // Start the preview cycling coroutine
                        if (_previewCoroutine != null)
                        {
                            __instance.StopCoroutine(_previewCoroutine);
                        }
                        _previewCoroutine = __instance.StartCoroutine(PreviewCycler());
                        Plugin.Logger.LogInfo("[UI] Hub preview cycling started.");
                    }
                }
                else
                {
                    // If we can't find the preview image, create one
                    CreatePreviewImage(containerTransform);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[UI] Could not setup hub preview: {ex.Message}");
            }
        }

        private static void CreatePreviewImage(Transform parent)
        {
            try
            {
                GameObject previewGo = new GameObject("EpochHubPreview");
                previewGo.transform.SetParent(parent, false);

                RectTransform rect = previewGo.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -60f);
                rect.sizeDelta = new Vector2(80f, 80f);

                _hubPreviewImage = previewGo.AddComponent<Image>();
                _hubPreviewImage.color = new Color(1f, 1f, 1f, 0.9f);
                _hubPreviewImage.raycastTarget = false;

                // Add a background to make it visible
                GameObject bgGo = new GameObject("Background");
                bgGo.transform.SetParent(previewGo.transform, false);
                RectTransform bgRect = bgGo.AddComponent<RectTransform>();
                bgRect.anchorMin = Vector2.zero;
                bgRect.anchorMax = Vector2.one;
                bgRect.sizeDelta = Vector2.zero;
                Image bgImage = bgGo.AddComponent<Image>();
                bgImage.color = new Color(0f, 0f, 0f, 0.5f);

                // Add border
                GameObject borderGo = new GameObject("Border");
                borderGo.transform.SetParent(previewGo.transform, false);
                RectTransform borderRect = borderGo.AddComponent<RectTransform>();
                borderRect.anchorMin = Vector2.zero;
                borderRect.anchorMax = Vector2.one;
                borderRect.sizeDelta = new Vector2(4f, 4f);
                Image borderImage = borderGo.AddComponent<Image>();
                borderImage.color = new Color(0.2f, 0.9f, 0.2f, 0.8f);

                // Start cycling
                if (_previewCoroutine != null)
                {
                    var mono = parent.GetComponent<MonoBehaviour>();
                    if (mono != null)
                    {
                        mono.StopCoroutine(_previewCoroutine);
                        _previewCoroutine = mono.StartCoroutine(PreviewCycler());
                    }
                }

                Plugin.Logger.LogInfo("[UI] Created hub preview image.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[UI] Could not create preview image: {ex.Message}");
            }
        }

        private static IEnumerator PreviewCycler()
        {
            WaitForSeconds wait = new WaitForSeconds(5f);
            int currentIndex = 0;
            List<WorldObject> visibleItems = new List<WorldObject>();

            while (true)
            {
                yield return wait;

                try
                {
                    if (EpochNeural.EpochHubInventory == null || _hubPreviewImage == null)
                        continue;

                    // Get all items in the hub inventory
                    var items = EpochNeural.EpochHubInventory.GetInsideWorldObjects();
                    if (items == null || items.Count == 0)
                    {
                        // Container is empty - show the "empty" state
                        _hubPreviewImage.sprite = null;
                        _hubPreviewImage.color = new Color(1f, 1f, 1f, 0.3f);
                        _hasEverHadItems = false;
                        _lastValidSprite = null;
                        currentIndex = 0;
                        continue;
                    }

                    // Mark that we've had items
                    _hasEverHadItems = true;

                    // Build list of unique resource types (first item of each stack)
                    visibleItems.Clear();
                    HashSet<string> seenGroups = new HashSet<string>();

                    foreach (WorldObject wo in items)
                    {
                        if (wo == null || wo.GetGroup() == null) continue;
                        string groupId = wo.GetGroup().GetId();
                        if (!seenGroups.Contains(groupId))
                        {
                            seenGroups.Add(groupId);
                            visibleItems.Add(wo);
                        }
                    }

                    if (visibleItems.Count == 0)
                    {
                        // This shouldn't happen if items.Count > 0, but just in case
                        if (_lastValidSprite != null)
                        {
                            _hubPreviewImage.sprite = _lastValidSprite;
                            _hubPreviewImage.color = new Color(1f, 1f, 1f, 1f);
                        }
                        else
                        {
                            _hubPreviewImage.sprite = null;
                            _hubPreviewImage.color = new Color(1f, 1f, 1f, 0.3f);
                        }
                        currentIndex = 0;
                        continue;
                    }

                    // Cycle through the items
                    if (currentIndex >= visibleItems.Count)
                    {
                        currentIndex = 0;
                    }

                    WorldObject currentItem = visibleItems[currentIndex];
                    if (currentItem != null && currentItem.GetGroup() != null)
                    {
                        Sprite icon = currentItem.GetGroup().GetImage();
                        if (icon != null)
                        {
                            _hubPreviewImage.sprite = icon;
                            _hubPreviewImage.color = new Color(1f, 1f, 1f, 1f);
                            _lastValidSprite = icon; // Cache the last valid sprite
                        }
                        else
                        {
                            // If no icon, try to get it from the group data
                            var groupData = currentItem.GetGroup().GetGroupData();
                            if (groupData != null && groupData.icon != null)
                            {
                                _hubPreviewImage.sprite = groupData.icon;
                                _hubPreviewImage.color = new Color(1f, 1f, 1f, 1f);
                                _lastValidSprite = groupData.icon;
                            }
                            else if (_lastValidSprite != null)
                            {
                                // Fallback to last valid sprite
                                _hubPreviewImage.sprite = _lastValidSprite;
                                _hubPreviewImage.color = new Color(1f, 1f, 1f, 1f);
                            }
                            else
                            {
                                _hubPreviewImage.sprite = null;
                                _hubPreviewImage.color = new Color(1f, 1f, 1f, 0.5f);
                            }
                        }
                    }

                    currentIndex++;
                    if (currentIndex >= visibleItems.Count)
                    {
                        currentIndex = 0;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"[UI] Preview cycle error: {ex.Message}");
                }
            }
        }

        internal static void LiftMasterCanvasVeil(MonoBehaviour runner)
        {
            if (_rootCanvas != null && !_rootCanvas.enabled && runner != null)
            {
                runner.StartCoroutine(StalledRevealCoroutine());
            }
        }

        private static IEnumerator StalledRevealCoroutine()
        {
            yield return new WaitForEndOfFrame();

            if (_rootCanvas != null)
            {
                _rootCanvas.enabled = true;
                _rootCanvas = null;
            }
        }

        // Cleanup when hub window closes
        [HarmonyPatch(typeof(UiWindowContainer), "OnDestroy")]
        [HarmonyPostfix]
        private static void PostfixClose()
        {
            if (_previewCoroutine != null)
            {
                _previewCoroutine = null;
            }
            _hubPreviewImage = null;
            _currentPreviewIndex = 0;
            // Don't reset _hasEverHadItems or _lastValidSprite here - we want to remember if we've had items
        }
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
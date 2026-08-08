using HarmonyLib;
using SpaceCraft;
using UnityEngine;
using UnityEngine.UI;

namespace EpochNeural
{
    // 1. Tracks container opening and closing lifecycles safely
    [HarmonyPatch(typeof(UiWindowContainer))]
    internal static class EpochUI
    {
        [HarmonyPatch("SetInventories")]
        [HarmonyPostfix]
        private static void PostfixOpen(Inventory inventoryLeft, Inventory inventoryRight)
        {
            if (inventoryRight == null || inventoryRight.GetSize() != EpochNeural.HubInventorySize)
                return;

            EpochNeural.PlayerInventory = inventoryLeft;
            EpochNeural.EpochHubInventory = inventoryRight;

            Plugin.Logger.LogInfo("[UI] Epoch Hub open session initialized successfully.");
        }

        [HarmonyPatch("OnClose")]
        [HarmonyPostfix]
        private static void PostfixClose()
        {
            EpochNeural.PlayerInventory = null;
            EpochNeural.EpochHubInventory = null;

            Plugin.Logger.LogInfo($"After Clear -> Player={(EpochNeural.PlayerInventory == null)}, Hub={(EpochNeural.EpochHubInventory == null)}");
        }
    }

    // 2. Main Layout Formatter: Handles stable grid alignment, 10 uncut visible rows, and scroll boundaries
    [HarmonyPatch(typeof(InventoryDisplayer), "TrueRefreshContent")]
    internal static class EpochGridFormatter
    {
        private const int TotalColumns = 8;

        [HarmonyPostfix]
        private static void PostfixLayout(InventoryDisplayer __instance)
        {
            // Security filter to ensure our mod code only triggers inside our custom storage chest session
            if (EpochNeural.EpochHubInventory == null)
                return;

            GridLayoutGroup grid = __instance.GetComponentInChildren<GridLayoutGroup>(true);
            if (grid == null || grid.transform.childCount < 200)
                return;

            // --- SECTION A: BYPASS AUTOMATIC WINDOW STRETCHING ---
            ContentSizeFitter fitter = __instance.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;

            ContentSizeFitter gridFitter = grid.GetComponent<ContentSizeFitter>();
            if (gridFitter != null) gridFitter.enabled = false;

            // --- SECTION B: GRID CONSTRAINTS CONFIGURATION ---
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = TotalColumns;

            const float cell = 76f;
            const float spacing = 3f;

            grid.cellSize = new Vector2(cell, cell);
            grid.spacing = new Vector2(spacing, spacing);
            grid.childAlignment = TextAnchor.UpperLeft;

            // Shift top item row down natively by 65px so it clears the title header panel zone completely
            grid.padding = new RectOffset(0, 0, 65, 0);

            // --- SECTION C: CALIBRATE INTERNAL ITEM CONTENT BOUNDS ---
            RectTransform gridRect = grid.GetComponent<RectTransform>();
            if (gridRect != null)
            {
                // Fix: Anchor content tightly to the top-center so scroll physics track boundaries correctly
                gridRect.anchorMin = new Vector2(0.5f, 1f);
                gridRect.anchorMax = new Vector2(0.5f, 1f);
                gridRect.pivot = new Vector2(0.5f, 1f);

                int rows = Mathf.CeilToInt((float)grid.transform.childCount / TotalColumns);
                float totalGridContentHeight = (rows * cell) + ((rows - 1) * spacing) + 85f;

                gridRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 630f);
                gridRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalGridContentHeight);
                gridRect.anchoredPosition = Vector2.zero;
            }

            // --- SECTION D: ALIGN MASTER CONTAINER WINDOW TO BACKPACK BACKDROP ---
            RectTransform displayerRect = __instance.GetComponent<RectTransform>();
            if (displayerRect != null)
            {
                displayerRect.anchorMin = new Vector2(0.5f, 0.5f);
                displayerRect.anchorMax = new Vector2(0.5f, 0.5f);
                displayerRect.pivot = new Vector2(0.5f, 0.5f);

                // Fix: Changed vertical height to 815f to move the bottom line down and show all 10 rows uncut!
                displayerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 650f);
                displayerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 815f);

                // Center position balanced to line up row 1 with your backpack panel
                displayerRect.anchoredPosition = new Vector2(displayerRect.anchoredPosition.x, -125f);
            }

            // --- SECTION E: CLEAN NATIVE SCROLL ENGINE INJECTION ---
            ScrollRect scrollSystem = __instance.GetComponent<ScrollRect>();
            if (scrollSystem == null)
            {
                // Inject masking to hide overflow items cleanly at the bottom window borders
                RectMask2D mask = __instance.gameObject.GetComponent<RectMask2D>();
                if (mask == null)
                {
                    mask = __instance.gameObject.AddComponent<RectMask2D>();
                }

                // Trims clipping bounds by 65px at the top so items hide safely beneath your buttons
                mask.padding = new Vector4(0f, 0f, 0f, 65f);

                scrollSystem = __instance.gameObject.AddComponent<ScrollRect>();
                scrollSystem.horizontal = false;
                scrollSystem.vertical = true;
                scrollSystem.scrollSensitivity = 45f;
                scrollSystem.content = gridRect;

                // Fix: Set scroll movement boundaries to standard Clamped constraints so it cannot overshoot or fly away
                scrollSystem.movementType = ScrollRect.MovementType.Clamped;
                scrollSystem.inertia = false;

                // Snap view directly up to row number 1 upon first opening
                scrollSystem.verticalNormalizedPosition = 1f;

                Plugin.Logger.LogInfo("[UI] Bound-locked scroll engine attached safely.");
            }

            if (scrollSystem != null && scrollSystem.content != gridRect)
            {
                scrollSystem.content = gridRect;
            }

            // --- SECTION F: BUTTONS HIERARCHY RE-PARENTING ---
            Transform iconsContainer = __instance.transform.Find("IconsContainer");
            Transform masterParentWindow = __instance.transform.parent;

            if (iconsContainer != null && masterParentWindow != null)
            {
                // Safely re-parent the buttons up one level to the main window container background
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

                    // Pins buttons firmly in position right above your container rows
                    buttonRect.anchoredPosition = new Vector2(displayerRect.anchoredPosition.x, displayerRect.anchoredPosition.y + 392f);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(buttonRect);
                }
            }
        }
    }
}

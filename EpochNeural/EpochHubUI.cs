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

    // 2. Main Layout Formatter: Handles grid alignment, 10 visible rows sizing, and safe inner masking
    [HarmonyPatch(typeof(InventoryDisplayer), "TrueRefreshContent")]
    internal static class EpochGridFormatter
    {
        private const int TotalColumns = 8;

        private static void Dump(Transform t, int depth)
        {
            Plugin.Logger.LogInfo($"{new string(' ', depth * 2)}{t.name}");

            for (int i = 0; i < t.childCount; i++)
                Dump(t.GetChild(i), depth + 1);
        }

        [HarmonyPostfix]
        private static void PostfixLayout(InventoryDisplayer __instance)
        {
            // CRITICAL FIX: Direct session verification filter.
            // If the player isn't inside our custom chest session, STOP immediately.
            // This prevents the code from executing inside building menus and crashing your game thread!
            if (EpochNeural.EpochHubInventory == null)
                return;

            GridLayoutGroup grid = __instance.GetComponentInChildren<GridLayoutGroup>(true);

            if (grid == null || grid.transform.childCount < 200)
                return;

            Plugin.Logger.LogInfo("===== INVENTORY HIERARCHY =====");
            Dump(__instance.transform, 0);
            Plugin.Logger.LogInfo("===============================");

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

            // --- SECTION C: CALIBRATE INTERNAL ITEM CONTENT BOUNDS ---
            RectTransform gridRect = grid.GetComponent<RectTransform>();

            if (gridRect != null)
            {
                gridRect.anchorMin = new Vector2(0.5f, 1f);
                gridRect.anchorMax = new Vector2(0.5f, 1f);
                gridRect.pivot = new Vector2(0.5f, 1f);

                int rows = Mathf.CeilToInt((float)grid.transform.childCount / TotalColumns);
                float totalGridContentHeight = rows * cell + (rows - 1) * spacing + 20f;

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

                // Final Hair Cut: Lowered height to 790f to clip those last few gray border pixels clean out of frame!
                displayerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 650f);
                displayerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 790f);


                // FIXED ALIGNMENT: Lowers the window coordinates symmetrically to line up with the backpack
                displayerRect.anchoredPosition = new Vector2(displayerRect.anchoredPosition.x, -120f);
            }

            // --- SECTION E: INNER MASK & SCROLL ENGINE SEPARATION ---
            ScrollRect scroll = __instance.GetComponent<ScrollRect>();

            if (scroll == null)
            {
                Plugin.Logger.LogInfo($"Displayer Object : {__instance.gameObject.name}");
                Plugin.Logger.LogInfo($"Grid Parent      : {grid.transform.parent.name}");

                Transform innerGridContainerTransform = grid.transform.parent;
                if (innerGridContainerTransform != null)
                {
                    innerGridContainerTransform.gameObject.AddComponent<RectMask2D>();
                }

                scroll = __instance.gameObject.AddComponent<ScrollRect>();

                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.scrollSensitivity = 45f;
                scroll.content = gridRect;

                Plugin.Logger.LogInfo("[UI] Dynamic scroll engine and isolated inner component mask successfully established.");
            }

            scroll.verticalNormalizedPosition = 1f;
        }
    }
}

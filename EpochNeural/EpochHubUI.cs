using HarmonyLib;
using SpaceCraft;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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

    // --- CHATGPT'S COMPLETE AUTHORITATIVE INVENTORY SCROLLER COMPONENT ---
    // Zero dependencies on Unity's layout math bounds. Captures raw scroll input delta 
    // and forcefully maintains the grid position over native engine resets inside LateUpdate.
    internal class DirectInventoryScroller : MonoBehaviour, IScrollHandler
    {
        public RectTransform Content;
        public float ScrollSpeed = 45f;
        public float MaxScroll = 1602f; // Deterministic static boundary limit: 2452f - 850f

        public static float SavedY = 0f;

        public void OnScroll(PointerEventData eventData)
        {
            float y = SavedY;

            // Unity mouse wheel down scroll delta values read as negative numbers natively
            y -= eventData.scrollDelta.y * ScrollSpeed;
            y = Mathf.Clamp(y, 0f, MaxScroll);

            SavedY = y;

            if (Content != null)
            {
                Vector2 p = Content.anchoredPosition;
                p.y = y;
                Content.anchoredPosition = p;
            }
        }

        // ChatGPT Epsilon Guard: Constantly checks and forces the position back to SavedY
        // right before drawing, winning complete architectural ownership over the game's loop.
        void LateUpdate()
        {
            if (Content == null)
                return;

            Vector2 p = Content.anchoredPosition;

            if (Mathf.Abs(p.y - SavedY) > 0.01f)
            {
                p.y = SavedY;
                Content.anchoredPosition = p;
            }
        }
    }

    // 2. Main Layout Formatter: Handles stable grid alignment and 10 uncut visible rows
    [HarmonyPatch(typeof(InventoryDisplayer), "TrueRefreshContent")]
    internal static class EpochGridFormatter
    {
        private const int TotalColumns = 8;
        private const float TopMaskTrim = 65f;

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
            grid.padding = new RectOffset(0, 0, Mathf.RoundToInt(TopMaskTrim), 0);

            // --- SECTION C: CALIBRATE INTERNAL ITEM CONTENT BOUNDS ---
            RectTransform gridRect = grid.GetComponent<RectTransform>();
            if (gridRect != null)
            {
                gridRect.anchorMin = new Vector2(0.5f, 1f);
                gridRect.anchorMax = new Vector2(0.5f, 1f);
                gridRect.pivot = new Vector2(0.5f, 1f);

                int rows = Mathf.CeilToInt((float)grid.transform.childCount / TotalColumns);
                float totalGridContentHeight = (rows * cell) + ((rows - 1) * spacing) + (TopMaskTrim + 20f); // Exactly 2452f

                gridRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 630f);
                gridRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalGridContentHeight);
                gridRect.anchoredPosition = new Vector2(0f, gridRect.anchoredPosition.y);
            }

            // --- SECTION D: ALIGN MASTER CONTAINER WINDOW TO BACKPACK BACKDROP ---
            RectTransform displayerRect = __instance.GetComponent<RectTransform>();
            if (displayerRect != null)
            {
                displayerRect.anchorMin = new Vector2(0.5f, 0.5f);
                displayerRect.anchorMax = new Vector2(0.5f, 0.5f);
                displayerRect.pivot = new Vector2(0.5f, 0.5f);

                // Height remains perfectly locked at 850f
                displayerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 650f);
                displayerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 850f);

                // Alignment remains perfectly locked at -72.5f
                displayerRect.anchoredPosition = new Vector2(displayerRect.anchoredPosition.x, -72.5f);
            }

            // --- SECTION E: FLIPPED DETERMINISTIC INPUT SCROLLER INJECTION ---
            // 1. Completely strip and disable native ScrollRect component functionality
            ScrollRect sr = __instance.GetComponent<ScrollRect>();
            if (sr != null)
            {
                sr.enabled = false;
            }

            // 2. Ensure layout clipping mask remains active to hide overflow items below window border
            RectMask2D mask = __instance.gameObject.GetComponent<RectMask2D>();
            if (mask == null)
            {
                mask = __instance.gameObject.AddComponent<RectMask2D>();
            }
            mask.padding = new Vector4(0f, 0f, 0f, TopMaskTrim);

            // 3. Inject our pure input-driven custom scroller component onto the main frame window
            DirectInventoryScroller customScroller = __instance.gameObject.GetComponent<DirectInventoryScroller>();
            if (customScroller == null)
            {
                customScroller = __instance.gameObject.AddComponent<DirectInventoryScroller>();
                Plugin.Logger.LogInfo("[UI] Authoritative DirectInventoryScroller engine attached safely.");
            }

            if (customScroller != null)
            {
                customScroller.Content = gridRect;
                customScroller.MaxScroll = 1602f; // Hardcoded static boundary wall limit
                customScroller.ScrollSpeed = 45f;

                // Sync current frame position immediately from the static persistent memory bank
                Vector2 p = gridRect.anchoredPosition;
                p.x = 0f;
                p.y = DirectInventoryScroller.SavedY;
                gridRect.anchoredPosition = p;
            }

            // --- SECTION F: BUTTONS HIERARCHY RE-PARENTING ---
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

                    // Pinned buttons tracking offset remains perfectly locked at +409.5f
                    buttonRect.anchoredPosition = new Vector2(displayerRect.anchoredPosition.x, displayerRect.anchoredPosition.y + 409.5f);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(buttonRect);
                }
            }
        }
    }
}

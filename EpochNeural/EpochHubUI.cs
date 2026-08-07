using HarmonyLib;
using SpaceCraft;
using UnityEngine;
using UnityEngine.UI;

namespace EpochNeural
{
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
            if (EpochNeural.EpochHubInventory == null)
                return;

            GridLayoutGroup grid = __instance.GetComponentInChildren<GridLayoutGroup>(true);

            if (grid == null || grid.transform.childCount < 200)
                return;

            Plugin.Logger.LogInfo("===== INVENTORY HIERARCHY =====");
            Dump(__instance.transform, 0);
            Plugin.Logger.LogInfo("===============================");

            ContentSizeFitter fitter = __instance.GetComponent<ContentSizeFitter>();
            if (fitter != null)
                fitter.enabled = false;

            ContentSizeFitter gridFitter = grid.GetComponent<ContentSizeFitter>();
            if (gridFitter != null)
                gridFitter.enabled = false;

            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = TotalColumns;

            const float cell = 76f;
            const float spacing = 3f;

            grid.cellSize = new Vector2(cell, cell);
            grid.spacing = new Vector2(spacing, spacing);
            grid.childAlignment = TextAnchor.UpperLeft;

            RectTransform gridRect = grid.GetComponent<RectTransform>();

            if (gridRect != null)
            {
                gridRect.anchorMin = new Vector2(0.5f, 1f);
                gridRect.anchorMax = new Vector2(0.5f, 1f);
                gridRect.pivot = new Vector2(0.5f, 1f);

                int rows = Mathf.CeilToInt((float)grid.transform.childCount / TotalColumns);

                float height =
                    rows * cell +
                    (rows - 1) * spacing +
                    20f;

                gridRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 630f);
                gridRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                gridRect.anchoredPosition = Vector2.zero;
            }

            RectTransform displayerRect = __instance.GetComponent<RectTransform>();

            if (displayerRect != null)
            {
                displayerRect.anchorMin = new Vector2(0.5f, 0.5f);
                displayerRect.anchorMax = new Vector2(0.5f, 0.5f);
                displayerRect.pivot = new Vector2(0.5f, 0.5f);

                displayerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 650f);
                displayerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 650f);

                displayerRect.anchoredPosition =
                    new Vector2(displayerRect.anchoredPosition.x, -40f);
            }

            ScrollRect scroll = __instance.GetComponent<ScrollRect>();

            if (scroll == null)
            {
                Plugin.Logger.LogInfo($"Displayer Object : {__instance.gameObject.name}");
                Plugin.Logger.LogInfo($"Grid Parent      : {grid.transform.parent.name}");

                scroll = __instance.gameObject.AddComponent<ScrollRect>();

                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.scrollSensitivity = 45f;
                scroll.content = gridRect;

                Plugin.Logger.LogInfo("[UI] Scrolling engine re-engaged smoothly.");
            }

            scroll.verticalNormalizedPosition = 1f;
        }
    }
}
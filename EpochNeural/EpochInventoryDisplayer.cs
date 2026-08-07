// ============================================================
// Project : Epoch Neural
// Module  : Epoch Hub
// File    : EpochInventoryDisplayer.cs
//
// Author  : Otto
// Version : Sprint 2.0
//
// Build Status:
// 🟢 Active Development
//
// Purpose:
// Controls how the Epoch Hub inventory is displayed.
//
// Responsibilities:
// • Inventory layout
// • Grid resizing
// • Scroll view
// • Display refresh
//
// Does NOT own:
// • Hub registration
// • Hub UI
// • Inventory contents
// • Progression
// ============================================================

using HarmonyLib;
using SpaceCraft;
using UnityEngine;
using UnityEngine.UI;

namespace EpochNeural
{
    // ============================================================
    // Epoch Inventory Displayer
    //
    // Controls the Hub inventory presentation.
    // ============================================================

    internal static class EpochInventoryDisplayer
    {
        // ============================================================
        // Configuration
        // ============================================================

        private const int Columns = 8;

        // ============================================================
        // Runtime State
        // ============================================================

        private static GridLayoutGroup HubGrid;

        private static ScrollRect HubScrollRect;

        // ============================================================
        // Inventory Refresh
        // ============================================================

        [HarmonyPatch(typeof(InventoryDisplayer), "TrueRefreshContent")]
        internal static class InventoryRefreshPatch
        {
            [HarmonyPostfix]
            private static void Postfix(
                InventoryDisplayer __instance)
            {
                EpochNeural.Log(
                    "Display",
                    "Epoch Hub inventory refresh detected.");

                HubGrid =
                    __instance.GetComponentInChildren<GridLayoutGroup>();

                if (HubGrid == null)
                {
                    EpochNeural.Warn(
                        "Display",
                        "GridLayoutGroup not found.");

                    return;
                }

                EpochNeural.Log(
                    "Display",
                    $"Epoch Hub grid contains {HubGrid.transform.childCount} slots.");
            }
        }

        // ============================================================
        // Private Helpers
        // ============================================================
    }
}
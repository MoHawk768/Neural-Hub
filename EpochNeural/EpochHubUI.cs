// ============================================================
// Project : Epoch Neural
// Module  : Epoch Hub
// File    : EpochHubUI.cs
//
// Author  : Otto
// Version : Sprint 2.0
//
// Build Status:
// 🟢 Active Development
//
// Purpose:
// Manages the Epoch Hub user interface.
//
// Responsibilities:
// • Hub window detection
// • UI creation
// • UI refresh
// • UI cleanup
//
// Does NOT own:
// • Hub registration
// • Inventory data
// • Inventory rendering
// • Progression
// ============================================================

using HarmonyLib;
using SpaceCraft;
using UnityEngine;
using UnityEngine.UI;

namespace EpochNeural
{
    // ============================================================
    // Epoch Hub UI
    //
    // Manages the Epoch Hub interface.
    // ============================================================

    internal static class EpochHubUI
    {
        // ============================================================
        // Configuration
        //
        // Hub UI configuration.
        // ============================================================

        private const int HubInventorySize = 240;

        // ============================================================
        // Runtime State
        //
        // Shared Hub UI objects.
        // ============================================================

        private static bool IsInitialized;

        private static GameObject HubPanel;

        // ============================================================
        // Initialization
        //
        // Initializes the Hub UI.
        // ============================================================

        // ============================================================
        // Initialize
        // ============================================================

        internal static void Initialize()
        {
            if (IsInitialized)
                return;

            EpochNeural.Log(
                "Hub",
                "Initializing Hub UI...");

            IsInitialized = true;
        }

        // ============================================================
        // Create Hub UI
        // ============================================================

        private static void CreateHubUI()
        {
            EpochNeural.Log(
                "Hub",
                "Creating Hub UI...");
        }

        // ============================================================
        // Refresh Hub UI
        // ============================================================

        private static void RefreshHubUI()
        {
            EpochNeural.Log(
                "Hub",
                "Refreshing Hub UI...");
        }

        // ============================================================
        // Cleanup Hub UI
        // ============================================================

        private static void CleanupHubUI()
        {
            EpochNeural.Log(
                "Hub",
                "Cleaning up Hub UI.");

            if (HubPanel != null)
            {
                Object.Destroy(
                    HubPanel);

                HubPanel = null;
            }

            EpochNeural.EpochHubWindow = null;
        }

        // ============================================================
        // Hub Window Detection
        // ============================================================

        [HarmonyPatch(typeof(UiWindowContainer), "SetInventories")]
        internal static class HubWindowPatch
        {
            [HarmonyPostfix]
            private static void Postfix(
                UiWindowContainer __instance,
                Inventory inventoryLeft,
                Inventory inventoryRight)
            {
                if (inventoryRight == null)
                    return;

                if (inventoryRight.GetSize() != HubInventorySize)
                    return;

                EpochNeural.Log(
                    "Hub",
                    "Epoch Hub opened.");

                EpochNeural.EpochHubWindow =
                    __instance;

                EpochNeural.PlayerInventory =
                    inventoryLeft;

                EpochNeural.EpochHubInventory =
                    inventoryRight;

                CreateHubUI();

                RefreshHubUI();
            }
        }

        // ============================================================
        // Hub Window Close Detection
        // ============================================================

        [HarmonyPatch(typeof(UiWindowContainer), "OnClose")]
        internal static class HubWindowClosePatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                CleanupHubUI();
            }
        }

        // ============================================================
        // Private Helpers
        // ============================================================
    }
}
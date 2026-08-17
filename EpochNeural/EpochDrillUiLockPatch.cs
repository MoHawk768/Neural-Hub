using System;
using HarmonyLib;
using SpaceCraft;
using UnityEngine;

namespace EpochNeural
{
    /// <summary>
    /// Epoch Node Extractors are permanently bound to the physical vein they
    /// were placed on. They must therefore not expose the vanilla ore-selector
    /// UI, which would allow the player to choose a different resource.
    ///
    /// This is deliberately a UI/interaction safeguard only. The authoritative
    /// extraction lock remains in EpochDrillManager.
    /// </summary>
    [HarmonyPatch]
    internal static class EpochDrillUiLockPatch
    {
        private const string DRILL_ID = "Epoch_Node_Drill";

        // ------------------------------------------------------------
        // HARD BLOCK: never allow the vanilla selector window to open.
        // This also protects clients if the ActionGroupSelector component
        // itself could not be disabled during world-object initialization.
        // ------------------------------------------------------------

        [HarmonyPatch(typeof(ActionGroupSelector), "OnAction")]
        [HarmonyPrefix]
        private static bool PrefixActionGroupSelector(ActionGroupSelector __instance)
        {
            if (!IsEpochDrill(__instance))
                return true;

            string resource = GetBoundResource(__instance);

            Plugin.Logger?.LogInfo(
                string.IsNullOrEmpty(resource)
                    ? "[Epoch Drill UI] Resource selector blocked; opening extractor inventory."
                    : $"[Epoch Drill UI] Resource selector blocked for locked extractor -> {resource}; opening inventory.");

            // The vanilla ActionGroupSelector is also the interaction point
            // that the player uses on an ore extractor. Do not disable it.
            // Redirect the interaction to the extractor's normal
            // ActionOpenable, which opens the physical inventory.
            // Epoch extractors do not contain an ActionOpenable component.
            // The vanilla ActionGroupSelector itself already proves that the
            // extractor has an InventoryAssociated. Open the normal Container
            // inventory window directly, using the same inventory/UI path that
            // vanilla ActionOpenable uses internally.
            var associated = __instance.GetComponentInParent<WorldObjectAssociated>();
            var inventoryAssociated = __instance.GetComponentInParent<InventoryAssociated>();

            if (associated == null || inventoryAssociated == null)
            {
                Plugin.Logger?.LogWarning(
                    "[Epoch Drill UI] Cannot open extractor inventory: missing WorldObjectAssociated or InventoryAssociated.");
                return false;
            }

            inventoryAssociated.GetInventory(delegate (Inventory objectInventory)
            {
                var player = Managers.GetManager<PlayersManager>()
                    .GetActivePlayerController();

                if (player == null)
                {
                    Plugin.Logger?.LogWarning(
                        "[Epoch Drill UI] Cannot open extractor inventory: active player controller missing.");
                    return;
                }

                Inventory playerInventory = player.GetPlayerBackpack().GetInventory();

                var openedUi = (UiWindowContainer)Managers.GetManager<WindowsHandler>()
                    .OpenAndReturnUi(DataConfig.UiType.Container);

                if (openedUi == null)
                {
                    Plugin.Logger?.LogWarning(
                        "[Epoch Drill UI] Cannot open extractor inventory: Container UI could not be opened.");
                    return;
                }

                openedUi.SetInventories(
                    playerInventory,
                    objectInventory,
                    hideLogistics: false);

                var worldObject = associated.GetWorldObject();

                if (worldObject != null)
                {
                    var worldObjectText = __instance.GetComponent<WorldObjectText>();

                    if (worldObjectText != null)
                    {
                        openedUi.SetContainerName(worldObjectText.GetText());
                    }
                }

                Plugin.Logger?.LogInfo(
                    $"[Epoch Drill UI] Extractor inventory opened successfully for [{resource}].");
            });

            return false;
        }

        // ------------------------------------------------------------
        // HOVER: keep the vanilla "Open" interaction hint.
        // The click is redirected by PrefixActionGroupSelector above.
        // ------------------------------------------------------------

        // ------------------------------------------------------------
        // The selector remains enabled because it is the player's interaction
        // point. OnAction is redirected above; the vanilla selector window
        // is never opened for Epoch extractors.
        // ------------------------------------------------------------

        private static bool IsEpochDrill(ActionGroupSelector selector)
        {
            if (selector == null)
                return false;

            try
            {
                var associated = selector.GetComponentInParent<WorldObjectAssociated>();
                var worldObject = associated?.GetWorldObject();

                return worldObject?.GetGroup()?.GetId() == DRILL_ID;
            }
            catch
            {
                return false;
            }
        }

        private static string GetBoundResource(ActionGroupSelector selector)
        {
            try
            {
                var associated = selector.GetComponentInParent<WorldObjectAssociated>();
                var worldObject = associated?.GetWorldObject();

                if (worldObject == null)
                    return null;

                return EpochDrillManager.GetBoundResourceId(worldObject.GetId());
            }
            catch
            {
                return null;
            }
        }
    }
}
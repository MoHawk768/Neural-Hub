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
                    ? "[Epoch Drill UI] Resource selector blocked for locked extractor."
                    : $"[Epoch Drill UI] Resource selector blocked for locked extractor -> {resource}.");

            return false;
        }

        // ------------------------------------------------------------
        // HOVER SAFEGUARD: suppress the selector's vanilla "Open" hint.
        // ------------------------------------------------------------

        [HarmonyPatch(typeof(ActionGroupSelector), "OnHover")]
        [HarmonyPrefix]
        private static bool PrefixActionGroupSelectorHover(ActionGroupSelector __instance)
        {
            if (!IsEpochDrill(__instance))
                return true;

            return false;
        }

        // ------------------------------------------------------------
        // INITIALIZATION SAFEGUARD: if we can identify the Epoch drill at
        // component initialization time, disable the selector component.
        // The OnAction prefix above remains the authoritative fallback.
        // ------------------------------------------------------------

        [HarmonyPatch(typeof(ActionGroupSelector), "Awake")]
        [HarmonyPostfix]
        private static void PostfixActionGroupSelectorAwake(ActionGroupSelector __instance)
        {
            try
            {
                if (!IsEpochDrill(__instance))
                    return;

                __instance.enabled = false;
                Plugin.Logger?.LogInfo(
                    "[Epoch Drill UI] Vanilla resource selector component disabled.");
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Drill UI] Failed to disable selector component: {ex.Message}");
            }
        }

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
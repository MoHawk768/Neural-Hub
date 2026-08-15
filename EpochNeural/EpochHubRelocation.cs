using System;
using System.Reflection;
using HarmonyLib;
using SpaceCraft;
using UnityEngine;

namespace EpochNeural
{
    /// <summary>
    /// Epoch Hub relocation.
    ///
    /// When the player attempts to place a second Epoch Hub,
    /// the existing Hub is relocated instead of creating a new one.
    ///
    /// The WorldObject itself is preserved.
    /// Therefore:
    /// - WorldObject ID is preserved
    /// - Hub inventory is preserved
    /// - Hub tier/state is preserved
    /// - Epoch logistics remain connected
    /// </summary>
    [HarmonyPatch]
    internal static class EpochHubRelocation
    {
        private const string HubId = "Epoch_Hub";

        private static readonly FieldInfo GhostGroupField =
            typeof(PlayerBuilder).GetField(
                "_ghostGroupConstructible",
                BindingFlags.Instance |
                BindingFlags.NonPublic);

        private static readonly FieldInfo GhostField =
            typeof(PlayerBuilder).GetField(
                "_ghost",
                BindingFlags.Instance |
                BindingFlags.NonPublic);

        // ============================================================
        // INTERCEPT HUB BUILD ACTION
        // ============================================================

        [HarmonyPatch(typeof(PlayerBuilder), "InputOnAction")]
        [HarmonyPriority(Priority.First)]
        [HarmonyPrefix]
        private static bool PrefixInputOnAction(PlayerBuilder __instance)
        {
            try
            {
                if (__instance == null)
                    return true;

                if (GhostGroupField == null || GhostField == null)
                    return true;

                Group ghostGroup =
                    GhostGroupField.GetValue(__instance) as Group;

                // Not an Epoch Hub -> completely vanilla.
                if (ghostGroup == null ||
                    ghostGroup.GetId() != HubId)
                {
                    return true;
                }

                // Find the player's current Hub ghost.
                ConstructibleGhost ghost =
                    GhostField.GetValue(__instance) as ConstructibleGhost;

                if (ghost == null)
                    return true;

                // No existing Hub -> normal first-time construction.
                WorldObject existingHub = FindExistingHub();

                if (existingHub == null)
                    return true;

                // ====================================================
                // EXISTING HUB FOUND
                // This is a relocation attempt.
                // ====================================================

                GhostPlacementChecker checker =
                    ghost.GetComponent<GhostPlacementChecker>();

                if (checker == null)
                {
                    Plugin.Logger?.LogWarning(
                        "[Epoch Relocation] GhostPlacementChecker missing.");
                    return true;
                }

                // Do not allow relocation into an invalid position.
                if (!checker.GetPositioningStatus())
                {
                    ShowMessage("EPOCH: HUB CANNOT BE PLACED HERE!");
                    return false;
                }

                Vector3 newPosition = ghost.transform.position;
                Quaternion newRotation = ghost.transform.rotation;

                if (newPosition.sqrMagnitude <= 0.000001f)
                {
                    Plugin.Logger?.LogWarning(
                        "[Epoch Relocation] Invalid ghost position.");
                    return false;
                }

                int hubWorldObjectId = existingHub.GetId();

                Plugin.Logger?.LogInfo(
                    $"[Epoch Relocation] Relocating existing Hub. " +
                    $"WorldObject ID: {hubWorldObjectId}");

                Plugin.Logger?.LogInfo(
                    $"[Epoch Relocation] Old position: {existingHub.GetPosition()}");

                Plugin.Logger?.LogInfo(
                    $"[Epoch Relocation] New position: {newPosition}");

                // ====================================================
                // IMPORTANT:
                //
                // Do NOT call DestroyWorldObject().
                //
                // That would destroy the Hub inventory.
                //
                // We only remove the physical GameObject.
                // The WorldObject and inventory remain alive.
                // ====================================================

                WorldObjectsHandler handler =
                    WorldObjectsHandler.Instance;

                if (handler == null)
                {
                    Plugin.Logger?.LogError(
                        "[Epoch Relocation] WorldObjectsHandler unavailable.");
                    return false;
                }

                handler.DestroyWorldObjectGOOnAllClients(
                    hubWorldObjectId);

                // Move and recreate the SAME WorldObject.
                handler.DropOnFloorWithRotation(
                    existingHub,
                    newPosition,
                    newRotation,
                    0f,
                    dropSound: false,
                    ownershipToNearestClient: true);

                // Make sure Epoch continues tracking the same ID.
                EpochNeural.SetHubWorldObjectId(
                    hubWorldObjectId);

                // Destroy the temporary construction ghost.
                ghost.DestroyGhost();

                // Cancel builder state without triggering normal
                // construction/resource consumption.
                __instance.InputOnCancelAction();

                Plugin.Logger?.LogInfo(
                    $"[Epoch Relocation] SUCCESS. " +
                    $"Hub WorldObject ID {hubWorldObjectId} preserved.");

                ShowMessage("EPOCH HUB RELOCATED");

                // Prevent the original PlayerBuilder.InputOnAction()
                // from running. That method would otherwise invoke
                // the vanilla Hub restriction.
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError(
                    $"[Epoch Relocation] Relocation error: {ex}");

                // Fail safely and allow normal game behaviour.
                return true;
            }
        }

        // ============================================================
        // FIND EXISTING HUB
        // ============================================================

        private static WorldObject FindExistingHub()
        {
            try
            {
                WorldObjectsHandler handler =
                    WorldObjectsHandler.Instance;

                if (handler == null)
                    return null;

                // First use Epoch's tracked ID.
                int trackedId =
                    EpochNeural.HubWorldObjectId;

                if (trackedId > 0)
                {
                    WorldObject tracked =
                        handler.GetWorldObjectViaId(trackedId);

                    if (tracked != null &&
                        tracked.GetGroup() != null &&
                        tracked.GetGroup().GetId() == HubId)
                    {
                        return tracked;
                    }
                }

                // Fallback: scan constructed WorldObjects.
                var constructed =
                    handler.GetConstructedWorldObjects();

                if (constructed == null)
                    return null;

                foreach (WorldObject wo in constructed)
                {
                    if (wo == null ||
                        wo.GetGroup() == null)
                    {
                        continue;
                    }

                    if (wo.GetGroup().GetId() == HubId)
                    {
                        EpochNeural.SetHubWorldObjectId(
                            wo.GetId());

                        return wo;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Relocation] FindExistingHub error: {ex.Message}");
            }

            return null;
        }

        // ============================================================
        // MESSAGE
        // ============================================================

        private static void ShowMessage(string message)
        {
            try
            {
                BaseHudHandler hud =
                    Managers.GetManager<BaseHudHandler>();

                if (hud != null)
                {
                    hud.DisplayCursorText(message, 3f);
                }
            }
            catch
            {
                // Never allow UI messaging to affect relocation.
            }
        }
    }
}
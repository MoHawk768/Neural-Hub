using System;
using System.Collections.Generic;
using HarmonyLib;
using SpaceCraft;
using UnityEngine;

namespace EpochNeural
{
    /// <summary>
    /// Handles drill placement validation and registration
    /// </summary>
    [HarmonyPatch]
    internal static class EpochDrillPlacementPatch
    {
        // ============================================================
        // PLACEMENT VALIDATION - Runs BEFORE placement
        // ============================================================

        [HarmonyPatch(typeof(PlayerBuilder), "InputOnAction")]
        [HarmonyPrefix]
        private static bool PrefixPlaceDrill(PlayerBuilder __instance)
        {
            try
            {
                // Get the ghost group (the object being placed)
                var ghostGroup = __instance.GetType().GetField("_ghostGroupConstructible",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(__instance) as Group;

                if (ghostGroup == null || ghostGroup.GetId() != "Epoch_Node_Drill")
                    return true; // Not our drill, let normal placement proceed

                // Check if Hub exists - if not, block placement
                if (!HubExists())
                {
                    ShowMessage("EPOCH: Build an Epoch Hub first!");
                    return false;
                }

                // Get the ghost position
                var ghost = __instance.GetType().GetField("_ghost",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(__instance) as ConstructibleGhost;

                if (ghost == null)
                    return true;

                Vector3 position = ghost.transform.position;

                // Check if in landing zone
                bool isInLandingZone = EpochDrillManager.IsLandingAreaPosition(position);

                if (isInLandingZone)
                {
                    // Landing zone drill - check if landing zone is already occupied
                    if (EpochDrillManager.IsLandingZoneOccupied())
                    {
                        ShowMessage("EPOCH: Landing Area already has a Node Extractor!");
                        return false;
                    }

                    // Check total drill limit (biome drills + landing zone drill)
                    int totalVeins = EpochDrillManager.GetTotalVeinsOnCurrentPlanet();
                    int currentDrills = EpochDrillManager.GetActiveDrillsCount();

                    if (currentDrills >= totalVeins)
                    {
                        ShowMessage($"EPOCH: Maximum Node Extractors reached! ({currentDrills}/{totalVeins})");
                        return false;
                    }

                    Plugin.Logger?.LogInfo($"[Epoch Drill] Landing Area Node Extractor placement approved.");
                    return true;
                }

                // Not in landing zone - check biome
                string sectorGroupId = GetSectorGroupId(position);

                // If UnknownBiome, treat it as a generic "Landing Area"
                if (string.IsNullOrEmpty(sectorGroupId) || sectorGroupId == "UnknownBiome")
                {
                    sectorGroupId = "Landing Area";
                    Plugin.Logger?.LogInfo($"[Epoch Drill] Using 'Landing Area' for position: {position}");
                }

                // Check if biome is occupied
                if (EpochDrillManager.IsBiomeOccupied(sectorGroupId))
                {
                    ShowMessage($"EPOCH: {sectorGroupId} already has a Node Extractor!");
                    return false;
                }

                // Check total drill limit
                int totalVeins2 = EpochDrillManager.GetTotalVeinsOnCurrentPlanet();
                int currentDrills2 = EpochDrillManager.GetActiveDrillsCount();

                if (currentDrills2 >= totalVeins2)
                {
                    ShowMessage($"EPOCH: Maximum Node Extractors reached! ({currentDrills2}/{totalVeins2})");
                    return false;
                }

                Plugin.Logger?.LogInfo($"[Epoch Drill] Node Extractor placement approved for biome: {sectorGroupId}");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Drill] Error in PrefixPlaceDrill: {ex.Message}");
                ShowMessage("EPOCH: Error checking Node Extractor placement!");
                return false;
            }
        }

        // ============================================================
        // PLACEMENT COMPLETION - Runs AFTER successful placement
        // ============================================================

        [HarmonyPatch(typeof(PlayerBuilder), "OnConstructed")]
        [HarmonyPostfix]
        private static void PostfixPlaceDrill(PlayerBuilder __instance)
        {
            try
            {
                // Find the newest UNREGISTERED Epoch Node Extractor.
                //
                // IMPORTANT:
                // PlayerBuilder.OnConstructed fires for EVERY object the player
                // builds, not just Node Extractors. The old code searched for the
                // newest drill every time, found an already-installed drill, tried
                // to register it again, failed because the landing/biome slot was
                // already occupied, and then DESTROYED the existing drill.
                //
                // Only process a drill whose WorldObject ID is not already in the
                // Epoch drill registry.
                var constructedObjects = WorldObjectsHandler.Instance?.GetConstructedWorldObjects();
                if (constructedObjects == null)
                    return;

                WorldObject latestDrill = null;
                var activeRegistry = EpochDrillManager.GetActiveDrillRegistry();

                foreach (var wo in constructedObjects)
                {
                    if (wo?.GetGroup()?.GetId() != "Epoch_Node_Drill")
                        continue;

                    int woId = wo.GetId();

                    // Already registered as a biome extractor.
                    if (activeRegistry != null && activeRegistry.ContainsValue(woId))
                        continue;

                    // Already registered as the landing-area extractor.
                    if (EpochDrillManager.IsLandingZoneDrill(woId))
                        continue;

                    // This is an unregistered drill. Keep the newest one.
                    if (latestDrill == null || woId > latestDrill.GetId())
                        latestDrill = wo;
                }

                // OnConstructed was triggered by a non-drill build, or there are
                // no newly placed drills to register. Nothing to do.
                if (latestDrill == null)
                    return;

                Vector3 position = latestDrill.GetPosition();
                bool isInLandingZone = EpochDrillManager.IsLandingAreaPosition(position);

                // Get biome name
                string biomeName = "Landing Area";
                if (!isInLandingZone)
                {
                    biomeName = GetSectorGroupId(position);
                    if (string.IsNullOrEmpty(biomeName) || biomeName == "UnknownBiome")
                        biomeName = "Landing Area";
                }

                // Register the drill.
                // Landing Area is a separate registry slot. Passing "Landing Area"
                // as a normal biome makes it count as Map 1/20 instead of Landing 1/1.
                bool registered = isInLandingZone
                    ? EpochDrillManager.TryRegisterDrill(null, latestDrill.GetId(), position)
                    : EpochDrillManager.TryRegisterDrill(biomeName, latestDrill.GetId(), position);

                if (registered)
                {
                    // Attach cleanup component to the drill's GameObject
                    var gameObject = latestDrill.GetGameObject();
                    if (gameObject != null)
                    {
                        var cleanup = gameObject.GetComponent<EpochDrillCleanup>();
                        if (cleanup == null)
                        {
                            cleanup = gameObject.AddComponent<EpochDrillCleanup>();
                        }
                        cleanup.Initialize(latestDrill.GetId(), biomeName);
                        Plugin.Logger?.LogInfo($"[Epoch Drill] Cleanup component attached to Node Extractor [{latestDrill.GetId()}]");
                    }

                    // Show notification on HUD
                    if (EpochHud.Instance != null)
                    {
                        EpochHud.Instance.ShowNotification($"{biomeName} Node Extractor Installed", true);
                    }
                    else
                    {
                        ShowMessage($"EPOCH: {biomeName} Node Extractor Installed!");
                    }

                    Plugin.Logger?.LogInfo($"[Epoch Drill] Node Extractor [{latestDrill.GetId()}] installed in {biomeName}");
                }
                else
                {
                    // Registration failed - destroy the drill
                    WorldObjectsHandler.Instance.DestroyWorldObject(latestDrill.GetId(), true);
                    ShowMessage("EPOCH: Failed to register Node Extractor!");
                    Plugin.Logger?.LogWarning($"[Epoch Drill] Node Extractor registration failed, destroyed.");
                }

                // Refresh HUD
                EpochDrillManager.RefreshNetworkTelemetry();
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Drill] Error in PostfixPlaceDrill: {ex.Message}");
            }
        }

        // ============================================================
        // DRILL DESTRUCTION - Show notification (PREFIX)
        // ============================================================

        [HarmonyPatch(typeof(WorldObjectsHandler), "DestroyWorldObject", new Type[] { typeof(int), typeof(bool) })]
        [HarmonyPrefix]
        private static void PrefixDestroyDrill(WorldObjectsHandler __instance, int woId, bool destroyGameObject)
        {
            try
            {
                // Check if this is a drill
                var worldObject = __instance.GetWorldObjectViaId(woId);
                if (worldObject == null)
                {
                    // The object might already be destroyed, try to clean up the registry
                    EpochDrillManager.CleanUpOrphanedDrill(woId);
                    Plugin.Logger?.LogWarning($"[Epoch Drill] DestroyWorldObject called for null WorldObject ID: {woId}, attempted cleanup.");
                    return;
                }

                var group = worldObject.GetGroup();
                if (group == null)
                {
                    Plugin.Logger?.LogWarning($"[Epoch Drill] DestroyWorldObject called for WorldObject with null group ID: {woId}");
                    return;
                }

                if (group.GetId() != "Epoch_Node_Drill")
                    return;

                Plugin.Logger?.LogInfo($"[Epoch Drill] DestroyWorldObject called for Node Extractor ID: {woId}");

                // ============================================================
                // FIX: Get biome name from the cleanup component if possible
                // ============================================================
                string biomeName = "Unknown Area";
                var gameObject = worldObject.GetGameObject();

                if (gameObject != null)
                {
                    var cleanup = gameObject.GetComponent<EpochDrillCleanup>();
                    if (cleanup != null)
                    {
                        biomeName = cleanup.GetBiomeName();
                        Plugin.Logger?.LogInfo($"[Epoch Drill] Using stored biome name: {biomeName}");
                    }
                }

                // Fallback: if cleanup component not found or biome is still Unknown, try sector detection
                if (string.IsNullOrEmpty(biomeName) || biomeName == "Unknown Area")
                {
                    Vector3 position = worldObject.GetPosition();
                    bool isInLandingZone = EpochDrillManager.IsLandingAreaPosition(position);
                    if (isInLandingZone)
                    {
                        biomeName = "Landing Area";
                    }
                    else
                    {
                        string sectorName = GetSectorGroupId(position);
                        if (!string.IsNullOrEmpty(sectorName) && sectorName != "UnknownBiome")
                        {
                            biomeName = sectorName;
                        }
                        else
                        {
                            biomeName = "Landing Area"; // Safe fallback
                        }
                    }
                }

                // Unregister the drill
                EpochDrillManager.UnregisterDrill(woId);

                // Mark cleanup component as cleaned up to prevent duplicate OnDestroy handling
                if (gameObject != null)
                {
                    var cleanup = gameObject.GetComponent<EpochDrillCleanup>();
                    if (cleanup != null)
                    {
                        cleanup.MarkCleanedUp();
                    }
                }

                // Show notification
                if (EpochHud.Instance != null)
                {
                    EpochHud.Instance.ShowNotification($"{biomeName} Node Extractor Removed", false);
                }

                Plugin.Logger?.LogInfo($"[Epoch Drill] Node Extractor [{woId}] removed from {biomeName}");
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Drill] Error in PrefixDestroyDrill: {ex.Message}");
            }
        }

        // ============================================================
        // DRILL DESTRUCTION - Backup cleanup (POSTFIX)
        // ============================================================

        [HarmonyPatch(typeof(WorldObjectsHandler), "DestroyWorldObject", new Type[] { typeof(int), typeof(bool) })]
        [HarmonyPostfix]
        private static void PostfixDestroyDrill(WorldObjectsHandler __instance, int woId, bool destroyGameObject)
        {
            try
            {
                // Check if this ID is still in any registry
                // If the prefix didn't catch it, clean it up here
                EpochDrillManager.CleanUpOrphanedDrill(woId);
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Drill] Error in PostfixDestroyDrill: {ex.Message}");
            }
        }

        // ============================================================
        // HELPER METHODS
        // ============================================================

        private static bool HubExists()
        {
            try
            {
                var constructedObjects = WorldObjectsHandler.Instance?.GetConstructedWorldObjects();
                if (constructedObjects == null)
                    return false;

                foreach (var wo in constructedObjects)
                {
                    if (wo?.GetGroup()?.GetId() == "Epoch_Hub")
                        return true;
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning($"[Epoch Drill] Error checking HubExists: {ex.Message}");
            }
            return false;
        }

        private static string GetSectorGroupId(Vector3 position)
        {
            try
            {
                Type sectorsType = Type.GetType("SpaceCraft.SectorsHandler, Assembly-CSharp");
                if (sectorsType == null)
                    return "UnknownBiome";

                object sectorsHandler = UnityEngine.Object.FindFirstObjectByType(sectorsType);
                if (sectorsHandler == null)
                    return "UnknownBiome";

                var sector = sectorsType.GetMethod("GetSectorWithPosition")?.Invoke(sectorsHandler, new object[] { position });
                if (sector == null)
                    return "UnknownBiome";

                var groupId = sector.GetType().GetMethod("GetGroupId")?.Invoke(sector, null) as string;
                return string.IsNullOrEmpty(groupId) ? "UnknownBiome" : groupId;
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning($"[Epoch Drill] Failed to get sector: {ex.Message}");
                return "UnknownBiome";
            }
        }

        private static void ShowMessage(string message)
        {
            try
            {
                var hud = Managers.GetManager<BaseHudHandler>();
                if (hud != null)
                {
                    hud.DisplayCursorText(message, 3f);
                }
                else
                {
                    Plugin.Logger?.LogWarning(message);
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Drill] Error showing message: {ex.Message}");
            }
        }
    }
}
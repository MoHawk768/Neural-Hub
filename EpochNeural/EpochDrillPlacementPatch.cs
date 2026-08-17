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
        // The placement prefix can resolve a buried/obstructed vein through
        // Epoch's validated build-site cache. OnConstructed only receives the
        // finished WorldObject, so retain the resolved physical vein briefly
        // until the corresponding drill is registered.
        private static MachineGenerationGroupVein _pendingPlacementVein;
        private static Vector3 _pendingPlacementPosition;
        private static float _pendingPlacementTime = -1f;

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

                // ============================================================
                // VEIN LOCK - The extractor must sit over a real ore vein.
                // The biome is NOT the extraction target anymore.
                // ============================================================
                MachineGenerationGroupVein vein =
                    EpochDrillManager.FindVeinUnderPosition(position);

                bool isInLandingZone =
                    EpochDrillManager.IsLandingAreaPosition(position);

                if (vein == null)
                {
                    // Buried/obstructed veins can be far below the playable
                    // surface, so the normal 15m physical lookup can fail even
                    // though the player is standing on perfectly valid vanilla
                    // terrain near the vein.
                    //
                    // IMPORTANT: this fallback does NOT bypass vanilla
                    // construction validation. We only resolve WHICH physical
                    // vein the player is targeting. PlayerBuilder's normal
                    // placement checks still decide whether the actual ghost
                    // can be built at this surface.
                    if (TryFindNearestPlacementVein(
                        position,
                        100f,
                        out MachineGenerationGroupVein nearbyVein,
                        out float nearbyDistance))
                    {
                        vein = nearbyVein;

                        Plugin.Logger?.LogInfo(
                            $"[Epoch Drill] Placement resolved buried/obstructed " +
                            $"vein={EpochDrillManager.GetVeinWorldObjectId(vein)}, " +
                            $"resource={EpochDrillManager.GetVeinResourceName(vein)}, " +
                            $"horizontalDistance={nearbyDistance:F1}m, " +
                            $"surface={position}.");

                        isInLandingZone =
                            EpochDrillManager.IsLandingAreaPosition(position);
                    }
                }

                if (vein == null)
                {
                    ShowMessage(
                        "EPOCH: No physical ore vein is within the Node Extractor range.");

                    Plugin.Logger?.LogInfo(
                        $"[Epoch Drill] Placement rejected: no physical vein " +
                        $"within the 100m Epoch placement range of {position}.");

                    return false;
                }

                // Preserve the exact physical vein resolved by this
                // placement attempt. This is especially important for buried
                // veins where FindVeinUnderPosition(position) cannot rediscover
                // the vein after the extractor is actually constructed.
                _pendingPlacementVein = vein;
                _pendingPlacementPosition = position;
                _pendingPlacementTime = Time.time;

                int veinWorldObjectId = EpochDrillManager.GetVeinWorldObjectId(vein);
                if (veinWorldObjectId <= 0)
                {
                    ShowMessage("EPOCH: Could not identify the ore vein!");
                    return false;
                }

                // One Epoch extractor per physical vein. Multiple veins in the
                // same biome are therefore completely independent.
                if (EpochDrillManager.IsVeinOccupied(veinWorldObjectId))
                {
                    ShowMessage("EPOCH: That ore vein already has a Node Extractor!");
                    Plugin.Logger?.LogWarning($"[Epoch Drill] Placement rejected: vein {veinWorldObjectId} already claimed.");
                    return false;
                }

                int totalVeins = EpochDrillManager.GetTotalVeinsOnCurrentPlanet();
                int currentDrills = EpochDrillManager.GetActiveDrillsCount();
                if (currentDrills >= totalVeins + 1)
                {
                    ShowMessage($"EPOCH: Maximum Node Extractors reached! ({currentDrills}/{totalVeins + 1})");
                    return false;
                }

                string resourceName = EpochDrillManager.GetVeinResourceName(vein);
                Plugin.Logger?.LogInfo(
                    $"[Epoch Drill] Placement approved. Vein={veinWorldObjectId}, Resource={resourceName}, Landing={isInLandingZone}");
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
        // BURIED / OBSTRUCTED VEIN RESOLUTION
        // ============================================================

        private static bool TryFindNearestPlacementVein(
            Vector3 position,
            float maxHorizontalDistance,
            out MachineGenerationGroupVein nearestVein,
            out float nearestDistance)
        {
            nearestVein = null;
            nearestDistance = float.MaxValue;

            try
            {
                var veins =
                    UnityEngine.Object.FindObjectsByType<MachineGenerationGroupVein>(
                        FindObjectsSortMode.None);

                foreach (var candidate in veins)
                {
                    if (candidate == null ||
                        !candidate.gameObject.activeInHierarchy)
                        continue;

                    Vector3 veinPosition = candidate.transform.position;

                    float dx = position.x - veinPosition.x;
                    float dz = position.z - veinPosition.z;

                    float horizontal =
                        Mathf.Sqrt((dx * dx) + (dz * dz));

                    if (horizontal > maxHorizontalDistance ||
                        horizontal >= nearestDistance)
                        continue;

                    nearestVein = candidate;
                    nearestDistance = horizontal;
                }

                return nearestVein != null;
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Drill] Buried-vein placement lookup failed: {ex.Message}");

                nearestVein = null;
                nearestDistance = float.MaxValue;
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
                bool isInLandingZone =
                    EpochDrillManager.IsLandingAreaPosition(position);

                // First recover the exact physical vein resolved by the
                // placement prefix. This is authoritative for buried/obstructed
                // veins and avoids trying to rediscover an underground vein from
                // the finished extractor's surface position.
                MachineGenerationGroupVein vein = null;

                if (_pendingPlacementVein != null &&
                    Time.time - _pendingPlacementTime <= 5f)
                {
                    float dx =
                        _pendingPlacementPosition.x - position.x;
                    float dz =
                        _pendingPlacementPosition.z - position.z;

                    float horizontal =
                        Mathf.Sqrt(dx * dx + dz * dz);

                    if (horizontal <= 8f)
                    {
                        vein = _pendingPlacementVein;

                        Plugin.Logger?.LogInfo(
                            $"[Epoch Drill] Post-construction recovered " +
                            $"placement-resolved vein " +
                            $"[{EpochDrillManager.GetVeinWorldObjectId(vein)}] " +
                            $"for finished drill at {position}.");
                    }
                }

                // Direct physical lookup remains the fallback for normal
                // exposed veins.
                if (vein == null)
                    vein = EpochDrillManager.FindVeinUnderPosition(position);

                if (vein == null)
                {
                    WorldObjectsHandler.Instance.DestroyWorldObject(
                        latestDrill.GetId(), true);

                    ShowMessage(
                        "EPOCH: No ore vein could be resolved for Node Extractor!");

                    Plugin.Logger?.LogWarning(
                        $"[Epoch Drill] Post-construction rejected drill " +
                        $"[{latestDrill.GetId()}]: no physical or cached Epoch vein " +
                        $"at {position}.");

                    return;
                }

                int veinWorldObjectId =
                    EpochDrillManager.GetVeinWorldObjectId(vein);
                string resourceName = EpochDrillManager.GetVeinResourceName(vein);

                // Register using the EXACT physical vein resolved by the
                // placement prefix. This is critical for buried/obstructed
                // veins: the finished extractor surface can be ~73m from the
                // physical vein, so TryRegisterDrill(position) would fail its
                // normal 15m lookup and destroy a valid extractor.
                bool registered = EpochDrillManager.TryRegisterDrillForVein(
                    latestDrill.GetId(),
                    vein);

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
                        // Keep the existing cleanup component for destruction/UI compatibility.
                        // The authoritative vein binding is stored by the game's WorldObject
                        // linked-world-object field, not by the biome name.
                        string biomeName = GetSectorGroupId(position);
                        if (string.IsNullOrEmpty(biomeName) || biomeName == "UnknownBiome")
                            biomeName = isInLandingZone ? "Landing Area" : "Unknown Area";
                        cleanup.Initialize(latestDrill.GetId(), biomeName);
                        Plugin.Logger?.LogInfo($"[Epoch Drill] Cleanup component attached to Node Extractor [{latestDrill.GetId()}]");
                    }

                    // Show notification on HUD
                    if (EpochHud.Instance != null)
                    {
                        EpochHud.Instance.ShowNotification($"{resourceName} Node Extractor Installed", true);
                    }
                    else
                    {
                        ShowMessage($"EPOCH: {resourceName} Node Extractor Installed!");
                    }

                    Plugin.Logger?.LogInfo(
                        $"[Epoch Drill] Node Extractor [{latestDrill.GetId()}] " +
                        $"locked to vein [{veinWorldObjectId}] resource [{resourceName}]");

                    // Prevent an old placement resolution from being reused
                    // by a later unrelated construction.
                    _pendingPlacementVein = null;
                    _pendingPlacementPosition = Vector3.zero;
                    _pendingPlacementTime = -1f;
                }
                else
                {
                    // Registration failed - destroy the drill
                    WorldObjectsHandler.Instance.DestroyWorldObject(latestDrill.GetId(), true);
                    ShowMessage("EPOCH: Failed to register Node Extractor!");
                    Plugin.Logger?.LogWarning(
                        $"[Epoch Drill] Node Extractor registration failed, destroyed.");

                    _pendingPlacementVein = null;
                    _pendingPlacementPosition = Vector3.zero;
                    _pendingPlacementTime = -1f;
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
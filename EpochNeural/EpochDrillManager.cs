using System;
using System.Collections.Generic;
using SpaceCraft;
using UnityEngine;

namespace EpochNeural
{
    // FIX: Forced explicit public visibility modifier so other scripts see it
    public static class EpochDrillManager
    {
        private static readonly Dictionary<string, int> _activeDrillRegistry = new Dictionary<string, int>();

        // ============================================================
        // VEIN DETECTION
        // ============================================================
        private static int _cachedVeinCount = -1;
        private static int _cachedPlanetHash = 0;
        private static Vector3 _cachedSpawnPosition = Vector3.zero;
        private static bool _hasCachedSpawnPosition = false;
        private const float LANDING_ZONE_RADIUS = 50f;

        // ============================================================
        // LANDING ZONE DRILL TRACKING
        // ============================================================
        private static int _landingZoneDrillId = -1;

        // ============================================================
        // PUBLIC METHODS
        // ============================================================

        public static int GetTotalVeinsOnCurrentPlanet()
        {
            try
            {
                var planetLoader = Managers.GetManager<PlanetLoader>();
                if (planetLoader == null)
                {
                    Plugin.Logger?.LogWarning("[Epoch Drill] PlanetLoader not found.");
                    return 12;
                }

                var planetData = planetLoader.GetCurrentPlanetData();
                if (planetData == null)
                {
                    Plugin.Logger?.LogWarning("[Epoch Drill] No current planet data.");
                    return 12;
                }

                int currentPlanetHash = planetData.GetPlanetHash();

                if (_cachedPlanetHash == currentPlanetHash && _cachedVeinCount >= 0)
                {
                    return _cachedVeinCount;
                }

                var veinObjects = UnityEngine.Object.FindObjectsByType<MachineGenerationGroupVein>(
                    UnityEngine.FindObjectsSortMode.None);

                int veinCount = 0;
                foreach (var vein in veinObjects)
                {
                    if (vein == null) continue;
                    veinCount++;
                }

                _cachedVeinCount = veinCount;
                _cachedPlanetHash = currentPlanetHash;

                Plugin.Logger?.LogInfo($"[Epoch Drill] Detected {veinCount} ore veins on current planet.");
                return veinCount > 0 ? veinCount : 12;
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Drill] Failed to count veins: {ex.Message}");
                return 12;
            }
        }

        public static Vector3 GetSpawnPosition()
        {
            if (_hasCachedSpawnPosition)
                return _cachedSpawnPosition;

            try
            {
                var planetLoader = Managers.GetManager<PlanetLoader>();
                if (planetLoader == null)
                {
                    Plugin.Logger?.LogWarning("[Epoch Drill] PlanetLoader not found for spawn position.");
                    return Vector3.zero;
                }

                var planetData = planetLoader.GetCurrentPlanetData();
                if (planetData == null)
                {
                    Plugin.Logger?.LogWarning("[Epoch Drill] No current planet data for spawn position.");
                    return Vector3.zero;
                }

                var spawnPositions = planetData.spawnPositions;
                if (spawnPositions != null && spawnPositions.Length > 0)
                {
                    var firstSpawn = spawnPositions[0];
                    if (firstSpawn != null && firstSpawn.positions != null && firstSpawn.positions.Count > 0)
                    {
                        _cachedSpawnPosition = firstSpawn.positions[0].position;
                        _hasCachedSpawnPosition = true;
                        Plugin.Logger?.LogInfo($"[Epoch Drill] Spawn position detected: {_cachedSpawnPosition}");
                        return _cachedSpawnPosition;
                    }
                }

                var playerController = Managers.GetManager<PlayersManager>()?.GetActivePlayerController();
                if (playerController != null)
                {
                    _cachedSpawnPosition = playerController.transform.position;
                    _hasCachedSpawnPosition = true;
                    Plugin.Logger?.LogInfo($"[Epoch Drill] Using player position as spawn: {_cachedSpawnPosition}");
                    return _cachedSpawnPosition;
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Drill] Failed to get spawn position: {ex.Message}");
            }

            return Vector3.zero;
        }

        public static bool IsInLandingZone(Vector3 position)
        {
            Vector3 spawnPos = GetSpawnPosition();
            if (spawnPos == Vector3.zero)
                return false;

            float distance = Vector3.Distance(position, spawnPos);
            bool isInZone = distance <= LANDING_ZONE_RADIUS;

            if (isInZone)
            {
                Plugin.Logger?.LogInfo($"[Epoch Drill] Position {position} is in landing zone (distance: {distance:F1}m)");
            }

            return isInZone;
        }

        /// <summary>
        /// Returns true when a Node Extractor belongs to the dedicated Landing Area slot.
        /// The current map does not expose the intended Landing Area through the normal
        /// sector lookup, so the same UnknownBiome -> Landing Area fallback used by
        /// placement is centralized here and reused for save/load restoration.
        /// </summary>
        public static bool IsLandingAreaPosition(Vector3 position)
        {
            if (IsInLandingZone(position))
                return true;

            try
            {
                Type sectorsType = Type.GetType("SpaceCraft.SectorsHandler, Assembly-CSharp");
                if (sectorsType != null)
                {
                    object sectorsHandler = UnityEngine.Object.FindFirstObjectByType(sectorsType);
                    if (sectorsHandler != null)
                    {
                        var sector = sectorsType.GetMethod("GetSectorWithPosition")?.Invoke(
                            sectorsHandler, new object[] { position });

                        if (sector != null)
                        {
                            string sectorGroupId =
                                sector.GetType().GetMethod("GetGroupId")?.Invoke(sector, null) as string;

                            if (!string.IsNullOrEmpty(sectorGroupId) &&
                                sectorGroupId != "UnknownBiome")
                            {
                                return sectorGroupId == "Landing Area";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Drill] Landing Area sector check failed: {ex.Message}");
            }

            // Match the placement code's existing UnknownBiome -> Landing Area rule.
            return true;
        }

        // ============================================================
        // NEW: Refresh registry from world objects on game load
        // ============================================================
        public static void RefreshRegistryFromWorld()
        {
            var constructedObjects = WorldObjectsHandler.Instance?.GetConstructedWorldObjects();
            if (constructedObjects == null)
            {
                Plugin.Logger?.LogWarning("[Epoch Drill] No constructed objects found for registry refresh.");
                return;
            }

            _activeDrillRegistry.Clear();
            _landingZoneDrillId = -1;

            Type sectorsType = Type.GetType("SpaceCraft.SectorsHandler, Assembly-CSharp");
            object sectorsHandler = sectorsType != null ? UnityEngine.Object.FindFirstObjectByType(sectorsType) : null;

            int restoredCount = 0;

            foreach (var wo in constructedObjects)
            {
                if (wo == null || wo.GetGroup() == null || wo.GetGroup().GetId() != "Epoch_Node_Drill")
                    continue;

                Vector3 position = wo.GetPosition();
                int worldObjectId = wo.GetId();

                // ============================================================
                // FIX: Determine biome name - Check Landing Zone FIRST
                // ============================================================
                bool isInLandingZone = IsLandingAreaPosition(position);
                string biomeName;

                if (isInLandingZone)
                {
                    // Landing Zone drill - always use "Landing Area"
                    biomeName = "Landing Area";

                    if (_landingZoneDrillId == -1)
                    {
                        _landingZoneDrillId = worldObjectId;
                        restoredCount++;
                        Plugin.Logger?.LogInfo($"[Epoch Network] Restored Landing Zone drill: {worldObjectId}");
                    }
                    else
                    {
                        // Duplicate landing zone drill - this shouldn't happen, but clean it up
                        Plugin.Logger?.LogWarning($"[Epoch Network] Duplicate Landing Zone drill detected: {worldObjectId}");
                        continue;
                    }
                }
                else
                {
                    // Not in Landing Zone - try to get biome from sector
                    biomeName = "Unknown Area";

                    try
                    {
                        if (sectorsHandler != null)
                        {
                            var sector = sectorsType.GetMethod("GetSectorWithPosition")?.Invoke(sectorsHandler, new object[] { position });
                            if (sector != null)
                            {
                                string sectorGroupId = sector.GetType().GetMethod("GetGroupId")?.Invoke(sector, null) as string;
                                if (!string.IsNullOrEmpty(sectorGroupId) && sectorGroupId != "UnknownBiome")
                                {
                                    biomeName = sectorGroupId;
                                }
                            }
                        }
                    }
                    catch { }

                    // If we still don't have a valid biome name, check if this drill has a cleanup component with stored biome
                    if (biomeName == "Unknown Area")
                    {
                        var gameObject = wo.GetGameObject();
                        if (gameObject != null)
                        {
                            var cleanup = gameObject.GetComponent<EpochDrillCleanup>();
                            if (cleanup != null)
                            {
                                string storedBiome = cleanup.GetBiomeName();
                                if (!string.IsNullOrEmpty(storedBiome) && storedBiome != "Unknown Area")
                                {
                                    biomeName = storedBiome;
                                    Plugin.Logger?.LogInfo($"[Epoch Network] Restored drill in {biomeName} from stored component: {worldObjectId}");
                                }
                            }
                        }
                    }

                    // Register biome drill
                    if (!_activeDrillRegistry.ContainsKey(biomeName))
                    {
                        _activeDrillRegistry[biomeName] = worldObjectId;
                        restoredCount++;
                        Plugin.Logger?.LogInfo($"[Epoch Network] Restored drill in {biomeName}: {worldObjectId}");
                    }
                    else
                    {
                        // Duplicate biome drill - this shouldn't happen, but log it
                        Plugin.Logger?.LogWarning($"[Epoch Network] Duplicate drill detected in biome {biomeName}: {worldObjectId}");
                    }
                }
            }

            Plugin.Logger?.LogInfo($"[Epoch Network] Refreshed registry: restored {restoredCount} drills.");
            RefreshNetworkTelemetry();
        }

        public static bool TryRegisterDrill(string sectorGroupId, int worldObjectId, Vector3 position)
        {
            if (string.IsNullOrEmpty(sectorGroupId))
            {
                if (IsLandingAreaPosition(position))
                {
                    Plugin.Logger?.LogInfo($"[Epoch Network] Drill [{worldObjectId}] placed in Landing Zone.");
                    return TryRegisterLandingZoneDrill(worldObjectId);
                }
                return false;
            }

            if (_activeDrillRegistry.ContainsKey(sectorGroupId))
            {
                Plugin.Logger?.LogWarning($"[Epoch Network] Sector Group [{sectorGroupId}] is occupied.");
                return false;
            }

            _activeDrillRegistry[sectorGroupId] = worldObjectId;
            Plugin.Logger?.LogInfo($"[Epoch Network] Drill [{worldObjectId}] linked to Group [{sectorGroupId}].");

            RefreshNetworkTelemetry();
            return true;
        }

        private static bool TryRegisterLandingZoneDrill(int worldObjectId)
        {
            if (_landingZoneDrillId != -1)
            {
                var existingDrill = WorldObjectsHandler.Instance?.GetWorldObjectViaId(_landingZoneDrillId);
                if (existingDrill != null)
                {
                    Plugin.Logger?.LogWarning($"[Epoch Network] Landing Zone already has a drill.");
                    return false;
                }
                else
                {
                    _landingZoneDrillId = -1;
                }
            }

            _landingZoneDrillId = worldObjectId;
            Plugin.Logger?.LogInfo($"[Epoch Network] Landing Zone Drill [{worldObjectId}] registered.");

            RefreshNetworkTelemetry();
            return true;
        }

        public static void UnregisterDrill(int worldObjectId)
        {
            // Check if this is the landing zone drill
            if (worldObjectId == _landingZoneDrillId)
            {
                _landingZoneDrillId = -1;
                Plugin.Logger?.LogInfo($"[Epoch Network] Landing Zone Drill [{worldObjectId}] unregistered.");
                RefreshNetworkTelemetry();
                return;
            }

            // Check if it's a biome drill
            string targetKey = null;
            foreach (var pair in _activeDrillRegistry)
            {
                if (pair.Value == worldObjectId)
                {
                    targetKey = pair.Key;
                    break;
                }
            }

            if (targetKey != null)
            {
                _activeDrillRegistry.Remove(targetKey);
                Plugin.Logger?.LogInfo($"[Epoch Network] Drill [{worldObjectId}] unregistered from [{targetKey}].");
                RefreshNetworkTelemetry();
                return;
            }

            Plugin.Logger?.LogWarning($"[Epoch Network] Drill [{worldObjectId}] not found in any registry during unregister.");
        }

        public static bool IsBiomeOccupied(string sectorGroupId)
        {
            if (string.IsNullOrEmpty(sectorGroupId)) return false;
            return _activeDrillRegistry.ContainsKey(sectorGroupId);
        }

        public static bool IsLandingZoneOccupied()
        {
            if (_landingZoneDrillId == -1)
                return false;

            var drill = WorldObjectsHandler.Instance?.GetWorldObjectViaId(_landingZoneDrillId);
            if (drill == null)
            {
                _landingZoneDrillId = -1;
                Plugin.Logger?.LogInfo($"[Epoch Network] Landing Zone drill cleaned up (was destroyed without unregister).");
                return false;
            }

            var gameObject = drill.GetGameObject();
            if (gameObject == null)
            {
                _landingZoneDrillId = -1;
                Plugin.Logger?.LogInfo($"[Epoch Network] Landing Zone drill cleaned up (game object was destroyed).");
                return false;
            }

            return true;
        }

        public static int GetActiveDrillsCount()
        {
            int biomeDrills = _activeDrillRegistry.Count;
            int landingDrill = IsLandingZoneOccupied() ? 1 : 0;
            return biomeDrills + landingDrill;
        }

        public static int GetActiveBiomeDrillsCount()
        {
            return _activeDrillRegistry.Count;
        }

        public static bool IsLandingZoneDrill(int worldObjectId)
        {
            return worldObjectId == _landingZoneDrillId;
        }

        public static Dictionary<string, int> GetActiveDrillRegistry()
        {
            return _activeDrillRegistry;
        }

        public static void RefreshNetworkTelemetry()
        {
            if (EpochHud.Instance != null)
            {
                int totalVeins = GetTotalVeinsOnCurrentPlanet();
                EpochHud.Instance.UpdateHud(
                    EpochVacuumSystem.IsInitialized(),
                    EpochVacuumSystem.GetHudObjects(),
                    EpochHubLogistics.GetTotalItemCount()
                );
            }
        }

        public static void ResetNetwork()
        {
            _activeDrillRegistry.Clear();
            _landingZoneDrillId = -1;
            _cachedVeinCount = -1;
            _cachedPlanetHash = 0;
            _hasCachedSpawnPosition = false;
            RefreshNetworkTelemetry();
        }

        internal static void CleanUpOrphanedDrill(int woId)
        {
            string targetKey = null;
            foreach (var pair in _activeDrillRegistry)
            {
                if (pair.Value == woId)
                {
                    targetKey = pair.Key;
                    break;
                }
            }

            if (targetKey != null)
            {
                _activeDrillRegistry.Remove(targetKey);
                Plugin.Logger?.LogInfo($"[Epoch Drill] Orphaned biome drill [{woId}] cleaned up from [{targetKey}].");
                RefreshNetworkTelemetry();
                return;
            }

            if (_landingZoneDrillId == woId)
            {
                _landingZoneDrillId = -1;
                Plugin.Logger?.LogInfo($"[Epoch Drill] Orphaned landing zone drill [{woId}] cleaned up.");
                RefreshNetworkTelemetry();
                return;
            }
        }
    }
}
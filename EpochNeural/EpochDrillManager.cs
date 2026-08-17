using System;
using System.Collections.Generic;
using SpaceCraft;
using UnityEngine;

namespace EpochNeural
{
    /// <summary>
    /// Epoch Node Extractor registry.
    ///
    /// Epoch binds an extractor to the physical MachineGenerationGroupVein found
    /// directly beneath it. Vanilla veins are NOT assumed to expose a
    /// WorldUniqueId at the time Epoch scans them; the log proved that assumption
    /// was false for this runtime.
    ///
    /// Therefore the authoritative Epoch vein identity is a deterministic
    /// physical signature: resource + quantized world position. If vanilla
    /// later provides a WorldUniqueId, it is used only as an optional native link.
    /// </summary>
    public static class EpochDrillManager
    {
        // Registry key = Epoch physical-vein signature.
        // Value = Epoch Node Extractor WorldObject ID.
        private static readonly Dictionary<string, int> _activeDrillRegistry =
            new Dictionary<string, int>();

        // Authoritative resource lock: extractor WorldObject ID -> GroupData.id.
        private static readonly Dictionary<int, string> _drillResourceRegistry =
            new Dictionary<int, string>();

        private static readonly HashSet<int> _knownDrillIds =
            new HashSet<int>();

        private sealed class CachedVein
        {
            public MachineGenerationGroupVein Vein;
            public Vector3 Position;
            public string ResourceId;
            public string Signature;
            public int SignatureHash;
        }

        private static readonly List<CachedVein> _knownVeins =
            new List<CachedVein>();

        private static int _cachedVeinCount = -1;
        private static int _cachedPlanetHash = 0;

        // The ghost is expected to sit on the vein surface/center.
        private const float MAX_VEIN_BIND_DISTANCE = 15f;

        // Position is quantized so tiny floating-point differences do not
        // create a different identity after a save/load cycle.
        private const float SIGNATURE_POSITION_STEP = 0.25f;

        // ============================================================
        // PLANET VEIN COUNT / CACHE
        // ============================================================

        public static int GetTotalVeinsOnCurrentPlanet()
        {
            try
            {
                var planetLoader = Managers.GetManager<PlanetLoader>();
                if (planetLoader == null)
                    return 12;

                var planetData = planetLoader.GetCurrentPlanetData();
                if (planetData == null)
                    return 12;

                int planetHash = planetData.GetPlanetHash();

                if (_cachedPlanetHash == planetHash &&
                    _cachedVeinCount >= 0 &&
                    _knownVeins.Count > 0)
                {
                    return _cachedVeinCount;
                }

                RefreshVeinCache();
                return _cachedVeinCount > 0 ? _cachedVeinCount : 12;
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError(
                    $"[Epoch Drill] Failed to count/cache veins: {ex}");

                return 12;
            }
        }

        private static bool RefreshVeinCache()
        {
            try
            {
                var veinObjects =
                    UnityEngine.Object.FindObjectsByType<MachineGenerationGroupVein>(
                        UnityEngine.FindObjectsSortMode.None);

                _knownVeins.Clear();

                foreach (var vein in veinObjects)
                {
                    if (vein == null || !vein.gameObject.activeInHierarchy)
                        continue;

                    string resource = GetVeinResourceName(vein);
                    Vector3 position = vein.transform.position;
                    string signature = BuildVeinSignature(resource, position);

                    _knownVeins.Add(new CachedVein
                    {
                        Vein = vein,
                        Position = position,
                        ResourceId = resource,
                        Signature = signature,
                        SignatureHash = StableHash(signature)
                    });
                }

                _cachedVeinCount = _knownVeins.Count;

                var planetLoader = Managers.GetManager<PlanetLoader>();
                var planetData = planetLoader?.GetCurrentPlanetData();
                if (planetData != null)
                    _cachedPlanetHash = planetData.GetPlanetHash();

                Plugin.Logger?.LogInfo(
                    $"[Epoch Drill] Cached {_knownVeins.Count} physical ore veins.");

                for (int i = 0; i < _knownVeins.Count; i++)
                {
                    var v = _knownVeins[i];
                    Plugin.Logger?.LogInfo(
                        $"[Epoch Drill] VEIN CACHE #{i + 1}: " +
                        $"key={v.SignatureHash}, resource={v.ResourceId}, " +
                        $"position={v.Position}");
                }

                return _knownVeins.Count > 0;
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError(
                    $"[Epoch Drill] Failed to refresh physical vein cache: {ex}");

                _knownVeins.Clear();
                _cachedVeinCount = 0;
                return false;
            }
        }

        // ============================================================
        // VEIN IDENTITY
        // ============================================================

        private static string BuildVeinSignature(
            string resourceName,
            Vector3 position)
        {
            int x = Mathf.RoundToInt(position.x / SIGNATURE_POSITION_STEP);
            int y = Mathf.RoundToInt(position.y / SIGNATURE_POSITION_STEP);
            int z = Mathf.RoundToInt(position.z / SIGNATURE_POSITION_STEP);

            return $"{resourceName}|{x}|{y}|{z}";
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;

                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }

                int result = (int)(hash & 0x7FFFFFFFu);
                return result == 0 ? 1 : result;
            }
        }

        private static string GetVeinRegistryKeyFromSignature(string signature)
        {
            return $"VEIN:{signature}";
        }

        private static string GetVeinRegistryKeyFromHash(int hash)
        {
            return $"VEINHASH:{hash}";
        }

        public static string GetVeinResourceId(MachineGenerationGroupVein vein)
        {
            try
            {
                if (vein == null)
                    return null;

                var groups = vein.GetGroups();
                if (groups != null && groups.Count > 0 && groups[0] != null &&
                    !string.IsNullOrWhiteSpace(groups[0].id))
                {
                    return groups[0].id;
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Drill] Failed to read physical vein resource group: {ex.Message}");
            }

            return null;
        }

        public static string GetVeinResourceName(MachineGenerationGroupVein vein)
        {
            string id = GetVeinResourceId(vein);
            if (string.IsNullOrWhiteSpace(id))
                return "Unknown Resource";

            string name = id.Replace("_", " ").Replace("-", " ").Trim();
            if (name.Length == 0)
                return "Unknown Resource";

            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        /// <summary>
        /// Returns the deterministic Epoch identity for a physical vein.
        /// This is NOT a vanilla WorldObject ID.
        /// </summary>
        public static int GetVeinWorldObjectId(MachineGenerationGroupVein vein)
        {
            if (vein == null)
                return 0;

            try
            {
                string resourceId = GetVeinResourceId(vein);
                if (string.IsNullOrWhiteSpace(resourceId))
                    return 0;

                string signature = BuildVeinSignature(
                    resourceId,
                    vein.transform.position);

                return StableHash(signature);
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Drill] Failed to build physical vein identity: {ex.Message}");
                return 0;
            }
        }

        // ============================================================
        // VEIN LOOKUP
        // ============================================================

        public static MachineGenerationGroupVein FindVeinUnderPosition(Vector3 position)
        {
            return TryFindVeinAtPosition(
                position,
                out var vein,
                out _,
                out _,
                out _)
                ? vein
                : null;
        }

        public static bool TryFindVeinAtPosition(
            Vector3 position,
            out MachineGenerationGroupVein nearestVein,
            out int veinWorldUniqueId,
            out string resourceName,
            out float distance)
        {
            nearestVein = null;
            veinWorldUniqueId = 0;
            resourceName = null;
            distance = float.MaxValue;

            try
            {
                if (_knownVeins.Count == 0)
                {
                    if (!RefreshVeinCache())
                    {
                        Plugin.Logger?.LogWarning(
                            $"[Epoch Drill] No physical ore veins available near {position}.");
                        return false;
                    }
                }

                CachedVein best = null;
                float bestDistance = float.MaxValue;

                foreach (var cached in _knownVeins)
                {
                    if (cached == null || cached.Vein == null)
                        continue;

                    // Keep cache positions current in case the scene moves them.
                    cached.Position = cached.Vein.transform.position;

                    float dx = position.x - cached.Position.x;
                    float dz = position.z - cached.Position.z;
                    float horizontalDistance =
                        Mathf.Sqrt((dx * dx) + (dz * dz));

                    if (horizontalDistance > MAX_VEIN_BIND_DISTANCE)
                        continue;

                    float d = Vector3.Distance(position, cached.Position);

                    if (d < bestDistance)
                    {
                        best = cached;
                        bestDistance = d;
                    }
                }

                if (best == null)
                {
                    Plugin.Logger?.LogWarning(
                        $"[Epoch Drill] No physical vein within " +
                        $"{MAX_VEIN_BIND_DISTANCE:F1}m of {position}.");
                    return false;
                }

                nearestVein = best.Vein;
                veinWorldUniqueId = best.SignatureHash;
                resourceName = best.ResourceId;
                distance = bestDistance;

                Plugin.Logger?.LogInfo(
                    $"[Epoch Drill] Vein candidate found: " +
                    $"key={veinWorldUniqueId}, resource={resourceName}, " +
                    $"veinPosition={best.Position}, drillPosition={position}, " +
                    $"distance={distance:F2}m.");

                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError(
                    $"[Epoch Drill] Vein detection failed: {ex}");

                return false;
            }
        }

        public static bool IsVeinOccupied(int veinWorldObjectId)
        {
            if (veinWorldObjectId <= 0)
                return false;

            string key = GetVeinRegistryKeyFromHash(veinWorldObjectId);

            if (!_activeDrillRegistry.TryGetValue(key, out int drillId))
                return false;

            var drill = WorldObjectsHandler.Instance?.GetWorldObjectViaId(drillId);

            if (drill != null)
                return true;

            _activeDrillRegistry.Remove(key);
            return false;
        }

        // ============================================================
        // SAVE / WORLD RESTORATION
        // ============================================================

        public static void RefreshRegistryFromWorld()
        {
            var constructedObjects =
                WorldObjectsHandler.Instance?.GetConstructedWorldObjects();

            if (constructedObjects == null)
            {
                Plugin.Logger?.LogWarning(
                    "[Epoch Drill] No constructed objects found for registry refresh.");
                return;
            }

            // Make sure the physical vein cache exists before restoring drills.
            if (_knownVeins.Count == 0)
                RefreshVeinCache();

            _activeDrillRegistry.Clear();
            _drillResourceRegistry.Clear();
            _knownDrillIds.Clear();

            int restoredCount = 0;

            foreach (var wo in constructedObjects)
            {
                if (wo == null ||
                    wo.GetGroup() == null ||
                    wo.GetGroup().GetId() != "Epoch_Node_Drill")
                {
                    continue;
                }

                int drillId = wo.GetId();
                _knownDrillIds.Add(drillId);

                Vector3 position = wo.GetPosition();

                if (!TryFindVeinAtPosition(
                        position,
                        out var vein,
                        out int veinKey,
                        out string resourceName,
                        out float distance))
                {
                    Plugin.Logger?.LogWarning(
                        $"[Epoch Drill] Existing drill [{drillId}] could not be " +
                        $"matched to a physical vein and will remain unbound.");
                    continue;
                }

                if (IsVeinOccupied(veinKey))
                {
                    Plugin.Logger?.LogWarning(
                        $"[Epoch Drill] Existing drill [{drillId}] maps to an " +
                        $"already occupied vein key [{veinKey}] and will remain unbound.");
                    continue;
                }

                _activeDrillRegistry[GetVeinRegistryKeyFromHash(veinKey)] = drillId;
                _drillResourceRegistry[drillId] = resourceName;
                ApplyLockedResourceToWorldObject(wo, resourceName);

                restoredCount++;

                Plugin.Logger?.LogInfo(
                    $"[Epoch Drill] Restored drill [{drillId}] -> " +
                    $"physical vein [{veinKey}] -> {resourceName} " +
                    $"(distance {distance:F2}m).");
            }

            Plugin.Logger?.LogInfo(
                $"[Epoch Drill] Vein registry refreshed: " +
                $"restored={restoredCount}, active={_activeDrillRegistry.Count}/" +
                $"{GetTotalVeinsOnCurrentPlanet()}.");

            RefreshNetworkTelemetry();
        }

        // ============================================================
        // REGISTRATION
        // ============================================================

        public static bool TryRegisterDrill(
            string sectorGroupId,
            int worldObjectId,
            Vector3 position)
        {
            if (worldObjectId <= 0)
                return false;

            if (!TryFindVeinAtPosition(
                    position,
                    out var vein,
                    out int veinKey,
                    out string resourceName,
                    out float distance))
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Drill] Drill [{worldObjectId}] could not be bound to a physical vein.");
                return false;
            }

            int totalVeins = GetTotalVeinsOnCurrentPlanet();

            if (_activeDrillRegistry.Count >= totalVeins)
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Drill] Maximum Node Extractors reached: " +
                    $"{_activeDrillRegistry.Count}/{totalVeins}.");
                return false;
            }

            if (IsVeinOccupied(veinKey))
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Drill] Physical vein [{veinKey}] ({resourceName}) " +
                    $"already has a Node Extractor.");
                return false;
            }

            _activeDrillRegistry[GetVeinRegistryKeyFromHash(veinKey)] =
                worldObjectId;
            _drillResourceRegistry[worldObjectId] = resourceName;

            var drillWorldObject =
                WorldObjectsHandler.Instance?.GetWorldObjectViaId(worldObjectId);
            ApplyLockedResourceToWorldObject(drillWorldObject, resourceName);

            _knownDrillIds.Add(worldObjectId);

            Plugin.Logger?.LogInfo(
                $"[Epoch Drill] LOCKED extractor [{worldObjectId}] -> " +
                $"physical vein [{veinKey}] -> {resourceName} " +
                $"(distance {distance:F2}m).");

            RefreshNetworkTelemetry();
            return true;
        }

        public static string GetBoundResourceId(int worldObjectId)
        {
            if (_drillResourceRegistry.TryGetValue(worldObjectId, out string resourceId))
                return resourceId;

            // Save/load safety: rebuild this one binding from the physical vein
            // if the in-memory map has not been populated yet.
            var wo = WorldObjectsHandler.Instance?.GetWorldObjectViaId(worldObjectId);
            if (wo != null && wo.GetGroup()?.GetId() == "Epoch_Node_Drill")
            {
                if (TryFindVeinAtPosition(
                        wo.GetPosition(),
                        out var vein,
                        out _,
                        out string restoredResource,
                        out _))
                {
                    _drillResourceRegistry[worldObjectId] = restoredResource;
                    ApplyLockedResourceToWorldObject(wo, restoredResource);
                    return restoredResource;
                }
            }

            return null;
        }

        public static string GetBoundResourceName(int worldObjectId)
        {
            string id = GetBoundResourceId(worldObjectId);
            if (string.IsNullOrWhiteSpace(id))
                return "Unknown Resource";

            string name = id.Replace("_", " ").Replace("-", " ").Trim();
            return name.Length == 0
                ? "Unknown Resource"
                : char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        private static void ApplyLockedResourceToWorldObject(
            WorldObject worldObject,
            string resourceId)
        {
            if (worldObject == null || string.IsNullOrWhiteSpace(resourceId))
                return;

            try
            {
                Group resourceGroup = GroupsHandler.GetGroupViaId(resourceId);
                if (resourceGroup != null)
                {
                    // Vanilla extractor UI reads this linked-group list. By
                    // reducing it to one group, the Epoch extractor has one
                    // authoritative resource: the vein's resource.
                    worldObject.SetLinkedGroups(new List<Group> { resourceGroup });

                    // --------------------------------------------------------
                    // LOCK THE VANILLA ORE SELECTOR
                    // --------------------------------------------------------
                    // The T3 extractor template carries ActionGroupSelector,
                    // which normally exposes Aluminium/Iron/etc. plus the
                    // other vanilla mining groups. Epoch must never allow that
                    // selector to change the already-bound vein resource.
                    var drillObject = worldObject.GetGameObject();
                    if (drillObject != null)
                    {
                        var selector = drillObject.GetComponentInChildren<ActionGroupSelector>(true);
                        if (selector != null)
                        {
                            if (selector.oreList != null)
                            {
                                selector.oreList.Clear();
                                GroupData resourceData = resourceGroup.GetGroupData();
                                if (resourceData != null)
                                    selector.oreList.Add(resourceData);
                            }

                            // Disable the vanilla selector action itself.
                            // EpochDrillUiLockPatch also hard-blocks OnAction,
                            // so this remains safe if Unity recreates/re-enables
                            // the component later.
                            selector.enabled = false;
                        }

                        // ----------------------------------------------------
                        // SHOW THE LOCKED RESOURCE IN THE INVENTORY UI
                        // ----------------------------------------------------
                        // ActionOpenable feeds groupLoading into the standard
                        // container UI. Setting it to the bound resource makes
                        // the extractor's inventory/header display the actual
                        // resource icon instead of the generic extractor icon.
                        var actionOpenable =
                            drillObject.GetComponentInChildren<ActionOpenable>(true);

                        if (actionOpenable != null)
                        {
                            GroupData resourceData = resourceGroup.GetGroupData();
                            if (resourceData != null)
                            {
                                actionOpenable.SetGroupLoading(resourceData);
                                actionOpenable.showEmptyLoading = false;
                            }
                        }

                        Plugin.Logger?.LogInfo(
                            $"[Epoch Drill UI] Locked UI to resource [{resourceId}] " +
                            $"for extractor [{worldObject.GetId()}].");
                    }
                }
                else
                {
                    Plugin.Logger?.LogWarning(
                        $"[Epoch Drill] Could not resolve bound resource group [{resourceId}] for extractor [{worldObject.GetId()}].");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Drill] Failed to apply locked resource [{resourceId}] to extractor [{worldObject.GetId()}]: {ex.Message}");
            }
        }

        public static bool IsKnownDrill(int worldObjectId)
        {
            return _knownDrillIds.Contains(worldObjectId);
        }

        // ============================================================
        // UNREGISTER / DESTRUCTION
        // ============================================================

        public static void UnregisterDrill(int worldObjectId)
        {
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

                Plugin.Logger?.LogInfo(
                    $"[Epoch Drill] Extractor [{worldObjectId}] " +
                    $"unregistered from [{targetKey}].");
            }

            _knownDrillIds.Remove(worldObjectId);
            _drillResourceRegistry.Remove(worldObjectId);
            RefreshNetworkTelemetry();
        }

        internal static void CleanUpOrphanedDrill(int worldObjectId)
        {
            UnregisterDrill(worldObjectId);
        }

        // ============================================================
        // LEGACY LANDING API
        // ============================================================

        public static bool IsBiomeOccupied(string sectorGroupId)
        {
            return false;
        }

        public static bool IsLandingZoneOccupied()
        {
            return false;
        }

        public static bool IsLandingZoneDrill(int worldObjectId)
        {
            return false;
        }

        public static bool IsLandingAreaPosition(Vector3 position)
        {
            return false;
        }

        public static bool IsInLandingZone(Vector3 position)
        {
            return false;
        }

        public static Vector3 GetSpawnPosition()
        {
            return Vector3.zero;
        }

        // ============================================================
        // TELEMETRY / HUD
        // ============================================================

        public static int GetActiveDrillsCount()
        {
            return _activeDrillRegistry.Count;
        }

        public static int GetActiveBiomeDrillsCount()
        {
            return _activeDrillRegistry.Count;
        }

        public static Dictionary<string, int> GetActiveDrillRegistry()
        {
            return _activeDrillRegistry;
        }

        public static void RefreshNetworkTelemetry()
        {
            if (EpochHud.Instance != null)
            {
                EpochHud.Instance.UpdateHud(
                    EpochVacuumSystem.IsInitialized(),
                    EpochVacuumSystem.GetHudObjects(),
                    EpochHubLogistics.GetTotalItemCount());
            }
        }

        public static void ResetNetwork()
        {
            _activeDrillRegistry.Clear();
            _drillResourceRegistry.Clear();
            _knownDrillIds.Clear();
            _knownVeins.Clear();

            _cachedVeinCount = -1;
            _cachedPlanetHash = 0;

            RefreshNetworkTelemetry();
        }
    }
}
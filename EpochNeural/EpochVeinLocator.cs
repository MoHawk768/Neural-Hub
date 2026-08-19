using System;
using System.Collections;
using System.Collections.Generic;
using SpaceCraft;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EpochNeural
{
    /// <summary>
    /// Visual diagnostic locator for Epoch Node Extractor placement.
    ///
    /// Continuously watches for the nearest physical
    /// MachineGenerationGroupVein around the player.
    ///
    /// When the player is within DISPLAY_RANGE metres horizontally,
    /// a world-direction marker appears on the normal player HUD even if
    /// the Epoch Node Extractor is not selected or being held.
    ///
    /// Diagnostic only:
    /// - Does NOT change placement rules.
    /// - Does NOT change the 15m binding distance.
    /// - Does NOT register or destroy extractors.
    /// - Does NOT modify the drill manager.
    /// - Uses horizontal X/Z distance, matching the vein-placement diagnostic.
    /// </summary>
    internal sealed class EpochVeinLocator : MonoBehaviour
    {
        private const string DRILL_GROUP_ID = "Epoch_Node_Drill";
        private const float SCAN_INTERVAL = 0.20f;
        private const float DISPLAY_RANGE = 50f;

        private static EpochVeinLocator _instance;

        private LocatorOverlay _overlay;
        private float _nextScanTime;

        private MachineGenerationGroupVein _nearestVein;
        private Vector3 _nearestVeinPosition;
        private float _nearestDistance = float.MaxValue;
        private string _nearestResource = "VEIN";

        private Vector3 _playerPosition;
        private Vector3 _buildSitePosition;
        private bool _hasBuildSite;
        private bool _active;
        private bool _siteScanRunning;
        private float _nextSiteScanTime;

        // Build-site discovery is expensive because each candidate is checked
        // through vanilla's real placement validator. Cache the result for
        // each physical vein so proximity updates do not repeatedly rescan
        // the same vein every second.
        private readonly Dictionary<int, BuildSiteCacheEntry> _buildSiteCache =
            new Dictionary<int, BuildSiteCacheEntry>();

        private int _buildSiteVeinInstanceId = 0;

        private sealed class BuildSiteCacheEntry
        {
            internal bool HasValidSite;
            internal Vector3 SitePosition;
            internal MachineGenerationGroupVein Vein;
        }

        // ------------------------------------------------------------
        // PERSISTENT LOCATOR LIFECYCLE
        // ------------------------------------------------------------

        public static void StartLocator(GameObject host)
        {
            if (_instance != null)
                return;

            if (host == null)
                return;

            _instance = host.GetComponent<EpochVeinLocator>();

            if (_instance == null)
                _instance = host.AddComponent<EpochVeinLocator>();

            Plugin.Logger?.LogInfo(
                "[Epoch Vein Locator] Persistent locator attached.");
        }

        private void OnDestroy()
        {
            if (_overlay != null)
            {
                Destroy(_overlay.gameObject);
                _overlay = null;
            }

            if (_instance == this)
                _instance = null;
        }

        // ------------------------------------------------------------
        // CONTINUOUS PLAYER PROXIMITY UPDATE
        // ------------------------------------------------------------

        private void Update()
        {
            try
            {
                var player =
                    Managers.GetManager<PlayersManager>()?
                        .GetActivePlayerController();

                if (player == null || !player.gameObject.activeInHierarchy)
                {
                    Deactivate();
                    return;
                }

                if (Camera.main == null)
                {
                    Deactivate();
                    return;
                }

                _playerPosition = player.transform.position;

                if (Time.time >= _nextScanTime)
                {
                    _nextScanTime = Time.time + SCAN_INTERVAL;
                    ScanNearestVein(_playerPosition);
                }

                if (_nearestVein == null ||
                    _nearestDistance > DISPLAY_RANGE)
                {
                    Deactivate();
                    return;
                }

                _active = true;

                EnsureOverlay();
                _overlay.SetVisible(true);
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Vein Locator] Update error: {ex.Message}");

                Deactivate();
            }
        }

        // ------------------------------------------------------------
        // FIND NEAREST PHYSICAL VEIN
        // ------------------------------------------------------------

        private void ScanNearestVein(Vector3 position)
        {
            try
            {
                var veins =
                    UnityEngine.Object.FindObjectsByType<MachineGenerationGroupVein>(
                        FindObjectsSortMode.None);

                MachineGenerationGroupVein best = null;
                float bestDistance = float.MaxValue;

                foreach (var vein in veins)
                {
                    if (vein == null ||
                        !vein.gameObject.activeInHierarchy)
                        continue;

                    Vector3 veinPosition = vein.transform.position;

                    // Ignore Y. The vanilla vein object can be far below
                    // the playable surface.
                    float dx = position.x - veinPosition.x;
                    float dz = position.z - veinPosition.z;

                    float horizontalDistance =
                        Mathf.Sqrt((dx * dx) + (dz * dz));

                    if (horizontalDistance < bestDistance)
                    {
                        bestDistance = horizontalDistance;
                        best = vein;
                    }
                }

                _nearestVein = best;
                _nearestDistance = bestDistance;

                if (best == null)
                {
                    _nearestResource = "NO VEIN";
                    return;
                }

                _nearestVeinPosition = best.transform.position;
                _nearestResource = GetFriendlyResourceName(best);

                int veinInstanceId = best.GetInstanceID();

                // Reuse a previously discovered result immediately. This is
                // especially important while the player remains near a vein:
                // the HUD scans frequently, but vanilla placement probing must
                // happen at most once per vein per locator lifetime.
                if (_buildSiteVeinInstanceId != veinInstanceId)
                {
                    _buildSiteVeinInstanceId = veinInstanceId;
                    _hasBuildSite = false;
                }

                if (_buildSiteCache.TryGetValue(
                        veinInstanceId,
                        out BuildSiteCacheEntry cached))
                {
                    _hasBuildSite = cached.HasValidSite;
                    _buildSitePosition = cached.SitePosition;
                }
                else if (!_siteScanRunning &&
                         Time.time >= _nextSiteScanTime)
                {
                    _nextSiteScanTime = Time.time + 1.0f;
                    StartCoroutine(FindNearestValidBuildSite(best));
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Vein Locator] Scan error: {ex.Message}");

                _nearestVein = null;
                _nearestDistance = float.MaxValue;
            }
        }

        // ------------------------------------------------------------
        // FIND A REAL, VANILLA-BUILDABLE SURFACE SITE
        // ------------------------------------------------------------

        private IEnumerator FindNearestValidBuildSite(
            MachineGenerationGroupVein vein)
        {
            if (vein == null || _siteScanRunning)
                yield break;

            _siteScanRunning = true;

            int veinInstanceId = vein.GetInstanceID();

            if (!_buildSiteCache.TryGetValue(
                    veinInstanceId,
                    out BuildSiteCacheEntry _))
            {
                _hasBuildSite = false;
            }

            Vector3 veinPos = vein.transform.position;

            try
            {
                // Search a bounded area around the physical vein, but choose
                // the VALID site that is most useful to the player rather than
                // automatically accepting the first valid point directly above
                // the vein. This matters for buried/obstructed veins such as
                // Obsidian, where the first valid surface can be on top of a
                // boulder while a usable surface exists farther away.
                const float MAX_SEARCH_RADIUS = 100f;
                const float RING_STEP = 5f;

                Vector3 playerPosition = Vector3.zero;
                bool havePlayerPosition = false;

                try
                {
                    var player = UnityEngine.Object.FindFirstObjectByType<PlayerMainController>();
                    if (player != null)
                    {
                        playerPosition = player.transform.position;
                        havePlayerPosition = true;
                    }
                }
                catch
                {
                    // Fall back to the original ring-order behaviour if the
                    // player controller cannot be resolved.
                }

                var candidates = new List<Vector3>();

                // Always include the vein center.
                candidates.Add(new Vector3(veinPos.x, 0f, veinPos.z));

                // Build concentric rings. We retain every candidate because
                // selection happens AFTER vanilla validation; this lets us
                // choose the best valid surface instead of the first valid one.
                for (float radius = RING_STEP;
                     radius <= MAX_SEARCH_RADIUS;
                     radius += RING_STEP)
                {
                    int samples = Mathf.Max(
                        12,
                        Mathf.CeilToInt(2f * Mathf.PI * radius / RING_STEP));

                    for (int i = 0; i < samples; i++)
                    {
                        float angle = (Mathf.PI * 2f * i) / samples;

                        candidates.Add(
                            new Vector3(
                                veinPos.x + Mathf.Cos(angle) * radius,
                                0f,
                                veinPos.z + Mathf.Sin(angle) * radius));
                    }
                }

                bool found = false;
                Vector3 bestSurface = Vector3.zero;
                float bestScore = float.MaxValue;
                float bestVeinDistance = float.MaxValue;
                int tested = 0;
                int validCount = 0;

                foreach (var candidateXZ in candidates)
                {
                    if (!TryGetSurfacePoint(
                        candidateXZ.x,
                        candidateXZ.z,
                        out Vector3 surface))
                    {
                        continue;
                    }

                    float veinDistance =
                        HorizontalDistance(surface, veinPos);

                    if (veinDistance > MAX_SEARCH_RADIUS)
                        continue;

                    tested++;

                    bool valid = false;

                    yield return StartCoroutine(
                        ProbeVanillaPlacement(
                            surface,
                            result => valid = result));

                    if (!valid)
                        continue;

                    validCount++;

                    // If the player position is available, favour the valid
                    // surface nearest to the player, while still requiring it
                    // to remain within the bounded vein search radius.
                    //
                    // A small vein-distance component prevents a very distant
                    // valid point from winning when several player-side sites
                    // are available.
                    float playerDistance = havePlayerPosition
                        ? HorizontalDistance(surface, playerPosition)
                        : veinDistance;

                    float score =
                        playerDistance +
                        (veinDistance * 0.10f);

                    if (!found || score < bestScore)
                    {
                        found = true;
                        bestScore = score;
                        bestSurface = surface;
                        bestVeinDistance = veinDistance;
                    }
                }

                if (found)
                {
                    _buildSiteCache[veinInstanceId] =
                        new BuildSiteCacheEntry
                        {
                            HasValidSite = true,
                            SitePosition = bestSurface,
                            Vein = vein
                        };

                    if (_nearestVein == vein)
                    {
                        _buildSitePosition = bestSurface;
                        _hasBuildSite = true;
                    }

                    Plugin.Logger?.LogInfo(
                        $"[Epoch Vein Locator] Build site found for " +
                        $"{_nearestResource}: vein={veinPos}, " +
                        $"site={bestSurface}, " +
                        $"horizontal={bestVeinDistance:F1}m, " +
                        $"validCandidates={validCount}, tested={tested}. " +
                        $"[PLAYER-GUIDED CACHED]");

                    yield break;
                }

                _buildSiteCache[veinInstanceId] =
                    new BuildSiteCacheEntry
                    {
                        HasValidSite = false,
                        SitePosition = Vector3.zero,
                        Vein = vein
                    };

                if (_nearestVein == vein)
                    _hasBuildSite = false;

                Plugin.Logger?.LogWarning(
                    $"[Epoch Vein Locator] No vanilla-valid build site found " +
                    $"within {MAX_SEARCH_RADIUS:F0}m of " +
                    $"{_nearestResource} vein at {veinPos}. " +
                    $"tested={tested}, valid={validCount}. " +
                    $"[CACHED AS INVALID]");
            }
            finally
            {
                _siteScanRunning = false;
            }
        }

        /// <summary>
        /// Returns the nearest cached build site that was already validated by
        /// vanilla placement rules. This is a lookup only; it never grants
        /// placement permission on its own.
        /// </summary>
        internal static bool TryGetClosestCachedBuildSite(
            Vector3 position,
            out MachineGenerationGroupVein vein,
            out Vector3 sitePosition)
        {
            vein = null;
            sitePosition = Vector3.zero;

            if (_instance == null)
                return false;

            const float HORIZONTAL_TOLERANCE = 6f;
            const float VERTICAL_TOLERANCE = 4f;

            float best = float.MaxValue;

            foreach (var pair in _instance._buildSiteCache)
            {
                BuildSiteCacheEntry entry = pair.Value;

                if (entry == null ||
                    !entry.HasValidSite ||
                    entry.Vein == null)
                    continue;

                float dx = entry.SitePosition.x - position.x;
                float dz = entry.SitePosition.z - position.z;
                float horizontal = Mathf.Sqrt(dx * dx + dz * dz);

                if (horizontal > HORIZONTAL_TOLERANCE)
                    continue;

                if (Mathf.Abs(entry.SitePosition.y - position.y) >
                    VERTICAL_TOLERANCE)
                    continue;

                if (horizontal >= best)
                    continue;

                best = horizontal;
                vein = entry.Vein;
                sitePosition = entry.SitePosition;
            }

            return vein != null;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private IEnumerator ProbeVanillaPlacement(
            Vector3 position,
            Action<bool> result)
        {
            result?.Invoke(false);

            GameObject probe = null;

            try
            {
                Group drillGroup = null;

                foreach (var group in GroupsHandler.GetAllGroups())
                {
                    if (group != null && group.GetId() == DRILL_GROUP_ID)
                    {
                        drillGroup = group;
                        break;
                    }
                }

                if (drillGroup == null)
                    yield break;

                var player =
                    Managers.GetManager<PlayersManager>()?
                        .GetActivePlayerController();

                if (player == null)
                    yield break;

                var aim = player.GetComponent<PlayerAimController>();

                if (aim == null)
                    yield break;

                GameObject prefab = drillGroup.GetAssociatedGameObject();

                if (prefab == null)
                    yield break;

                // IMPORTANT:
                // This object exists only to ask vanilla whether a position is
                // legal. It must NEVER look like a real construction ghost.
                probe = Instantiate(prefab);
                probe.name = "EpochDrillPlacementProbe";
                probe.transform.position = position;

                // Hide every visual representation on the temporary probe.
                // Do not disable the GameObject itself because the vanilla
                // GhostPlacementChecker needs to run its normal lifecycle.
                foreach (var renderer in
                    probe.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer != null)
                        renderer.enabled = false;
                }

                foreach (var canvas in
                    probe.GetComponentsInChildren<Canvas>(true))
                {
                    if (canvas != null)
                        canvas.enabled = false;
                }

                // Do not reference UnityEngine.Collider here: the mod project
                // intentionally does not reference PhysicsModule directly.
                // The probe is temporary and has all renderers disabled, while
                // vanilla placement validation remains responsible for the
                // actual placement test.

                // Reuse a ConstructibleGhost if the Epoch drill prefab already
                // contains one. Never add a second ghost component.
                var ghost = probe.GetComponent<ConstructibleGhost>();

                if (ghost == null)
                    ghost = probe.AddComponent<ConstructibleGhost>();

                ghost.InitGhost(drillGroup, aim, null, null);

                // A background probe cannot literally be aimed at by the
                // player, so remove only its aim constraint. All other vanilla
                // placement constraints remain active.
                var aimConstraints =
                    probe.GetComponentsInChildren<ConstraintOnAim>(true);

                foreach (var aimConstraint in aimConstraints)
                {
                    if (aimConstraint != null)
                        Destroy(aimConstraint);
                }

                var checker =
                    probe.GetComponent<GhostPlacementChecker>();

                // GhostPlacementChecker builds its constraint list in Start()
                // and refreshes it periodically.
                yield return new WaitForSeconds(0.06f);

                if (checker != null)
                {
                    bool valid = checker.GetPositioningStatus();

                    if (Plugin.DebugLogging)
                    {
                        Plugin.Logger?.LogInfo(
                            $"[Epoch Vein Locator] Vanilla placement probe at " +
                            $"{position} -> {(valid ? "VALID" : "INVALID")}.");
                    }

                    result?.Invoke(valid);
                }
            }
            finally
            {
                // Critical: every probe is destroyed regardless of whether
                // initialization succeeds, the coroutine yields, or an
                // exception occurs. No construction ghost may survive.
                if (probe != null)
                    Destroy(probe);
            }
        }

        // ------------------------------------------------------------
        // RUNTIME PHYSICS SURFACE QUERY
        //
        // This uses Unity's PhysicsModule through reflection so the mod does
        // not need a direct UnityEngine.PhysicsModule assembly reference.
        // ------------------------------------------------------------

        private static bool TryGetSurfacePoint(
            float x,
            float z,
            out Vector3 surface)
        {
            surface = Vector3.zero;

            try
            {
                Type physicsType =
                    Type.GetType("UnityEngine.Physics, UnityEngine.PhysicsModule");

                Type rayType =
                    Type.GetType("UnityEngine.Ray, UnityEngine.CoreModule") ??
                    Type.GetType("UnityEngine.Ray, UnityEngine.PhysicsModule");

                Type hitType =
                    Type.GetType("UnityEngine.RaycastHit, UnityEngine.PhysicsModule");

                if (physicsType == null ||
                    rayType == null ||
                    hitType == null)
                    return false;

                object ray = Activator.CreateInstance(
                    rayType,
                    new object[]
                    {
                        new Vector3(x, 1000f, z),
                        Vector3.down
                    });

                object hit = Activator.CreateInstance(hitType);

                Type queryType = Type.GetType(
                    "UnityEngine.QueryTriggerInteraction, UnityEngine.PhysicsModule");

                if (queryType == null)
                    return false;

                object queryIgnore =
                    Enum.Parse(queryType, "Ignore");

                var methods = physicsType.GetMethods(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static);

                foreach (var method in methods)
                {
                    if (method.Name != "Raycast")
                        continue;

                    var parameters = method.GetParameters();

                    if (parameters.Length != 5)
                        continue;

                    if (parameters[0].ParameterType != rayType)
                        continue;

                    if (parameters[1].ParameterType != hitType.MakeByRefType())
                        continue;

                    if (parameters[2].ParameterType != typeof(float))
                        continue;

                    if (parameters[3].ParameterType != typeof(int))
                        continue;

                    if (parameters[4].ParameterType != queryType)
                        continue;

                    object[] args =
                    {
                        ray,
                        hit,
                        2000f,
                        ~0,
                        queryIgnore
                    };

                    bool didHit =
                        (bool)method.Invoke(null, args);

                    if (!didHit)
                        continue;

                    var pointProperty =
                        hitType.GetProperty("point");

                    if (pointProperty == null)
                        return false;

                    surface =
                        (Vector3)pointProperty.GetValue(args[1]);

                    return true;
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Vein Locator] Surface query failed: {ex.Message}");
            }

            return false;
        }

        // ------------------------------------------------------------
        // PLAYER-FACING RESOURCE NAME
        // ------------------------------------------------------------

        private static string GetFriendlyResourceName(
            MachineGenerationGroupVein vein)
        {
            if (vein == null)
                return "ORE VEIN";

            try
            {
                // MachineGenerationGroupVein already carries the actual
                // GroupData list used by the vanilla game. Its first group
                // supplies the resource group ID. This is preferable to
                // displaying OreVeinIdentifer.ToString(), which produces
                // internal values such as "tier1" / "tier2".
                var groups = vein.GetGroups();

                if (groups != null && groups.Count > 0)
                {
                    var groupData = groups[0];

                    if (groupData != null &&
                        !string.IsNullOrWhiteSpace(groupData.id))
                    {
                        return FormatResourceName(groupData.id);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Vein Locator] Could not read vein resource group: {ex.Message}");
            }

            return "ORE VEIN";
        }

        private static string FormatResourceName(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
                return "ORE VEIN";

            string name = groupId
                .Replace("_", " ")
                .Replace("-", " ")
                .Trim();

            if (name.Length == 0)
                return "ORE VEIN";

            // Keep normal resource names such as Aluminium, Iridium,
            // Silicon, etc. readable without exposing internal tier names.
            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        // ------------------------------------------------------------
        // OVERLAY
        // ------------------------------------------------------------

        private void EnsureOverlay()
        {
            if (_overlay != null)
                return;

            var go = new GameObject("EpochVeinLocatorOverlay");
            _overlay = go.AddComponent<LocatorOverlay>();
            _overlay.Build(this);
        }

        private void Deactivate()
        {
            _active = false;
            _nearestVein = null;
            _nearestDistance = float.MaxValue;
            _hasBuildSite = false;
            _buildSiteVeinInstanceId = 0;

            // Deliberately keep _buildSiteCache. Leaving the player's
            // proximity must not cause an expensive vanilla placement scan
            // to run again when they return to the same vein.

            if (_overlay != null)
                _overlay.SetVisible(false);
        }

        // ------------------------------------------------------------
        // OVERLAY MONOBEHAVIOUR
        // ------------------------------------------------------------

        private sealed class LocatorOverlay : MonoBehaviour
        {
            private Canvas _canvas;
            private RectTransform _root;
            private Image _background;
            private TextMeshProUGUI _arrow;
            private TextMeshProUGUI _resource;
            private TextMeshProUGUI _distance;
            private TextMeshProUGUI _hint;

            private bool _built;
            private EpochVeinLocator _owner;

            internal void Build(EpochVeinLocator owner)
            {
                _owner = owner;

                if (_built)
                    return;

                _built = true;

                _canvas = gameObject.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 5000;

                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode =
                    CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                gameObject.AddComponent<GraphicRaycaster>();

                var rootObject = new GameObject(
                    "EpochVeinLocatorMarker",
                    typeof(RectTransform));

                rootObject.transform.SetParent(
                    _canvas.transform,
                    false);

                _root = rootObject.GetComponent<RectTransform>();
                _root.sizeDelta = new Vector2(300f, 145f);

                _background = rootObject.AddComponent<Image>();
                _background.raycastTarget = false;
                _background.color = new Color(
                    0.02f,
                    0.02f,
                    0.02f,
                    0.82f);

                _arrow = CreateText(
                    "Arrow",
                    52,
                    FontStyles.Bold);

                _resource = CreateText(
                    "Resource",
                    27,
                    FontStyles.Bold);

                _distance = CreateText(
                    "Distance",
                    25,
                    FontStyles.Bold);

                _hint = CreateText(
                    "Hint",
                    17,
                    FontStyles.Normal);

                _arrow.rectTransform.anchorMin =
                    new Vector2(0.5f, 1f);
                _arrow.rectTransform.anchorMax =
                    new Vector2(0.5f, 1f);
                _arrow.rectTransform.pivot =
                    new Vector2(0.5f, 1f);
                _arrow.rectTransform.anchoredPosition =
                    new Vector2(0f, -4f);
                _arrow.rectTransform.sizeDelta =
                    new Vector2(290f, 55f);

                _resource.rectTransform.anchorMin =
                    new Vector2(0.5f, 1f);
                _resource.rectTransform.anchorMax =
                    new Vector2(0.5f, 1f);
                _resource.rectTransform.pivot =
                    new Vector2(0.5f, 1f);
                _resource.rectTransform.anchoredPosition =
                    new Vector2(0f, -53f);
                _resource.rectTransform.sizeDelta =
                    new Vector2(290f, 32f);

                _distance.rectTransform.anchorMin =
                    new Vector2(0.5f, 1f);
                _distance.rectTransform.anchorMax =
                    new Vector2(0.5f, 1f);
                _distance.rectTransform.pivot =
                    new Vector2(0.5f, 1f);
                _distance.rectTransform.anchoredPosition =
                    new Vector2(0f, -84f);
                _distance.rectTransform.sizeDelta =
                    new Vector2(290f, 30f);

                _hint.rectTransform.anchorMin =
                    new Vector2(0.5f, 0f);
                _hint.rectTransform.anchorMax =
                    new Vector2(0.5f, 0f);
                _hint.rectTransform.pivot =
                    new Vector2(0.5f, 0f);
                _hint.rectTransform.anchoredPosition =
                    new Vector2(0f, 7f);
                _hint.rectTransform.sizeDelta =
                    new Vector2(290f, 25f);

                SetVisible(false);
            }

            private TextMeshProUGUI CreateText(
                string name,
                float fontSize,
                FontStyles style)
            {
                var go = new GameObject(
                    name,
                    typeof(RectTransform));

                go.transform.SetParent(
                    _root,
                    false);

                var text = go.AddComponent<TextMeshProUGUI>();

                if (TMP_Settings.defaultFontAsset != null)
                    text.font = TMP_Settings.defaultFontAsset;

                text.fontSize = fontSize;
                text.fontStyle = style;
                text.alignment = TextAlignmentOptions.Center;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.raycastTarget = false;

                return text;
            }

            internal void SetVisible(bool visible)
            {
                if (!_built)
                    return;

                _canvas.enabled = visible;
            }

            private void Update()
            {
                if (!_built ||
                    !_canvas.enabled ||
                    !_owner._active ||
                    _owner._nearestVein == null ||
                    Camera.main == null)
                    return;

                UpdateMarker();
            }

            private void UpdateMarker()
            {
                float distance = _owner._nearestDistance;

                if (distance > DISPLAY_RANGE)
                {
                    SetVisible(false);
                    return;
                }

                // Prefer the nearest location that the game's own build
                // constraints accepted. If no valid site has been found yet,
                // fall back to the vein's X/Z so the locator still points
                // toward the detected deposit.
                Vector3 markerWorldPosition = _owner._hasBuildSite
                    ? _owner._buildSitePosition + Vector3.up * 2.0f
                    : new Vector3(
                        _owner._nearestVeinPosition.x,
                        _owner._playerPosition.y + 2.0f,
                        _owner._nearestVeinPosition.z);

                Vector3 screen =
                    Camera.main.WorldToScreenPoint(markerWorldPosition);

                bool behindCamera = screen.z <= 0f;

                if (behindCamera)
                {
                    // Flip the point so we can calculate the direction
                    // toward an off-screen vein.
                    screen *= -1f;
                }

                float screenX = screen.x;
                float screenY = Screen.height - screen.y;

                bool offScreen =
                    behindCamera ||
                    screenX < 35f ||
                    screenX > Screen.width - 35f ||
                    screenY < 35f ||
                    screenY > Screen.height - 35f;

                if (offScreen)
                {
                    Vector2 direction = new Vector2(
                        screen.x - Screen.width * 0.5f,
                        (Screen.height - screen.y) -
                        Screen.height * 0.5f);

                    if (direction.sqrMagnitude < 0.01f)
                        direction = Vector2.up;

                    direction.Normalize();

                    float edgeX = Screen.width * 0.5f +
                                  direction.x *
                                  Mathf.Min(Screen.width * 0.42f, 360f);

                    float edgeY = Screen.height * 0.5f +
                                  direction.y *
                                  Mathf.Min(Screen.height * 0.36f, 260f);

                    _root.position =
                        new Vector2(edgeX, edgeY);

                    _arrow.text =
                        GetDirectionArrow(direction);

                    _hint.text = _owner._hasBuildSite
                        ? "TURN TOWARD EXTRACTOR SITE"
                        : "TURN TOWARD VEIN";
                }
                else
                {
                    _root.position =
                        new Vector2(screenX, screenY);

                    Vector2 center =
                        new Vector2(
                            Screen.width * 0.5f,
                            Screen.height * 0.5f);

                    Vector2 direction =
                        new Vector2(
                            screen.x - center.x,
                            (Screen.height - screen.y) -
                            center.y);

                    _arrow.text =
                        GetDirectionArrow(direction);

                    _hint.text = _owner._hasBuildSite
                        ? "PLACE EXTRACTOR HERE"
                        : "NEAREST PHYSICAL VEIN";
                }

                _resource.text =
                    _owner._nearestResource.ToUpperInvariant();

                _distance.text =
                    $"{distance:F1} m";
            }

            private static string GetDirectionArrow(
                Vector2 direction)
            {
                if (direction.sqrMagnitude < 0.01f)
                    return "▼";

                float angle =
                    Mathf.Atan2(
                        direction.y,
                        direction.x) *
                    Mathf.Rad2Deg;

                if (angle >= -22.5f && angle < 22.5f)
                    return "→";

                if (angle >= 22.5f && angle < 67.5f)
                    return "↗";

                if (angle >= 67.5f && angle < 112.5f)
                    return "↑";

                if (angle >= 112.5f && angle < 157.5f)
                    return "↖";

                if (angle >= 157.5f || angle < -157.5f)
                    return "←";

                if (angle >= -157.5f && angle < -112.5f)
                    return "↙";

                if (angle >= -112.5f && angle < -67.5f)
                    return "↓";

                return "↘";
            }
        }
    }
}
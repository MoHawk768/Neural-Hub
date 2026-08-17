using System;
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
        private bool _active;

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
        // PLAYER-FACING RESOURCE NAME
        // ------------------------------------------------------------

        private string GetFriendlyResourceName(
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

        private string FormatResourceName(string groupId)
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
            _overlay.SetOwner(this);
            _overlay.Build();
        }

        private void Deactivate()
        {
            _active = false;
            _nearestVein = null;
            _nearestDistance = float.MaxValue;

            if (_overlay != null)
                _overlay.SetVisible(false);
        }

        // ------------------------------------------------------------
        // OVERLAY MONOBEHAVIOUR
        // ------------------------------------------------------------

        private sealed class LocatorOverlay : MonoBehaviour
        {
            private EpochVeinLocator _owner;
            private Canvas _canvas;
            private RectTransform _root;
            private Image _background;
            private TextMeshProUGUI _arrow;
            private TextMeshProUGUI _resource;
            private TextMeshProUGUI _distance;
            private TextMeshProUGUI _hint;

            private bool _built;

            internal void SetOwner(EpochVeinLocator owner)
            {
                _owner = owner;
            }

            internal void Build()
            {
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
                text.enableWordWrapping = false;
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
                    _owner == null ||
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

                // The actual vanilla vein transform can be hundreds of
                // metres below the playable surface. Lift the marker to the
                // extractor ghost's height while retaining the vein X/Z.
                Vector3 markerWorldPosition = new Vector3(
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

                    _hint.text = "TURN TOWARD VEIN";
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

                    _hint.text = "NEAREST PHYSICAL VEIN";
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
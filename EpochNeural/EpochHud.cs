using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceCraft;

namespace EpochNeural
{
    public partial class EpochHud : MonoBehaviour
    {
        public static EpochHud Instance { get; private set; }

        private GameObject _hudCanvasObject;
        private Canvas _hudCanvas;
        private GameObject _panelObject;

        private TextMeshProUGUI _txtHeader;
        private TextMeshProUGUI _txtStatus;
        private TextMeshProUGUI _txtPlanet;
        private TextMeshProUGUI _txtDiscovery;
        private TextMeshProUGUI _txtDrills;
        private TextMeshProUGUI _txtLeft;
        private TextMeshProUGUI _txtCount;
        private TextMeshProUGUI _txtStackCap;
        private TextMeshProUGUI _txtNextUpgrade;
        private TextMeshProUGUI _txtNotification;

        private string _txtStatusContent = "ENTER HUB TO ACTIVATE";
        private Color _statusColor = new Color(1.0f, 0.2f, 0.2f);
        private string _txtPlanetContent = "Planet : Unknown";
        private string _txtDiscoveryContent = "Biome Resource Discovered: 0 / 0";
        private string _txtDrillsContent = "Active Node Extractors   : 0 / 0";
        private string _txtLeftContent = "Biome Resources Left : 0";
        private string _txtCountContent = "Hub Item Count       : 0";
        private string _txtNextUpgradeContent = "Next Upgrade         : --";
        private bool _isVisible = true;
        private int _planetMaxDrillGoal = 12;

        private Coroutine _notificationCoroutine;
        private Coroutine _etaUpdateCoroutine;
        private const float NOTIFICATION_DURATION = 10f;
        private const float ETA_UPDATE_INTERVAL = 1f;

        private readonly HashSet<string> _permanentlyScannedOres = new HashSet<string>();

        private static readonly Color GreenColor = new Color(0.2f, 0.9f, 0.2f);
        private static readonly Color BrightGreenColor = new Color(0.0f, 1.0f, 0.0f);
        private static readonly Color OrangeColor = new Color(1.0f, 0.6f, 0.0f);
        private static readonly Color GoldColor = new Color(1.0f, 0.84f, 0.0f);
        private static readonly Color CyanColor = new Color(0.0f, 0.8f, 1.0f);
        private static readonly Color RedColor = new Color(1.0f, 0.2f, 0.2f);

        private double _cachedCurrentTi = 0;
        private double _cachedTiRate = 0;
        private double _cachedTargetTi = 0;
        private string _cachedNextTierName = "";

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this.gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }

        private void Start()
        {
            StartCoroutine(InitializeHudRoutine());
        }

        private IEnumerator InitializeHudRoutine()
        {
            yield return new WaitForSeconds(3.0f);

            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            while (sceneName == "MainMenu" || sceneName == "Loading" || sceneName == "Splash" ||
                   sceneName.Contains("Menu") || string.IsNullOrEmpty(sceneName))
            {
                yield return new WaitForSeconds(0.5f);
                sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            }

            var planetLoader = Managers.GetManager<PlanetLoader>();
            while (planetLoader == null || !planetLoader.GetIsLoaded())
            {
                yield return new WaitForSeconds(0.5f);
                planetLoader = Managers.GetManager<PlanetLoader>();
            }

            try
            {
                string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (currentScene.Contains("Humble") || currentScene.Contains("Toxicity")) _planetMaxDrillGoal = 8;
                else if (currentScene.Contains("Selenea") || currentScene.Contains("Moon") || currentScene.Contains("Aqualis")) _planetMaxDrillGoal = 7;
                else _planetMaxDrillGoal = 12;

                _hudCanvasObject = new GameObject("EpochHudCanvas");
                DontDestroyOnLoad(_hudCanvasObject);
                _hudCanvas = _hudCanvasObject.AddComponent<Canvas>();
                _hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _hudCanvas.sortingOrder = 999;

                CanvasScaler scaler = _hudCanvasObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);

                _panelObject = new GameObject("HudPanel");
                _panelObject.transform.SetParent(_hudCanvasObject.transform, false);
                RectTransform rect = _panelObject.AddComponent<RectTransform>();

                // Position at bottom-left, just above the vitals bars
                // Adjusted: 5mm right (+25 pixels) and 15mm up (+75 pixels)
                rect.anchorMin = new Vector2(0, 0);
                rect.anchorMax = new Vector2(0, 0);
                rect.pivot = new Vector2(0, 0);
                rect.anchoredPosition = new Vector2(45, 195); // 25px right + 75px up from original (20, 120)
                rect.sizeDelta = new Vector2(500, 380);

                _panelObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

                // Status line - bold red, above header
                _txtStatus = CreateGenericTextObject("LineStatus", new Vector2(0, 0), 20, RedColor);
                _txtStatus.text = _txtStatusContent;
                _txtStatus.fontStyle = FontStyles.Bold;
                _txtStatus.alignment = TextAlignmentOptions.TopLeft;
                RectTransform statusRect = _txtStatus.GetComponent<RectTransform>();
                statusRect.anchoredPosition = new Vector2(15, -10);
                statusRect.sizeDelta = new Vector2(-30, 35);

                // Header with tier name
                _txtHeader = CreateGenericTextObject("Line1", new Vector2(15, -45), 18, GoldColor);
                _txtHeader.text = "Epoch Neural Network";
                _txtHeader.fontStyle = FontStyles.Bold;
                _txtHeader.alignment = TextAlignmentOptions.TopLeft;

                _txtPlanet = CreateGenericTextObject("LinePlanet", new Vector2(15, -77), 14, GreenColor);
                _txtPlanet.text = "Planet : Unknown";

                _txtDiscovery = CreateGenericTextObject("Line2", new Vector2(15, -105), 14, GreenColor);
                _txtDrills = CreateGenericTextObject("Line3", new Vector2(15, -133), 14, GreenColor);
                _txtLeft = CreateGenericTextObject("Line4", new Vector2(15, -161), 14, GreenColor);
                _txtCount = CreateGenericTextObject("Line5", new Vector2(15, -189), 14, GreenColor);
                _txtStackCap = CreateGenericTextObject("Line6", new Vector2(15, -217), 14, GreenColor);
                _txtNextUpgrade = CreateGenericTextObject("Line7", new Vector2(15, -245), 14, CyanColor);

                _txtNotification = CreateGenericTextObject("LineNotification", new Vector2(15, -285), 14, BrightGreenColor);
                _txtNotification.text = "";
                _txtNotification.gameObject.SetActive(false);

                _panelObject.SetActive(true);
                _isVisible = true;

                UpdateHudData(false, 0, 0, 0, 0, 0);
                _txtPlanetContent = $"Planet : {GetCurrentPlanetName()}";

                StartCoroutine(UpdateHudLoop());
                StartETACoroutine();
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[Epoch HUD] Initialization failed: {ex.Message}");
            }
        }

        private TextMeshProUGUI CreateGenericTextObject(string name, Vector2 anchoredPosition, int size, Color c)
        {
            GameObject textObj = new GameObject(name);
            textObj.transform.SetParent(_panelObject.transform, false);
            RectTransform rect = textObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(-30, 28);

            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset fontAsset = FontAssetHelper.GetDefaultFont();
            if (fontAsset != null) tmp.font = fontAsset;
            tmp.fontSize = size;
            tmp.color = c;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.text = "...";
            return tmp;
        }

        private void StartETACoroutine()
        {
            if (_etaUpdateCoroutine != null)
            {
                StopCoroutine(_etaUpdateCoroutine);
            }
            _etaUpdateCoroutine = StartCoroutine(UpdateETALoop());
        }

        private IEnumerator UpdateHudLoop()
        {
            while (true)
            {
                if (_isVisible && _panelObject != null)
                {
                    if (_txtHeader != null)
                    {
                        var tierData = EpochNeural.CurrentTierData;
                        string tierName = tierData?.Name ?? "Epoch Hub";
                        _txtHeader.text = $"Epoch Neural Network [{tierName}]";
                    }
                    if (_txtStatus != null)
                    {
                        _txtStatus.text = _txtStatusContent;
                        _txtStatus.color = _statusColor;
                    }
                    if (_txtPlanet != null) _txtPlanet.text = _txtPlanetContent;
                    if (_txtDiscovery != null) _txtDiscovery.text = _txtDiscoveryContent;
                    if (_txtDrills != null) _txtDrills.text = _txtDrillsContent;
                    if (_txtLeft != null) _txtLeft.text = _txtLeftContent;
                    if (_txtCount != null) _txtCount.text = _txtCountContent;
                    if (_txtStackCap != null)
                    {
                        var tierData = EpochNeural.CurrentTierData;
                        int stackCap = tierData?.StackCap ?? 25;
                        _txtStackCap.text = $"Stack Cap            : {stackCap}";
                    }
                }
                yield return new WaitForSeconds(0.2f);
            }
        }

        private IEnumerator UpdateETALoop()
        {
            UpdateETA();

            while (true)
            {
                yield return new WaitForSeconds(ETA_UPDATE_INTERVAL);

                if (_isVisible && _panelObject != null && _txtNextUpgrade != null)
                {
                    UpdateETA();
                }
            }
        }

        private void UpdateETA()
        {
            try
            {
                if (EpochNeural.IsAtMaxTier())
                {
                    _txtNextUpgrade.text = "Next Upgrade         : COMPLETE!";
                    _txtNextUpgrade.color = GoldColor;
                    return;
                }

                var nextTier = EpochNeural.GetNextTierData();
                if (nextTier == null)
                {
                    _txtNextUpgrade.text = "Next Upgrade         : COMPLETE!";
                    _txtNextUpgrade.color = GoldColor;
                    return;
                }

                var worldUnitsHandler = Managers.GetManager<WorldUnitsHandler>();
                if (worldUnitsHandler == null)
                {
                    _txtNextUpgrade.text = "Next Upgrade         : --";
                    return;
                }

                var terraUnit = worldUnitsHandler.GetUnit(DataConfig.WorldUnitType.Terraformation);
                if (terraUnit == null)
                {
                    _txtNextUpgrade.text = "Next Upgrade         : --";
                    return;
                }

                double currentTi = terraUnit.GetValue();
                double targetTi = nextTier.UnlockTi;

                if (currentTi >= targetTi)
                {
                    _txtNextUpgrade.text = $"Next Upgrade         : {nextTier.Name} - NOW!";
                    _txtNextUpgrade.color = BrightGreenColor;
                    return;
                }

                double tiPerSecond = terraUnit.GetIncreaseValuePersSec();

                try
                {
                    var oxygenUnit = worldUnitsHandler.GetUnit(DataConfig.WorldUnitType.Oxygen);
                    var heatUnit = worldUnitsHandler.GetUnit(DataConfig.WorldUnitType.Heat);
                    var pressureUnit = worldUnitsHandler.GetUnit(DataConfig.WorldUnitType.Pressure);
                    var biomassUnit = worldUnitsHandler.GetUnit(DataConfig.WorldUnitType.Biomass);
                    var purificationUnit = worldUnitsHandler.GetUnit(DataConfig.WorldUnitType.Purification);

                    if (oxygenUnit != null) tiPerSecond += oxygenUnit.GetIncreaseValuePersSec();
                    if (heatUnit != null) tiPerSecond += heatUnit.GetIncreaseValuePersSec();
                    if (pressureUnit != null) tiPerSecond += pressureUnit.GetIncreaseValuePersSec();
                    if (biomassUnit != null) tiPerSecond += biomassUnit.GetIncreaseValuePersSec();
                    if (purificationUnit != null) tiPerSecond += purificationUnit.GetIncreaseValuePersSec();
                }
                catch { }

                _cachedCurrentTi = currentTi;
                _cachedTiRate = tiPerSecond;
                _cachedTargetTi = targetTi;
                _cachedNextTierName = nextTier.Name;

                if (tiPerSecond <= 0)
                {
                    _txtNextUpgrade.text = $"Next Upgrade         : {nextTier.Name} (no generation)";
                    _txtNextUpgrade.color = OrangeColor;
                    return;
                }

                double secondsNeeded = (targetTi - currentTi) / tiPerSecond;
                if (secondsNeeded < 0) secondsNeeded = 0;

                string timeString = FormatTime(secondsNeeded);

                Color etaColor = CyanColor;
                if (secondsNeeded < 60)
                {
                    etaColor = BrightGreenColor;
                }
                else if (secondsNeeded < 3600)
                {
                    etaColor = new Color(0.0f, 1.0f, 0.5f);
                }
                else if (secondsNeeded >= 86400)
                {
                    etaColor = new Color(0.7f, 0.5f, 1.0f);
                }

                _txtNextUpgrade.text = $"Next Upgrade         : {nextTier.Name} in {timeString}";
                _txtNextUpgrade.color = etaColor;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[Epoch HUD] ETA update error: {ex.Message}");
                if (_txtNextUpgrade != null)
                {
                    _txtNextUpgrade.text = "Next Upgrade         : --";
                }
            }
        }

        private string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;

            if (seconds < 60)
            {
                return $"{Mathf.CeilToInt((float)seconds)} sec";
            }
            else if (seconds < 3600)
            {
                int minutes = Mathf.FloorToInt((float)seconds / 60);
                int remainingSeconds = Mathf.FloorToInt((float)seconds % 60);
                if (remainingSeconds > 0)
                {
                    return $"{minutes}m {remainingSeconds}s";
                }
                return $"{minutes} min";
            }
            else if (seconds < 86400)
            {
                float hours = (float)seconds / 3600f;
                int wholeHours = Mathf.FloorToInt(hours);
                int minutes = Mathf.FloorToInt((hours - wholeHours) * 60);
                if (minutes > 0)
                {
                    return $"{wholeHours}h {minutes}m";
                }
                return $"{hours:F1} hours";
            }
            else
            {
                float days = (float)seconds / 86400f;
                int wholeDays = Mathf.FloorToInt(days);
                float remainingHours = (days - wholeDays) * 24;
                if (remainingHours > 1)
                {
                    return $"{wholeDays}d {remainingHours:F0}h";
                }
                return $"{days:F1} days";
            }
        }

        // ============================================================
        // NOTIFICATION SYSTEM
        // ============================================================

        public void ShowNotification(string message, bool isSuccess = true)
        {
            if (_txtNotification == null) return;

            if (_notificationCoroutine != null)
            {
                StopCoroutine(_notificationCoroutine);
                _notificationCoroutine = null;
            }

            string icon = isSuccess ? "✔️" : "❌";
            Color color = isSuccess ? BrightGreenColor : Color.red;

            _txtNotification.text = $"[{icon} {message}]";
            _txtNotification.color = color;
            _txtNotification.gameObject.SetActive(true);

            _notificationCoroutine = StartCoroutine(HideNotificationAfterDelay(NOTIFICATION_DURATION));
        }

        public void ShowUpgradeNotification(string tierName)
        {
            if (_txtNotification == null) return;

            if (_notificationCoroutine != null)
            {
                StopCoroutine(_notificationCoroutine);
                _notificationCoroutine = null;
            }

            _txtNotification.text = $"▲ HUB UPGRADED TO {tierName.ToUpper()}! ▲";
            _txtNotification.color = GoldColor;
            _txtNotification.gameObject.SetActive(true);

            _notificationCoroutine = StartCoroutine(HideNotificationAfterDelay(NOTIFICATION_DURATION));
        }

        private IEnumerator HideNotificationAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_txtNotification != null)
            {
                _txtNotification.text = "";
                _txtNotification.gameObject.SetActive(false);
            }
            _notificationCoroutine = null;
        }

        // ============================================================
        // UPDATE METHODS
        // ============================================================

        public void UpdateHud(bool hubActive, int worldObjects, int containerItemCount)
        {
            int localAvailable = 0;
            int localLearned = 0;

            int placedDrillsCount = EpochDrillManager.GetActiveDrillsCount();

            if (hubActive)
            {
                _txtStatusContent = "[ONLINE]";
                _statusColor = BrightGreenColor;
                if (_etaUpdateCoroutine == null)
                {
                    StartETACoroutine();
                }
            }
            else
            {
                _txtStatusContent = "ENTER HUB TO ACTIVATE";
                _statusColor = RedColor;
                if (_etaUpdateCoroutine != null)
                {
                    StopCoroutine(_etaUpdateCoroutine);
                    _etaUpdateCoroutine = null;
                    _txtNextUpgrade.text = "Next Upgrade         : --";
                }
            }

            string planetName = GetCurrentPlanetName();
            _txtPlanetContent = $"Planet : {planetName}";

            try
            {
                int totalVeins = EpochDrillManager.GetTotalVeinsOnCurrentPlanet();
                _planetMaxDrillGoal = totalVeins > 0 ? totalVeins : 12;

                var discovery = EpochVacuumDiscovery.DiscoverCollectibles();
                if (discovery != null && discovery.Objects != null && EpochNeural.EpochHubInventory != null)
                {
                    var hubItems = EpochNeural.EpochHubInventory.GetInsideWorldObjects();
                    foreach (WorldObject wo in discovery.Objects)
                    {
                        if (wo?.GetGroup() is GroupItem) _permanentlyScannedOres.Add(wo.GetGroup().GetId());
                    }

                    if (hubItems != null)
                    {
                        foreach (WorldObject wo in hubItems)
                        {
                            if (wo?.GetGroup() is GroupItem) _permanentlyScannedOres.Add(wo.GetGroup().GetId());
                        }
                    }

                    localAvailable = _permanentlyScannedOres.Count;

                    foreach (string id in _permanentlyScannedOres)
                    {
                        if (hubItems == null) continue;
                        foreach (WorldObject wo in hubItems)
                        {
                            if (wo?.GetGroup() != null && wo.GetGroup().GetId() == id) { localLearned++; break; }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[Epoch HUD] Update error: {ex.Message}");
            }

            UpdateHudData(hubActive, worldObjects, containerItemCount, localLearned, localAvailable, placedDrillsCount);
        }

        public void UpdateHudData(bool active, int left, int count, int learned, int available, int activeDrills)
        {
            if (_panelObject != null && !_panelObject.activeSelf)
            {
                _panelObject.SetActive(true);
            }

            _txtDiscoveryContent = $"Biome Resource Discovered: {learned} / {available}";
            _txtDrillsContent = $"Active Node Extractors   : {activeDrills} / {_planetMaxDrillGoal}";
            _txtLeftContent = $"Biome Resources Left : {left:N0}";
            _txtCountContent = $"Hub Item Count       : {count:N0}";
        }

        public void ToggleVisibility()
        {
            _isVisible = !_isVisible;
            if (_panelObject != null) _panelObject.SetActive(_isVisible);
        }

        // ============================================================
        // HELPER METHODS
        // ============================================================

        private string GetCurrentPlanetName()
        {
            try
            {
                var planetLoader = Managers.GetManager<PlanetLoader>();
                if (planetLoader != null)
                {
                    var planetData = planetLoader.GetCurrentPlanetData();
                    if (planetData != null && !string.IsNullOrEmpty(planetData.id))
                    {
                        string name = planetData.id;
                        if (name.Length > 0)
                        {
                            return char.ToUpper(name[0]) + name.Substring(1);
                        }
                        return name;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[Epoch HUD] Could not get planet name: {ex.Message}");
            }
            return "Unknown";
        }
    }

    public static class FontAssetHelper
    {
        private static TMPro.TMP_FontAsset _cachedFont;
        public static TMPro.TMP_FontAsset GetDefaultFont()
        {
            if (_cachedFont != null) return _cachedFont;
            try
            {
                TMPro.TMP_FontAsset[] loadedFonts = UnityEngine.Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>();
                if (loadedFonts != null && loadedFonts.Length > 0) { _cachedFont = loadedFonts[0]; return _cachedFont; }
            }
            catch { }
            return TMPro.TMP_Settings.defaultFontAsset;
        }
    }
}
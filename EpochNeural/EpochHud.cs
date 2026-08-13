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
        private TextMeshProUGUI _txtNotification;

        private string _txtHeaderContent = "Epoch Neural Network";
        private string _txtStatusContent = "ENTER HUB TO ACTIVATE";
        private Color _statusColor = new Color(1.0f, 0.6f, 0.0f); // Orange/amber
        private string _txtPlanetContent = "Planet : Unknown";
        private string _txtDiscoveryContent = "Biome Resource Discovered: 0 / 0";
        private string _txtDrillsContent = "Active Node Extractors   : 0 / 0";
        private string _txtLeftContent = "Biome Resources Left : 0";
        private string _txtCountContent = "Hub Item Count       : 0";
        private bool _isVisible = true;
        private int _planetMaxDrillGoal = 12;

        private Coroutine _notificationCoroutine;
        private const float NOTIFICATION_DURATION = 10f;

        private readonly HashSet<string> _permanentlyScannedOres = new HashSet<string>();
        private readonly HashSet<string> _occupiedBiomesRegistry = new HashSet<string>();

        // Green color for all text
        private static readonly Color GreenColor = new Color(0.2f, 0.9f, 0.2f);
        private static readonly Color BrightGreenColor = new Color(0.0f, 1.0f, 0.0f);

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
            // Wait for the game to be fully loaded
            yield return new WaitForSeconds(3.0f);

            // Check if we're in the game world (not the main menu)
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            // If we're in the main menu or loading screen, wait until we're in a game scene
            while (sceneName == "MainMenu" || sceneName == "Loading" || sceneName == "Splash" ||
                   sceneName.Contains("Menu") || string.IsNullOrEmpty(sceneName))
            {
                yield return new WaitForSeconds(0.5f);
                sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            }

            // Also wait for PlanetLoader to be ready
            var planetLoader = Managers.GetManager<PlanetLoader>();
            while (planetLoader == null || !planetLoader.GetIsLoaded())
            {
                yield return new WaitForSeconds(0.5f);
                planetLoader = Managers.GetManager<PlanetLoader>();
            }

            // Now initialize the HUD (this is outside the while loop so we can use try-catch)
            try
            {
                string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (currentScene.Contains("Humble") || currentScene.Contains("Toxicity")) _planetMaxDrillGoal = 8;
                else if (currentScene.Contains("Selenea") || currentScene.Contains("Moon") || currentScene.Contains("Aqualis")) _planetMaxDrillGoal = 7;
                else _planetMaxDrillGoal = 12;

                _hudCanvasObject = new GameObject("EpochDevHudCanvas");
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
                rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(35, -20);
                rect.sizeDelta = new Vector2(460, 300);

                // CHANGED: Made background completely transparent
                _panelObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

                _txtHeader = CreateGenericTextObject("Line1", new Vector2(15, -15), 20, BrightGreenColor);
                _txtHeader.text = "Epoch Neural Network";
                _txtHeader.fontStyle = FontStyles.Bold;

                // Status text - starts with "ENTER HUB TO ACTIVATE" in orange
                _txtStatus = CreateGenericTextObject("LineStatus", new Vector2(290, -15), 16, _statusColor);
                _txtStatus.text = _txtStatusContent;
                _txtStatus.fontStyle = FontStyles.Bold;

                _txtPlanet = CreateGenericTextObject("LinePlanet", new Vector2(15, -47), 16, GreenColor);
                _txtPlanet.text = "Planet : Unknown";

                _txtDiscovery = CreateGenericTextObject("Line2", new Vector2(15, -79), 16, GreenColor);
                _txtDrills = CreateGenericTextObject("Line3", new Vector2(15, -111), 16, GreenColor);
                _txtLeft = CreateGenericTextObject("Line4", new Vector2(15, -143), 16, GreenColor);
                _txtCount = CreateGenericTextObject("Line5", new Vector2(15, -175), 16, GreenColor);

                _txtNotification = CreateGenericTextObject("LineNotification", new Vector2(15, -207), 16, GreenColor);
                _txtNotification.text = "";
                _txtNotification.gameObject.SetActive(false);

                _panelObject.SetActive(true);
                _isVisible = true;

                UpdateHudData(false, 0, 0, 0, 0, 0);
                _txtPlanetContent = $"Planet : {GetCurrentPlanetName()}";

                StartCoroutine(UpdateHudLoop());
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
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = anchoredPosition; rect.sizeDelta = new Vector2(-30, 30);

            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset fontAsset = FontAssetHelper.GetDefaultFont();
            if (fontAsset != null) tmp.font = fontAsset;
            tmp.fontSize = size; tmp.color = c; tmp.alignment = TextAlignmentOptions.TopLeft; tmp.text = "...";
            return tmp;
        }

        private IEnumerator UpdateHudLoop()
        {
            while (true)
            {
                if (_isVisible && _panelObject != null)
                {
                    if (_txtHeader != null) _txtHeader.text = _txtHeaderContent;
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
                }
                yield return new WaitForSeconds(0.2f);
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

            // Update status based on hubActive
            if (hubActive)
            {
                _txtStatusContent = "[ONLINE]";
                _statusColor = BrightGreenColor;
            }
            else
            {
                _txtStatusContent = "ENTER HUB TO ACTIVATE";
                _statusColor = new Color(1.0f, 0.6f, 0.0f); // Orange/amber
            }

            // Get current planet name
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
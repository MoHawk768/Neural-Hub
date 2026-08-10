using System;
using System.Collections;
using BepInEx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EpochNeural
{
    public class EpochDevHud : MonoBehaviour
    {
        public static EpochDevHud Instance { get; private set; }

        private GameObject _hudCanvasObject;
        private Canvas _hudCanvas;
        private GameObject _panelObject;

        private TextMeshProUGUI _line1Text;
        private TextMeshProUGUI _line2Text;
        private TextMeshProUGUI _line3Text;

        private string _line1Content = "System: Initialising...";
        private string _line2Content = "Discovery: Idle";
        private string _line3Content = "World State: Waiting";

        private bool _isVisible = true;
        private float _updateInterval = 0.2f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this.gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }

        private void Start()
        {
            StartCoroutine(InitializeHudRoutine());
        }

        private IEnumerator InitializeHudRoutine()
        {
            // Give Planet Crafter's world UI loading sequence plenty of headroom
            yield return new WaitForSeconds(3.0f);

            try
            {
                CreateHudCanvas();
                CreateBackgroundPanel();
                CreateTextLines();
                StartCoroutine(UpdateHudLoop());
                LogInfo("EpochDevHud successfully initialized and injected into runtime.");
            }
            catch (Exception ex)
            {
                LogError($"Critical error during HUD initialization: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void CreateHudCanvas()
        {
            _hudCanvasObject = new GameObject("EpochDevHudCanvas");
            DontDestroyOnLoad(_hudCanvasObject);

            _hudCanvas = _hudCanvasObject.AddComponent<Canvas>();
            _hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _hudCanvas.sortingOrder = 999;

            CanvasScaler scaler = _hudCanvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            _hudCanvasObject.AddComponent<GraphicRaycaster>();
        }

        private void CreateBackgroundPanel()
        {
            _panelObject = new GameObject("HudPanel");
            _panelObject.transform.SetParent(_hudCanvasObject.transform, false);

            RectTransform rect = _panelObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(20, -20);
            rect.sizeDelta = new Vector2(400, 140);

            Image background = _panelObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.65f);
        }

        private void CreateTextLines()
        {
            _line1Text = CreateGenericTextObject("Line1", new Vector2(15, -15));
            _line1Text.fontSize = 16;
            _line1Text.color = Color.cyan;

            _line2Text = CreateGenericTextObject("Line2", new Vector2(15, -50));
            _line2Text.fontSize = 14;
            _line2Text.color = Color.white;

            _line3Text = CreateGenericTextObject("Line3", new Vector2(15, -85));
            _line3Text.fontSize = 14;
            _line3Text.color = Color.green;
        }

        private TextMeshProUGUI CreateGenericTextObject(string name, Vector2 anchoredPosition)
        {
            GameObject textObj = new GameObject(name);
            textObj.transform.SetParent(_panelObject.transform, false);

            RectTransform rect = textObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(-30, 30);

            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();

            // Safe assignment protection loop
            TMP_FontAsset fontAsset = FontAssetHelper.GetDefaultFont();
            if (fontAsset != null)
            {
                tmp.font = fontAsset;
            }

            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.text = "Awaiting Data...";

            return tmp;
        }

        private IEnumerator UpdateHudLoop()
        {
            while (true)
            {
                if (_isVisible)
                {
                    if (_line1Text != null) _line1Text.text = _line1Content;
                    if (_line2Text != null) _line2Text.text = _line2Content;
                    if (_line3Text != null) _line3Text.text = _line3Content;
                }
                yield return new WaitForSeconds(_updateInterval);
            }
        }

        public void UpdateHud(bool hubActive, int worldObjects, int containerItemCount)
        {
            _line1Content = $"Hub Status           : {(hubActive ? "ACTIVE" : "NOT ACTIVE")}";
            _line2Content = $"World Objects Total  : {worldObjects:N0}";
            _line3Content = $"Container Item Count : {containerItemCount:N0}";
        }

        public void ToggleVisibility()
        {
            _isVisible = !_isVisible;
            if (_panelObject != null)
            {
                _panelObject.SetActive(_isVisible);
            }
        }

        private void LogInfo(string message) => Debug.Log($"[EpochHUD] [INFO] {message}");
        private void LogError(string message) => Debug.LogError($"[EpochHUD] [ERROR] {message}");
    }

    public static class FontAssetHelper
    {
        private static TMP_FontAsset _cachedFont;

        public static TMP_FontAsset GetDefaultFont()
        {
            if (_cachedFont != null) return _cachedFont;

            try
            {
                TMP_FontAsset[] loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (loadedFonts != null && loadedFonts.Length > 0)
                {
                    _cachedFont = loadedFonts[0];
                    return _cachedFont;
                }
            }
            catch { }

            return TMP_Settings.defaultFontAsset;
        }
    }
}

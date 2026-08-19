using System;
using System.Reflection;
using HarmonyLib;
using SpaceCraft;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EpochNeural
{
    /// <summary>
    /// Epoch Hub relocation system.
    ///
    /// When the player selects Epoch_Hub while one already exists:
    ///
    ///     YES    -> enter relocation mode for the existing Hub.
    ///     CANCEL -> return to the construction menu.
    ///
    /// The actual relocation continues to use the existing WorldObject,
    /// preserving its ID, inventory, tier and Epoch state.
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

        private static GameObject _confirmationCanvas;
        private static UiWindowConstruction _pendingConstructionWindow;
        private static GroupConstructible _pendingHubGroup;

        private static bool _confirmationOpen;
        private static bool _relocationStarting;

        // ============================================================
        // INTERCEPT HUB SELECTION
        // ============================================================

        [HarmonyPatch(typeof(UiWindowConstruction), "Construct")]
        [HarmonyPrefix]
        private static bool PrefixConstruct(
            UiWindowConstruction __instance,
            GroupConstructible groupConstructible)
        {
            try
            {
                if (groupConstructible == null)
                    return true;

                // Only Epoch Hub gets special treatment.
                if (groupConstructible.GetId() != HubId)
                    return true;

                // Internal call after YES.
                if (_relocationStarting)
                    return true;

                WorldObject existingHub = FindExistingHub();

                // No Hub exists -> completely normal construction.
                if (existingHub == null)
                    return true;

                // Don't open multiple confirmation dialogs.
                if (_confirmationOpen)
                    return false;

                _pendingConstructionWindow = __instance;
                _pendingHubGroup = groupConstructible;

                _confirmationOpen = true;

                Plugin.Logger?.LogInfo(
                    $"[Epoch Relocation] Existing Hub detected. " +
                    $"Opening relocation confirmation for WorldObject ID {existingHub.GetId()}.");

                ShowRelocationConfirmation();

                // Stop vanilla Construct().
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError(
                    $"[Epoch Relocation] Construct interception failed: {ex}");

                ClearConfirmation();

                return true;
            }
        }

        // ============================================================
        // CONFIRMATION UI
        // ============================================================

        private static void ShowRelocationConfirmation()
        {
            try
            {
                DestroyConfirmation();

                _confirmationCanvas =
                    new GameObject("EpochHubRelocationConfirmation");

                UnityEngine.Object.DontDestroyOnLoad(
                    _confirmationCanvas);

                Canvas canvas =
                    _confirmationCanvas.AddComponent<Canvas>();

                canvas.renderMode =
                    RenderMode.ScreenSpaceOverlay;

                canvas.sortingOrder = 5000;

                CanvasScaler scaler =
                    _confirmationCanvas.AddComponent<CanvasScaler>();

                scaler.uiScaleMode =
                    CanvasScaler.ScaleMode.ScaleWithScreenSize;

                scaler.referenceResolution =
                    new Vector2(1920f, 1080f);

                scaler.screenMatchMode =
                    CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

                scaler.matchWidthOrHeight = 0.5f;

                _confirmationCanvas.AddComponent<GraphicRaycaster>();

                // ----------------------------------------------------
                // BACKGROUND DIMMER
                // ----------------------------------------------------

                GameObject dimmer =
                    CreateUIObject(
                        "Dimmer",
                        _confirmationCanvas.transform);

                Image dimmerImage =
                    dimmer.AddComponent<Image>();

                dimmerImage.color =
                    new Color(0f, 0f, 0f, 0.55f);

                SetFullScreen(dimmer.GetComponent<RectTransform>());

                // ----------------------------------------------------
                // MAIN PANEL
                // ----------------------------------------------------

                GameObject panel =
                    CreateUIObject(
                        "Panel",
                        _confirmationCanvas.transform);

                Image panelImage =
                    panel.AddComponent<Image>();

                panelImage.color =
                    new Color(0.025f, 0.03f, 0.04f, 0.96f);

                RectTransform panelRect =
                    panel.GetComponent<RectTransform>();

                panelRect.anchorMin =
                    new Vector2(0.5f, 0.5f);

                panelRect.anchorMax =
                    new Vector2(0.5f, 0.5f);

                panelRect.pivot =
                    new Vector2(0.5f, 0.5f);

                panelRect.sizeDelta =
                    new Vector2(850f, 350f);

                panelRect.anchoredPosition =
                    Vector2.zero;

                // ----------------------------------------------------
                // TITLE
                // ----------------------------------------------------

                TMP_Text title =
                    CreateText(
                        "Title",
                        panel.transform,
                        "EPOCH HUB",
                        32f);

                RectTransform titleRect =
                    title.GetComponent<RectTransform>();

                titleRect.anchorMin =
                    new Vector2(0.5f, 1f);

                titleRect.anchorMax =
                    new Vector2(0.5f, 1f);

                titleRect.pivot =
                    new Vector2(0.5f, 1f);

                titleRect.sizeDelta =
                    new Vector2(760f, 55f);

                titleRect.anchoredPosition =
                    new Vector2(0f, -35f);

                title.alignment =
                    TextAlignmentOptions.Center;

                title.color =
                    new Color(1f, 0.82f, 0.15f);

                // ----------------------------------------------------
                // MESSAGE
                // ----------------------------------------------------

                TMP_Text message =
                    CreateText(
                        "Message",
                        panel.transform,
                        "An Epoch Hub already exists on this planet.\n\n" +
                        "Would you like to relocate your existing Hub\n" +
                        "to the new location?",
                        22f);

                RectTransform messageRect =
                    message.GetComponent<RectTransform>();

                messageRect.anchorMin =
                    new Vector2(0.5f, 0.5f);

                messageRect.anchorMax =
                    new Vector2(0.5f, 0.5f);

                messageRect.pivot =
                    new Vector2(0.5f, 0.5f);

                messageRect.sizeDelta =
                    new Vector2(760f, 140f);

                messageRect.anchoredPosition =
                    new Vector2(0f, 25f);

                message.alignment =
                    TextAlignmentOptions.Center;

                message.color =
                    Color.white;

                // ----------------------------------------------------
                // YES BUTTON
                // ----------------------------------------------------

                Button yesButton =
                    CreateButton(
                        "YesButton",
                        panel.transform,
                        "YES  -  RELOCATE HUB");

                RectTransform yesRect =
                    yesButton.GetComponent<RectTransform>();

                yesRect.anchorMin =
                    new Vector2(0.5f, 0f);

                yesRect.anchorMax =
                    new Vector2(0.5f, 0f);

                yesRect.pivot =
                    new Vector2(0.5f, 0f);

                yesRect.sizeDelta =
                    new Vector2(330f, 70f);

                yesRect.anchoredPosition =
                    new Vector2(-180f, 35f);

                yesButton.onClick.AddListener(
                    ConfirmRelocation);

                // ----------------------------------------------------
                // CANCEL BUTTON
                // ----------------------------------------------------

                Button cancelButton =
                    CreateButton(
                        "CancelButton",
                        panel.transform,
                        "CANCEL");

                RectTransform cancelRect =
                    cancelButton.GetComponent<RectTransform>();

                cancelRect.anchorMin =
                    new Vector2(0.5f, 0f);

                cancelRect.anchorMax =
                    new Vector2(0.5f, 0f);

                cancelRect.pivot =
                    new Vector2(0.5f, 0f);

                cancelRect.sizeDelta =
                    new Vector2(330f, 70f);

                cancelRect.anchoredPosition =
                    new Vector2(180f, 35f);

                cancelButton.onClick.AddListener(
                    CancelRelocation);

                // ----------------------------------------------------
                // GAMEPAD / KEYBOARD UI SELECTION
                // ----------------------------------------------------

                if (EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(
                        yesButton.gameObject);
                }

                Plugin.Logger?.LogInfo(
                    "[Epoch Relocation] Confirmation dialog displayed.");
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError(
                    $"[Epoch Relocation] Failed to create confirmation UI: {ex}");

                ClearConfirmation();
            }
        }

        // ============================================================
        // YES
        // ============================================================

        private static void ConfirmRelocation()
        {
            try
            {
                GroupConstructible group =
                    _pendingHubGroup;

                UiWindowConstruction window =
                    _pendingConstructionWindow;

                WorldObject existingHub =
                    FindExistingHub();

                if (group == null || existingHub == null)
                {
                    ShowMessage(
                        "EPOCH: EXISTING HUB COULD NOT BE FOUND.");

                    ClearConfirmation();
                    return;
                }

                DestroyConfirmation();

                PlayerMainController player =
                    Managers.GetManager<PlayersManager>()
                        ?.GetActivePlayerController();

                PlayerBuilder builder =
                    player?.GetPlayerBuilder();

                if (builder == null)
                {
                    Plugin.Logger?.LogError(
                        "[Epoch Relocation] PlayerBuilder unavailable.");

                    ClearConfirmation();
                    return;
                }

                _relocationStarting = true;

                bool ghostCreated =
    builder.SetNewGhost(
        group,
        existingHub);

                _relocationStarting = false;

                if (ghostCreated)
                {
                    ConstructibleGhost relocationGhost =
                        GetCurrentGhost(builder);

                    if (relocationGhost != null)
                    {
                        // Start relocation using the EXISTING Hub orientation.
                        // This prevents Quaternion.identity from reaching
                        // DropOnFloorWithRotation(), which would cause the
                        // vanilla game to apply a random 90-180 degree rotation.
                        relocationGhost.transform.rotation =
                            existingHub.GetRotation();

                        Plugin.Logger?.LogInfo(
                            $"[Epoch Relocation] Initial rotation restored from existing Hub: " +
                            $"{existingHub.GetRotation().eulerAngles}");
                    }
                }

                if (!ghostCreated)
                {
                    Plugin.Logger?.LogWarning(
                        "[Epoch Relocation] Failed to create relocation ghost.");

                    ClearConfirmation();
                    return;
                }

                // Same behaviour as vanilla construction:
                // close the construction menu after creating the ghost.
                if (window != null)
                {
                    window.CloseAll();
                }

                Plugin.Logger?.LogInfo(
                    $"[Epoch Relocation] Player confirmed relocation. " +
                    $"Relocation ghost created for Hub ID {existingHub.GetId()}.");
            }
            catch (Exception ex)
            {
                _relocationStarting = false;

                Plugin.Logger?.LogError(
                    $"[Epoch Relocation] ConfirmRelocation failed: {ex}");

                ClearConfirmation();
            }
        }

        // ============================================================
        // CANCEL
        // ============================================================

        private static void CancelRelocation()
        {
            Plugin.Logger?.LogInfo(
                "[Epoch Relocation] Player cancelled Hub relocation.");

            DestroyConfirmation();
            ClearConfirmation();
        }

        // ============================================================
        // EXISTING RELOCATION PLACEMENT
        // ============================================================

        [HarmonyPatch(typeof(PlayerBuilder), "InputOnAction")]
        [HarmonyPriority(Priority.First)]
        [HarmonyPrefix]
        private static bool PrefixInputOnAction(
            PlayerBuilder __instance)
        {
            try
            {
                if (__instance == null)
                    return true;

                if (GhostGroupField == null ||
                    GhostField == null)
                {
                    return true;
                }

                Group ghostGroup =
                    GhostGroupField.GetValue(__instance) as Group;

                if (ghostGroup == null ||
                    ghostGroup.GetId() != HubId)
                {
                    return true;
                }

                ConstructibleGhost ghost =
                    GhostField.GetValue(__instance)
                    as ConstructibleGhost;

                if (ghost == null)
                    return true;

                WorldObject existingHub =
                    FindExistingHub();

                // First Hub -> let vanilla handle it.
                if (existingHub == null)
                    return true;

                GhostPlacementChecker checker =
                    ghost.GetComponent<GhostPlacementChecker>();

                if (checker == null ||
                    !checker.GetPositioningStatus())
                {
                    ShowMessage(
                        "EPOCH: HUB CANNOT BE PLACED HERE!");

                    return false;
                }

                Vector3 newPosition =
                    ghost.transform.position;

                Quaternion newRotation =
                    ghost.transform.rotation;

                if (newPosition.sqrMagnitude <= 0.000001f)
                    return false;

                int hubWorldObjectId =
                    existingHub.GetId();

                Plugin.Logger?.LogInfo(
                    $"[Epoch Relocation] Relocating existing Hub. " +
                    $"WorldObject ID: {hubWorldObjectId}");

                Plugin.Logger?.LogInfo(
                    $"[Epoch Relocation] Old position: " +
                    $"{existingHub.GetPosition()}");

                Plugin.Logger?.LogInfo(
                    $"[Epoch Relocation] New position: " +
                    $"{newPosition}");

                WorldObjectsHandler handler =
                    WorldObjectsHandler.Instance;

                if (handler == null)
                {
                    Plugin.Logger?.LogError(
                        "[Epoch Relocation] WorldObjectsHandler unavailable.");

                    return false;
                }

                // Remove ONLY the old physical GameObject.
                // Do NOT destroy the WorldObject itself.
                handler.DestroyWorldObjectGOOnAllClients(
                    hubWorldObjectId);

                // Move/recreate the SAME WorldObject.
                handler.DropOnFloorWithRotation(
                    existingHub,
                    newPosition,
                    newRotation,
                    0f,
                    dropSound: false,
                    ownershipToNearestClient: true);

                // Preserve Epoch's tracked Hub ID.
                EpochNeural.SetHubWorldObjectId(
                    hubWorldObjectId);

                // Destroy relocation ghost.
                ghost.DestroyGhost();

                // Clear builder state without invoking normal
                // construction/resource consumption.
                __instance.InputOnCancelAction();

                Plugin.Logger?.LogInfo(
                    $"[Epoch Relocation] SUCCESS. " +
                    $"Hub WorldObject ID {hubWorldObjectId} preserved.");

                ShowMessage(
                    "EPOCH HUB RELOCATED");

                // Prevent vanilla second-Hub protection.
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError(
                    $"[Epoch Relocation] Relocation error: {ex}");

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
        // UI HELPERS
        // ============================================================

        private static GameObject CreateUIObject(
            string name,
            Transform parent)
        {
            GameObject obj =
                new GameObject(
                    name,
                    typeof(RectTransform));

            obj.transform.SetParent(
                parent,
                false);

            return obj;
        }

        private static TMP_Text CreateText(
            string name,
            Transform parent,
            string text,
            float fontSize)
        {
            GameObject obj =
                CreateUIObject(name, parent);

            TextMeshProUGUI tmp =
                obj.AddComponent<TextMeshProUGUI>();

            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.textWrappingMode = TextWrappingModes.Normal;

            return tmp;
        }

        private static Button CreateButton(
            string name,
            Transform parent,
            string label)
        {
            GameObject obj =
                CreateUIObject(name, parent);

            Image image =
                obj.AddComponent<Image>();

            image.color =
                new Color(0.05f, 0.06f, 0.08f, 0.95f);

            Button button =
                obj.AddComponent<Button>();

            ColorBlock colors =
                button.colors;

            colors.normalColor =
                new Color(0.08f, 0.09f, 0.12f);

            colors.highlightedColor =
                new Color(0.18f, 0.20f, 0.24f);

            colors.pressedColor =
                new Color(0.30f, 0.32f, 0.36f);

            colors.selectedColor =
                new Color(0.18f, 0.20f, 0.24f);

            button.colors = colors;

            TMP_Text text =
                CreateText(
                    "Label",
                    obj.transform,
                    label,
                    21f);

            RectTransform textRect =
                text.GetComponent<RectTransform>();

            SetFullScreen(textRect);

            text.alignment =
                TextAlignmentOptions.Center;

            text.color =
                Color.white;

            return button;
        }

        private static void SetFullScreen(
            RectTransform rect)
        {
            rect.anchorMin =
                Vector2.zero;

            rect.anchorMax =
                Vector2.one;

            rect.offsetMin =
                Vector2.zero;

            rect.offsetMax =
                Vector2.zero;

            rect.anchoredPosition =
                Vector2.zero;
        }

        private static void DestroyConfirmation()
        {
            if (_confirmationCanvas != null)
            {
                UnityEngine.Object.Destroy(
                    _confirmationCanvas);

                _confirmationCanvas = null;
            }

            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            _confirmationOpen = false;
        }

        private static void ClearConfirmation()
        {
            _pendingConstructionWindow = null;
            _pendingHubGroup = null;
            _confirmationOpen = false;
            _relocationStarting = false;

            DestroyConfirmation();
        }

        // ============================================================
        // MESSAGE
        // ============================================================

        private static void ShowMessage(
            string message)
        {
            try
            {
                BaseHudHandler hud =
                    Managers.GetManager<BaseHudHandler>();

                if (hud != null)
                {
                    hud.DisplayCursorText(
                        message,
                        3f);
                }
            }
            catch
            {
                Plugin.Logger?.LogWarning(message);
            }
        }

        // ============================================================
        // RELOCATION GHOST HELPER
        // ============================================================

        private static ConstructibleGhost GetCurrentGhost(
            PlayerBuilder builder)
        {
            try
            {
                if (builder == null || GhostField == null)
                    return null;

                return GhostField.GetValue(builder)
                    as ConstructibleGhost;
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning(
                    $"[Epoch Relocation] Could not retrieve relocation ghost: {ex.Message}");

                return null;
            }
        }
    }
}
using UnityEngine;
using UnityEngine.UI;
using SpaceCraft;
using HarmonyLib;
using System;
using System.Collections;

namespace EpochNeural
{
    /// <summary>
    /// Epoch Hub Visuals - Zeolite with Pearl Shimmer
    /// </summary>
    public sealed class EpochHubVisuals : MonoBehaviour
    {
        // ============================================================
        // ZEOLITE WITH PEARL SHIMMER SETTINGS
        // ============================================================

        // Zeolite base colors
        private static readonly Color ZeoliteBase = new Color(0.85f, 0.82f, 0.78f, 1.0f);
        private static readonly Color ZeoliteBaseAlt = new Color(0.80f, 0.77f, 0.73f, 1.0f);

        // Pearl shimmer colors (iridescent)
        private static readonly Color PearlWhite = new Color(0.95f, 0.93f, 0.90f, 1.0f);
        private static readonly Color PearlBlue = new Color(0.60f, 0.70f, 0.85f, 1.0f);
        private static readonly Color PearlPink = new Color(0.85f, 0.70f, 0.75f, 1.0f);
        private static readonly Color PearlGold = new Color(0.85f, 0.78f, 0.60f, 1.0f);

        // Specular
        private static readonly Color ZeoliteSpecular = new Color(0.90f, 0.88f, 0.85f, 1.0f);

        private int framesToWait = 15;

        // ============================================================
        // EPOCH HUB PHYSICAL SCREEN
        // ============================================================
        private Image _physicalGroupImage;
        private Inventory _hubInventory;
        private Coroutine _screenCoroutine;
        private Sprite _originalScreenSprite;
        private bool _originalSpriteCaptured;
        private const float ScreenCycleSeconds = 3f;

        private int enforcementCycles = 120;
        private bool _visualsLocked = false;
        private Material _zeoliteMat;

        private Material CreateZeoliteMaterial()
        {
            if (_zeoliteMat != null) return _zeoliteMat;

            Shader standardShader = Shader.Find("Standard");
            if (standardShader == null)
            {
                standardShader = Shader.Find("Universal Render Pipeline/Lit");
            }
            if (standardShader == null)
            {
                Plugin.Logger?.LogError("[Epoch Visuals] No suitable shader found!");
                return null;
            }

            Material mat = new Material(standardShader);

            mat.SetColor("_Color", ZeoliteBase);
            mat.SetColor("_BaseColor", ZeoliteBase);
            mat.SetFloat("_Metallic", 0.05f);
            mat.SetFloat("_Glossiness", 0.75f);
            mat.SetFloat("_Smoothness", 0.75f);
            mat.SetColor("_SpecColor", ZeoliteSpecular);

            mat.EnableKeyword("_METALLICGLOSSMAP");
            mat.EnableKeyword("_SPECULAR_SETUP");

            _zeoliteMat = mat;
            return mat;
        }

        private Color GetPearlShimmer()
        {
            // Time-based shimmer that cycles through pearl colors
            float time = Time.time * 0.15f;

            // Cycle through different pearl colors
            float cycle1 = Mathf.Sin(time) * 0.5f + 0.5f;
            float cycle2 = Mathf.Sin(time + 1.0f) * 0.5f + 0.5f;
            float cycle3 = Mathf.Sin(time + 2.0f) * 0.5f + 0.5f;

            // Blend between colors
            Color shimmer = Color.Lerp(PearlWhite, PearlBlue, cycle1);
            shimmer = Color.Lerp(shimmer, PearlPink, cycle2 * 0.5f);
            shimmer = Color.Lerp(shimmer, PearlGold, cycle3 * 0.3f);

            // Keep it subtle (only 15% intensity shift)
            return Color.Lerp(ZeoliteBase, shimmer, 0.15f);
        }
        private void Start()
        {
            StartCoroutine(InitializePhysicalScreen());
        }

        private IEnumerator InitializePhysicalScreen()
        {
            // EpochUI already assigns the active Hub inventory when the
            // container is opened. Use that as the authoritative source.
            _hubInventory = EpochNeural.EpochHubInventory;

            // Keep InventoryAssociated as a fallback for world-object
            // initialization, but do not depend on it.
            if (_hubInventory == null)
            {
                InventoryAssociated inventoryAssociated =
                    GetComponentInParent<InventoryAssociated>();

                if (inventoryAssociated == null)
                {
                    inventoryAssociated = GetComponent<InventoryAssociated>();
                }

                if (inventoryAssociated != null)
                {
                    inventoryAssociated.GetInventory(OnHubInventoryReady);
                }
            }

            yield return null;

            FindPhysicalScreen();

            float timeout = Time.time + 5f;

            while (_hubInventory == null &&
                   Time.time < timeout)
            {
                _hubInventory = EpochNeural.EpochHubInventory;
                yield return new WaitForSeconds(0.25f);
            }

            if (_physicalGroupImage == null)
            {
                FindPhysicalScreen();
            }

            if (_physicalGroupImage != null &&
                _screenCoroutine == null)
            {
                _screenCoroutine =
                    StartCoroutine(PhysicalScreenCycler());

                Plugin.Logger?.LogInfo(
                    "[Epoch Screen] Physical Hub display cycling started.");
            }
            else if (_physicalGroupImage == null)
            {
                Plugin.Logger?.LogWarning(
                    "[Epoch Screen] Physical GroupImage could not be found.");
            }
        }

        private void OnHubInventoryReady(Inventory inventory)
        {
            if (inventory == null)
                return;

            _hubInventory = inventory;
            EpochNeural.EpochHubInventory = inventory;

            Plugin.Logger?.LogInfo(
                "[Epoch Screen] Hub inventory linked to physical display.");
        }

        private void FindPhysicalScreen()
        {
            Transform groupImageTransform =
                FindDeepChild(transform, "GroupImage");

            if (groupImageTransform == null)
            {
                Plugin.Logger?.LogWarning(
                    "[Epoch Screen] GroupImage not found under Epoch Hub Container.");
                return;
            }

            Image image =
                groupImageTransform.GetComponent<Image>();

            if (image == null)
            {
                Plugin.Logger?.LogWarning(
                    "[Epoch Screen] GroupImage exists but has no Image component.");
                return;
            }

            _physicalGroupImage = image;

            if (!_originalSpriteCaptured)
            {
                _originalScreenSprite = image.sprite;
                _originalSpriteCaptured = true;
            }

            Plugin.Logger?.LogInfo(
                "[Epoch Screen] Physical target found: " +
                GetTransformPath(groupImageTransform));
        }

        private static Transform FindDeepChild(
    Transform parent,
    string childName)
        {
            if (parent == null)
                return null;

            foreach (Transform child in parent)
            {
                if (child == null)
                    continue;

                if (child.name == childName)
                    return child;

                Transform result =
                    FindDeepChild(child, childName);

                if (result != null)
                    return result;
            }

            return null;
        }

        private IEnumerator PhysicalScreenCycler()
        {
            int currentStackIndex = 0;

            while (true)
            {
                yield return new WaitForSeconds(ScreenCycleSeconds);

                try
                {
                    if (_physicalGroupImage == null)
                    {
                        FindPhysicalScreen();

                        if (_physicalGroupImage == null)
                            continue;
                    }

                    // IMPORTANT:
                    // GetInsideWorldObjects() contains every physical item.
                    // That means 416 individual items can exist even though
                    // the Hub UI displays them as a much smaller number of
                    // compressed stacks.
                    //
                    // The outside screen must follow the SAME compressed
                    // stack model as the Hub UI, so we use
                    // ActiveFrameCompressedStacks here.
                    var compressedStacks =
                        EpochHubLogistics.ActiveFrameCompressedStacks;

                    if (compressedStacks == null || compressedStacks.Count == 0)
                    {
                        EpochHubLogistics.RefreshCompressedStacks();
                        compressedStacks =
                            EpochHubLogistics.ActiveFrameCompressedStacks;
                    }

                    if (compressedStacks == null || compressedStacks.Count == 0)
                    {
                        currentStackIndex = 0;

                        if (_originalSpriteCaptured)
                        {
                            _physicalGroupImage.sprite =
                                _originalScreenSprite;
                        }

                        Plugin.Logger?.LogInfo(
                            "[Epoch Screen] Cycle tick: Hub has no compressed stacks.");

                        continue;
                    }

                    // The compressed list represents actual occupied
                    // inventory stacks. One screen step = one stack.
                    if (currentStackIndex >= compressedStacks.Count)
                    {
                        currentStackIndex = 0;
                    }

                    var currentStack =
                        compressedStacks[currentStackIndex];

                    WorldObject currentItem = currentStack.wo;
                    int stackCount = currentStack.count;

                    if (currentItem == null)
                    {
                        Plugin.Logger?.LogWarning(
                            $"[Epoch Screen] Compressed stack " +
                            $"{currentStackIndex} has no WorldObject.");

                        currentStackIndex++;

                        if (currentStackIndex >= compressedStacks.Count)
                            currentStackIndex = 0;

                        continue;
                    }

                    Group group = currentItem.GetGroup();

                    if (group == null)
                    {
                        Plugin.Logger?.LogWarning(
                            $"[Epoch Screen] Compressed stack " +
                            $"{currentStackIndex} has no Group.");

                        currentStackIndex++;

                        if (currentStackIndex >= compressedStacks.Count)
                            currentStackIndex = 0;

                        continue;
                    }

                    Sprite icon = group.GetImage();

                    if (icon == null)
                    {
                        var groupData = group.GetGroupData();

                        if (groupData != null)
                        {
                            icon = groupData.icon;
                        }
                    }

                    string resourceId =
                        group.GetId() ?? "unknown";

                    if (icon != null)
                    {
                        _physicalGroupImage.sprite = icon;
                        _physicalGroupImage.color =
                            new Color(1f, 1f, 1f, 1f);
                        _physicalGroupImage.preserveAspect = true;

                        Plugin.Logger?.LogInfo(
                            $"[Epoch Screen] Display -> {resourceId} " +
                            $"STACK {currentStackIndex + 1}/" +
                            $"{compressedStacks.Count} " +
                            $"COUNT {stackCount}");
                    }
                    else
                    {
                        Plugin.Logger?.LogWarning(
                            $"[Epoch Screen] Icon is NULL for {resourceId} " +
                            $"at compressed stack " +
                            $"{currentStackIndex + 1}/" +
                            $"{compressedStacks.Count}.");
                    }

                    // Advance exactly ONE compressed stack.
                    currentStackIndex++;

                    if (currentStackIndex >= compressedStacks.Count)
                    {
                        currentStackIndex = 0;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger?.LogError(
                        $"[Epoch Screen] Cycle error: {ex}");

                    currentStackIndex = 0;
                }
            }
        }

        private static string GetTransformPath(Transform target)
        {
            if (target == null)
                return "<null>";

            string path = target.name;
            Transform current = target.parent;

            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }



        private void FixedUpdate()
        {
            // The visual material only needs to be enforced for the initial
            // setup period.  DO NOT destroy this component afterwards:
            // the same component owns the physical Hub screen cycler.
            if (!_visualsLocked)
            {
                if (framesToWait > 0)
                {
                    framesToWait--;
                    return;
                }

                try
                {
                    ExecuteSkinningPipeline();
                }
                catch (System.Exception ex)
                {
                    Plugin.Logger?.LogError($"[Epoch Visuals] Rendering pipeline exception: {ex}");
                }

                enforcementCycles--;

                if (enforcementCycles <= 0)
                {
                    _visualsLocked = true;
                    Plugin.Logger.LogInfo(
                        "[Epoch Visuals] Zeolite with Pearl Shimmer locked; physical screen controller remains active.");
                }
            }
        }

        private void ExecuteSkinningPipeline()
        {
            WorldObjectAssociated woa = GetComponentInParent<WorldObjectAssociated>() ?? GetComponent<WorldObjectAssociated>();
            if (woa == null || woa.GetWorldObject() == null || woa.GetWorldObject().GetGroup() == null) return;
            if (woa.GetWorldObject().GetGroup().GetId() != EpochNeural.HubId) return;

            Material zeoliteMat = CreateZeoliteMaterial();
            if (zeoliteMat == null) return;

            // Get current pearl shimmer color
            Color pearlColor = GetPearlShimmer();

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;

                GameObject obj = renderer.gameObject;
                if (obj == null) continue;

                string objName = obj.name;

                // SKIP hologram/glass parts
                if (objName.Contains("Holo") || objName.Contains("Glass") ||
                    objName.Contains("Screen") || objName.Contains("Transparent") ||
                    objName.Contains("Container3Holo"))
                {
                    continue;
                }

                Material[] materials = renderer.materials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;

                    string matName = materials[i].name;
                    if (matName.Contains("Holo") || matName.Contains("Glass") ||
                        matName.Contains("Screen") || matName.Contains("Transparent"))
                    {
                        continue;
                    }

                    Material newMat = new Material(zeoliteMat);

                    // Apply pearl shimmer color
                    newMat.SetColor("_Color", pearlColor);
                    newMat.SetColor("_BaseColor", pearlColor);

                    Texture mainTex = materials[i].GetTexture("_MainTex");
                    if (mainTex != null)
                        newMat.SetTexture("_MainTex", mainTex);

                    materials[i] = newMat;
                }
                renderer.materials = materials;
            }
        }
    }
}

//==================================================================
// BOOTSTRAP HOOK
//==================================================================
namespace EpochNeural
{
    [HarmonyPatch(typeof(WorldObjectsHandler), "InstantiateWorldObject")]
    internal static class EpochHubVisualsBootstrap
    {
        [HarmonyPostfix]
        private static void Postfix(GameObject __result)
        {
            if (__result == null) return;

            WorldObjectAssociated woa = __result.GetComponent<WorldObjectAssociated>();
            if (woa == null) return;

            WorldObject wo = woa.GetWorldObject();
            if (wo == null || wo.GetGroup() == null) return;

            if (wo.GetGroup().GetId() != EpochNeural.HubId) return;

            Transform containerTargetMesh = FindDeepChild(__result.transform, "Container");
            if (containerTargetMesh != null)
            {
                if (containerTargetMesh.gameObject.GetComponent<EpochHubVisuals>() == null)
                {
                    containerTargetMesh.gameObject.AddComponent<EpochHubVisuals>();
                    Plugin.Logger?.LogInfo("[Epoch Visuals] Zeolite with Pearl Shimmer applied.");
                }
            }
            else
            {
                if (__result.GetComponent<EpochHubVisuals>() == null)
                {
                    __result.AddComponent<EpochHubVisuals>();
                    Plugin.Logger?.LogInfo("[Epoch Visuals] Zeolite with Pearl Shimmer applied (fallback).");
                }
            }
        }

        private static Transform FindDeepChild(Transform parent, string childName)
        {
            if (parent == null) return null;
            foreach (Transform child in parent)
            {
                if (child.name == childName) return child;
                Transform result = FindDeepChild(child, childName);
                if (result != null) return result;
            }
            return null;
        }


    }
}
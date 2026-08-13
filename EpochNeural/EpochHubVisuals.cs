using UnityEngine;
using SpaceCraft;
using HarmonyLib;

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
        private int enforcementCycles = 120;
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

        private void FixedUpdate()
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
                Plugin.Logger.LogError($"[Epoch Visuals] Rendering pipeline exception: {ex}");
            }

            enforcementCycles--;
            if (enforcementCycles <= 0)
            {
                Plugin.Logger.LogInfo("[Epoch Visuals] Zeolite with Pearl Shimmer locked.");
                Destroy(this);
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
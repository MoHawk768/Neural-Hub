using UnityEngine;
using SpaceCraft;
using HarmonyLib;

namespace EpochNeural
{
    /// <summary>
    /// Permanent production visual override for the Epoch Hub.
    /// Transforms the chassis panels into a highly polished, premium reflective Gold finish.
    /// This component NEVER touches inventories, UI, scrolling, or data systems.
    /// </summary>
    public sealed class EpochHubVisuals : MonoBehaviour
    {
        // Deep, rich saturated golden albedo constants to prevent white base glare washouts
        private static readonly Color GoldBaseColor = new Color(1.0f, 0.72f, 0.15f, 1.0f);

        // Golden specular reflection highlights to force pure gold reflections under harsh lighting
        private static readonly Color GoldSpecularColor = new Color(1.0f, 0.85f, 0.40f, 1.0f);

        private static readonly Color UraniumGreen = new Color(0.20f, 1.00f, 0.15f, 1f);

        private int framesToWait = 15;      // Buffer before the very first paint pass
        private int enforcementCycles = 120; // Continuously locks materials for ~2 seconds to defeat GhostFx sweeps

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
                Plugin.Logger.LogInfo("[Epoch Visuals] Construction animation sweeps out-waited successfully. Gold finish locked.");
                // Self-destructs only after the game completely finishes trying to overwrite our textures!
                Destroy(this);
            }
        }

        private void ExecuteSkinningPipeline()
        {
            WorldObjectAssociated woa = GetComponentInParent<WorldObjectAssociated>() ?? GetComponent<WorldObjectAssociated>();
            if (woa == null || woa.GetWorldObject() == null || woa.GetWorldObject().GetGroup() == null) return;
            if (woa.GetWorldObject().GetGroup().GetId() != EpochNeural.HubId) return;

            MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            int baseColorId = Shader.PropertyToID("_BaseColor");
            int colorId = Shader.PropertyToID("_Color");
            int metallicId = Shader.PropertyToID("_Metallic");
            int smoothnessId = Shader.PropertyToID("_Smoothness");
            int glossinessId = Shader.PropertyToID("_Glossiness");
            int specColorId = Shader.PropertyToID("_SpecColor");
            int specHighlightsId = Shader.PropertyToID("_SpecularHighlights");
            int emissionColorId = Shader.PropertyToID("_EmissionColor");
            int emissionId = Shader.PropertyToID("_Emission");

            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer == null) continue;

                // Protect text displays and UI screen overlays from receiving asset templates
                string objName = renderer.gameObject.name;
                if (objName == "Screen" || objName == "Text" || objName == "Screen_A" || objName.Contains("Ghost"))
                    continue;

                Material[] originalMaterials = renderer.sharedMaterials;
                Material[] clonedMaterials = new Material[originalMaterials.Length];
                bool modified = false;

                for (int i = 0; i < originalMaterials.Length; i++)
                {
                    Material originalMat = originalMaterials[i];
                    if (originalMat == null) continue;

                    string matName = originalMat.name;
                    string shaderName = originalMat.shader != null ? originalMat.shader.name : "";

                    // --- PREMIUM POLISHED GOLD CHASSIS OVERRIDE STEP ---
                    if (matName.Contains("TPC_Wall_Atlas") || matName.Contains("Wall_Atlas") ||
                        matName.Contains("GenericGrey") || matName.Contains("ConstructMaterial") ||
                        shaderName.Contains("dissolve"))
                    {
                        // Clone the material to preserve baseline textures while overwriting the color maps
                        Material clonedMat = new Material(originalMat);

                        // Inject warm golden albedo base attributes
                        if (clonedMat.HasProperty(baseColorId)) clonedMat.SetColor(baseColorId, GoldBaseColor);
                        if (clonedMat.HasProperty(colorId)) clonedMat.SetColor(colorId, GoldBaseColor);

                        // Configure full physical metallic specular calculations and mirror gloss
                        if (clonedMat.HasProperty(metallicId)) clonedMat.SetFloat(metallicId, 1.0f);
                        if (clonedMat.HasProperty(smoothnessId)) clonedMat.SetFloat(smoothnessId, 0.94f);
                        if (clonedMat.HasProperty(glossinessId)) clonedMat.SetFloat(glossinessId, 0.94f);

                        // Overdrive specular highlight paths to catch light vectors in gold tinting
                        if (clonedMat.HasProperty(specColorId)) clonedMat.SetColor(specColorId, GoldSpecularColor);
                        if (clonedMat.HasProperty(specHighlightsId)) clonedMat.SetFloat(specHighlightsId, 1.0f);

                        clonedMat.EnableKeyword("_SPECULAR_SETUP");
                        clonedMat.EnableKeyword("_METALLICGLOSSMAP");
                        clonedMat.DisableKeyword("_EMISSION");

                        clonedMaterials[i] = clonedMat;
                        modified = true;
                    }
                    // --- HOLOGRAM RECOLOR STEP ---
                    else if (matName.Contains("Container3Holo"))
                    {
                        Material clonedMat = new Material(originalMat);

                        if (clonedMat.HasProperty(baseColorId)) clonedMat.SetColor(baseColorId, UraniumGreen);
                        if (clonedMat.HasProperty(colorId)) clonedMat.SetColor(colorId, UraniumGreen);
                        if (clonedMat.HasProperty(emissionId)) clonedMat.EnableKeyword("_EMISSION");
                        if (clonedMat.HasProperty(emissionColorId)) clonedMat.SetColor(emissionColorId, UraniumGreen * 8.0f);

                        clonedMaterials[i] = clonedMat;
                        modified = true;
                    }
                    else
                    {
                        clonedMaterials[i] = originalMat;
                    }
                }

                if (modified)
                {
                    renderer.materials = clonedMaterials;
                }
            }
        }
    }
}

//==================================================================
// SEPARATE BOOTSTRAP HOOK (Keeps files perfectly isolated)
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

            Transform containerTargetMesh = FindDeepChild(__result.transform, "Container");
            if (containerTargetMesh != null)
            {
                if (containerTargetMesh.gameObject.GetComponent<EpochHubVisuals>() == null)
                {
                    containerTargetMesh.gameObject.AddComponent<EpochHubVisuals>();
                }
            }
            else
            {
                if (__result.GetComponent<EpochHubVisuals>() == null)
                {
                    __result.AddComponent<EpochHubVisuals>();
                }
            }
        }

        private static Transform FindDeepChild(Transform parent, string childName)
        {
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

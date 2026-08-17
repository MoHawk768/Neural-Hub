using System;
using System.Collections.Generic;
using HarmonyLib;
using SpaceCraft;
using UnityEngine;

namespace EpochNeural
{
    /// <summary>
    /// Locks each Epoch Node Extractor to the single physical vein selected
    /// at placement. The vanilla WorldObject link is used when a real vanilla
    /// vein WorldUniqueId exists. When the current runtime exposes no
    /// WorldUniqueId on the vein, Epoch uses the physical vein signature and
    /// forces MachineGenerator to use that vein's resource/groups.
    /// </summary>
    [HarmonyPatch(typeof(MachineGenerator), "SetMiningRayGeneration")]
    internal static class EpochDrillVeinBindingPatch
    {
        private static readonly System.Reflection.FieldInfo MiningRaysCastedField =
            typeof(MachineGenerator).GetField(
                "_miningRaysCasted",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);

        private static readonly System.Reflection.FieldInfo MiningTerraStageField =
            typeof(MachineGenerator).GetField(
                "_miningTerraStage",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);

        [HarmonyPrefix]
        private static bool Prefix(MachineGenerator __instance)
        {
            try
            {
                var associated =
                    __instance.GetComponent<WorldObjectAssociated>();

                WorldObject drillWorldObject =
                    associated?.GetWorldObject();

                if (drillWorldObject == null ||
                    drillWorldObject.GetGroup()?.GetId() != "Epoch_Node_Drill")
                {
                    return true;
                }

                // Always resolve the physical vein from the extractor's
                // position. This is safe because the placement system has
                // already validated/registered the exact physical vein.
                MachineGenerationGroupVein vein =
                    EpochDrillManager.FindVeinUnderPosition(
                        __instance.transform.position);

                if (vein == null)
                {
                    Plugin.Logger?.LogWarning(
                        $"[Epoch Drill] No physical vein found for extractor " +
                        $"[{drillWorldObject.GetId()}].");

                    return false;
                }

                string resourceName =
                    EpochDrillManager.GetVeinResourceName(vein);

                // Lock vanilla generator to ONLY the selected vein's resource.
                __instance.oreAllowedToMine =
                    new List<DataConfig.OreVeinIdentifer>
                    {
                        vein.GetOreVeinIdentifer()
                    };

                __instance.onlyUseFirstVeinDetected = true;

                // Use only the groups belonging to the selected physical vein.
                var veinGroups = vein.GetGroups();

                if (veinGroups != null && veinGroups.Count > 0)
                {
                    __instance.groupDatas =
                        new List<GroupData>(veinGroups);
                }

                // If the vein exposes a genuine vanilla WorldUniqueId,
                // preserve the game's native finite-vein linkage.
                int nativeVeinId = 0;

                try
                {
                    var uniqueId =
                        vein.GetComponent<WorldUniqueId>();

                    if (uniqueId != null)
                        nativeVeinId = uniqueId.GetWorldUniqueId();
                }
                catch
                {
                    nativeVeinId = 0;
                }

                if (nativeVeinId > 0)
                {
                    drillWorldObject.SetLinkedWorldObject(nativeVeinId);

                    Plugin.Logger?.LogInfo(
                        $"[Epoch Drill] Extractor [{drillWorldObject.GetId()}] " +
                        $"native-linked to vein [{nativeVeinId}] " +
                        $"resource [{resourceName}].");
                }
                else
                {
                    // IMPORTANT:
                    // Do NOT write the Epoch synthetic vein key into
                    // LinkedWorldObject. That field must contain a real
                    // WorldObject ID or remain zero.
                    Plugin.Logger?.LogInfo(
                        $"[Epoch Drill] Extractor [{drillWorldObject.GetId()}] " +
                        $"locked by physical signature to resource [{resourceName}]. " +
                        $"Vanilla vein has no WorldUniqueId in this runtime.");
                }

                try
                {
                    MiningRaysCastedField?.SetValue(__instance, true);

                    var stage =
                        Managers.GetManager<TerraformStagesHandler>()?
                            .GetCurrentGlobalStage();

                    if (stage != null)
                        MiningTerraStageField?.SetValue(
                            __instance,
                            stage);
                }
                catch
                {
                }

                // Epoch has supplied the exact physical vein and resource.
                // Prevent vanilla from performing another unrestricted scan.
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError(
                    $"[Epoch Drill] Vein binding patch failed: {ex}");

                return true;
            }
        }
    }
}
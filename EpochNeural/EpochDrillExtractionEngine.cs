using HarmonyLib;
using SpaceCraft;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EpochNeural
{
    public class EpochDrillExtractionEngine : MonoBehaviour
    {
        private static EpochDrillExtractionEngine _instance;
        private float _nextExtractionTickTime;
        private const float EXTRACTION_INTERVAL = 22f;
        private const int ORES_PER_PULSE = 5;

        public static void InitializeEngine(GameObject persistentContainer)
        {
            if (_instance != null || persistentContainer == null)
                return;

            _instance = persistentContainer.AddComponent<EpochDrillExtractionEngine>();
            Plugin.Logger?.LogInfo("[Epoch Extraction] Engine initialized.");
        }

        private void Start()
        {
            _nextExtractionTickTime = Time.time + EXTRACTION_INTERVAL;
            Plugin.Logger?.LogInfo(
                $"[Epoch Extraction] Background drill engine active. Interval: {EXTRACTION_INTERVAL}s, {ORES_PER_PULSE} ore per pulse.");
        }

        private void Update()
        {
            if (Time.time < _nextExtractionTickTime)
                return;

            try
            {
                ExecuteNetworkExtractionPulse();
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Extraction] Pulse failure: {ex}");
            }
            finally
            {
                _nextExtractionTickTime = Time.time + EXTRACTION_INTERVAL;
            }
        }

        private static void ExecuteNetworkExtractionPulse()
        {
            var worldObjectsList = WorldObjectsHandler.Instance?.GetConstructedWorldObjects();

            if (worldObjectsList == null)
            {
                Plugin.Logger?.LogDebug("[Epoch Extraction] No world objects found.");
                return;
            }


            bool generatedAnything = false;

            foreach (WorldObject wo in worldObjectsList)
            {
                if (wo == null || wo.GetGroup() == null ||
                    wo.GetGroup().GetId() != EpochDrillAsset.DrillId)
                    continue;

                int drillId = wo.GetId();
                string targetOreId = EpochDrillManager.GetBoundResourceId(drillId);

                if (string.IsNullOrWhiteSpace(targetOreId))
                {
                    Plugin.Logger?.LogWarning(
                        $"[Epoch Extraction] Extractor [{drillId}] has no physical-vein resource binding; skipping.");
                    continue;
                }

                if (EpochHubLogistics.IsResourceSlotFull(targetOreId))
                {
                    Plugin.Logger?.LogDebug(
                        $"[Epoch Extraction] Slot full for {targetOreId}, skipping extractor [{drillId}].");
                    continue;
                }

                Group oreGroup = GroupsHandler.GetGroupViaId(targetOreId);
                if (oreGroup == null)
                {
                    Plugin.Logger?.LogWarning(
                        $"[Epoch Extraction] Bound resource group [{targetOreId}] not found for extractor [{drillId}].");
                    continue;
                }

                try
                {
                    int inventoryId = wo.GetLinkedInventoryId();
                    if (inventoryId == 0)
                    {
                        Plugin.Logger?.LogDebug(
                            $"[Epoch Extraction] Extractor {drillId} has no inventory.");
                        continue;
                    }

                    Inventory extractorInventory =
                        InventoriesHandler.Instance.GetInventoryById(inventoryId);

                    if (extractorInventory == null || extractorInventory.IsFull())
                    {
                        Plugin.Logger?.LogDebug(
                            $"[Epoch Extraction] Extractor {drillId} inventory unavailable/full.");
                        continue;
                    }

                    int extractedCount = 0;

                    // PROGRESSION RULE DETECTOR: The drill deposits items into its local box as long as the resource identity exists in the Hub's learned registry.
                    if (EpochVacuumSystem.IsInitialized() && EpochVacuumSystem.IsResourceLearned(targetOreId))
                    {
                        for (int i = 0; i < ORES_PER_PULSE; i++)
                        {
                            if (extractorInventory.IsFull())
                                break;

                            // AUTHORITATIVE DATABASE INTERLOCK
                            // Instantiates a tracking ID, registers it in the master ledger, and drops it cleanly into the machine's grid array
                            InventoriesHandler.Instance.AddItemToInventory(oreGroup, extractorInventory, (success, newObjectId) =>
                            {
                                if (success)
                                {
                                    extractedCount++;
                                }
                            });
                        }
                    }

                    else
                    {
                        Plugin.Logger?.LogDebug($"[Epoch Extraction] Skipping drill pulse for {targetOreId} - resource has not been introduced/learned by the hub grid yet.");
                    }


                    if (extractedCount > 0)
                    {
                        generatedAnything = true;
                        string displayName =
                            EpochDrillManager.GetBoundResourceName(drillId);

                        Plugin.Logger?.LogInfo(
                            $"[Epoch Extraction] Generated {extractedCount} {displayName} " +
                            $"([{targetOreId}]) in extractor {drillId} from locked physical vein.");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger?.LogError(
                        $"[Epoch Extraction] Failed for extractor [{drillId}] bound to [{targetOreId}]: {ex}");
                }
            }

            if (generatedAnything)
            {
                EpochHubLogistics.RefreshCompressedStacks();
                EpochDrillManager.RefreshNetworkTelemetry();
                Plugin.Logger?.LogInfo(
                    "[Epoch Extraction] Physical-vein extraction transaction pulse finalized.");
            }
        }

        private void OnDestroy()
        {
            _instance = null;
            Plugin.Logger?.LogInfo("[Epoch Extraction] Engine destroyed.");
        }
    }

    // ============================================================
    // SEPARATE EXTRACTOR SEPARATION ENGINE
    // Wipes native production templates during initialization so vanilla drops generation
    // ============================================================
    [HarmonyPatch(typeof(MachineGenerator), "SetMiningRayGeneration")]
    internal static class EpochDisableVanillaDrillGenerationPatch
    {
        [HarmonyPostfix]
        private static void Postfix(MachineGenerator __instance)
        {
            if (__instance == null) return;

            var worldObjectAssociated = __instance.GetComponent<WorldObjectAssociated>();
            if (worldObjectAssociated != null)
            {
                WorldObject wo = worldObjectAssociated.GetWorldObject();
                if (wo != null && wo.GetGroup()?.GetId() == "Epoch_Node_Drill")
                {
                    // Wipe out vanilla's allowed list and data templates for this specific object.
                    // With zero production definitions, the vanilla ticking loop handles zero items.
                    __instance.oreAllowedToMine = new List<DataConfig.OreVeinIdentifer>();
                    __instance.groupDatas = new List<GroupData>();

                    Plugin.Logger?.LogInfo($"[Epoch Kill Vanilla] Native production template silenced safely for drill ID: {wo.GetId()}");
                }
            }
        }
    }
}

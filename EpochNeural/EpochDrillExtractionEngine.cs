using System;
using System.Collections.Generic;
using SpaceCraft;
using UnityEngine;

namespace EpochNeural
{
    public class EpochDrillExtractionEngine : MonoBehaviour
    {
        private static EpochDrillExtractionEngine _instance;
        private float _nextExtractionTickTime;
        private const float EXTRACTION_INTERVAL = 70f;
        private const int ORES_PER_PULSE = 1;

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
            var worldObjectsList =
                WorldObjectsHandler.Instance?.GetConstructedWorldObjects();
            var hubInventory = EpochNeural.EpochHubInventory;

            if (worldObjectsList == null || hubInventory == null)
            {
                Plugin.Logger?.LogDebug(
                    "[Epoch Extraction] No hub or world objects found.");
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

                    for (int i = 0; i < ORES_PER_PULSE; i++)
                    {
                        if (EpochHubLogistics.IsResourceSlotFull(targetOreId) ||
                            extractorInventory.IsFull())
                            break;

                        WorldObject newOre =
                            WorldObjectsHandler.Instance.CreateNewWorldObject(oreGroup);

                        if (newOre == null)
                            break;

                        InventoriesHandler.Instance.AddWorldObjectToInventory(
                            newOre,
                            extractorInventory);

                        extractedCount++;
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
}
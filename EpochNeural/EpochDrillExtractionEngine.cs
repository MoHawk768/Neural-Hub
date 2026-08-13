using System;
using System.Collections;
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
        private const int ORES_PER_PULSE = 1; // Reverted to 1

        public static void InitializeEngine(GameObject persistentContainer)
        {
            if (_instance != null || persistentContainer == null) return;
            _instance = persistentContainer.AddComponent<EpochDrillExtractionEngine>();
            Plugin.Logger?.LogInfo("[Epoch Extraction] Engine initialized.");
        }

        private void Start()
        {
            _nextExtractionTickTime = Time.time + EXTRACTION_INTERVAL;
            Plugin.Logger?.LogInfo($"[Epoch Extraction] Background drill engine active. Interval: {EXTRACTION_INTERVAL}s, {ORES_PER_PULSE} ore per pulse.");
        }

        private void Update()
        {
            if (Time.time < _nextExtractionTickTime) return;

            try
            {
                ExecuteNetworkExtractionPulse();
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[Epoch Extraction] Pulse failure: {ex.Message}");
            }
            finally
            {
                _nextExtractionTickTime = Time.time + EXTRACTION_INTERVAL;
            }
        }

        private static string GetOreIdFromSector(string sectorGroupId)
        {
            if (string.IsNullOrEmpty(sectorGroupId)) return "iron";

            var sectorLower = sectorGroupId.ToLowerInvariant();

            if (sectorLower.Contains("superalloy") || sectorLower.Contains("super_alloy"))
                return "superalloy";
            if (sectorLower.Contains("zeolite"))
                return "zeolite";
            if (sectorLower.Contains("osmium"))
                return "osmium";
            if (sectorLower.Contains("uranium"))
                return "uranium";
            if (sectorLower.Contains("aluminum") || sectorLower.Contains("alu"))
                return "aluminum";
            if (sectorLower.Contains("iridium"))
                return "iridium";
            if (sectorLower.Contains("sulfur"))
                return "sulfur";
            if (sectorLower.Contains("iron"))
                return "iron";

            return "iron";
        }

        private static void ExecuteNetworkExtractionPulse()
        {
            var worldObjectsList = WorldObjectsHandler.Instance?.GetConstructedWorldObjects();
            var hubInventory = EpochNeural.EpochHubInventory;
            if (worldObjectsList == null || hubInventory == null)
            {
                Plugin.Logger?.LogDebug("[Epoch Extraction] No hub or world objects found.");
                return;
            }

            Type sectorsType = Type.GetType("SpaceCraft.SectorsHandler, Assembly-CSharp");
            object sectorsHandler = sectorsType != null ? UnityEngine.Object.FindFirstObjectByType(sectorsType) : null;
            if (sectorsHandler == null)
            {
                Plugin.Logger?.LogWarning("[Epoch Extraction] SectorsHandler not found.");
                return;
            }

            bool generatedAnything = false;

            foreach (WorldObject wo in worldObjectsList)
            {
                if (wo == null || wo.GetGroup() == null || wo.GetGroup().GetId() != EpochDrillAsset.DrillId)
                    continue;

                string sectorGroupId = "UnknownBiome";
                try
                {
                    var sector = sectorsHandler.GetType().GetMethod("GetSectorWithPosition")?.Invoke(sectorsHandler, new object[] { wo.GetPosition() });
                    if (sector != null)
                    {
                        sectorGroupId = sector.GetType().GetMethod("GetGroupId")?.Invoke(sector, null) as string;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Logger?.LogError($"[Epoch Extraction] Sector detection failed: {ex.Message}");
                }

                string targetOreId = GetOreIdFromSector(sectorGroupId);

                if (EpochHubLogistics.IsResourceSlotFull(targetOreId))
                {
                    Plugin.Logger?.LogDebug($"[Epoch Extraction] Slot full for {targetOreId}, skipping.");
                    continue;
                }

                Group oreGroup = GroupsHandler.GetGroupViaId(targetOreId);
                if (oreGroup != null)
                {
                    try
                    {
                        int extractedCount = 0;

                        // Extract 1 ore per pulse (vanilla)
                        for (int i = 0; i < ORES_PER_PULSE; i++)
                        {
                            if (EpochHubLogistics.IsResourceSlotFull(targetOreId))
                            {
                                Plugin.Logger?.LogDebug($"[Epoch Extraction] Slot became full for {targetOreId}, stopping early.");
                                break;
                            }

                            int inventoryId = wo.GetLinkedInventoryId();
                            if (inventoryId == 0)
                            {
                                Plugin.Logger?.LogDebug($"[Epoch Extraction] Extractor {wo.GetId()} has no inventory.");
                                break;
                            }

                            Inventory extractorInventory = InventoriesHandler.Instance.GetInventoryById(inventoryId);
                            if (extractorInventory == null)
                            {
                                Plugin.Logger?.LogDebug($"[Epoch Extraction] Extractor {wo.GetId()} inventory not found.");
                                break;
                            }

                            if (extractorInventory.IsFull())
                            {
                                Plugin.Logger?.LogDebug($"[Epoch Extraction] Extractor inventory full, stopping.");
                                break;
                            }

                            WorldObject newOre = WorldObjectsHandler.Instance.CreateNewWorldObject(oreGroup);
                            if (newOre != null)
                            {
                                InventoriesHandler.Instance.AddWorldObjectToInventory(newOre, extractorInventory);
                                extractedCount++;
                                Plugin.Logger?.LogDebug($"[Epoch Extraction] Generated {targetOreId} in extractor inventory.");
                            }
                        }

                        if (extractedCount > 0)
                        {
                            generatedAnything = true;
                            Plugin.Logger?.LogInfo($"[Epoch Extraction] Generated {extractedCount} {targetOreId} in extractor {wo.GetId()} from {sectorGroupId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Logger?.LogError($"[Epoch Extraction] Failed to generate {targetOreId}: {ex.Message}");
                    }
                }
                else
                {
                    Plugin.Logger?.LogWarning($"[Epoch Extraction] Unknown resource type: {targetOreId}");
                }
            }

            if (generatedAnything)
            {
                EpochHubLogistics.RefreshCompressedStacks();
                EpochDrillManager.RefreshNetworkTelemetry();
                Plugin.Logger?.LogInfo("[Epoch Extraction] Biome transmission transaction pulse finalized.");
            }
        }

        private void OnDestroy()
        {
            _instance = null;
            Plugin.Logger?.LogInfo("[Epoch Extraction] Engine destroyed.");
        }
    }
}
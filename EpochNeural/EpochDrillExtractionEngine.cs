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
        private const float EXTRACTION_INTERVAL = 30f;

        public static void InitializeEngine(GameObject persistentContainer)
        {
            if (_instance != null || persistentContainer == null) return;
            _instance = persistentContainer.AddComponent<EpochDrillExtractionEngine>();
        }

        private void Start()
        {
            _nextExtractionTickTime = Time.time + EXTRACTION_INTERVAL;
            Plugin.Logger?.LogInfo("[Epoch Extraction] Background drill engine active.");
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

        private static void ExecuteNetworkExtractionPulse()
        {
            var worldObjectsList = WorldObjectsHandler.Instance?.GetConstructedWorldObjects();
            var hubInventory = EpochNeural.EpochHubInventory;
            if (worldObjectsList == null || hubInventory == null) return;

            Type sectorsType = Type.GetType("SpaceCraft.SectorsHandler, Assembly-CSharp");
            object sectorsHandler = sectorsType != null ? UnityEngine.Object.FindFirstObjectByType(sectorsType) : null;
            if (sectorsHandler == null) return;

            bool generatedAnything = false;

            foreach (WorldObject wo in worldObjectsList)
            {
                if (wo == null || wo.GetGroup() == null || wo.GetGroup().GetId() != "Epoch_Node_Drill")
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
                catch { }

                string targetOreId = "iron";
                if (sectorGroupId.Contains("Sulfur") || sectorGroupId.Contains("Sulfur")) targetOreId = "sulfur";
                else if (sectorGroupId.Contains("Iridium")) targetOreId = "iridium";
                else if (sectorGroupId.Contains("Alu") || sectorGroupId.Contains("Aluminum")) targetOreId = "aluminum";
                else if (sectorGroupId.Contains("Uranium")) targetOreId = "uranium";
                else if (sectorGroupId.Contains("Osmium")) targetOreId = "osmium";
                else if (sectorGroupId.Contains("Zeolite")) targetOreId = "zeolite";
                else if (sectorGroupId.Contains("Super")) targetOreId = "superalloy";

                // FIXED: Declared as universal base Group type to bypass compile reference conversion restrictions
                Group oreGroup = GroupsHandler.GetGroupViaId(targetOreId);
                if (oreGroup != null && !EpochHubLogistics.IsResourceSlotFull(targetOreId))
                {
                    try
                    {
                        // FIXED: Safely passing the generic blueprint asset into the creator method
                        WorldObject newOre = WorldObjectsHandler.Instance.CreateNewWorldObject(oreGroup);
                        InventoriesHandler.Instance.AddWorldObjectToInventory(newOre, hubInventory);
                        generatedAnything = true;
                    }
                    catch { }
                }
            }

            if (generatedAnything)
            {
                EpochHubLogistics.RefreshCompressedStacks();
                EpochDrillManager.RefreshNetworkTelemetry();
                Plugin.Logger?.LogInfo("[Epoch Extraction] Biome transmission transaction pulse finalized successfully.");
            }
        }
    }
}

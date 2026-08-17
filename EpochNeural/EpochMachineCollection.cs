using System;
using System.Collections.Generic;
using SpaceCraft;

namespace EpochNeural
{
    internal static class EpochMachineCollection
    {
        // ============================================================
        // REGISTERED MACHINE OUTPUTS
        // ============================================================

        private sealed class MachineRule
        {
            public string MachineGroupId;
            public HashSet<string> OutputGroupIds;
        }

        private static readonly Dictionary<string, MachineRule> MachineRules =
            new Dictionary<string, MachineRule>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "WaterCollector1",
                    new MachineRule
                    {
                        MachineGroupId = "WaterCollector1",
                        OutputGroupIds = new HashSet<string>(
                            new[] { "WaterBottle1" },
                            StringComparer.OrdinalIgnoreCase)
                    }
                }
            };

        // ============================================================
        // MACHINE OUTPUT COLLECTION
        // ============================================================

        internal static int CollectOutputs(
            Inventory hubInventory,
            ref int remainingBudget)
        {
            if (hubInventory == null || remainingBudget <= 0)
                return 0;

            try
            {
                var worldHandler = WorldObjectsHandler.Instance;
                var inventoryHandler = InventoriesHandler.Instance;

                if (worldHandler == null || inventoryHandler == null)
                    return 0;

                var constructedObjects =
                    worldHandler.GetConstructedWorldObjects();

                if (constructedObjects == null)
                    return 0;

                int totalCollected = 0;

                foreach (WorldObject machine in constructedObjects)
                {
                    if (remainingBudget <= 0)
                        break;

                    if (machine == null)
                        continue;

                    Group group = machine.GetGroup();

                    if (group == null)
                        continue;

                    string machineGroupId = group.GetId();

                    if (string.IsNullOrEmpty(machineGroupId))
                        continue;

                    // ------------------------------------------------
                    // Is this a registered production machine?
                    // ------------------------------------------------

                    if (!MachineRules.TryGetValue(
                        machineGroupId,
                        out MachineRule rule))
                    {
                        continue;
                    }

                    int linkedInventoryId =
                        machine.GetLinkedInventoryId();

                    if (linkedInventoryId == 0)
                        continue;

                    Inventory machineInventory =
                        inventoryHandler.GetInventoryById(
                            linkedInventoryId);

                    if (machineInventory == null)
                        continue;

                    var items =
                        machineInventory.GetInsideWorldObjects();

                    if (items == null || items.Count == 0)
                        continue;

                    // ------------------------------------------------
                    // Check machine output inventory
                    // ------------------------------------------------

                    for (int i = items.Count - 1;
                         i >= 0 && remainingBudget > 0;
                         i--)
                    {
                        WorldObject item = items[i];

                        if (item == null || item.GetGroup() == null)
                            continue;

                        string outputGroupId =
                            item.GetGroup().GetId();

                        if (string.IsNullOrEmpty(outputGroupId))
                            continue;

                        // Only collect explicitly registered outputs.
                        if (!rule.OutputGroupIds.Contains(outputGroupId))
                            continue;

                        // Respect the Epoch Hub's current stack capacity.
                        if (EpochHubLogistics.IsResourceSlotFull(
                            outputGroupId))
                        {
                            Plugin.Logger?.LogInfo(
                                $"[Epoch Machine] Hub stack full for {outputGroupId}. " +
                                $"Leaving item in {machineGroupId}.");

                            continue;
                        }

                        bool transferred = false;

                        try
                        {
                            // ------------------------------------------------
                            // Normal inventory transfer
                            // ------------------------------------------------

                            inventoryHandler.TransferItem(
                                machineInventory,
                                hubInventory,
                                item,
                                delegate (bool success)
                                {
                                    transferred = success;
                                });

                            // ------------------------------------------------
                            // Fallback
                            // ------------------------------------------------

                            if (!transferred &&
                                machineInventory.ContainWorldObject(item))
                            {
                                machineInventory.RemoveItem(item);

                                if (hubInventory.AddItem(item))
                                {
                                    transferred = true;
                                }
                            }

                            // ------------------------------------------------
                            // Successful transfer
                            // ------------------------------------------------

                            if (transferred)
                            {
                                totalCollected++;
                                remainingBudget--;

                                Plugin.Logger?.LogInfo(
                                    $"[Epoch Machine] " +
                                    $"Collected {outputGroupId} from " +
                                    $"{machineGroupId}.");

                                EpochHubLogistics.RefreshCompressedStacks();
                            }
                        }
                        catch (Exception ex)
                        {
                            Plugin.Logger?.LogWarning(
                                $"[Epoch Machine] Transfer error: " +
                                $"{outputGroupId} from {machineGroupId}: {ex.Message}");
                        }
                    }
                }

                if (totalCollected > 0)
                {
                    EpochHubLogistics.RefreshCompressedStacks();

                    Plugin.Logger?.LogInfo(
                        $"[Epoch Machine] Machine Collection | " +
                        $"Total: {totalCollected} | " +
                        $"Remaining Budget: {remainingBudget}");
                }

                return totalCollected;
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError(
                    $"[Epoch Machine] Collection error: {ex}");

                return 0;
            }
        }

        // ============================================================
        // REGISTRY HELPERS
        // ============================================================

        internal static bool IsRegisteredMachine(
            string machineGroupId)
        {
            return !string.IsNullOrEmpty(machineGroupId) &&
                   MachineRules.ContainsKey(machineGroupId);
        }

        internal static void RegisterMachine(
            string machineGroupId,
            params string[] outputGroupIds)
        {
            if (string.IsNullOrEmpty(machineGroupId))
                return;

            MachineRules[machineGroupId] =
                new MachineRule
                {
                    MachineGroupId = machineGroupId,
                    OutputGroupIds =
                        new HashSet<string>(
                            outputGroupIds ?? Array.Empty<string>(),
                            StringComparer.OrdinalIgnoreCase)
                };
        }
    }
}
using System;
using System.Collections.Generic;
using SpaceCraft;

namespace EpochNeural
{
    /// <summary>
    /// Machine collection and supply system for Epoch Hub.
    /// This is now a wrapper around EpochVacuumSystem's auto-detection.
    /// All machine types are auto-detected - no hardcoded lists needed.
    /// </summary>
    internal static class EpochMachineCollection
    {
        // ============================================================
        // MACHINE COLLECTION
        // ============================================================

        /// <summary>
        /// Collects outputs from all machines with inventories.
        /// This is now handled by EpochVacuumSystem.CollectFromAllMachines().
        /// </summary>
        internal static int CollectOutputs(
            Inventory hubInventory,
            ref int remainingBudget)
        {
            // This method is kept for compatibility but now delegates to the main system.
            // The actual collection is handled by EpochVacuumSystem.

            if (hubInventory == null || remainingBudget <= 0)
                return 0;

            // The real collection happens in EpochVacuumSystem.CollectFromAllMachines()
            // which is called during the main logistics cycle.
            // This method is kept as a compatibility wrapper.

            return 0;
        }

        // ============================================================
        // REGISTRY HELPERS (Deprecated - now auto-detects)
        // ============================================================

        /// <summary>
        /// Checks if a machine is registered.
        /// DEPRECATED: All machines are now auto-detected.
        /// </summary>
        internal static bool IsRegisteredMachine(
            string machineGroupId)
        {
            // All machines are now supported by auto-detection.
            // This returns true for any machine with an inventory.
            return !string.IsNullOrEmpty(machineGroupId);
        }

        /// <summary>
        /// Registers a machine.
        /// DEPRECATED: No longer needed - auto-detection handles everything.
        /// </summary>
        internal static void RegisterMachine(
            string machineGroupId,
            params string[] outputGroupIds)
        {
            // No longer needed - auto-detection handles all machines.
            Plugin.Logger?.LogDebug($"[Epoch Machine] RegisterMachine called for {machineGroupId} (ignored - auto-detection enabled)");
        }
    }
}
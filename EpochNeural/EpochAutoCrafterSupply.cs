using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SpaceCraft;

namespace EpochNeural
{
    internal static class EpochAutoCrafterSupply
    {
        private sealed class RecipeState
        {
            internal Group RecipeGroup;
            internal List<Group> Ingredients;
        }

        private static readonly Dictionary<int, RecipeState> _recipes =
            new Dictionary<int, RecipeState>();

        [HarmonyPatch(typeof(MachineAutoCrafter), "CraftIfPossible")]
        [HarmonyPrefix]
        private static bool CaptureRecipePrefix(
            MachineAutoCrafter __instance,
            Group linkedGroup)
        {
            try
            {
                if (__instance == null || linkedGroup == null)
                    return true;

                WorldObject machine = GetMachineWorldObject(__instance);
                if (machine == null)
                    return true;

                string id = machine.GetGroup()?.GetId();
                if (string.IsNullOrEmpty(id) ||
                    id.IndexOf("AutoCrafter", StringComparison.OrdinalIgnoreCase) < 0)
                    return true;

                Recipe recipe = linkedGroup.GetRecipe();
                if (recipe == null)
                    return true;

                List<Group> ingredients = recipe.GetIngredientsGroupInRecipe();
                if (ingredients == null || ingredients.Count == 0)
                    return true;

                _recipes[machine.GetId()] = new RecipeState
                {
                    RecipeGroup = linkedGroup,
                    Ingredients = new List<Group>(ingredients)
                };

                Plugin.Logger?.LogInfo(
                    $"[Epoch Supply] AutoCrafter recipe captured | {id} | Ingredients: {ingredients.Count}");
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogDebug(
                    $"[Epoch Supply] AutoCrafter recipe capture skipped: {ex.Message}");
            }

            // Vanilla remains completely responsible for the craft.
            return true;
        }

        internal static int TrySupply(
            WorldObject machine,
            Inventory machineInventory,
            Inventory hub,
            ref int remainingBudget)
        {
            if (machine == null || machineInventory == null ||
                hub == null || remainingBudget <= 0)
                return 0;

            RecipeState state;
            if (!_recipes.TryGetValue(machine.GetId(), out state) ||
                state == null || state.Ingredients == null ||
                state.Ingredients.Count == 0)
            {
                Plugin.Logger?.LogDebug(
                    $"[Epoch Supply] AutoCrafter {machine.GetGroup()?.GetId()} has no captured recipe yet.");
                return 0;
            }

            int supplied = 0;

            foreach (Group required in state.Ingredients)
            {
                if (remainingBudget <= 0)
                    break;

                if (required == null)
                    continue;

                if (machineInventory.ContainGroup(required))
                    continue;

                WorldObject item = FindHubItem(hub, required);
                if (item == null)
                    continue;

                bool success = false;

                try
                {
                    InventoriesHandler.Instance.TransferItem(
                        hub,
                        machineInventory,
                        item,
                        delegate (bool transferred)
                        {
                            success = transferred;
                        });
                }
                catch (Exception ex)
                {
                    Plugin.Logger?.LogWarning(
                        $"[Epoch Supply] AutoCrafter transfer {required.GetId()} failed: {ex.Message}");
                    continue;
                }

                if (!success)
                {
                    Plugin.Logger?.LogDebug(
                        $"[Epoch Supply] AutoCrafter native transfer rejected {required.GetId()}.");
                    continue;
                }

                supplied++;
                remainingBudget--;

                Plugin.Logger?.LogInfo(
                    $"[Epoch Supply] AutoCrafter supplied {required.GetId()} to " +
                    $"{machine.GetGroup()?.GetId()}.");

                EpochHubLogistics.RefreshCompressedStacks();
            }

            return supplied;
        }

        private static WorldObject GetMachineWorldObject(MachineAutoCrafter instance)
        {
            try
            {
                return instance.GetComponent<WorldObjectAssociated>()?.GetWorldObject();
            }
            catch
            {
                return null;
            }
        }

        private static WorldObject FindHubItem(Inventory hub, Group required)
        {
            var items = hub.GetInsideWorldObjects();
            if (items == null)
                return null;

            foreach (WorldObject item in items)
            {
                if (item == null || item.GetGroup() == null)
                    continue;

                if (item.GetIsLockedInInventory())
                    continue;

                if (item.GetGroup() == required)
                    return item;
            }

            return null;
        }
    }
}
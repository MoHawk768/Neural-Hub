// ============================================================
// Project : Epoch Neural
// Module  : Epoch Hub
// File    : EpochNeural.cs
//
// Author  : Otto
// Version : Sprint 2.0
//
// Build Status:
// 🟢 Active Development
//
// Purpose:
// Central manager for the entire Epoch ecosystem.
//
// Responsibilities:
// • Harmony bootstrap
// • Runtime state
// • Reflection cache
// • Epoch Hub registration
// • Localization
// • Central logging
// ============================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SpaceCraft;
using UnityEngine;

namespace EpochNeural
{

    // ============================================================
    // Epoch Neural
    //
    // CEO of the Epoch ecosystem.
    //
    // Responsibilities
    // ------------------------------------------------------------
    // • Harmony bootstrap
    // • Runtime state
    // • Reflection cache
    // • Epoch Hub registration
    // • Localization
    // • Central logging
    //
    // Does NOT own
    // ------------------------------------------------------------
    // • UI
    // • Inventory rendering
    // • Inventory layout
    // • Progression
    // ============================================================

    [HarmonyPatch(typeof(StaticDataHandler), "LoadStaticData")]
    internal static class EpochNeural
    {

        // ============================================================
        // Reflection Cache
        //
        // Cached reflection fields.
        //
        // Reflection is performed once during startup
        // and reused throughout the Epoch ecosystem.
        //
        // Never perform reflection anywhere else.
        // ============================================================

        private static readonly FieldInfo GroupBackingIdField =
    typeof(Group).GetField(
        "<id>k__BackingField",
        BindingFlags.Instance |
        BindingFlags.Public |
        BindingFlags.NonPublic);

        private static readonly FieldInfo LocalizationDictionaryField =
            typeof(Localization).GetField(
                "localizationDictionary",
                BindingFlags.Static |
                BindingFlags.NonPublic);

        // ============================================================
        // Runtime State
        //
        // Shared runtime objects.
        //
        // These values are shared across the Epoch ecosystem.
        //
        // Never place gameplay logic here.
        // ============================================================

        internal static bool IsInitialized;

        internal static UiWindowContainer EpochHubWindow;

        internal static Inventory EpochHubInventory;

        internal static Inventory PlayerInventory;

        // ============================================================
        // Central Logging
        //
        // Unified logging for the entire Epoch ecosystem.
        //
        // All logging passes through this class.
        //
        // Never call Plugin.Logger directly outside this class.
        // ============================================================

        // ============================================================
        // Log Information
        // ============================================================

        internal static void Log(
            string source,
            string message)
        {
            Plugin.Logger.LogInfo(
                $"[{source}] {message}");
        }

        // ============================================================
        // Log Warning
        // ============================================================

        internal static void Warn(
            string source,
            string message)
        {
            Plugin.Logger.LogWarning(
                $"[{source}] {message}");
        }

        // ============================================================
        // Log Error
        // ============================================================

        internal static void Error(
            string source,
            string message)
        {
            Plugin.Logger.LogError(
                $"[{source}] {message}");
        }

        // ============================================================
        // Log Exception
        // ============================================================

        internal static void Error(
            string source,
            Exception exception)
        {
            Plugin.Logger.LogError(
                $"[{source}] {exception}");
        }

        // ============================================================
        // Epoch Initialization
        //
        // Harmony entry point.
        // ============================================================

        // ============================================================
        // Initialize Epoch
        // ============================================================

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (IsInitialized)
                return;

            Log(
                "Epoch",
                "Initializing Epoch Neural...");

            RegisterEpochHub();

            RegisterLocalization();

            EpochHubUI.Initialize();

            IsInitialized = true;

            Log(
                "Epoch",
                "Initialization complete.");
        }

        // ============================================================
        // Epoch Hub Registration
        //
        // Registers the Epoch Hub.
        // ============================================================

        // ============================================================
        // Register Epoch Hub
        // ============================================================

        private static void RegisterEpochHub()
        {
            Log(
                "Epoch",
                "Registering Epoch Hub...");

            List<Group> groups =
                GroupsHandler.GetAllGroups();

            if (groups == null)
            {
                Error(
                    "Epoch",
                    "Unable to retrieve group list.");

                return;
            }

            if (groups.Exists(group =>
                group != null &&
                group.GetId() == "Epoch_Hub"))
            {
                Log(
                    "Epoch",
                    "Epoch Hub already registered.");

                return;
            }

            Log(
                "Epoch",
                "Cloning Container1...");

            GroupConstructible container =
    GroupsHandler.GetGroupViaId(
        "Container1") as GroupConstructible;

            if (container == null)
            {
                Error(
                    "Epoch",
                    "Unable to locate Container1.");

                return;
            }

            GroupDataConstructible containerData =
                container.GetGroupData()
                    as GroupDataConstructible;

            if (containerData == null)
            {
                Error(
                    "Epoch",
                    "Container1 has invalid GroupData.");

                return;
            }

            Log(
    "Epoch",
    "Container1 located.");

            GroupDataConstructible hubData =
                ScriptableObject.CreateInstance<GroupDataConstructible>();

            foreach (FieldInfo field in typeof(GroupDataConstructible).GetFields(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.FlattenHierarchy))
            {
                try
                {
                    field.SetValue(
                        hubData,
                        field.GetValue(containerData));
                }
                catch
                {
                }
            }

            Log(
                "Epoch",
                "Configuring Epoch Hub...");

            hubData.id = "Epoch_Hub";
            hubData.name = "Epoch Hub";
            hubData.inventorySize = 80;

            hubData.secondaryInventoriesSize =
                new List<int>
                {
        80,
        80,
        80,
        80,
        80,
        80,
        80,
        80
                };

            hubData.unlockingWorldUnit =
                DataConfig.WorldUnitType.Terraformation;

            hubData.unlockingValue = 0f;

            hubData.hideInCrafter = false;

            GroupConstructible epochHub =
                new GroupConstructible(hubData);

            epochHub.id = "Epoch_Hub";

            epochHub.SetRecipe(
                new Recipe(
                    new List<GroupDataItem>()));

            ApplyCompilerBackingID(
                epochHub,
                "Epoch_Hub");

            groups.Add(
                epochHub);

            GroupsHandler.SetAllGroups(
                groups);

            Log(
                "Epoch",
                "Epoch Hub registered.");
        }

        // ============================================================
        // Localization
        //
        // Registers Epoch localization.
        // ============================================================

        // ============================================================
        // Register Localization
        // ============================================================

        private static void RegisterLocalization()
        {
            Log(
                "Epoch",
                "Registering localization...");

            AddLocalization(
                "GROUP_NAME_Epoch_Hub",
                "Epoch Hub");

            AddLocalization(
                "GROUP_DESC_Epoch_Hub",
                "Stores every obtainable resource.");
        }

        // ============================================================
        // Private Helpers
        //
        // Shared utility methods.
        // ============================================================

        // ============================================================
        // Apply Compiler Backing ID
        // ============================================================

        private static void ApplyCompilerBackingID(
            Group group,
            string id)
        {
            try
            {
                GroupBackingIdField?.SetValue(
                    group,
                    id);
            }
            catch (Exception exception)
            {
                Error(
                    "Epoch",
                    exception);
            }
        }

        // ============================================================
        // Add Localization
        // ============================================================

        private static void AddLocalization(
    string key,
    string value)
        {
            try
            {
                Localization.GetLocalizedString(
                    "LANGUAGE");

                Dictionary<string, Dictionary<string, string>>
                    dictionary =
                        LocalizationDictionaryField
                            ?.GetValue(null)
                            as Dictionary<string,
                                Dictionary<string, string>>;

                if (dictionary == null)
                    return;

                foreach (Dictionary<string, string>
                    language in dictionary.Values)
                {
                    language[key] = value;
                }
            }
            catch (Exception exception)
            {
                Error(
                    "Epoch",
                    exception);
            }
        }
    }
}
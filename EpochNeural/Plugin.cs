using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;

namespace EpochNeural
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.epoch.neuralcratesystem";
        public const string PluginName = "Epoch Neural Crate System";
        public const string PluginVersion = "2.0.0";

        // Development-only logging switch.
        // Kept for project compatibility; critical logging remains enabled.
        internal static bool DebugLogging = false;

        internal static new ManualLogSource Logger;

        private void Awake()
        {
            Logger = base.Logger;

            Logger.LogInfo("");
            Logger.LogInfo("==================================================");
            Logger.LogInfo("             EPOCH HUB CONTROL CORE");
            Logger.LogInfo("==================================================");
            Logger.LogInfo($"Mod Version : {PluginVersion}");
            Logger.LogInfo("Core Status : ONLINE");
            Logger.LogInfo("Framework   : INITIALIZING");
            Logger.LogInfo("==================================================");

            try
            {
                var harmony = new Harmony(PluginGUID);
                harmony.PatchAll();

                Logger.LogInfo("[BOOT] Harmony patches applied.");

                EpochVacuumRunner.StartRunner();

                Logger.LogInfo("[BOOT] Persistent runner started.");
                Logger.LogInfo("[BOOT] Waiting for first Epoch Hub...");
                Logger.LogInfo("==================================================");
            }
            catch (Exception ex)
            {
                Logger.LogError("==================================================");
                Logger.LogError("[BOOT] CRITICAL FAILURE");
                Logger.LogError(ex);
                Logger.LogError("==================================================");
            }
        }

        private void OnDestroy()
        {
            EpochVacuumRunner.StopRunner();
            EpochVacuumSystem.ResetSystem();

            Logger.LogInfo("==================================================");
            Logger.LogInfo("[SHUTDOWN] Epoch Hub offline.");
            Logger.LogInfo("==================================================");
        }
    }
}
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

        internal static new ManualLogSource Logger;

        private void Awake()
        {
            Logger = base.Logger;

            Logger.LogInfo("==================================================");
            Logger.LogInfo($"{PluginName} v{PluginVersion}");
            Logger.LogInfo("Clean framework active. Booting core systems...");
            Logger.LogInfo("==================================================");

            try
            {
                Harmony harmony = new Harmony(PluginGUID);
                harmony.PatchAll();
                Logger.LogInfo("[Epoch] All unified patches initialized successfully.");
            }
            catch (Exception ex)
            {
                Logger.LogError("[Epoch] Core boot sequence hit a critical fault:");
                Logger.LogError(ex);
            }
        }
    }
}

using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace EpochNeural
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.epoch.neuralcratesystem";
        public const string PluginName = "Epoch Neural Crate System";
        public const string PluginVersion = "1.0.0";

        internal static new ManualLogSource Logger;

        private void Awake()
        {
            Logger = base.Logger;

            Logger.LogInfo("==================================================");
            Logger.LogInfo("Epoch Neural Crate System");
            Logger.LogInfo($"Version {PluginVersion}");
            Logger.LogInfo("Boot sequence started.");
            Logger.LogInfo("==================================================");

            try
            {
                Harmony harmony = new Harmony(PluginGUID);

                harmony.PatchAll();

                Logger.LogInfo("[Epoch] Harmony patches applied.");
                Logger.LogInfo("[Epoch] Boot sequence complete.");
            }
            catch (System.Exception ex)
            {
                Logger.LogError("[Epoch] Boot sequence failed.");
                Logger.LogError(ex);
            }
        }
    }
}
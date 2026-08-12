using SpaceCraft;
using UnityEngine;

namespace EpochNeural
{
    /// <summary>
    /// Persistent MonoBehaviour that keeps the vacuum system running.
    /// Lives on a GameObject that persists across scenes.
    /// </summary>
    public class EpochVacuumRunner : MonoBehaviour
    {
        private static EpochVacuumRunner _instance;
        private bool _isRunning = true;
        private float _lastLogTime = 0f;
        private const float LOG_INTERVAL = 60f;

        public static void StartRunner()
        {
            if (_instance != null)
                return;

            _instance = FindFirstObjectByType<EpochVacuumRunner>();

            if (_instance != null)
                return;

            GameObject go = new GameObject("EpochVacuumRunner");
            DontDestroyOnLoad(go);

            _instance = go.AddComponent<EpochVacuumRunner>();
            go.AddComponent<EpochDevHud>();

            // PRODUCTION UPGRADE: Attaches the 30-second background extraction core natively
            EpochDrillExtractionEngine.InitializeEngine(go);

            if (Plugin.Logger != null)
            {
                Plugin.Logger.LogInfo("EpochVacuumRunner, EpochDevHud, and Drill Engine attached to persistent context.");
            }
        }

        public static void StopRunner()
        {
            if (_instance != null)
            {
                if (Plugin.Logger != null)
                {
                    Plugin.Logger.LogInfo("Stopping EpochVacuumRunner and cleaning up game objects.");
                }

                Destroy(_instance.gameObject);
                _instance = null;
            }
        }

        private void Update()
        {
            if (!_isRunning)
                return;

            // No active player = no loaded world.
            // Reset the runtime once and wait for the next world to load.
            if (Managers.GetManager<PlayersManager>()?.GetActivePlayerController() == null)
            {
                EpochVacuumSystem.ResetSystem();
                return;
            }

            try
            {
                EpochVacuumSystem.UpdateVacuum();
            }
            catch (System.Exception ex)
            {
                Plugin.Logger?.LogError($"Error in runner processing step: {ex}");
            }

            if (Time.time - _lastLogTime >= LOG_INTERVAL)
            {
                _lastLogTime = Time.time;
                Plugin.Logger?.LogInfo("EpochVacuumRunner background thread processing normal.");
            }
        }
    }
}

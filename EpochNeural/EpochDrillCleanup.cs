using SpaceCraft;
using UnityEngine;

namespace EpochNeural
{
    /// <summary>
    /// Component attached to drills to handle cleanup when destroyed
    /// </summary>
    public class EpochDrillCleanup : MonoBehaviour
    {
        private int _worldObjectId;
        private string _biomeName;
        private bool _cleanedUp;

        public void Initialize(int worldObjectId, string biomeName)
        {
            _worldObjectId = worldObjectId;
            _biomeName = biomeName;
            _cleanedUp = false;
        }

        private void OnDestroy()
        {
            if (_cleanedUp)
                return;

            if (_worldObjectId == 0)
                return;

            Plugin.Logger?.LogInfo($"[Epoch Drill Cleanup] OnDestroy called for Node Extractor ID: {_worldObjectId}");

            EpochDrillManager.UnregisterDrill(_worldObjectId);

            if (EpochHud.Instance != null)
            {
                string displayName = string.IsNullOrEmpty(_biomeName) ? "Landing Area" : _biomeName;
                EpochHud.Instance.ShowNotification($"{displayName} Node Extractor Removed", false);
            }

            _cleanedUp = true;
        }
    }
}
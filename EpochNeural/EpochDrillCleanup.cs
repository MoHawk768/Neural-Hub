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

        /// <summary>
        /// Returns the stored biome name for this drill
        /// </summary>
        public string GetBiomeName()
        {
            return string.IsNullOrEmpty(_biomeName) ? "Unknown Area" : _biomeName;
        }

        /// <summary>
        /// Returns the stored world object ID
        /// </summary>
        public int GetWorldObjectId()
        {
            return _worldObjectId;
        }

        /// <summary>
        /// Called when the GameObject is destroyed.
        /// Only runs cleanup if the Prefix patch didn't handle it.
        /// </summary>
        private void OnDestroy()
        {
            if (_cleanedUp)
                return;

            if (_worldObjectId == 0)
                return;

            Plugin.Logger?.LogInfo($"[Epoch Drill Cleanup] OnDestroy called for Node Extractor ID: {_worldObjectId}");

            // Try to unregister - this is a safety net in case the Prefix patch didn't fire
            EpochDrillManager.UnregisterDrill(_worldObjectId);

            // Show notification if HUD exists
            if (EpochHud.Instance != null)
            {
                string displayName = string.IsNullOrEmpty(_biomeName) ? "Landing Area" : _biomeName;
                EpochHud.Instance.ShowNotification($"{displayName} Node Extractor Removed", false);
            }

            _cleanedUp = true;
        }

        /// <summary>
        /// Mark as cleaned up to prevent duplicate processing
        /// </summary>
        public void MarkCleanedUp()
        {
            _cleanedUp = true;
        }
    }
}
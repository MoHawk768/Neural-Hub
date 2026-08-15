using System;
using System.Collections;
using SpaceCraft;
using UnityEngine;

namespace EpochNeural
{
    /// <summary>
    /// Hub relocation system with tier-based recovery
    /// </summary>
    public class EpochRelocation : MonoBehaviour
    {
        private static EpochRelocation _instance;

        // Relocation state
        private bool _isRelocating = false;
        private bool _isRecovering = false;
        private float _relocationStartTime = 0f;
        private int _relocationTier = 1;
        private int _totalRecoverySteps = 1;
        private int _currentRecoveryStep = 0;
        private float _stepStartTime = 0f;
        private float _stepDuration = 300f; // 5 minutes per step

        // Event for HUD updates
        public event Action OnRecoveryUpdated;

        public static EpochRelocation Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("EpochRelocation");
                    _instance = go.AddComponent<EpochRelocation>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        public bool IsRelocating => _isRelocating;
        public bool IsRecovering => _isRecovering;
        public int CurrentRecoveryStep => _currentRecoveryStep;
        public int TotalRecoverySteps => _totalRecoverySteps;
        public float RecoveryPercentage => GetRecoveryPercentage();

        private void Update()
        {
            if (_isRelocating)
            {
                // Check if relocation time is complete
                float elapsed = Time.time - _relocationStartTime;
                float totalRelocationTime = _relocationTier * _stepDuration;

                if (elapsed >= totalRelocationTime)
                {
                    CompleteRelocation();
                }
            }

            if (_isRecovering)
            {
                // Check if current recovery step is complete
                float elapsed = Time.time - _stepStartTime;

                if (elapsed >= _stepDuration)
                {
                    AdvanceRecoveryStep();
                }

                // Trigger HUD update every second
                OnRecoveryUpdated?.Invoke();
            }
        }

        /// <summary>
        /// Start the relocation process
        /// </summary>
        public void StartRelocation(int tier)
        {
            if (_isRelocating || _isRecovering)
            {
                Plugin.Logger?.LogWarning("[Relocation] Already relocating or recovering");
                return;
            }

            _relocationTier = tier;
            _totalRecoverySteps = tier;
            _currentRecoveryStep = 0;
            _relocationStartTime = Time.time;
            _isRelocating = true;
            _isRecovering = false;

            // Set Hub offline
            EpochNeural.IsRelocating = true;
            EpochNeural.IsRecovering = false;
            EpochNeural.CurrentRecoveryBudget = 50;
            EpochNeural.RecoveryPercentage = 0f;

            Plugin.Logger?.LogInfo($"[Relocation] Starting relocation for Tier {tier} Hub. Time: {tier * 5} minutes");

            // Show notification
            if (EpochHud.Instance != null)
            {
                EpochHud.Instance.ShowNotification($"Relocating Hub... {tier * 5} minutes", false);
            }

            // Start relocation UI
            OnRecoveryUpdated?.Invoke();
        }

        /// <summary>
        /// Complete the relocation, start recovery
        /// </summary>
        private void CompleteRelocation()
        {
            _isRelocating = false;
            _isRecovering = true;
            _currentRecoveryStep = 0;
            _stepStartTime = Time.time;

            EpochNeural.IsRelocating = false;
            EpochNeural.IsRecovering = true;

            // Set initial budget to Tier 1
            EpochNeural.CurrentRecoveryBudget = 50;
            EpochNeural.RecoveryPercentage = 0f;

            Plugin.Logger?.LogInfo($"[Relocation] Relocation complete. Starting recovery for {_relocationTier} steps.");

            // Show notification
            if (EpochHud.Instance != null)
            {
                EpochHud.Instance.ShowNotification("Hub relocated! Recovering...", false);
            }

            OnRecoveryUpdated?.Invoke();
        }

        /// <summary>
        /// Advance to the next recovery step
        /// </summary>
        private void AdvanceRecoveryStep()
        {
            _currentRecoveryStep++;
            _stepStartTime = Time.time;

            if (_currentRecoveryStep >= _totalRecoverySteps)
            {
                // Recovery complete!
                _isRecovering = false;
                EpochNeural.IsRecovering = false;
                EpochNeural.RecoveryPercentage = 100f;

                // Set budget to final tier
                var finalTierData = EpochNeural.GetTierData(_relocationTier);
                if (finalTierData != null)
                {
                    EpochNeural.CurrentRecoveryBudget = finalTierData.Budget;
                }

                Plugin.Logger?.LogInfo($"[Relocation] Recovery complete! Hub at Tier {_relocationTier}");

                if (EpochHud.Instance != null)
                {
                    EpochHud.Instance.ShowNotification("Hub fully recovered!", true);
                }
            }
            else
            {
                // Update to next tier budget
                var stepTierData = EpochNeural.GetTierData(_currentRecoveryStep + 1);
                if (stepTierData != null)
                {
                    EpochNeural.CurrentRecoveryBudget = stepTierData.Budget;
                }

                float percent = ((float)_currentRecoveryStep / _totalRecoverySteps) * 100f;
                EpochNeural.RecoveryPercentage = percent;

                Plugin.Logger?.LogInfo($"[Relocation] Recovery step {_currentRecoveryStep + 1}/{_totalRecoverySteps}. Budget: {EpochNeural.CurrentRecoveryBudget}");

                if (EpochHud.Instance != null)
                {
                    EpochHud.Instance.ShowNotification($"Hub recovering: {Mathf.RoundToInt(percent)}%", false);
                }
            }

            OnRecoveryUpdated?.Invoke();
        }

        /// <summary>
        /// Get the current recovery percentage
        /// </summary>
        private float GetRecoveryPercentage()
        {
            if (!_isRecovering)
                return 100f;

            float totalElapsed = 0f;
            for (int i = 0; i < _currentRecoveryStep; i++)
            {
                totalElapsed += _stepDuration;
            }
            totalElapsed += (Time.time - _stepStartTime);

            float totalRecoveryTime = _totalRecoverySteps * _stepDuration;
            float percentage = (totalElapsed / totalRecoveryTime) * 100f;

            return Mathf.Clamp(percentage, 0f, 100f);
        }

        /// <summary>
        /// Get the current budget during recovery
        /// </summary>
        public int GetCurrentBudget()
        {
            if (_isRecovering)
            {
                return EpochNeural.CurrentRecoveryBudget;
            }

            var currentTierData = EpochNeural.CurrentTierData;
            return currentTierData?.Budget ?? 50;
        }

        /// <summary>
        /// Get the current stack cap during recovery
        /// </summary>
        public int GetCurrentStackCap()
        {
            if (_isRecovering)
            {
                var recoveryTierData = EpochNeural.GetTierData(_currentRecoveryStep + 1);
                return recoveryTierData?.StackCap ?? 25;
            }

            var currentTierData = EpochNeural.CurrentTierData;
            return currentTierData?.StackCap ?? 25;
        }

        /// <summary>
        /// Cancel relocation (for debug or if something goes wrong)
        /// </summary>
        public void CancelRelocation()
        {
            _isRelocating = false;
            _isRecovering = false;
            EpochNeural.IsRelocating = false;
            EpochNeural.IsRecovering = false;

            Plugin.Logger?.LogInfo("[Relocation] Relocation cancelled");

            if (EpochHud.Instance != null)
            {
                EpochHud.Instance.ShowNotification("Relocation cancelled", false);
            }

            OnRecoveryUpdated?.Invoke();
        }

        /// <summary>
        /// Get relocation status text
        /// </summary>
        public string GetStatusText()
        {
            if (_isRelocating)
            {
                float elapsed = Time.time - _relocationStartTime;
                float totalTime = _relocationTier * _stepDuration;
                float remaining = Mathf.Max(0f, totalTime - elapsed);
                return $"RELOCATING: {FormatTime(remaining)} remaining";
            }
            else if (_isRecovering)
            {
                float totalElapsed = 0f;
                for (int i = 0; i < _currentRecoveryStep; i++)
                {
                    totalElapsed += _stepDuration;
                }
                totalElapsed += (Time.time - _stepStartTime);
                float totalRecoveryTime = _totalRecoverySteps * _stepDuration;
                float remaining = Mathf.Max(0f, totalRecoveryTime - totalElapsed);
                return $"RECOVERING: {FormatTime(remaining)} remaining";
            }
            return null;
        }

        private string FormatTime(float seconds)
        {
            if (seconds < 60)
                return $"{Mathf.CeilToInt(seconds)}s";
            else if (seconds < 3600)
                return $"{Mathf.FloorToInt(seconds / 60)}m {Mathf.FloorToInt(seconds % 60)}s";
            else
                return $"{Mathf.FloorToInt(seconds / 3600)}h {Mathf.FloorToInt((seconds % 3600) / 60)}m";
        }

        private void OnDestroy()
        {
            _instance = null;
        }
    }
}
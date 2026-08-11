using UnityEngine;
using ExtraterrestrialExhaust.CameraSystem;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// Presentation companion for the EE5 gate-open beat. The gate remains
    /// responsible for disabling collision and lifting; this component only
    /// supplies the readable burst and camera punctuation at activation.
    /// </summary>
    [RequireComponent(typeof(EnergyGate))]
    public sealed class EnergyGatePresentation : MonoBehaviour
    {
        [SerializeField] Color unlockColor = new Color(0.2f, 1f, 0.85f, 1f);
        [SerializeField, Min(0.1f)] float burstScale = 1.35f;
        [SerializeField, Min(0f)] float cameraShakeStrength = 0.08f;
        [SerializeField, Min(0f)] float cameraShakeDuration = 0.24f;

        EnergyGate energyGate;

        void Awake() => energyGate = GetComponent<EnergyGate>();

        void OnEnable()
        {
            if (energyGate)
                energyGate.Disabled += HandleDisabled;
        }

        void OnDisable()
        {
            if (energyGate)
                energyGate.Disabled -= HandleDisabled;
        }

        void HandleDisabled()
        {
            ObjectiveSignalBurst.Spawn(transform.position, unlockColor, burstScale);
            if (cameraShakeStrength > 0f && cameraShakeDuration > 0f)
                PlayerCameraFollow.Instance?.Shake(cameraShakeStrength, cameraShakeDuration);
        }
    }
}

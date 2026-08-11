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
        [SerializeField] LineRenderer gateLine;
        [SerializeField] Color unlockColor = new Color(0.2f, 1f, 0.85f, 1f);
        [SerializeField, Min(0.1f)] float burstScale = 1.35f;
        [SerializeField, Min(0f)] float cameraShakeStrength = 0.08f;
        [SerializeField, Min(0f)] float cameraShakeDuration = 0.24f;
        [SerializeField, Min(0f)] float unlockPulseDuration = 1.8f;
        [SerializeField, Min(0f)] float unlockPulseWidthMultiplier = 1.55f;
        [SerializeField, Min(0f)] float unlockPulseSpeed = 18f;

        EnergyGate energyGate;
        float pulseRemaining;
        float baseWidthMultiplier = 1f;

        void Awake()
        {
            energyGate = GetComponent<EnergyGate>();
            if (!gateLine)
                gateLine = GetComponent<LineRenderer>();
            if (gateLine)
                baseWidthMultiplier = gateLine.widthMultiplier;
        }

        void OnEnable()
        {
            if (energyGate)
                energyGate.Disabled += HandleDisabled;
        }

        void OnDisable()
        {
            if (energyGate)
                energyGate.Disabled -= HandleDisabled;
            pulseRemaining = 0f;
            RestoreGateLine();
        }

        void Update()
        {
            if (!gateLine || pulseRemaining <= 0f)
                return;

            pulseRemaining = Mathf.Max(0f, pulseRemaining - Time.deltaTime);
            float progress = unlockPulseDuration > 0f
                ? Mathf.Clamp01(1f - pulseRemaining / unlockPulseDuration)
                : 1f;
            float pulse = 0.5f + Mathf.Sin(Time.time * unlockPulseSpeed) * 0.5f;
            float fade = 1f - Mathf.SmoothStep(0f, 1f, progress);
            Color color = Color.Lerp(unlockColor, Color.white, pulse * 0.35f);
            color.a *= Mathf.Lerp(0.55f, 1f, fade);
            gateLine.startColor = color;
            gateLine.endColor = color;
            gateLine.widthMultiplier = Mathf.Lerp(
                baseWidthMultiplier,
                baseWidthMultiplier * unlockPulseWidthMultiplier,
                Mathf.SmoothStep(0f, 1f, pulse) * fade);

            if (pulseRemaining <= 0f)
                RestoreGateLine();
        }

        void HandleDisabled()
        {
            pulseRemaining = unlockPulseDuration;
            ObjectiveSignalBurst.Spawn(transform.position, unlockColor, burstScale);
            if (cameraShakeStrength > 0f && cameraShakeDuration > 0f)
                PlayerCameraFollow.Instance?.Shake(cameraShakeStrength, cameraShakeDuration);
        }

        void RestoreGateLine()
        {
            if (!gateLine)
                return;

            gateLine.widthMultiplier = baseWidthMultiplier;
            gateLine.startColor = unlockColor;
            gateLine.endColor = unlockColor;
        }
    }
}

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
        [SerializeField] SpriteRenderer gateArtwork;
        [SerializeField] Color unlockColor = new Color(0.2f, 1f, 0.85f, 1f);
        [SerializeField, Min(0.1f)] float burstScale = 1.35f;
        [SerializeField, Min(0f)] float cameraShakeStrength = 0.08f;
        [SerializeField, Min(0f)] float cameraShakeDuration = 0.24f;
        [SerializeField, Min(0f)] float unlockPulseDuration = 1.8f;
        [SerializeField, Min(0f)] float unlockPulseWidthMultiplier = 1.55f;
        [SerializeField, Min(0f)] float unlockPulseSpeed = 18f;
        [Header("Key Approach")]
        [SerializeField] Color approachColor = new Color(1f, 0.8f, 0.18f, 1f);
        [SerializeField, Min(0f)] float approachPulseDuration = 0.85f;
        [SerializeField, Min(0f)] float approachPulseWidthMultiplier = 1.25f;
        [SerializeField, Min(0f)] float approachPulseSpeed = 24f;

        EnergyGate energyGate;
        ProgrammableLaserGate programmableLaserGate;
        float pulseRemaining;
        float approachRemaining;
        float baseWidthMultiplier = 1f;
        Color baseColor = new Color(0.2f, 0.55f, 1f, 1f);
        Color baseArtworkColor = Color.white;

        void Awake()
        {
            energyGate = GetComponent<EnergyGate>();
            programmableLaserGate = GetComponent<ProgrammableLaserGate>();
            RepairAuthoredGateArtwork();
            if (!gateLine)
                gateLine = GetComponent<LineRenderer>();
            if (!gateArtwork)
            {
                Transform artwork = transform.Find("Gate Artwork");
                gateArtwork = artwork ? artwork.GetComponent<SpriteRenderer>() : null;
            }
            if (gateLine)
            {
                baseWidthMultiplier = gateLine.widthMultiplier;
                baseColor = gateLine.startColor;
            }
            if (gateArtwork)
                baseArtworkColor = gateArtwork.color;

            // Older serialized scenes can preserve the original square
            // outline, but ProgrammableLaserGate is now the authored barrier.
            // Showing both makes the gate read like two conflicting colliders,
            // so retain the line only as a fallback for laser-free variants.
            if (programmableLaserGate && gateLine)
                gateLine.enabled = false;
        }

        void RepairAuthoredGateArtwork()
        {
            Transform artwork = transform.Find("Gate Artwork");
            if (!artwork)
                return;

            // The imported gate art has an offset canvas. Correct the child
            // visual without moving the gate's gameplay collider.
            artwork.localPosition = Ee5SliceProfile.EnergyGateArtworkLocalPosition;
            artwork.localRotation = Quaternion.Euler(0f, 0f, 90f);
            artwork.localScale = Vector3.one * Ee5SliceProfile.EnergyGateArtworkScale;
        }

        void OnEnable()
        {
            if (energyGate)
                energyGate.Disabled += HandleDisabled;

            if (programmableLaserGate && gateLine)
                gateLine.enabled = false;
        }

        void OnDisable()
        {
            if (energyGate)
                energyGate.Disabled -= HandleDisabled;
            pulseRemaining = 0f;
            approachRemaining = 0f;
            RestoreGateLine();
            RestoreGateArtwork();
        }

        void Update()
        {
            if (!gateLine)
                return;

            if (pulseRemaining > 0f)
            {
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
                UpdateArtworkColor(
                    Color.Lerp(unlockColor, Color.white, pulse * 0.35f),
                    Mathf.Lerp(0.65f, 1f, fade));

                if (pulseRemaining <= 0f)
                {
                    RestoreGateLine();
                    RestoreGateArtwork();
                }
                return;
            }

            if (approachRemaining <= 0f || (energyGate && energyGate.IsDisabled))
                return;

            approachRemaining = Mathf.Max(0f, approachRemaining - Time.deltaTime);
            float approachProgress = approachPulseDuration > 0f
                ? Mathf.Clamp01(1f - approachRemaining / approachPulseDuration)
                : 1f;
            float approachPulse = 0.5f
                + Mathf.Sin(Time.time * approachPulseSpeed) * 0.5f;
            float approachFade = 1f - Mathf.SmoothStep(0f, 1f, approachProgress);
            Color approachLineColor = Color.Lerp(
                baseColor,
                approachColor,
                Mathf.SmoothStep(0f, 1f, approachPulse));
            approachLineColor.a *= Mathf.Lerp(0.45f, 0.95f, approachFade);
            gateLine.startColor = approachLineColor;
            gateLine.endColor = approachLineColor;
            gateLine.widthMultiplier = Mathf.Lerp(
                baseWidthMultiplier,
                baseWidthMultiplier * approachPulseWidthMultiplier,
                approachPulse * approachFade);
            UpdateArtworkColor(
                Color.Lerp(baseArtworkColor, approachColor, Mathf.SmoothStep(0f, 1f, approachPulse)),
                Mathf.Lerp(0.8f, 1f, approachFade));

            if (approachRemaining <= 0f)
            {
                RestoreGateLine();
                RestoreGateArtwork();
            }
        }

        /// <summary>
        /// Gives the player a short incoming-key cue before the key reaches the
        /// target socket. The gate still unlocks only through EnergyGate.
        /// </summary>
        public void BeginKeyApproach()
        {
            if (energyGate && energyGate.IsDisabled)
                return;

            approachRemaining = approachPulseDuration;
            programmableLaserGate?.BeginKeyApproach();
        }

        void HandleDisabled()
        {
            approachRemaining = 0f;
            pulseRemaining = unlockPulseDuration;
            ObjectiveSignalBurst.Spawn(transform.position, unlockColor, burstScale);
            // The programmable beams own the barrier pulse; the artwork still
            // needs its persistent unlocked tint even when the preserved
            // square outline has been suppressed.
            if (programmableLaserGate)
            {
                programmableLaserGate.BeginUnlockPulse();
                RestoreGateArtwork();
            }
            if (cameraShakeStrength > 0f && cameraShakeDuration > 0f)
                PlayerCameraFollow.Instance?.Shake(cameraShakeStrength, cameraShakeDuration);
        }

        void RestoreGateLine()
        {
            if (!gateLine)
                return;

            gateLine.widthMultiplier = baseWidthMultiplier;
            Color color = energyGate && energyGate.IsDisabled
                ? unlockColor
                : baseColor;
            gateLine.startColor = color;
            gateLine.endColor = color;
        }

        void UpdateArtworkColor(Color color, float alphaMultiplier)
        {
            if (!gateArtwork)
                return;

            color.a = baseArtworkColor.a * Mathf.Clamp01(alphaMultiplier);
            gateArtwork.color = color;
        }

        void RestoreGateArtwork()
        {
            if (!gateArtwork)
                return;

            if (energyGate && energyGate.IsDisabled)
            {
                Color color = Color.Lerp(baseArtworkColor, unlockColor, 0.42f);
                color.a = baseArtworkColor.a;
                gateArtwork.color = color;
            }
            else
            {
                gateArtwork.color = baseArtworkColor;
            }
        }
    }
}

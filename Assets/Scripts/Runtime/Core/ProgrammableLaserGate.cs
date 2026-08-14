using System.Collections.Generic;
using UnityEngine;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// Presentation/network layer for the EE5 programmable laser barrier.
    /// EnergyGate remains the single authority for the blocking collider and
    /// lift handoff; this component owns the readable multi-beam barrier and
    /// exposes a script/UnityEvent-friendly disable entry point.
    /// </summary>
    [RequireComponent(typeof(EnergyGate))]
    public sealed class ProgrammableLaserGate : MonoBehaviour
    {
        [SerializeField, Min(1)] int beamCount = 3;
        [SerializeField, Min(0f)] float beamSpacing = 0.12f;
        [SerializeField, Min(0.001f)] float coreWidth = 0.055f;
        [SerializeField, Min(0.001f)] float glowWidth = 0.32f;
        [SerializeField, Min(0f)] float pulseSpeed = 3f;
        [SerializeField, Range(0f, 1f)] float pulseAmount = 0.18f;
        [SerializeField] Color coreColor = new Color(0.55f, 0.95f, 1f, 1f);
        [SerializeField] Color glowColor = new Color(0.03f, 0.35f, 1f, 0.42f);
        [SerializeField] int sortingOrder = 12;
        [Header("EE5 Emitter")]
        [SerializeField] Color emberColor = new Color(0.08f, 0.45f, 1f, 0.85f);
        [SerializeField, Min(0f)] float emberRate = 28f;
        [Header("Objective Cues")]
        [SerializeField] Color approachCoreColor = new Color(1f, 0.82f, 0.18f, 1f);
        [SerializeField] Color approachGlowColor = new Color(1f, 0.45f, 0.04f, 0.5f);
        [SerializeField] Color unlockCoreColor = new Color(0.2f, 1f, 0.85f, 1f);
        [SerializeField] Color unlockGlowColor = new Color(0.05f, 1f, 0.68f, 0.58f);
        [SerializeField, Min(0f)] float approachCueDuration = 0.85f;
        [SerializeField, Min(0f)] float unlockCueDuration = 1.8f;
        [SerializeField, Min(1f)] float approachWidthMultiplier = 1.35f;
        [SerializeField, Min(1f)] float unlockWidthMultiplier = 1.7f;

        EnergyGate gate;
        Material runtimeMaterial;
        readonly List<LineRenderer> coreLines = new();
        readonly List<LineRenderer> glowLines = new();
        float pulseOffset;
        float approachCueRemaining;
        float unlockCueRemaining;
        bool setupComplete;
        ParticleSystem embers;
        Transform emberTransform;
        Vector3 closedGatePosition;
        bool capturedClosedGatePosition;
        bool creatingPresentation;

        public bool IsDisabled => gate && gate.IsDisabled;
        public int BeamCount => beamCount;
        public bool HasEmberEmitter => embers != null;

        void Awake()
        {
            gate = GetComponent<EnergyGate>();
            pulseOffset = Random.value * Mathf.PI * 2f;
            CaptureClosedGatePosition();
            EnsureSetup();
        }

        void OnEnable()
        {
            if (!gate)
                gate = GetComponent<EnergyGate>();

            CaptureClosedGatePosition();
            EnsureSetup();
            ApplyLines(0f, coreColor, glowColor);
        }

        void OnDisable()
        {
            approachCueRemaining = 0f;
            unlockCueRemaining = 0f;
            for (int i = 0; i < coreLines.Count; i++)
            {
                if (coreLines[i])
                    coreLines[i].enabled = false;
                if (glowLines[i])
                    glowLines[i].enabled = false;
            }
            if (embers)
                embers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void OnValidate()
        {
            beamCount = Mathf.Max(1, beamCount);
            beamSpacing = Mathf.Max(0f, beamSpacing);
            coreWidth = Mathf.Max(0.001f, coreWidth);
            glowWidth = Mathf.Max(coreWidth, glowWidth);

            // Adding a component during another component's OnValidate is
            // illegal in Unity 6 and produces a misleading builder error.
            // Awake creates the runtime beams before the public scene plays;
            // an already-configured instance can still refresh its editor
            // preview below without mutating hierarchy during validation.
            if (!Application.isPlaying || !setupComplete || creatingPresentation)
                return;

            EnsureSetup();
            if (!Application.isPlaying)
                ApplyLines(0f, coreColor, glowColor);
        }

        void Update()
        {
            if (!setupComplete || !gate)
                return;

            float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed + pulseOffset) * pulseAmount;
            Color activeCoreColor = coreColor;
            Color activeGlowColor = glowColor;
            float cueWidthMultiplier = 1f;

            if (unlockCueRemaining > 0f)
            {
                unlockCueRemaining = Mathf.Max(
                    0f,
                    unlockCueRemaining - Time.deltaTime);
                float cueProgress = unlockCueDuration > 0f
                    ? 1f - unlockCueRemaining / unlockCueDuration
                    : 1f;
                float cuePulse = 0.5f
                    + Mathf.Sin(Time.time * 18f + pulseOffset) * 0.5f;
                float cueFade = 1f - Mathf.SmoothStep(0f, 1f, cueProgress);
                activeCoreColor = Color.Lerp(
                    unlockCoreColor,
                    Color.white,
                    cuePulse * 0.28f);
                activeGlowColor = unlockGlowColor;
                cueWidthMultiplier = Mathf.Lerp(
                    1f,
                    unlockWidthMultiplier,
                    cuePulse * cueFade);
            }
            else if (approachCueRemaining > 0f)
            {
                approachCueRemaining = Mathf.Max(
                    0f,
                    approachCueRemaining - Time.deltaTime);
                float cueProgress = approachCueDuration > 0f
                    ? 1f - approachCueRemaining / approachCueDuration
                    : 1f;
                float cuePulse = 0.5f
                    + Mathf.Sin(Time.time * 24f + pulseOffset) * 0.5f;
                float cueFade = 1f - Mathf.SmoothStep(0f, 1f, cueProgress);
                activeCoreColor = Color.Lerp(
                    approachCoreColor,
                    Color.white,
                    cuePulse * 0.2f);
                activeGlowColor = approachGlowColor;
                cueWidthMultiplier = Mathf.Lerp(
                    1f,
                    approachWidthMultiplier,
                    cuePulse * cueFade);
            }

            ApplyLines(pulse * cueWidthMultiplier, activeCoreColor, activeGlowColor);
        }

        /// <summary>Key/generator systems can use the same entry point as EE5's network.</summary>
        public bool DisableWalls() => gate && gate.TryDisableGate();

        /// <summary>
        /// Shows the incoming-key cue on the actual barrier beams. The old
        /// square outline may be suppressed in preserved scenes, so objective
        /// presentation must not depend on that legacy renderer.
        /// </summary>
        public void BeginKeyApproach()
        {
            if (gate && gate.IsDisabled)
                return;

            approachCueRemaining = Mathf.Max(
                approachCueRemaining,
                approachCueDuration);
        }

        /// <summary>Plays the unlock burst on the visible laser barrier.</summary>
        public void BeginUnlockPulse()
        {
            approachCueRemaining = 0f;
            unlockCueRemaining = Mathf.Max(
                unlockCueRemaining,
                unlockCueDuration);
        }

        void EnsureSetup()
        {
            if (creatingPresentation)
                return;

            if (!gate)
                gate = GetComponent<EnergyGate>();
            if (!gate)
                return;

            creatingPresentation = true;
            try
            {
                // A programmable barrier needs at least one visible beam.
                beamCount = Mathf.Max(1, beamCount);

                while (coreLines.Count < beamCount)
                {
                    int index = coreLines.Count;
                    Transform beamRoot = FindOrCreateChild($"Laser Beam {index + 1:00}");
                    LineRenderer glow = FindOrCreateLine(beamRoot, "Glow", glowLines.Count);
                    LineRenderer core = FindOrCreateLine(beamRoot, "Core", coreLines.Count);
                    glowLines.Add(glow);
                    coreLines.Add(core);
                }

                // A builder pass can reduce the authored count. Keep excess beams
                // dormant instead of destroying user-authored child objects.
                for (int i = beamCount; i < coreLines.Count; i++)
                {
                    if (coreLines[i])
                        coreLines[i].enabled = false;
                    if (glowLines[i])
                        glowLines[i].enabled = false;
                }

                EnsureEmbers();
                setupComplete = true;
            }
            finally
            {
                creatingPresentation = false;
            }
        }

        void CaptureClosedGatePosition()
        {
            if (!gate || gate.IsDisabled)
                return;

            closedGatePosition = transform.position;
            capturedClosedGatePosition = true;
        }

        Transform FindOrCreateChild(string childName)
        {
            Transform child = transform.Find(childName);
            if (child)
                return child;

            GameObject childObject = new GameObject(childName);
            childObject.transform.SetParent(transform, false);
            return childObject.transform;
        }

        LineRenderer FindOrCreateLine(Transform parent, string childName, int index)
        {
            Transform child = parent.Find(childName);
            GameObject lineObject = child ? child.gameObject : new GameObject(childName);
            if (!child)
                lineObject.transform.SetParent(parent, false);

            LineRenderer line = lineObject.GetComponent<LineRenderer>();
            if (!line)
                line = lineObject.AddComponent<LineRenderer>();

            line.useWorldSpace = false;
            line.positionCount = 2;
            line.numCapVertices = 4;
            line.sortingOrder = sortingOrder + (childName == "Core" ? 1 : 0);
            line.sharedMaterial = GetMaterial();
            line.textureMode = LineTextureMode.Stretch;
            ConfigureEnergyWidth(line, childName == "Core");
            ApplyEnergyGradient(line, childName == "Core" ? coreColor : glowColor, childName == "Core");
            return line;
        }

        static void ConfigureEnergyWidth(LineRenderer line, bool hotCore)
        {
            if (!line)
                return;

            // EE5's Laser Glow wall has a dense emitter end and a tapered far
            // end rather than a constant-width debug line. Keep the curve
            // normalized here; ApplyLines owns only the per-frame multiplier.
            line.widthCurve = new AnimationCurve(
                new Keyframe(0f, hotCore ? 0.72f : 0.88f),
                new Keyframe(0.16f, hotCore ? 1.16f : 1.08f),
                new Keyframe(0.38f, hotCore ? 1.04f : 0.96f),
                new Keyframe(0.72f, 0.78f),
                new Keyframe(1f, hotCore ? 0.48f : 0.62f));
        }

        static void ApplyEnergyGradient(LineRenderer line, Color tint, bool hotCore)
        {
            if (!line)
                return;

            Color deep = Color.Lerp(new Color(0.015f, 0.05f, 0.35f, hotCore ? 0.92f : 0.34f), tint, 0.22f);
            Color mid = Color.Lerp(new Color(0.04f, 0.35f, 1f, hotCore ? 1f : 0.52f), tint, 0.38f);
            Color hot = hotCore
                ? Color.Lerp(new Color(0.8f, 1f, 1f, 1f), tint, 0.24f)
                : Color.Lerp(new Color(0.16f, 0.7f, 1f, 0.65f), tint, 0.42f);
            Color upper = Color.Lerp(new Color(0.01f, 0.035f, 0.24f, hotCore ? 0.72f : 0.24f), tint, 0.16f);
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(deep, 0f),
                    new GradientColorKey(hot, 0.16f),
                    new GradientColorKey(mid, 0.34f),
                    new GradientColorKey(mid, 0.58f),
                    new GradientColorKey(deep, 0.78f),
                    new GradientColorKey(upper, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(deep.a, 0f),
                    new GradientAlphaKey(hot.a, 0.16f),
                    new GradientAlphaKey(mid.a, 0.34f),
                    new GradientAlphaKey(mid.a, 0.58f),
                    new GradientAlphaKey(deep.a, 0.78f),
                    new GradientAlphaKey(upper.a, 1f)
                });
            line.colorGradient = gradient;
        }

        void ApplyLines(float pulse, Color activeCoreColor, Color activeGlowColor)
        {
            if (!setupComplete || !gate)
                return;

            float height = gate.BarrierHeight;
            if (height <= 0.001f)
                height = 3.8f;

            float progress = gate.OpeningProgress;
            float visibleScale = gate.IsRouteClear
                ? 0f
                : gate.IsOpening
                    ? 1f - Mathf.SmoothStep(0f, 1f, progress)
                    : 1f;
            // The collider root retreats upward, while EE5's LaserWall keeps
            // its emitter planted and retracts the rendered energy toward the
            // ceiling. Offset the local points by that lift so the beam reads
            // as one anchored energy wall instead of a blue line drifting away.
            float rootLift = capturedClosedGatePosition
                ? transform.position.y - closedGatePosition.y
                : 0f;
            float bottom = -height * 0.5f - rootLift;
            float top = bottom + height * visibleScale;
            bool visible = !gate.IsRouteClear && visibleScale > 0.001f;
            // OnEnable applies the authored base pose before Update produces
            // its first pulse. Treat a non-positive value as neutral width so
            // the gate never flashes as a hairline for one frame.
            float pulseScale = pulse > 0f ? pulse : 1f;

            for (int i = 0; i < coreLines.Count; i++)
            {
                bool active = i < beamCount && visible;
                LineRenderer core = coreLines[i];
                LineRenderer glow = glowLines[i];
                if (!core || !glow)
                    continue;

                core.enabled = active;
                glow.enabled = active;
                if (!active)
                    continue;

                float x = (i - (beamCount - 1) * 0.5f) * beamSpacing;
                Transform beamRoot = core.transform.parent;
                beamRoot.localPosition = new Vector3(x, 0f, 0f);
                core.SetPosition(0, new Vector3(0f, bottom, 0f));
                core.SetPosition(1, new Vector3(0f, top, 0f));
                glow.SetPosition(0, new Vector3(0f, bottom, 0f));
                glow.SetPosition(1, new Vector3(0f, top, 0f));
                core.widthMultiplier = coreWidth * pulseScale;
                glow.widthMultiplier = glowWidth * pulseScale;
                ApplyEnergyGradient(core, activeCoreColor, true);
                ApplyEnergyGradient(glow, activeGlowColor, false);
            }

            UpdateEmbers(bottom, visible, activeCoreColor);
        }

        void EnsureEmbers()
        {
            if (embers)
                return;

            Transform child = transform.Find("Alien Blue Embers");
            GameObject emberObject = child ? child.gameObject : new GameObject("Alien Blue Embers");
            if (!child)
                emberObject.transform.SetParent(transform, false);

            emberTransform = emberObject.transform;
            embers = emberObject.GetComponent<ParticleSystem>();
            if (!embers)
                embers = emberObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = embers.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(emberColor, coreColor);
            main.gravityModifier = 0f;
            main.maxParticles = 90;

            ParticleSystem.EmissionModule emission = embers.emission;
            emission.enabled = true;
            emission.rateOverTime = emberRate;
            ParticleSystem.ShapeModule shape = embers.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.32f;
            shape.radiusThickness = 0.18f;
            ParticleSystem.NoiseModule noise = embers.noise;
            noise.enabled = true;
            noise.strength = 0.32f;
            noise.frequency = 0.75f;
            ParticleSystemRenderer renderer = emberObject.GetComponent<ParticleSystemRenderer>();
            renderer.material = GetMaterial();
            renderer.sortingOrder = sortingOrder + 2;
            if (Application.isPlaying)
                embers.Play(true);
        }

        void UpdateEmbers(float bottom, bool visible, Color activeCoreColor)
        {
            if (!embers)
                return;

            if (emberTransform)
                emberTransform.localPosition = new Vector3(0f, bottom + 0.2f, 0f);
            ParticleSystem.MainModule main = embers.main;
            main.startColor = new ParticleSystem.MinMaxGradient(emberColor, activeCoreColor);
            if (visible)
            {
                if (!embers.isPlaying)
                    embers.Play(true);
            }
            else if (embers.isPlaying)
            {
                embers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        Material GetMaterial()
        {
            if (runtimeMaterial)
                return runtimeMaterial;

            // Use the shared Unity 6/URP fallback chain. A direct
            // Sprites/Default lookup can return null in the active render
            // pipeline and make the gate look absent even though its
            // gameplay collider is working.
            runtimeMaterial = RuntimeVisualMaterial.Create(
                "Programmable Laser Gate (Runtime)");
            return runtimeMaterial;
        }

        void OnDestroy()
        {
            if (!runtimeMaterial)
                return;

            if (Application.isPlaying)
                Destroy(runtimeMaterial);
            else
                DestroyImmediate(runtimeMaterial);
        }
    }
}

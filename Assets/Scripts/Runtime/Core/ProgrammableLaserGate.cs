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

        EnergyGate gate;
        Material runtimeMaterial;
        readonly List<LineRenderer> coreLines = new();
        readonly List<LineRenderer> glowLines = new();
        float pulseOffset;
        bool setupComplete;

        public bool IsDisabled => gate && gate.IsDisabled;
        public int BeamCount => beamCount;

        void Awake()
        {
            gate = GetComponent<EnergyGate>();
            pulseOffset = Random.value * Mathf.PI * 2f;
            EnsureSetup();
        }

        void OnEnable()
        {
            if (!gate)
                gate = GetComponent<EnergyGate>();

            EnsureSetup();
            ApplyLines(0f);
        }

        void OnDisable()
        {
            for (int i = 0; i < coreLines.Count; i++)
            {
                if (coreLines[i])
                    coreLines[i].enabled = false;
                if (glowLines[i])
                    glowLines[i].enabled = false;
            }
        }

        void OnValidate()
        {
            beamCount = Mathf.Max(1, beamCount);
            beamSpacing = Mathf.Max(0f, beamSpacing);
            coreWidth = Mathf.Max(0.001f, coreWidth);
            glowWidth = Mathf.Max(coreWidth, glowWidth);
            EnsureSetup();
            if (!Application.isPlaying)
                ApplyLines(0f);
        }

        void Update()
        {
            if (!setupComplete || !gate)
                return;

            float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed + pulseOffset) * pulseAmount;
            ApplyLines(pulse);
        }

        /// <summary>Key/generator systems can use the same entry point as EE5's network.</summary>
        public bool DisableWalls() => gate && gate.TryDisableGate();

        void EnsureSetup()
        {
            if (!gate)
                gate = GetComponent<EnergyGate>();
            if (!gate)
                return;

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

            setupComplete = true;
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
            return line;
        }

        void ApplyLines(float pulse)
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
            float bottom = -height * 0.5f;
            float top = bottom + height * visibleScale;
            bool visible = !gate.IsRouteClear && visibleScale > 0.001f;
            float pulseScale = Mathf.Max(0.1f, pulse);

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
                core.startWidth = coreWidth * pulseScale;
                core.endWidth = coreWidth * pulseScale;
                glow.startWidth = glowWidth * pulseScale;
                glow.endWidth = glowWidth * pulseScale;
                core.startColor = coreColor;
                core.endColor = coreColor;
                glow.startColor = glowColor;
                glow.endColor = glowColor;
            }
        }

        Material GetMaterial()
        {
            if (runtimeMaterial)
                return runtimeMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            runtimeMaterial = new Material(shader)
            {
                name = "Programmable Laser Gate (Runtime)"
            };
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

using UnityEngine;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// Lightweight, deterministic presentation for the extraction point.
    ///
    /// The EE5 exit was assembled from several scene-specific visual scripts.
    /// This keeps the useful visual language—dark core, layered rings, and a
    /// stronger pulse when the exit is available—inside one reusable component.
    /// </summary>
    [RequireComponent(typeof(LevelExit))]
    public sealed class ExtractionPortalPresentation : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] float portalDiameter = 3.8f;
        [SerializeField, Range(24, 128)] int ringSegments = 80;
        [SerializeField, Min(0f)] float rotationSpeed = 32f;
        [SerializeField, Min(0f)] float pulseSpeed = 2.3f;
        [SerializeField] Color coreColor = new Color(0.015f, 0.008f, 0.04f, 0.98f);
        [SerializeField] Color innerRingColor = new Color(0.9f, 0.72f, 1f, 0.74f);
        [SerializeField] Color outerRingColor = new Color(0.22f, 0.68f, 1f, 0.5f);

        LevelExit levelExit;
        Transform generatedRoot;
        LineRenderer core;
        LineRenderer innerRing;
        LineRenderer outerRing;
        Material lineMaterial;

        void Awake()
        {
            levelExit = GetComponent<LevelExit>();
            BuildVisuals();
        }

        void Update()
        {
            if (!levelExit)
                return;

            float pulse = 0.9f + Mathf.Sin(Time.time * pulseSpeed) * 0.1f;
            float stateIntensity = levelExit.IsCapturing
                ? 1.35f
                : levelExit.IsUnlocked ? 1f : 0.35f;

            if (generatedRoot)
                generatedRoot.Rotate(0f, 0f, rotationSpeed * Time.deltaTime * stateIntensity);

            SetRingState(core, coreColor, pulse * stateIntensity);
            SetRingState(innerRing, innerRingColor, pulse * stateIntensity);
            SetRingState(outerRing, outerRingColor, pulse * stateIntensity);
        }

        void OnDestroy()
        {
            if (lineMaterial)
                Destroy(lineMaterial);
        }

        void BuildVisuals()
        {
            if (generatedRoot)
                return;

            generatedRoot = new GameObject("Generated Portal Visuals").transform;
            generatedRoot.SetParent(transform, false);

            // The core is intentionally a wide dark ring. Its line width nearly
            // fills the center while keeping the effect sprite-free and cheap.
            core = CreateRing("Portal Core", portalDiameter * 0.25f, 0.95f, 2);
            innerRing = CreateRing("Inner Ring", portalDiameter * 0.36f, 0.075f, 3);
            outerRing = CreateRing("Outer Ring", portalDiameter * 0.5f, 0.045f, 2);
        }

        LineRenderer CreateRing(string objectName, float radius, float width, int sortingOrder)
        {
            GameObject ringObject = new GameObject(objectName);
            ringObject.transform.SetParent(generatedRoot, false);

            LineRenderer ring = ringObject.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = ringSegments;
            ring.startWidth = width;
            ring.endWidth = width;
            ring.numCapVertices = 2;
            ring.numCornerVertices = 2;
            ring.sortingOrder = sortingOrder;
            ring.material = GetLineMaterial();

            for (int i = 0; i < ringSegments; i++)
            {
                float angle = i / (float)ringSegments * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f));
            }

            return ring;
        }

        Material GetLineMaterial()
        {
            if (!lineMaterial)
                lineMaterial = new Material(Shader.Find("Sprites/Default"));
            return lineMaterial;
        }

        static void SetRingState(LineRenderer ring, Color baseColor, float intensity)
        {
            if (!ring)
                return;

            Color color = baseColor;
            color.a = Mathf.Clamp01(baseColor.a * intensity);
            ring.startColor = color;
            ring.endColor = color;
        }
    }
}

using UnityEngine;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// Short-lived objective confirmation effect. It is spawned independently
    /// so a key or gate can be destroyed or moved without cutting the beat off.
    /// </summary>
    public sealed class ObjectiveSignalBurst : MonoBehaviour
    {
        const int RayCount = 12;
        const int RingSegments = 28;
        const float Lifetime = 0.42f;

        LineRenderer ring;
        LineRenderer[] rays;
        Color color;
        float age;
        float scale = 1f;

        public static void Spawn(Vector3 position, Color accent, float effectScale = 1f)
        {
            GameObject burstObject = new GameObject("Objective Signal Burst");
            burstObject.transform.position = position;
            ObjectiveSignalBurst burst = burstObject.AddComponent<ObjectiveSignalBurst>();
            burst.Initialize(accent, effectScale);
        }

        void Initialize(Color accent, float effectScale)
        {
            color = accent;
            scale = Mathf.Max(0.1f, effectScale);
            ring = CreateLine("Objective Ring", 0.055f, 32);
            ring.loop = true;

            rays = new LineRenderer[RayCount];
            for (int i = 0; i < rays.Length; i++)
                rays[i] = CreateLine("Objective Ray", 0.035f, 34);

            UpdateGeometry(0f);
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / Lifetime);
            float eased = 1f - Mathf.Pow(1f - t, 2f);
            float radius = Mathf.Lerp(0.14f, 0.88f, eased) * scale;
            float rayLength = Mathf.Lerp(0.2f, 0.6f, eased) * scale;
            UpdateGeometry(eased, radius, rayLength);

            if (t >= 1f)
                Destroy(gameObject);
        }

        LineRenderer CreateLine(string objectName, float width, int sortingOrder)
        {
            GameObject lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.startWidth = width * scale;
            line.endWidth = width * 0.12f * scale;
            line.startColor = color;
            line.endColor = color;
            line.sortingOrder = sortingOrder;
            Material material = RuntimeVisualMaterial.Create("Objective Signal Burst");
            if (material)
                line.material = material;
            return line;
        }

        void UpdateGeometry(float eased)
        {
            UpdateGeometry(
                eased,
                Mathf.Lerp(0.14f, 0.88f, eased) * scale,
                Mathf.Lerp(0.2f, 0.6f, eased) * scale);
        }

        void UpdateGeometry(float eased, float radius, float rayLength)
        {
            Color faded = color;
            faded.a = Mathf.Clamp01(color.a) * (1f - eased);

            ring.startColor = faded;
            ring.endColor = faded;
            ring.positionCount = RingSegments;
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i / (float)RingSegments * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f));
            }

            for (int i = 0; i < rays.Length; i++)
            {
                float angle = i / (float)RayCount * Mathf.PI * 2f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                rays[i].startColor = faded;
                rays[i].endColor = faded;
                rays[i].SetPosition(0, direction * radius * 0.72f);
                rays[i].SetPosition(1, direction * (radius + rayLength));
            }
        }

        void OnDestroy()
        {
            DestroyLineMaterial(ring);
            if (rays == null)
                return;

            for (int i = 0; i < rays.Length; i++)
                DestroyLineMaterial(rays[i]);
        }

        static void DestroyLineMaterial(LineRenderer line)
        {
            if (line && line.material)
                Destroy(line.material);
        }
    }
}

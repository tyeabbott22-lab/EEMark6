using UnityEngine;
using ExtraterrestrialExhaust.Core;

namespace ExtraterrestrialExhaust.Player
{
    /// <summary>Short-lived pickup confirmation that survives pickup cleanup.</summary>
    public sealed class PickupCollectionBurst : MonoBehaviour
    {
        const int RayCount = 8;
        const int RingSegments = 24;
        const float Lifetime = 0.28f;

        LineRenderer ring;
        LineRenderer[] rays;
        SpriteRenderer icon;
        Color color;
        float age;
        float ringRadius;
        float rayLength;

        public static void Spawn(Vector3 position, Color accent, Sprite iconSprite)
        {
            GameObject burstObject = new GameObject("Pickup Collection Burst");
            burstObject.transform.position = position;
            PickupCollectionBurst burst = burstObject.AddComponent<PickupCollectionBurst>();
            burst.Initialize(accent, iconSprite);
        }

        void Initialize(Color accent, Sprite iconSprite)
        {
            color = accent;
            ringRadius = 0.14f;
            rayLength = 0.22f;

            ring = CreateLine("Pickup Ring", 0.045f, 26);
            ring.loop = true;
            rays = new LineRenderer[RayCount];
            for (int i = 0; i < rays.Length; i++)
                rays[i] = CreateLine("Pickup Ray", 0.035f, 12);

            if (iconSprite)
            {
                GameObject iconObject = new GameObject("Pickup Icon");
                iconObject.transform.SetParent(transform, false);
                icon = iconObject.AddComponent<SpriteRenderer>();
                icon.sprite = iconSprite;
                icon.color = Color.white;
                icon.sortingOrder = 30;
                icon.transform.localScale = Vector3.one * 0.72f;
            }

            UpdateGeometry(0f);
        }

        void OnDestroy()
        {
            DestroyLineMaterial(ring);
            if (rays == null)
                return;

            for (int i = 0; i < rays.Length; i++)
                DestroyLineMaterial(rays[i]);
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / Lifetime);
            float eased = 1f - Mathf.Pow(1f - t, 2f);
            ringRadius = Mathf.Lerp(0.14f, 0.7f, eased);
            rayLength = Mathf.Lerp(0.22f, 0.62f, eased);
            UpdateGeometry(eased);

            if (icon)
            {
                icon.transform.localScale = Vector3.one *
                    Mathf.Lerp(0.72f, 1.05f, eased);
                Color iconColor = Color.white;
                iconColor.a = 1f - t;
                icon.color = iconColor;
            }

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
            line.startWidth = width;
            line.endWidth = width * 0.12f;
            line.startColor = color;
            line.endColor = color;
            line.sortingOrder = sortingOrder;
            Material material = RuntimeVisualMaterial.Create("Pickup Collection Burst");
            if (material)
                line.material = material;
            return line;
        }

        void UpdateGeometry(float eased)
        {
            Color faded = color;
            faded.a = 1f - eased;
            SetRingGeometry(faded);

            for (int i = 0; i < rays.Length; i++)
            {
                float angle = i / (float)RayCount * Mathf.PI * 2f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                rays[i].startColor = faded;
                rays[i].endColor = faded;
                rays[i].SetPosition(0, direction * ringRadius * 0.72f);
                rays[i].SetPosition(1, direction * (ringRadius + rayLength));
            }
        }

        void SetRingGeometry(Color faded)
        {
            ring.startColor = faded;
            ring.endColor = faded;
            ring.positionCount = RingSegments;
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i / (float)RingSegments * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * ringRadius,
                    Mathf.Sin(angle) * ringRadius,
                    0f));
            }
        }

        static void DestroyLineMaterial(LineRenderer line)
        {
            if (line && line.material)
                Destroy(line.material);
        }
    }
}

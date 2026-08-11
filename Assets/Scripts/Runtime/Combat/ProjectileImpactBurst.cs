using UnityEngine;

namespace ExtraterrestrialExhaust.Combat
{
    /// <summary>Procedural impact burst used until authored VFX are migrated.</summary>
    public sealed class ProjectileImpactBurst : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] float lifetime = 0.12f;
        [SerializeField, Min(0f)] float radius = 0.28f;

        float remaining;
        LineRenderer[] rays;
        Color[] rayStartColors;
        Color[] rayEndColors;
        Material[] rayMaterials;

        public static void Spawn(Vector2 position, Color color, Vector2 impactNormal = default)
        {
            GameObject burstObject = new GameObject("Projectile Impact");
            burstObject.transform.position = position;
            ProjectileImpactBurst burst = burstObject.AddComponent<ProjectileImpactBurst>();
            burst.CreateRays(color, impactNormal);
        }

        void Awake()
        {
            remaining = lifetime;
        }

        void Update()
        {
            if (rays == null)
                return;

            remaining -= Time.deltaTime;
            float scale = Mathf.Clamp01(remaining / lifetime);
            transform.localScale = Vector3.one * (1f + (1f - scale));
            for (int i = 0; i < rays.Length; i++)
            {
                Color start = rayStartColors[i];
                Color end = rayEndColors[i];
                start.a *= scale;
                end.a *= scale;
                rays[i].startColor = start;
                rays[i].endColor = end;
            }

            if (remaining <= 0f)
                Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (rayMaterials == null)
                return;

            foreach (Material material in rayMaterials)
            {
                if (material)
                    Destroy(material);
            }
        }

        void CreateRays(Color color, Vector2 impactNormal)
        {
            rays = new LineRenderer[4];
            rayStartColors = new Color[rays.Length];
            rayEndColors = new Color[rays.Length];
            rayMaterials = new Material[rays.Length];
            float baseAngle = impactNormal.sqrMagnitude > 0.001f
                ? Mathf.Atan2(impactNormal.y, impactNormal.x)
                : 0f;
            for (int i = 0; i < rays.Length; i++)
            {
                GameObject rayObject = new GameObject("Impact Ray");
                rayObject.transform.SetParent(transform, false);
                LineRenderer ray = rayObject.AddComponent<LineRenderer>();
                ray.useWorldSpace = false;
                ray.positionCount = 2;
                ray.SetPosition(0, Vector3.zero);
                float angle = baseAngle + i * 90f * Mathf.Deg2Rad;
                ray.SetPosition(1, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                ray.startWidth = 0.05f;
                ray.endWidth = 0.01f;
                ray.startColor = color;
                ray.endColor = new Color(color.r, color.g, color.b, 0f);
                Shader shader = Shader.Find("Sprites/Default");
                if (shader)
                {
                    rayMaterials[i] = new Material(shader);
                    rayMaterials[i].name = "Projectile Impact Ray";
                    ray.sharedMaterial = rayMaterials[i];
                }
                rayStartColors[i] = ray.startColor;
                rayEndColors[i] = ray.endColor;
                rays[i] = ray;
            }
        }
    }
}

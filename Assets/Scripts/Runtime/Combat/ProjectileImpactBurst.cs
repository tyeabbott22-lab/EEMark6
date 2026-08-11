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

        public static void Spawn(Vector2 position, Color color)
        {
            GameObject burstObject = new GameObject("Projectile Impact");
            burstObject.transform.position = position;
            ProjectileImpactBurst burst = burstObject.AddComponent<ProjectileImpactBurst>();
            burst.CreateRays(color);
        }

        void Awake()
        {
            remaining = lifetime;
        }

        void Update()
        {
            remaining -= Time.deltaTime;
            float scale = Mathf.Clamp01(remaining / lifetime);
            transform.localScale = Vector3.one * (1f + (1f - scale));
            foreach (LineRenderer ray in rays)
            {
                Color start = ray.startColor;
                start.a = scale;
                ray.startColor = start;
                ray.endColor = start;
            }

            if (remaining <= 0f)
                Destroy(gameObject);
        }

        void CreateRays(Color color)
        {
            rays = new LineRenderer[4];
            for (int i = 0; i < rays.Length; i++)
            {
                GameObject rayObject = new GameObject("Impact Ray");
                rayObject.transform.SetParent(transform, false);
                LineRenderer ray = rayObject.AddComponent<LineRenderer>();
                ray.useWorldSpace = false;
                ray.positionCount = 2;
                ray.SetPosition(0, Vector3.zero);
                float angle = i * 90f * Mathf.Deg2Rad;
                ray.SetPosition(1, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                ray.startWidth = 0.05f;
                ray.endWidth = 0.01f;
                ray.startColor = color;
                ray.endColor = new Color(color.r, color.g, color.b, 0f);
                ray.material = new Material(Shader.Find("Sprites/Default"));
                rays[i] = ray;
            }
        }
    }
}

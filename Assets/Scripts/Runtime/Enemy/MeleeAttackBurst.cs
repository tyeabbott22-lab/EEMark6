using UnityEngine;

namespace ExtraterrestrialExhaust.Enemy
{
    /// <summary>
    /// Short, procedural confirmation for the EE5-style contact attack.
    /// The melee controller stays kinematic and deterministic; this effect
    /// carries the visual weight of the strike without rotating or nudging
    /// the enemy body into the player.
    /// </summary>
    public sealed class MeleeAttackBurst : MonoBehaviour
    {
        const int ArcSegments = 9;
        const int SparkCount = 2;
        const float Lifetime = 0.18f;

        LineRenderer slash;
        LineRenderer[] sparks;
        Material[] materials;
        Color accent;
        float effectScale;
        float age;

        public static void Spawn(
            Vector2 position,
            Vector2 direction,
            Color color,
            float scale = 1f)
        {
            GameObject burstObject = new GameObject("Melee Strike Burst");
            burstObject.transform.position = position;
            MeleeAttackBurst burst = burstObject.AddComponent<MeleeAttackBurst>();
            burst.Initialize(direction, color, scale);
        }

        void Initialize(Vector2 direction, Color color, float scale)
        {
            accent = color;
            effectScale = Mathf.Max(0.1f, scale);
            if (direction.sqrMagnitude > 0.001f)
            {
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }

            materials = new Material[1 + SparkCount];
            slash = CreateLine("Melee Slash", 0.12f, 91, 0);
            sparks = new LineRenderer[SparkCount];
            for (int i = 0; i < sparks.Length; i++)
                sparks[i] = CreateLine("Melee Spark", 0.06f, 92, i + 1);

            UpdateGeometry(0f);
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / Lifetime);
            UpdateGeometry(t);

            if (t >= 1f)
                Destroy(gameObject);
        }

        LineRenderer CreateLine(
            string objectName,
            float width,
            int sortingOrder,
            int materialIndex)
        {
            GameObject lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.startWidth = width * effectScale;
            line.endWidth = width * 0.08f * effectScale;
            line.startColor = accent;
            line.endColor = accent;
            line.sortingOrder = sortingOrder;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader)
            {
                Material material = new Material(shader);
                material.name = "Melee Strike Material";
                line.sharedMaterial = material;
                materials[materialIndex] = material;
            }

            return line;
        }

        void UpdateGeometry(float t)
        {
            float eased = 1f - Mathf.Pow(1f - t, 2f);
            float radius = Mathf.Lerp(0.16f, 0.78f, eased) * effectScale;
            Color faded = accent;
            faded.a *= 1f - Mathf.SmoothStep(0f, 1f, t);

            if (slash)
            {
                slash.positionCount = ArcSegments;
                slash.startColor = faded;
                slash.endColor = faded;
                for (int i = 0; i < ArcSegments; i++)
                {
                    float arcT = i / (float)(ArcSegments - 1);
                    float angle = Mathf.Lerp(-68f, 68f, arcT) * Mathf.Deg2Rad;
                    float arcRadius = radius * Mathf.Lerp(0.92f, 1.08f, Mathf.Sin(arcT * Mathf.PI));
                    slash.SetPosition(i, new Vector3(
                        Mathf.Cos(angle) * arcRadius,
                        Mathf.Sin(angle) * arcRadius,
                        0f));
                }
            }

            for (int i = 0; i < sparks.Length; i++)
            {
                LineRenderer spark = sparks[i];
                if (!spark)
                    continue;

                float angle = (i == 0 ? -42f : 42f) * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                float startRadius = radius * 0.72f;
                float endRadius = radius + Mathf.Lerp(0.22f, 0.45f, eased) * effectScale;
                spark.startColor = faded;
                spark.endColor = faded;
                spark.SetPosition(0, direction * startRadius);
                spark.SetPosition(1, direction * endRadius);
            }
        }

        void OnDestroy()
        {
            if (materials == null)
                return;

            for (int i = 0; i < materials.Length; i++)
                if (materials[i]) Destroy(materials[i]);
        }
    }
}

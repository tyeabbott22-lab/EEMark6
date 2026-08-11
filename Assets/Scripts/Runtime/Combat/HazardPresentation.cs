using UnityEngine;

namespace ExtraterrestrialExhaust.Combat
{
    /// <summary>
    /// EE5-inspired heat hazard readout: pulsing warning lines, a soft scale
    /// swell, and upward embers. It listens to ContactHazard for a brief hit
    /// accent but never owns damage or cooldown rules.
    /// </summary>
    [RequireComponent(typeof(ContactHazard))]
    public sealed class HazardPresentation : MonoBehaviour
    {
        [SerializeField] Color bodyColor = new Color(1f, 0.02f, 0.01f, 0.92f);
        [SerializeField] Color pulseColor = new Color(1f, 0.3f, 0.01f, 0.95f);
        [SerializeField] Color hotFlashColor = new Color(1f, 0.8f, 0.1f, 1f);
        [SerializeField, Min(0f)] float pulseFrequency = 4.2f;
        [SerializeField, Range(0f, 1f)] float pulseStrength = 0.72f;
        [SerializeField, Min(0f)] float scalePulse = 0.035f;
        [SerializeField, Min(0f)] float emberRate = 18f;
        [SerializeField, Min(0.1f)] float emberRadius = 1.1f;

        ContactHazard hazard;
        ParticleSystem embers;
        LineRenderer[] lines;
        Color[] startColors;
        Color[] endColors;
        float hitPulse;
        Vector3 baseScale;

        void Awake()
        {
            hazard = GetComponent<ContactHazard>();
            lines = GetComponentsInChildren<LineRenderer>(true);
            startColors = new Color[lines.Length];
            endColors = new Color[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                startColors[i] = lines[i].startColor;
                endColors[i] = lines[i].endColor;
            }

            baseScale = transform.localScale;
            EnsureEmbers();
        }

        void OnEnable()
        {
            if (hazard)
                hazard.PlayerDamaged += HandlePlayerDamaged;
            if (embers && !embers.isPlaying)
                embers.Play(true);
        }

        void OnDisable()
        {
            if (hazard)
                hazard.PlayerDamaged -= HandlePlayerDamaged;
            if (embers)
                embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void OnDestroy()
        {
            if (embers)
            {
                ParticleSystemRenderer renderer = embers.GetComponent<ParticleSystemRenderer>();
                if (renderer && renderer.sharedMaterial)
                    Destroy(renderer.sharedMaterial);
            }
        }

        void Update()
        {
            float pulse = Mathf.Sin(Time.time * pulseFrequency * Mathf.PI * 2f) * 0.5f + 0.5f;
            float intensity = Mathf.Clamp01(pulse * pulseStrength + hitPulse);
            transform.localScale = baseScale * (1f + pulse * scalePulse + hitPulse * 0.04f);

            Color glowColor = Color.Lerp(
                bodyColor,
                Color.Lerp(pulseColor, hotFlashColor, hitPulse),
                intensity);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i])
                    continue;

                Color start = Color.Lerp(startColors[i], glowColor, 0.72f);
                Color end = Color.Lerp(endColors[i], glowColor, 0.42f);
                start.a = Mathf.Clamp01(Mathf.Max(start.a, glowColor.a));
                end.a = Mathf.Clamp01(Mathf.Max(end.a, glowColor.a * 0.72f));
                lines[i].startColor = start;
                lines[i].endColor = end;
            }

            hitPulse = Mathf.MoveTowards(hitPulse, 0f, Time.deltaTime * 7f);
            if (embers)
            {
                ParticleSystem.EmissionModule emission = embers.emission;
                emission.rateOverTime = emberRate * Mathf.Lerp(0.75f, 1.25f, pulse);
            }
        }

        void HandlePlayerDamaged(DamageInfo damage)
        {
            hitPulse = 1f;
        }

        void EnsureEmbers()
        {
            Transform emberTransform = transform.Find("Heat Embers");
            if (emberTransform)
                embers = emberTransform.GetComponent<ParticleSystem>();

            if (!embers)
            {
                GameObject emberObject = new GameObject("Heat Embers");
                emberObject.transform.SetParent(transform, false);
                embers = emberObject.AddComponent<ParticleSystem>();
            }

            ParticleSystem.MainModule main = embers.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.85f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.06f, 0f, 0.72f),
                new Color(1f, 0.62f, 0.08f, 0.42f));
            main.gravityModifier = -0.08f;
            main.maxParticles = 80;

            ParticleSystem.EmissionModule emission = embers.emission;
            emission.enabled = true;
            emission.rateOverTime = emberRate;

            ParticleSystem.ShapeModule shape = embers.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = emberRadius;
            shape.radiusThickness = 0.35f;

            ParticleSystem.NoiseModule noise = embers.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.85f;

            ParticleSystem.ColorOverLifetimeModule color = embers.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.1f, 0f), 0f),
                    new GradientColorKey(new Color(1f, 0.55f, 0.06f), 0.35f),
                    new GradientColorKey(new Color(0.32f, 0f, 0f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.7f, 0.15f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = gradient;

            ParticleSystemRenderer renderer = embers.GetComponent<ParticleSystemRenderer>();
            if (renderer)
            {
                renderer.sortingOrder = 41;
                renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            }

            embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}

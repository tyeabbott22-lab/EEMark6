using UnityEngine;

namespace ExtraterrestrialExhaust.Combat
{
    /// <summary>
    /// EE5-style exhaust for both player and enemy projectiles.
    ///
    /// The projectile still owns movement and collision. This component only
    /// creates the short-lived visual wake, so projectile teams can share one
    /// prefab while retaining different color language.
    /// </summary>
    [RequireComponent(typeof(PlayerProjectile))]
    public sealed class ProjectileExhaustPresentation : MonoBehaviour
    {
        const float RearOffset = 0.12f;
        const float EmissionRate = 72f;
        const float ParticleLifetime = 0.14f;
        const float ParticleSpeed = 1.2f;
        const float ParticleSize = 0.055f;

        ParticleSystem particles;
        ParticleSystemRenderer particleRenderer;
        Material runtimeMaterial;
        Color theme = Color.white;

        void Awake()
        {
            EnsureParticles();
            ApplyLook();
        }

        void OnEnable()
        {
            if (particles && !particles.isPlaying)
                particles.Play(true);
        }

        void OnDisable()
        {
            if (particles)
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void OnDestroy()
        {
            if (runtimeMaterial)
                Destroy(runtimeMaterial);
        }

        public void SetColorTheme(Color color)
        {
            theme = color;
            ApplyLook();
        }

        void EnsureParticles()
        {
            if (particles)
                return;

            Transform exhaustTransform = transform.Find("Projectile Exhaust");
            if (!exhaustTransform)
            {
                GameObject exhaustObject = new GameObject("Projectile Exhaust");
                exhaustObject.transform.SetParent(transform, false);
                exhaustTransform = exhaustObject.transform;
            }

            exhaustTransform.localPosition = new Vector3(-RearOffset, 0f, 0f);
            exhaustTransform.localRotation = Quaternion.identity;
            exhaustTransform.localScale = Vector3.one;
            particles = exhaustTransform.GetComponent<ParticleSystem>();
            if (!particles)
                particles = exhaustTransform.gameObject.AddComponent<ParticleSystem>();

            particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            if (!runtimeMaterial)
            {
                runtimeMaterial = new Material(Shader.Find("Sprites/Default"));
                runtimeMaterial.name = "Runtime Projectile Exhaust";
            }

            if (particleRenderer)
                particleRenderer.sharedMaterial = runtimeMaterial;
        }

        void ApplyLook()
        {
            if (!particles)
                return;

            Color hotCore = Color.Lerp(Color.white, theme, 0.42f);
            hotCore.a = 1f;
            Color midFlame = theme;
            midFlame.a = 0.9f;
            Color tailFade = Color.Lerp(theme, Color.black, 0.55f);
            tailFade.a = 0f;

            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.startLifetime = ParticleLifetime;
            main.startSpeed = ParticleSpeed;
            main.startSize = ParticleSize;
            main.startRotation = 0f;
            main.gravityModifier = 0f;
            main.maxParticles = 96;
            main.startColor = new ParticleSystem.MinMaxGradient(hotCore, midFlame);

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = EmissionRate;
            emission.rateOverDistance = 0f;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = 0.012f;
            shape.radiusThickness = 0f;
            shape.arc = 360f;
            shape.randomDirectionAmount = 0.05f;
            shape.sphericalDirectionAmount = 0f;
            shape.randomPositionAmount = 0.01f;
            shape.alignToDirection = false;
            shape.rotation = new Vector3(0f, 0f, 180f);

            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.35f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);

            ParticleSystem.LimitVelocityOverLifetimeModule limit = particles.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.35f;
            limit.drag = 1.5f;

            ParticleSystem.SizeOverLifetimeModule sizeOverLife = particles.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(0.35f, 0.65f),
                    new Keyframe(1f, 0.05f)));

            ParticleSystem.ColorOverLifetimeModule colorOverLife = particles.colorOverLifetime;
            colorOverLife.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(hotCore, 0f),
                    new GradientColorKey(midFlame, 0.35f),
                    new GradientColorKey(tailFade, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(hotCore.a, 0f),
                    new GradientAlphaKey(midFlame.a, 0.4f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLife.color = gradient;

            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = false;

            if (particleRenderer)
            {
                particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
                particleRenderer.velocityScale = 0.12f;
                particleRenderer.lengthScale = 1.35f;
                particleRenderer.maxParticleSize = 0.35f;
                particleRenderer.sortingOrder = 19;
                particleRenderer.sharedMaterial = runtimeMaterial;
            }

            particles.Clear(true);
            if (isActiveAndEnabled)
                particles.Play(true);
        }
    }
}

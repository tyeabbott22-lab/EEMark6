using UnityEngine;

namespace ExtraterrestrialExhaust.Player
{
    /// <summary>
    /// Visual response for flight input. It deliberately does not apply physics;
    /// it mirrors the command state with exhaust, squash, and stretch.
    /// </summary>
    [RequireComponent(typeof(PlayerFlightInput))]
    [RequireComponent(typeof(PlayerFlightMotor))]
    public sealed class PlayerFlightPresentation : MonoBehaviour
    {
        [SerializeField] Transform visual;
        [SerializeField] Transform leftExhaust;
        [SerializeField] Transform rightExhaust;
        [SerializeField] PlayerFlightStateMachine stateMachine;
        [SerializeField] PlayerFlightMotor flightMotor;
        [SerializeField] SpriteRenderer visualRenderer;
        [SerializeField] Sprite[] flightFrames;
        [SerializeField] Sprite[] thrustFrames;
        [SerializeField, Min(0.1f)] float animationFramesPerSecond = 8f;
        [SerializeField, Min(0.1f)] float thrustFramesPerSecond = 14f;
        [SerializeField] AudioSource thrustAudio;
        [SerializeField] AudioClip thrustClip;
        [SerializeField, Min(0f)] float exhaustLength = 0.55f;
        [SerializeField, Min(0f)] float turnExhaustAmount = 1f;
        [SerializeField] Vector2 squashScale = new Vector2(1.25f, 0.75f);
        [SerializeField, Min(0f)] float squashDuration = 0.12f;
        [SerializeField, Min(0f)] float squashReturnSpeed = 14f;

        PlayerFlightInput input;
        ParticleSystem leftExhaustParticles;
        ParticleSystem rightExhaustParticles;
        Vector3 visualBaseScale;
        float squashTimer;
        Sprite[] currentFrames;
        int frameIndex;
        float frameTimer;

        void Awake()
        {
            input = GetComponent<PlayerFlightInput>();
            stateMachine = stateMachine ? stateMachine : GetComponent<PlayerFlightStateMachine>();
            flightMotor = flightMotor ? flightMotor : GetComponent<PlayerFlightMotor>();
            visual = visual ? visual : transform.Find("Craft Visual");
            visual = visual ? visual : transform;
            visualRenderer = visualRenderer ? visualRenderer : visual.GetComponent<SpriteRenderer>();
            visualBaseScale = visual.localScale;
            EnsureExhaust(ref leftExhaust, "Left Exhaust", -0.28f);
            EnsureExhaust(ref rightExhaust, "Right Exhaust", 0.28f);
            EnsureParticleExhaust(ref leftExhaustParticles, "Left Exhaust Particles", leftExhaust);
            EnsureParticleExhaust(ref rightExhaustParticles, "Right Exhaust Particles", rightExhaust);
        }

        void OnEnable()
        {
            if (flightMotor)
                flightMotor.Flipped += HandleFlipped;
        }

        void OnDisable()
        {
            if (flightMotor)
                flightMotor.Flipped -= HandleFlipped;
        }

        void Update()
        {
            if (stateMachine && !stateMachine.AcceptsPlayerInput)
            {
                squashTimer = 0f;
                AnimateExhaust(leftExhaust, leftExhaustParticles, 0f);
                AnimateExhaust(rightExhaust, rightExhaustParticles, 0f);
                UpdateThrustAudio(false);
                UpdateSpriteAnimation(false);
                return;
            }

            Vector2 command = input.Move;
            float thrust = Mathf.Clamp01(Mathf.Max(0f, command.y));
            float turn = Mathf.Clamp01(Mathf.Abs(command.x));
            bool stabilizing = command.y < -0.2f;
            float leftExhaustAmount = stabilizing ? 0f : thrust;
            float rightExhaustAmount = stabilizing ? 0f : thrust;

            // Match EE5's asymmetric thruster read: thrust uses both flames,
            // while rotation uses the flame on the side producing the torque.
            if (!stabilizing && turn > 0.01f)
            {
                float turningAmount = turn * turnExhaustAmount;
                bool turningLeft = command.x < 0f;
                bool facingRight = !flightMotor || flightMotor.FacingRight;
                bool useRightThruster = turningLeft == facingRight;
                if (useRightThruster)
                    rightExhaustAmount = Mathf.Max(rightExhaustAmount, turningAmount);
                else
                    leftExhaustAmount = Mathf.Max(leftExhaustAmount, turningAmount);
            }

            AnimateExhaust(leftExhaust, leftExhaustParticles, leftExhaustAmount);
            AnimateExhaust(rightExhaust, rightExhaustParticles, rightExhaustAmount);
            UpdateThrustAudio(!stabilizing && (thrust > 0.01f || turn > 0.01f));
            UpdateSpriteAnimation(!stabilizing && thrust > 0.01f);

            Vector3 targetScale = visualBaseScale;
            if (flightMotor && !flightMotor.FacingRight)
                targetScale.x = -Mathf.Abs(targetScale.x);

            if (input.WasFlipPressed || input.Move.y < -0.2f)
                squashTimer = squashDuration;

            if (squashTimer > 0f)
            {
                squashTimer = Mathf.Max(0f, squashTimer - Time.deltaTime);
                targetScale.x = Mathf.Sign(targetScale.x) * Mathf.Abs(visualBaseScale.x) * squashScale.x;
                targetScale.y = visualBaseScale.y * squashScale.y;
            }

            float scaleT = 1f - Mathf.Exp(-squashReturnSpeed * Time.deltaTime);
            visual.localScale = Vector3.Lerp(visual.localScale, targetScale, scaleT);
        }

        /// <summary>
        /// Restores transient flight visuals after a life ends. Capture uses
        /// scripted presentation, so this is intentionally called by respawn
        /// rather than inferred from every non-flight state.
        /// </summary>
        public void ResetPresentation()
        {
            squashTimer = 0f;
            AnimateExhaust(leftExhaust, leftExhaustParticles, 0f);
            AnimateExhaust(rightExhaust, rightExhaustParticles, 0f);
            UpdateThrustAudio(false);

            if (!visual)
                return;

            Vector3 targetScale = visualBaseScale;
            if (flightMotor && !flightMotor.FacingRight)
                targetScale.x = -Mathf.Abs(targetScale.x);
            visual.localScale = targetScale;
        }

        void HandleFlipped(bool facingRight)
        {
            // EE5 couples the visual flip to a short squash pulse. Keeping the
            // event on the motor prevents update-order drift between input and
            // presentation when a flip is triggered by a non-keyboard device.
            squashTimer = squashDuration;
        }

        void EnsureExhaust(ref Transform exhaust, string name, float xPosition)
        {
            if (!exhaust)
            {
                GameObject exhaustObject = new GameObject(name);
                exhaustObject.transform.SetParent(transform, false);
                exhaustObject.transform.localPosition = new Vector3(xPosition, -0.35f, 0f);
                exhaust = exhaustObject.transform;
            }

            if (!exhaust.TryGetComponent(out LineRenderer line))
            {
                line = exhaust.gameObject.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.positionCount = 2;
                line.startWidth = 0.12f;
                line.endWidth = 0.02f;
                line.startColor = new Color(0.2f, 0.85f, 1f);
                line.endColor = new Color(0.6f, 0.1f, 1f, 0f);
                line.material = new Material(Shader.Find("Sprites/Default"));
            }
        }

        void EnsureParticleExhaust(
            ref ParticleSystem particles,
            string name,
            Transform exhaust)
        {
            if (!particles)
            {
                GameObject particleObject = new GameObject(name);
                particleObject.transform.SetParent(exhaust, false);
                particles = particleObject.AddComponent<ParticleSystem>();
            }

            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, 0.34f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.25f, 0.92f, 1f, 0.82f),
                new Color(0.62f, 0.12f, 1f, 0.2f));
            main.maxParticles = 90;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.035f;

            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            velocity.y = new ParticleSystem.MinMaxCurve(-1.5f, -0.55f);

            ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.18f, 0.78f, 1f), 0.35f),
                    new GradientColorKey(new Color(0.56f, 0.08f, 1f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.85f, 0f),
                    new GradientAlphaKey(0.45f, 0.45f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = gradient;

            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = 8;
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void AnimateExhaust(Transform exhaust, ParticleSystem particles, float amount)
        {
            LineRenderer line = exhaust.GetComponent<LineRenderer>();
            line.enabled = amount > 0.01f;
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, Vector3.down * Mathf.Lerp(0.08f, exhaustLength, amount));
            line.startWidth = Mathf.Lerp(0.04f, 0.14f, amount);

            if (!particles)
                return;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = Mathf.Lerp(0f, 24f, amount);
            if (amount > 0.01f)
            {
                if (!particles.isPlaying)
                    particles.Play(true);
            }
            else if (particles.isPlaying)
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        void UpdateSpriteAnimation(bool thrusting)
        {
            Sprite[] frames = thrusting && thrustFrames != null && thrustFrames.Length > 0
                ? thrustFrames
                : flightFrames;
            if (visualRenderer == null || frames == null || frames.Length == 0)
                return;

            if (currentFrames != frames)
            {
                currentFrames = frames;
                frameIndex = 0;
                frameTimer = 0f;
                visualRenderer.sprite = currentFrames[0];
            }

            if (frames.Length <= 1)
                return;

            frameTimer += Time.deltaTime;
            float frameDuration = 1f / (thrusting ? thrustFramesPerSecond : animationFramesPerSecond);
            if (frameTimer < frameDuration)
                return;

            frameTimer -= frameDuration;
            frameIndex = (frameIndex + 1) % frames.Length;
            visualRenderer.sprite = frames[frameIndex];
        }

        void UpdateThrustAudio(bool thrusting)
        {
            if (!thrustAudio || !thrustClip)
                return;

            if (thrustAudio.clip != thrustClip)
            {
                thrustAudio.clip = thrustClip;
                thrustAudio.loop = true;
            }

            if (thrusting && !thrustAudio.isPlaying)
                thrustAudio.Play();
            else if (!thrusting && thrustAudio.isPlaying)
                thrustAudio.Stop();
        }
    }
}

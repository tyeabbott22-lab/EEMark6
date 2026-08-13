using System.Collections.Generic;
using UnityEngine;
using ExtraterrestrialExhaust.Core;

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
        [SerializeField] bool enforceEe5Profile = true;

        [Header("Rotation Boost Exhaust")]
        [SerializeField, Min(1f)] float boostedExhaustLengthMultiplier = 1.25f;
        [SerializeField, Min(1f)] float boostedExhaustWidthMultiplier = 1.15f;
        [SerializeField, Min(1f)] float boostedExhaustYScale = 1.5f;
        [SerializeField, Min(1f)] float boostedParticleEmissionMultiplier = 1.4f;
        [SerializeField] Color boostedExhaustStartColor = new Color(0.75f, 1f, 1f, 1f);
        [SerializeField] Color boostedExhaustMidColor = new Color(0.15f, 0.75f, 1f, 0.92f);
        [SerializeField] Color boostedExhaustEndColor = new Color(0.12f, 0.4f, 1f, 0f);

        [Header("Particle Collision")]
        // EE5 lets exhaust particles meet the room surfaces. This is visual
        // feedback only; particles never become gameplay hitboxes.
        [SerializeField] bool exhaustParticlesCollide = true;
        [SerializeField] LayerMask exhaustCollisionMask = ~0;
        [SerializeField, Range(0f, 1f)] float exhaustCollisionDampen = 0.72f;
        [SerializeField, Range(0f, 1f)] float exhaustCollisionBounce = 0.04f;
        [SerializeField, Range(0f, 1f)] float exhaustCollisionLifetimeLoss = 1f;
        [SerializeField, Range(0.01f, 2f)] float exhaustCollisionRadiusScale = 0.35f;
        [SerializeField] int exhaustSortingOrder = -1;

        [Header("Exhaust Anchors")]
        [SerializeField] Vector3 leftExhaustAnchor = new Vector3(-0.28f, -0.35f, 0f);
        [SerializeField] Vector3 rightExhaustAnchor = new Vector3(0.28f, -0.35f, 0f);
        [SerializeField, Min(0f)] float exhaustLength = 0.55f;
        [SerializeField, Min(0f)] float turnExhaustAmount = 1f;
        [SerializeField] Vector2 squashScale = new Vector2(1.25f, 0.75f);
        [SerializeField, Min(0f)] float squashDuration = 0.12f;
        [SerializeField, Min(0f)] float squashReturnSpeed = 14f;

        PlayerFlightInput input;
        ParticleSystem leftExhaustParticles;
        ParticleSystem rightExhaustParticles;
        Vector3 visualBaseScale;
        Vector3 leftExhaustBaseLocalPosition;
        Vector3 rightExhaustBaseLocalPosition;
        Vector3 leftParticleBaseScale = Vector3.one;
        Vector3 rightParticleBaseScale = Vector3.one;
        bool exhaustBaseFacingRight = true;
        bool exhaustAnchorsCached;
        float squashTimer;
        Sprite[] currentFrames;
        int frameIndex;
        float frameTimer;
        Gradient normalExhaustGradient;
        Gradient boostedExhaustGradient;
        readonly List<Material> runtimeMaterials = new();
        bool externalCaptureActive;

        void Awake()
        {
            if (enforceEe5Profile)
                ApplyEe5Profile();

            normalExhaustGradient = CreateExhaustGradient(
                Color.white,
                new Color(0.18f, 0.78f, 1f),
                new Color(0.56f, 0.08f, 1f),
                0.85f,
                0.45f);
            boostedExhaustGradient = CreateExhaustGradient(
                boostedExhaustStartColor,
                boostedExhaustMidColor,
                boostedExhaustEndColor,
                1f,
                boostedExhaustMidColor.a);

            input = GetComponent<PlayerFlightInput>();
            stateMachine = stateMachine ? stateMachine : GetComponent<PlayerFlightStateMachine>();
            flightMotor = flightMotor ? flightMotor : GetComponent<PlayerFlightMotor>();
            visual = visual ? visual : transform.Find("Craft Visual");
            visual = visual ? visual : transform;
            visualRenderer = visualRenderer ? visualRenderer : visual.GetComponent<SpriteRenderer>();
            visualBaseScale = visual.localScale;
            EnsureExhaust(ref leftExhaust, "Left Exhaust", leftExhaustAnchor);
            EnsureExhaust(ref rightExhaust, "Right Exhaust", rightExhaustAnchor);
            ApplyAuthoredExhaustAnchors();
            EnsureParticleExhaust(ref leftExhaustParticles, "Left Exhaust Particles", leftExhaust);
            EnsureParticleExhaust(ref rightExhaustParticles, "Right Exhaust Particles", rightExhaust);
            CacheExhaustAnchors();
            SyncExhaustAnchors();
        }

        void ApplyEe5Profile()
        {
            boostedExhaustLengthMultiplier = Ee5SliceProfile.PlayerBoostedExhaustLengthMultiplier;
            boostedExhaustWidthMultiplier = Ee5SliceProfile.PlayerBoostedExhaustWidthMultiplier;
            boostedExhaustYScale = Ee5SliceProfile.PlayerBoostedExhaustYScale;
            boostedParticleEmissionMultiplier =
                Ee5SliceProfile.PlayerBoostedParticleEmissionMultiplier;
            boostedExhaustStartColor = Ee5SliceProfile.PlayerBoostedExhaustCoreColor;
            boostedExhaustMidColor = Ee5SliceProfile.PlayerBoostedExhaustMidColor;
            boostedExhaustEndColor = Ee5SliceProfile.PlayerBoostedExhaustTipColor;
            leftExhaustAnchor = Ee5SliceProfile.PlayerLeftExhaustAnchor;
            rightExhaustAnchor = Ee5SliceProfile.PlayerRightExhaustAnchor;
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
            StopPresentation();
        }

        void OnDestroy()
        {
            StopPresentation();
            foreach (Material material in runtimeMaterials)
            {
                if (material)
                    Destroy(material);
            }
        }

        void Update()
        {
            SyncExhaustAnchors();

            // LevelExit owns the craft scale during extraction. Without this
            // handoff, the scripted flight state restores neutral scale every
            // render frame while the portal is trying to pull the craft in.
            if (externalCaptureActive)
            {
                squashTimer = 0f;
                AnimateExhaust(leftExhaust, leftExhaustParticles, 0f, false);
                AnimateExhaust(rightExhaust, rightExhaustParticles, 0f, false);
                UpdateThrustAudio(false);
                UpdateSpriteAnimation(false);
                return;
            }

            if (stateMachine && !stateMachine.AcceptsPlayerInput)
            {
                squashTimer = 0f;
                AnimateExhaust(leftExhaust, leftExhaustParticles, 0f, false);
                AnimateExhaust(rightExhaust, rightExhaustParticles, 0f, false);
                UpdateThrustAudio(false);
                UpdateSpriteAnimation(false);
                RestoreNeutralScale();
                return;
            }

            if (flightMotor && flightMotor.IsInStopperZone)
            {
                // The EE5 stopper suppresses the control response as well as
                // the physics force. Do not leave a held thrust command
                // visually burning through the neutral coasting volume.
                squashTimer = 0f;
                AnimateExhaust(leftExhaust, leftExhaustParticles, 0f, false);
                AnimateExhaust(rightExhaust, rightExhaustParticles, 0f, false);
                UpdateThrustAudio(false);
                UpdateSpriteAnimation(false);
                RestoreNeutralScale();
                return;
            }

            GetFlightPresentationState(
                out Vector2 command,
                out float thrust,
                out float turn,
                out bool stabilizing);
            float leftExhaustAmount = stabilizing ? 0f : thrust;
            float rightExhaustAmount = stabilizing ? 0f : thrust;
            bool leftBoosted = false;
            bool rightBoosted = false;

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

                // EE5 uses the turning flame as a visibly hotter boost only
                // while the craft is under thrust. The side changes with the
                // facing state, so a flip never leaves the boost on the old
                // visual side for a frame.
                if (thrust > 0.01f)
                {
                    if (useRightThruster)
                        rightBoosted = true;
                    else
                        leftBoosted = true;
                }
            }

            AnimateExhaust(leftExhaust, leftExhaustParticles, leftExhaustAmount, leftBoosted);
            AnimateExhaust(rightExhaust, rightExhaustParticles, rightExhaustAmount, rightBoosted);
            UpdateThrustAudio(!stabilizing && (thrust > 0.01f || turn > 0.01f));
            UpdateSpriteAnimation(!stabilizing && thrust > 0.01f);

            Vector3 targetScale = visualBaseScale;
            if (flightMotor && !flightMotor.FacingRight)
                targetScale.x = -Mathf.Abs(targetScale.x);

            if (input.WasFlipPressed || stabilizing)
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
            externalCaptureActive = false;
            squashTimer = 0f;
            ResetSpriteAnimation();
            SyncExhaustAnchors();
            AnimateExhaust(leftExhaust, leftExhaustParticles, 0f, false);
            AnimateExhaust(rightExhaust, rightExhaustParticles, 0f, false);
            UpdateThrustAudio(false);
            UpdateSpriteAnimation(false);

            if (!visual)
                return;

            Vector3 targetScale = visualBaseScale;
            if (flightMotor && !flightMotor.FacingRight)
                targetScale.x = -Mathf.Abs(targetScale.x);
            visual.localScale = targetScale;
        }

        /// <summary>
        /// Temporarily gives a scripted route animation ownership of the
        /// player's visual transform. Physics and gameplay state remain owned
        /// by the caller; this only prevents normal flight presentation from
        /// fighting the scripted scale.
        /// </summary>
        public void BeginExternalCapture()
        {
            externalCaptureActive = true;
            squashTimer = 0f;
            AnimateExhaust(leftExhaust, leftExhaustParticles, 0f, false);
            AnimateExhaust(rightExhaust, rightExhaustParticles, 0f, false);
            UpdateThrustAudio(false);
        }

        public void EndExternalCapture()
        {
            externalCaptureActive = false;
        }

        void HandleFlipped(bool facingRight)
        {
            // EE5 couples the visual flip to a short squash pulse. Keeping the
            // event on the motor prevents update-order drift between input and
            // presentation when a flip is triggered by a non-keyboard device.
            squashTimer = squashDuration;
            SyncExhaustAnchors();
            RefreshExhaustPresentation();
        }

        void RestoreNeutralScale()
        {
            if (!visual)
                return;

            Vector3 targetScale = visualBaseScale;
            if (flightMotor && !flightMotor.FacingRight)
                targetScale.x = -Mathf.Abs(targetScale.x);

            float scaleT = 1f - Mathf.Exp(-squashReturnSpeed * Time.deltaTime);
            visual.localScale = Vector3.Lerp(visual.localScale, targetScale, scaleT);
        }

        void CacheExhaustAnchors()
        {
            leftExhaustBaseLocalPosition = leftExhaust
                ? leftExhaust.localPosition
                : Vector3.zero;
            rightExhaustBaseLocalPosition = rightExhaust
                ? rightExhaust.localPosition
                : Vector3.zero;
            leftParticleBaseScale = leftExhaustParticles
                ? leftExhaustParticles.transform.localScale
                : Vector3.one;
            rightParticleBaseScale = rightExhaustParticles
                ? rightExhaustParticles.transform.localScale
                : Vector3.one;
            exhaustBaseFacingRight = !flightMotor || flightMotor.FacingRight;
            exhaustAnchorsCached = true;
        }

        void ApplyAuthoredExhaustAnchors()
        {
            if (leftExhaust && (!visual || !leftExhaust.IsChildOf(visual)))
                leftExhaust.localPosition = enforceEe5Profile
                    ? Ee5SliceProfile.PlayerLeftExhaustAnchor
                    : leftExhaustAnchor;

            if (rightExhaust && (!visual || !rightExhaust.IsChildOf(visual)))
                rightExhaust.localPosition = enforceEe5Profile
                    ? Ee5SliceProfile.PlayerRightExhaustAnchor
                    : rightExhaustAnchor;
        }

        void SyncExhaustAnchors()
        {
            if (!exhaustAnchorsCached || !flightMotor)
                return;

            bool mirrored = flightMotor.FacingRight != exhaustBaseFacingRight;
            float mirrorSign = mirrored ? -1f : 1f;

            if (leftExhaust && (!visual || !leftExhaust.IsChildOf(visual)))
            {
                Vector3 position = leftExhaustBaseLocalPosition;
                position.x = leftExhaustBaseLocalPosition.x * mirrorSign;
                leftExhaust.localPosition = position;
            }

            if (rightExhaust && (!visual || !rightExhaust.IsChildOf(visual)))
            {
                Vector3 position = rightExhaustBaseLocalPosition;
                position.x = rightExhaustBaseLocalPosition.x * mirrorSign;
                rightExhaust.localPosition = position;
            }
        }

        void RefreshExhaustPresentation()
        {
            if (!input)
                return;

            GetFlightPresentationState(
                out Vector2 command,
                out float thrust,
                out float turn,
                out bool stabilizing);
            float leftAmount = stabilizing ? 0f : thrust;
            float rightAmount = stabilizing ? 0f : thrust;
            bool leftBoosted = false;
            bool rightBoosted = false;

            if (!stabilizing && turn > 0.01f)
            {
                float turningAmount = turn * turnExhaustAmount;
                bool turningLeft = command.x < 0f;
                bool useRightThruster = turningLeft == flightMotor.FacingRight;
                if (useRightThruster)
                {
                    rightAmount = Mathf.Max(rightAmount, turningAmount);
                    rightBoosted = thrust > 0.01f;
                }
                else
                {
                    leftAmount = Mathf.Max(leftAmount, turningAmount);
                    leftBoosted = thrust > 0.01f;
                }
            }

            AnimateExhaust(leftExhaust, leftExhaustParticles, leftAmount, leftBoosted);
            AnimateExhaust(rightExhaust, rightExhaustParticles, rightAmount, rightBoosted);
        }

        /// <summary>
        /// Resolves the command that is actually being presented. The motor's
        /// fixed-step command is authoritative whenever it exists; reading
        /// input.Move here would let a render-frame flip refresh exhaust from
        /// a newer command than the physics body has consumed. That one-frame
        /// disagreement is especially visible when a turn and flip overlap.
        /// </summary>
        void GetFlightPresentationState(
            out Vector2 command,
            out float thrust,
            out float turn,
            out bool stabilizing)
        {
            command = flightMotor ? flightMotor.AppliedFlightInput : input.Move;
            PlayerFlightControlMode controlMode = flightMotor
                ? flightMotor.ControlMode
                : ResolveFallbackControlMode(command);
            bool motorIsTurning = controlMode == PlayerFlightControlMode.Turning
                || controlMode == PlayerFlightControlMode.TurningAndThrusting;
            bool motorIsThrusting = controlMode == PlayerFlightControlMode.Thrusting
                || controlMode == PlayerFlightControlMode.TurningAndThrusting;
            thrust = motorIsThrusting
                ? Mathf.Clamp01(Mathf.Max(0f, command.y))
                : 0f;
            turn = motorIsTurning
                ? Mathf.Clamp01(Mathf.Abs(command.x))
                : 0f;
            stabilizing = controlMode == PlayerFlightControlMode.Stabilizing;
        }

        static PlayerFlightControlMode ResolveFallbackControlMode(Vector2 command)
        {
            if (command.y < -0.2f)
                return PlayerFlightControlMode.Stabilizing;

            bool turning = Mathf.Abs(command.x) >= 0.01f;
            bool thrusting = command.y > 0.2f;
            return turning
                ? (thrusting
                    ? PlayerFlightControlMode.TurningAndThrusting
                    : PlayerFlightControlMode.Turning)
                : (thrusting
                    ? PlayerFlightControlMode.Thrusting
                    : PlayerFlightControlMode.Coasting);
        }

        void EnsureExhaust(ref Transform exhaust, string name, Vector3 localPosition)
        {
            if (!exhaust)
            {
                GameObject exhaustObject = new GameObject(name);
                exhaustObject.transform.SetParent(transform, false);
                exhaustObject.transform.localPosition = localPosition;
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
            }

            if (!line.sharedMaterial)
                line.sharedMaterial = CreateRuntimeMaterial($"{name} Material");
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
            // Unity 6 requires all linear velocity axes to use the same
            // MinMaxCurve mode. Author the unused Z axis explicitly instead
            // of inheriting its default constant mode from the module.
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
            color.enabled = true;
            color.color = normalExhaustGradient;

            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = exhaustSortingOrder;
            if (!renderer.sharedMaterial)
                renderer.sharedMaterial = CreateRuntimeMaterial($"{name} Particle Material");
            ConfigureExhaustParticleCollision(particles);
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void ConfigureExhaustParticleCollision(ParticleSystem particles)
        {
            if (!particles)
                return;

            ParticleSystem.CollisionModule collision = particles.collision;
            collision.enabled = exhaustParticlesCollide;
            if (!exhaustParticlesCollide)
                return;

            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision2D;
            collision.collidesWith = exhaustCollisionMask;
            collision.enableDynamicColliders = true;
            collision.dampen = new ParticleSystem.MinMaxCurve(exhaustCollisionDampen);
            collision.bounce = new ParticleSystem.MinMaxCurve(exhaustCollisionBounce);
            collision.lifetimeLoss = new ParticleSystem.MinMaxCurve(exhaustCollisionLifetimeLoss);
            collision.radiusScale = exhaustCollisionRadiusScale;
            collision.quality = ParticleSystemCollisionQuality.High;
            collision.sendCollisionMessages = false;
        }

        Material CreateRuntimeMaterial(string materialName)
        {
            Material material = RuntimeVisualMaterial.Create(materialName);
            if (!material)
                return null;
            runtimeMaterials.Add(material);
            return material;
        }

        void StopPresentation()
        {
            AnimateExhaust(leftExhaust, leftExhaustParticles, 0f, false);
            AnimateExhaust(rightExhaust, rightExhaustParticles, 0f, false);
            UpdateThrustAudio(false);
        }

        void AnimateExhaust(
            Transform exhaust,
            ParticleSystem particles,
            float amount,
            bool boosted)
        {
            if (!exhaust)
                return;

            LineRenderer line = exhaust.GetComponent<LineRenderer>();
            if (!line)
                return;

            line.enabled = amount > 0.01f;
            line.SetPosition(0, Vector3.zero);
            float boostScale = boosted ? boostedExhaustLengthMultiplier : 1f;
            line.SetPosition(
                1,
                Vector3.down * Mathf.Lerp(0.08f, exhaustLength * boostScale, amount));
            line.startWidth = Mathf.Lerp(0.04f, 0.14f, amount)
                * (boosted ? boostedExhaustWidthMultiplier : 1f);
            line.startColor = boosted
                ? boostedExhaustStartColor
                : new Color(0.2f, 0.85f, 1f);
            line.endColor = boosted
                ? boostedExhaustEndColor
                : new Color(0.6f, 0.1f, 1f, 0f);

            if (!particles)
                return;

            ParticleSystem.EmissionModule emission = particles.emission;
            float emissionMultiplier = boosted ? boostedParticleEmissionMultiplier : 1f;
            emission.rateOverTime = Mathf.Lerp(0f, 24f * emissionMultiplier, amount);
            ApplyParticleBoostScale(particles, boosted);
            ParticleSystem.MainModule main = particles.main;
            float particleSizeMultiplier = boosted ? boostedExhaustWidthMultiplier : 1f;
            main.startSize = new ParticleSystem.MinMaxCurve(
                0.035f * particleSizeMultiplier,
                0.09f * particleSizeMultiplier);
            main.startColor = boosted
                ? new ParticleSystem.MinMaxGradient(
                    boostedExhaustStartColor,
                    boostedExhaustEndColor)
                : new ParticleSystem.MinMaxGradient(
                    new Color(0.25f, 0.92f, 1f, 0.82f),
                    new Color(0.62f, 0.12f, 1f, 0.2f));
            ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
            color.enabled = true;
            color.color = boosted ? boostedExhaustGradient : normalExhaustGradient;
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

        static Gradient CreateExhaustGradient(
            Color core,
            Color mid,
            Color tip,
            float coreAlpha,
            float midAlpha)
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(core, 0f),
                    new GradientColorKey(mid, 0.45f),
                    new GradientColorKey(tip, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(coreAlpha, 0f),
                    new GradientAlphaKey(midAlpha, 0.45f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        void ApplyParticleBoostScale(ParticleSystem particles, bool boosted)
        {
            if (!particles)
                return;

            Vector3 baseScale = particles == leftExhaustParticles
                ? leftParticleBaseScale
                : rightParticleBaseScale;
            particles.transform.localScale = new Vector3(
                baseScale.x,
                baseScale.y * (boosted ? boostedExhaustYScale : 1f),
                baseScale.z);
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

        void ResetSpriteAnimation()
        {
            currentFrames = null;
            frameIndex = 0;
            frameTimer = 0f;
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

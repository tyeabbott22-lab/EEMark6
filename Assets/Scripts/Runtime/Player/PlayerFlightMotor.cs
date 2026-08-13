using System;
using UnityEngine;
using ExtraterrestrialExhaust.CameraSystem;
using ExtraterrestrialExhaust.Core;

namespace ExtraterrestrialExhaust.Player
{
    /// <summary>
    /// The mutually exclusive control contracts used by physics and
    /// presentation. Keeping this explicit prevents a stale visual input read
    /// from disagreeing with the motor's stabilize/stopper/scripted branch.
    /// </summary>
    public enum PlayerFlightControlMode
    {
        Scripted,
        Stopper,
        Coasting,
        Stabilizing,
        Turning,
        Thrusting,
        TurningAndThrusting
    }

    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(PlayerFlightInput))]
    [RequireComponent(typeof(PlayerFlightStateMachine))]
    public sealed class PlayerFlightMotor : MonoBehaviour
    {
        [Header("Flight")]
        [SerializeField, Min(0f)] float thrustForce = 55f;
        [SerializeField, Min(0f)] float rotationTorque = 0.4f;
        [SerializeField] bool rotationAddsThrust = true;
        [SerializeField, Min(0f)] float rotationBoostMultiplier = 0.225f;

        [Header("Stabilization")]
        [SerializeField, Min(0f)] float stabilizationSpeed = 720f;
        [SerializeField, Range(0f, 1f)] float angularDamping = 0.85f;
        [SerializeField] float stabilizationAngle;
        [SerializeField] Transform visual;
        [SerializeField] bool allowFlip = true;
        [SerializeField] bool enforceEe5Profile = true;

        [Header("Presentable Neutral Upright Assist")]
        [Tooltip("Short-range settle after a turn is released. It preserves deliberate flips and complements the authored S/C stabilize command.")]
        [SerializeField] bool uprightAssistEnabled = Ee5SliceProfile.UprightAssistEnabled;
        [SerializeField, Min(0f)] float uprightAssistWindow = Ee5SliceProfile.UprightAssistWindow;
        [SerializeField, Min(0f)] float uprightAssistSpeed = Ee5SliceProfile.UprightAssistSpeed;
        [SerializeField, Min(0f)] float uprightAssistAngularBrake = Ee5SliceProfile.UprightAssistAngularBrake;
        [SerializeField, Min(0f)] float uprightAssistMaxAngularSpeed = Ee5SliceProfile.UprightAssistMaxAngularSpeed;
        [Tooltip("Delay before neutral upright correction engages after turn input is released, preserving the authored torque handoff.")]
        [SerializeField, Min(0f)] float uprightAssistReleaseDelay = Ee5SliceProfile.UprightAssistReleaseDelay;
        [Tooltip("Removes only velocity directed into a contacted surface, preserving EE5-style tangential follow-through.")]
        [SerializeField] bool removeVelocityIntoColliders = true;

        [Header("Stopper Zone")]
        [Tooltip("EE5's lower-center volume suppresses new flight input while the craft coasts through it.")]
        [SerializeField] string stopperTag = "StopperZone";

        Rigidbody2D body;
        PlayerFlightInput input;
        PlayerFlightStateMachine stateMachine;
        bool facingRight = true;
        bool initialFacingRight = true;
        bool inStopperZone;
        RigidbodyConstraints2D constraintsBeforeStopper;
        bool savedStopperConstraints;
        float turnReleaseTimer;
        float lastSpinScoreRotation;
        float intentionalSpinDegrees;
        float brittlePassThroughTimer;
        Vector2 brittlePassThroughVelocity;
        float brittleAngularVelocityRetention = 0.18f;
        readonly ContactPoint2D[] contactBuffer = new ContactPoint2D[8];

        public Rigidbody2D Body => body;
        public Transform Visual => visual;
        public bool FacingRight => facingRight;
        public bool IsInStopperZone => inStopperZone;
        public PlayerFlightControlMode ControlMode { get; private set; } =
            PlayerFlightControlMode.Scripted;
        /// <summary>
        /// The command consumed by the last physics step. Presentation uses
        /// this instead of raw Update-time input so exhaust never advertises
        /// thrust while a scripted, stopper, or stabilize branch owns control.
        /// </summary>
        public Vector2 AppliedFlightInput { get; private set; }
        public Vector2 CurrentFlightInput => input ? input.Move : Vector2.zero;
        public event Action<bool> Flipped;

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            if (enforceEe5Profile)
                ApplyEe5Profile();

            input = GetComponent<PlayerFlightInput>();
            stateMachine = GetComponent<PlayerFlightStateMachine>();
            if (!visual)
                visual = transform.Find("Craft Visual");
            if (!visual)
                visual = transform;
            facingRight = visual.localScale.x >= 0f;
            initialFacingRight = facingRight;
            lastSpinScoreRotation = body.rotation;
        }

        void ApplyEe5Profile()
        {
            thrustForce = Ee5SliceProfile.ThrustForce;
            // Use the realScene3 sniper contract at runtime. This intentionally
            // overrides stale prefab/scene overrides in one place while the
            // reusable prefab is being cleaned up later.
            rotationTorque = Ee5SliceProfile.PlayerPresentableRotationTorque;
            rotationAddsThrust = true;
            rotationBoostMultiplier = Ee5SliceProfile.RotationBoostMultiplier;
            stabilizationSpeed = Ee5SliceProfile.StabilizationSpeed;
            angularDamping = Ee5SliceProfile.FlightAngularDamping;
            stabilizationAngle = 0f;
            uprightAssistEnabled = Ee5SliceProfile.UprightAssistEnabled;
            uprightAssistWindow = Ee5SliceProfile.UprightAssistWindow;
            uprightAssistSpeed = Ee5SliceProfile.UprightAssistSpeed;
            uprightAssistAngularBrake = Ee5SliceProfile.UprightAssistAngularBrake;
            uprightAssistMaxAngularSpeed = Ee5SliceProfile.UprightAssistMaxAngularSpeed;
            uprightAssistReleaseDelay = Ee5SliceProfile.UprightAssistReleaseDelay;
            removeVelocityIntoColliders = Ee5SliceProfile.PlayerRemoveVelocityIntoColliders;

            // The generated scene and reusable prefab share one presentable
            // flight contract. Do not let a stale prefab override silently
            // reintroduce the soupier raw drag while the EE6 player is being
            // brought into shape. Ee5SliceProfile keeps the exact EE5 values
            // beside this short-term bridge for the later prefab rip.
            // Keep the physics root at unit scale as well. The enlarged craft
            // art lives on Craft Visual; scaling this object changes collider
            // size and Rigidbody2D inertia, which is exactly the kind of old
            // presentation hack that makes turning feel inexplicably slow.
            transform.localScale = Vector3.one;
            body.mass = Ee5SliceProfile.PlayerMass;
            body.bodyType = RigidbodyType2D.Dynamic;
            body.simulated = true;
            body.constraints = RigidbodyConstraints2D.None;
            body.linearDamping = Ee5SliceProfile.PlayerFlightLinearDamping;
            body.angularDamping = Ee5SliceProfile.PlayerFlightAngularDamping;
            body.gravityScale = Ee5SliceProfile.PlayerGravityScale;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            CircleCollider2D playerCollider = GetComponent<CircleCollider2D>();
            if (playerCollider)
            {
                playerCollider.radius = Ee5SliceProfile.PlayerHitboxRadius;
                playerCollider.offset = Ee5SliceProfile.PlayerHitboxOffset;
                playerCollider.isTrigger = false;
            }
        }

        void OnDrawGizmosSelected()
        {
            CircleCollider2D playerCollider = GetComponent<CircleCollider2D>();
            if (!playerCollider)
                return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 1f, 0.85f, 0.85f);
            Gizmos.DrawWireSphere(playerCollider.offset, playerCollider.radius);
            Gizmos.matrix = previousMatrix;
        }

        void Update()
        {
            if (allowFlip
                && !inStopperZone
                && stateMachine.CurrentState == PlayerFlightState.FreeFlight
                && input.ConsumeFlipRequest())
                Flip();
        }

        void FixedUpdate()
        {
            Vector2 command = input ? input.Move : Vector2.zero;
            AppliedFlightInput = Vector2.zero;

            // EE5 awards a flip for a deliberate 360-degree flight rotation,
            // not only for the separate X-facing toggle. Track it on the
            // physics clock so interpolation and render rate cannot create
            // duplicate credits or make the result depend on frame timing.
            TrackIntentionalSpinScore(command);

            if (stateMachine.CurrentState != PlayerFlightState.FreeFlight)
            {
                // Scripted capture and death own the transform explicitly.
                // Do not let gravity or residual drag create a second motion
                // source while those state machines are in control.
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                turnReleaseTimer = 0f;
                ControlMode = PlayerFlightControlMode.Scripted;
                return;
            }

            if (inStopperZone)
            {
                // The EE5 stopper is a flight-control volume, not a wall. It
                // kills rotation and new input but deliberately preserves the
                // craft's linear momentum while it coasts through the strip.
                body.angularVelocity = 0f;
                turnReleaseTimer = 0f;
                ControlMode = PlayerFlightControlMode.Stopper;
                return;
            }

            // EE5's JetpackMotor treats stabilization as an exclusive input:
            // it rotates the craft toward the authored angle and skips thrust,
            // turning, and collision-velocity cleanup for that physics step.
            // Keeping this branch early prevents a held turn input from
            // fighting the stabilizer and preserves the reference's clean
            // "hit C/S, settle" feel.
            if (command.y < -0.2f)
            {
                turnReleaseTimer = 0f;
                ControlMode = PlayerFlightControlMode.Stabilizing;
                AppliedFlightInput = command;
                Stabilize();
                return;
            }

            bool turning = Mathf.Abs(command.x) >= 0.01f;
            bool thrusting = command.y > 0.2f;
            ControlMode = turning
                ? (thrusting
                    ? PlayerFlightControlMode.TurningAndThrusting
                    : PlayerFlightControlMode.Turning)
                : (thrusting
                    ? PlayerFlightControlMode.Thrusting
                    : PlayerFlightControlMode.Coasting);
            if (turning)
                turnReleaseTimer = 0f;
            else
                turnReleaseTimer += Time.fixedDeltaTime;

            AppliedFlightInput = command;

            // The imported EE5 stack has several historical input paths that
            // can all write to the same Rigidbody2D. Keep those paths
            // available below for the eventual prefab reconstruction, but
            // make the current playable slice deterministic: one bounded
            // response owns rotation, while thrust remains a single force.
            bool usePresentableTurnResponse =
                Ee5SliceProfile.PlayerPresentableTurnResponseOwnsRotation;
            if (!usePresentableTurnResponse)
                ApplyRotation(command.x);

            if (!turning && turnReleaseTimer >= uprightAssistReleaseDelay)
                ApplyNeutralUprightAssist();

            if (thrusting)
                body.AddRelativeForce(Vector2.up * thrustForce, ForceMode2D.Force);

            if (usePresentableTurnResponse
                && turning
                && rotationAddsThrust)
            {
                body.AddRelativeForce(
                    Vector2.up * (Mathf.Abs(command.x)
                        * thrustForce
                        * rotationBoostMultiplier),
                    ForceMode2D.Force);
            }

            // realScene3's sniper instance also has JetpackInput's direct
            // fallback enabled beside JetpackMotor. It is a strange but
            // observable part of the reference feel, so reproduce it as a
            // named compatibility pass owned by the motor.
            if (!usePresentableTurnResponse)
                ApplyEe5LegacyDirectPhysicsAssist(command, thrusting, turning);

            // Presentable bridge: the imported EE5 stack reaches its rotation
            // envelope quickly because the tiny authored collider has very low
            // rotational inertia. Use the same bounded angular-speed handoff
            // here instead of making the player wait on a slow torque ramp.
            // Release uses a short angular brake below. It does not auto-upright
            // the craft, so the player can still stop at a deliberate angle or
            // hold a turn for a full flip without fighting a hidden correction.
            if (turning)
                ApplyPresentableTurnResponse(command.x);
            else if (turnReleaseTimer >= Ee5SliceProfile.PlayerPresentableReleaseBrakeDelay)
                ApplyPresentableReleaseBrake();

            // A brittle break has already disabled the contact collider, but
            // Unity can still report the old contact until the next physics
            // step. Preserve the authored EE5 follow-through before the
            // generic wall-slide cleanup gets another chance to eat it.
            ApplyBrittleFollowThrough();

            // Keep the reference's full flip authority. A zero profile value
            // means no artificial angular-velocity ceiling is applied.
            float maxAngularVelocity = Ee5SliceProfile.PlayerPresentableMaxAngularVelocity;
            if (maxAngularVelocity > 0f)
                body.angularVelocity = Mathf.Clamp(
                    body.angularVelocity,
                    -maxAngularVelocity,
                    maxAngularVelocity);

            if (brittlePassThroughTimer <= 0f && removeVelocityIntoColliders)
                RemoveVelocityIntoColliders();
        }

        void TrackIntentionalSpinScore(Vector2 command)
        {
            if (!body)
                return;

            float currentRotation = body.rotation;
            float rotationDelta = Mathf.Abs(
                Mathf.DeltaAngle(lastSpinScoreRotation, currentRotation));
            lastSpinScoreRotation = currentRotation;

            bool intentionalSpin = stateMachine
                && stateMachine.CurrentState == PlayerFlightState.FreeFlight
                && !inStopperZone
                && command.y >= -0.2f
                && Mathf.Abs(command.x) >= 0.01f;

            if (!intentionalSpin)
            {
                intentionalSpinDegrees = 0f;
                return;
            }

            intentionalSpinDegrees += rotationDelta;
            while (intentionalSpinDegrees >= 360f)
            {
                intentionalSpinDegrees -= 360f;
                FindFirstObjectByType<ScoreSystem>()?.Award(ScoreReason.Flip);
            }
        }

        void ApplyRotation(float inputAmount)
        {
            if (Mathf.Abs(inputAmount) < 0.01f)
                return;

            body.AddTorque(-inputAmount * rotationTorque, ForceMode2D.Force);
            if (rotationAddsThrust)
                body.AddRelativeForce(Vector2.up * (Mathf.Abs(inputAmount) * thrustForce * rotationBoostMultiplier), ForceMode2D.Force);
        }

        void ApplyPresentableTurnResponse(float inputAmount)
        {
            float targetAngularVelocity = -Mathf.Clamp(inputAmount, -1f, 1f)
                * Ee5SliceProfile.PlayerPresentableMaxAngularVelocity;
            body.angularVelocity = Mathf.MoveTowards(
                body.angularVelocity,
                targetAngularVelocity,
                Ee5SliceProfile.PlayerPresentableTurnAcceleration
                * Time.fixedDeltaTime);
        }

        void ApplyPresentableReleaseBrake()
        {
            body.angularVelocity = Mathf.MoveTowards(
                body.angularVelocity,
                0f,
                Ee5SliceProfile.PlayerPresentableReleaseBrake
                * Time.fixedDeltaTime);
        }

        void ApplyEe5LegacyDirectPhysicsAssist(
            Vector2 command,
            bool thrusting,
            bool turning)
        {
            if (!Ee5SliceProfile.PlayerLegacyDirectPhysicsAssist
                || !body.simulated
                || body.bodyType != RigidbodyType2D.Dynamic)
                return;

            if (thrusting)
                body.AddRelativeForce(Vector2.up * thrustForce, ForceMode2D.Force);

            if (!turning)
                return;

            body.AddTorque(-command.x * rotationTorque, ForceMode2D.Force);
            if (rotationAddsThrust)
            {
                body.AddRelativeForce(
                    Vector2.up * (Mathf.Abs(command.x) * thrustForce * rotationBoostMultiplier),
                    ForceMode2D.Force);
            }
        }

        void Stabilize()
        {
            float targetAngle = Mathf.MoveTowardsAngle(
                body.rotation,
                stabilizationAngle,
                stabilizationSpeed * Time.fixedDeltaTime);

            body.MoveRotation(targetAngle);
            body.angularVelocity *= angularDamping;
        }

        void ApplyNeutralUprightAssist()
        {
            if (!uprightAssistEnabled || uprightAssistWindow <= 0f)
                return;

            float error = Mathf.DeltaAngle(body.rotation, stabilizationAngle);
            bool spinWithinAssistRange = uprightAssistMaxAngularSpeed <= 0f
                || Mathf.Abs(body.angularVelocity) <= uprightAssistMaxAngularSpeed;

            // This is the temporary presentability bridge while the imported
            // prefab values are still being reconciled. It only acts near the
            // authored upright angle and below a modest spin speed, so a held
            // turn still produces the realScene-style deliberate flip.
            body.angularVelocity = Mathf.MoveTowards(
                body.angularVelocity,
                0f,
                uprightAssistAngularBrake
                * (spinWithinAssistRange ? 1f : 0.35f)
                * Time.fixedDeltaTime);

            if (!spinWithinAssistRange || Mathf.Abs(error) > uprightAssistWindow)
                return;

            body.MoveRotation(Mathf.MoveTowardsAngle(
                body.rotation,
                stabilizationAngle,
                uprightAssistSpeed * Time.fixedDeltaTime));
        }

        /// <summary>
        /// EE5 keeps the craft from sticking or ricocheting when it grazes a
        /// wall: remove only the component moving into each contact normal and
        /// leave the tangent component available for a controlled slide.
        /// </summary>
        void RemoveVelocityIntoColliders()
        {
            int contactCount = body.GetContacts(contactBuffer);
            for (int i = 0; i < contactCount; i++)
            {
                Vector2 normal = contactBuffer[i].normal;
                float intoSurface = Vector2.Dot(body.linearVelocity, normal);
                if (intoSurface < 0f)
                    body.linearVelocity -= normal * intoSurface;
            }
        }

        /// <summary>
        /// Gives a brittle-wall break a short physics-clock handoff. The wall
        /// owns the impact decision; the motor owns velocity persistence so
        /// collision cleanup, drag, and the next input step cannot immediately
        /// turn a successful break into a dead stop.
        /// </summary>
        public void ApplyBrittleFollowThrough(
            Vector2 retainedVelocity,
            Vector2 positionNudge,
            float duration,
            float angularVelocityRetention)
        {
            if (!body || retainedVelocity.sqrMagnitude <= 0.0001f)
                return;

            brittlePassThroughVelocity = retainedVelocity;
            brittlePassThroughTimer = Mathf.Max(
                brittlePassThroughTimer,
                Mathf.Max(0f, duration));
            brittleAngularVelocityRetention = Mathf.Clamp01(angularVelocityRetention);

            body.linearVelocity = retainedVelocity;
            body.angularVelocity *= brittleAngularVelocityRetention;
            body.position += positionNudge;
            Physics2D.SyncTransforms();
        }

        void ApplyBrittleFollowThrough()
        {
            if (brittlePassThroughTimer <= 0f)
                return;

            brittlePassThroughTimer -= Time.fixedDeltaTime;
            if (brittlePassThroughVelocity.sqrMagnitude > body.linearVelocity.sqrMagnitude)
                body.linearVelocity = brittlePassThroughVelocity;

            body.angularVelocity *= brittleAngularVelocityRetention;
        }

        public void Flip()
        {
            if (inStopperZone)
                return;

            facingRight = !facingRight;
            Vector3 scale = visual.localScale;
            scale.x = Mathf.Abs(scale.x) * (facingRight ? 1f : -1f);
            visual.localScale = scale;
            PlayerCameraFollow.Instance?.ZoomForPlayerFlip();
            Flipped?.Invoke(facingRight);
        }

        /// <summary>
        /// Restores the authored room-start facing for reusable in-place
        /// respawns without awarding a flip or triggering camera feedback.
        /// </summary>
        public void ResetFacingForRespawn()
        {
            if (inStopperZone)
                ExitStopperZone();

            facingRight = initialFacingRight;
            if (!visual)
                return;

            Vector3 scale = visual.localScale;
            scale.x = Mathf.Abs(scale.x) * (facingRight ? 1f : -1f);
            visual.localScale = scale;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (IsStopper(other))
                EnterStopperZone();
        }

        void OnTriggerExit2D(Collider2D other)
        {
            if (IsStopper(other))
                ExitStopperZone();
        }

        void OnCollisionEnter2D(Collision2D collision)
        {
            if (collision != null && IsStopper(collision.collider))
                EnterStopperZone();

            // EE5 routes collision feedback through the flight motor itself,
            // not through hull damage. The gold slice keeps wall contact
            // non-damaging, but a hard slam must still read in the camera.
            PlayerCameraFollow.Instance?.TryShakeForWallImpact(collision);
        }

        void OnCollisionStay2D(Collision2D collision)
        {
            if (collision != null && IsStopper(collision.collider))
            {
                inStopperZone = true;
                // Match EE5's solid-stopper fallback: remove the horizontal
                // component while the collider is holding the craft, but
                // leave vertical drift available for the authored pass-through.
                body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
                body.angularVelocity = 0f;
            }
        }

        void OnCollisionExit2D(Collision2D collision)
        {
            if (collision != null && IsStopper(collision.collider))
                ExitStopperZone();
        }

        bool IsStopper(Collider2D other)
        {
            return other && !string.IsNullOrEmpty(stopperTag) && other.CompareTag(stopperTag);
        }

        void EnterStopperZone()
        {
            if (!inStopperZone)
            {
                constraintsBeforeStopper = body.constraints;
                savedStopperConstraints = true;
                body.constraints |= RigidbodyConstraints2D.FreezeRotation;
            }

            inStopperZone = true;
            input.ClearInputState();
            body.angularVelocity = 0f;
        }

        void ExitStopperZone()
        {
            inStopperZone = false;

            if (!savedStopperConstraints)
                return;

            body.constraints = constraintsBeforeStopper;
            savedStopperConstraints = false;
        }
    }
}

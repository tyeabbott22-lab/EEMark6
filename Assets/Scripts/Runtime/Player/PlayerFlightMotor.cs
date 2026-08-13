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

        [Header("Optional Neutral Upright Assist")]
        [Tooltip("Prototype/accessibility assist. Disabled by the EE5 gold profile; S/C remains the authored stabilize command.")]
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
            rotationTorque = Ee5SliceProfile.RotationTorque;
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

            // The generated scene and the reusable prefab are one authored
            // flight contract. Do not let a stale prefab override silently
            // reintroduce a different damping/body mode after the builder has
            // validated the scene. This is the same profile used by the EE5
            // realScene instance, with the current EE6 linear carry-through.
            body.mass = Ee5SliceProfile.PlayerMass;
            body.bodyType = RigidbodyType2D.Dynamic;
            body.simulated = true;
            body.constraints = RigidbodyConstraints2D.None;
            body.linearDamping = Ee5SliceProfile.PlayerFlightLinearDamping;
            body.angularDamping = Ee5SliceProfile.PlayerFlightAngularDamping;
            body.gravityScale = Ee5SliceProfile.PlayerGravityScale;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
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
            ApplyRotation(command.x);

            if (!turning && turnReleaseTimer >= uprightAssistReleaseDelay)
                ApplyNeutralUprightAssist();

            if (thrusting)
                body.AddRelativeForce(Vector2.up * thrustForce, ForceMode2D.Force);

            if (removeVelocityIntoColliders)
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

            // This is intentionally an optional prototype aid, not part of the
            // EE5 gold contract. When enabled, it still never becomes a hidden
            // flip or a replacement for the explicit stabilize command.
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

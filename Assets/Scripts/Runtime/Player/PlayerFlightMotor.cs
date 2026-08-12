using System;
using UnityEngine;
using ExtraterrestrialExhaust.CameraSystem;
using ExtraterrestrialExhaust.Core;

namespace ExtraterrestrialExhaust.Player
{
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

        [Header("Neutral Upright Assist")]
        [Tooltip("Gently returns small uncommanded tilt toward the authored neutral angle. Rotation input disables this for intentional flips.")]
        [SerializeField] bool uprightAssistEnabled = true;
        [SerializeField, Min(0f)] float uprightAssistWindow = Ee5SliceProfile.UprightAssistWindow;
        [SerializeField, Min(0f)] float uprightAssistSpeed = Ee5SliceProfile.UprightAssistSpeed;
        [SerializeField, Min(0f)] float uprightAssistAngularBrake = Ee5SliceProfile.UprightAssistAngularBrake;
        [SerializeField, Min(0f)] float uprightAssistMaxAngularSpeed = Ee5SliceProfile.UprightAssistMaxAngularSpeed;
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
        readonly ContactPoint2D[] contactBuffer = new ContactPoint2D[8];

        public Rigidbody2D Body => body;
        public Transform Visual => visual;
        public bool FacingRight => facingRight;
        public bool IsInStopperZone => inStopperZone;
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
            removeVelocityIntoColliders = Ee5SliceProfile.PlayerRemoveVelocityIntoColliders;

            body.mass = Ee5SliceProfile.PlayerMass;
            body.gravityScale = Ee5SliceProfile.PlayerGravityScale;
            // The EE5 prefab values are the reference point, but this Unity 6
            // pass needs a little more carry-through so a held Q/E turn can
            // complete a readable flip instead of feeling submerged in syrup.
            body.linearDamping = Ee5SliceProfile.PlayerFlightLinearDamping;
            body.angularDamping = Ee5SliceProfile.PlayerFlightAngularDamping;
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
            if (stateMachine.CurrentState != PlayerFlightState.FreeFlight)
            {
                // Scripted capture and death own the transform explicitly.
                // Do not let gravity or residual drag create a second motion
                // source while those state machines are in control.
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                return;
            }

            if (inStopperZone)
            {
                // The EE5 stopper is a flight-control volume, not a wall. It
                // kills rotation and new input but deliberately preserves the
                // craft's linear momentum while it coasts through the strip.
                body.angularVelocity = 0f;
                return;
            }

            // EE5's JetpackMotor treats stabilization as an exclusive input:
            // it rotates the craft toward the authored angle and skips thrust,
            // turning, and collision-velocity cleanup for that physics step.
            // Keeping this branch early prevents a held turn input from
            // fighting the stabilizer and preserves the reference's clean
            // “hit C/S, settle” feel.
            if (input.Move.y < -0.2f)
            {
                Stabilize();
                return;
            }

            ApplyRotation(input.Move.x);

            if (Mathf.Abs(input.Move.x) < 0.01f)
                ApplyNeutralUprightAssist();

            if (input.Move.y > 0.2f)
                body.AddRelativeForce(Vector2.up * thrustForce, ForceMode2D.Force);

            if (removeVelocityIntoColliders)
                RemoveVelocityIntoColliders();
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

            // Do not apply a hidden flip: this is a released-turn settle assist,
            // not a second stabilization button. Bleed a little residual spin
            // even outside the angle window, then ease the final tilt only when
            // the craft is close enough. This keeps Q/E responsive while making
            // a short accidental tap settle instead of becoming a full flip.
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

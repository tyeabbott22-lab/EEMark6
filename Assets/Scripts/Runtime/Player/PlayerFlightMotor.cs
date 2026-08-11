using UnityEngine;
using ExtraterrestrialExhaust.CameraSystem;

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

        Rigidbody2D body;
        PlayerFlightInput input;
        PlayerFlightStateMachine stateMachine;
        bool facingRight = true;

        public Rigidbody2D Body => body;
        public Transform Visual => visual;
        public bool FacingRight => facingRight;

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            input = GetComponent<PlayerFlightInput>();
            stateMachine = GetComponent<PlayerFlightStateMachine>();
            if (!visual)
                visual = transform.Find("Craft Visual");
            if (!visual)
                visual = transform;
            facingRight = visual.localScale.x >= 0f;
        }

        void Update()
        {
            if (allowFlip && stateMachine.CurrentState == PlayerFlightState.FreeFlight && input.WasFlipPressed)
                Flip();
        }

        void FixedUpdate()
        {
            if (stateMachine.CurrentState != PlayerFlightState.FreeFlight)
            {
                body.angularVelocity = 0f;
                return;
            }

            ApplyRotation(input.Move.x);

            if (input.Move.y > 0.2f)
                body.AddRelativeForce(Vector2.up * thrustForce, ForceMode2D.Force);

            if (input.Move.y < -0.2f)
                Stabilize();
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

        public void Flip()
        {
            facingRight = !facingRight;
            Vector3 scale = visual.localScale;
            scale.x = Mathf.Abs(scale.x) * (facingRight ? 1f : -1f);
            visual.localScale = scale;
            PlayerCameraFollow.Instance?.ZoomForPlayerFlip();
        }
    }
}

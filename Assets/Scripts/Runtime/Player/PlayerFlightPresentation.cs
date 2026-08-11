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
        [SerializeField] AudioSource thrustAudio;
        [SerializeField] AudioClip thrustClip;
        [SerializeField, Min(0f)] float exhaustLength = 0.55f;
        [SerializeField, Min(0f)] float turnExhaustAmount = 1f;
        [SerializeField] Vector2 squashScale = new Vector2(1.25f, 0.75f);
        [SerializeField, Min(0f)] float squashDuration = 0.12f;
        [SerializeField, Min(0f)] float squashReturnSpeed = 14f;

        PlayerFlightInput input;
        Vector3 visualBaseScale;
        float squashTimer;

        void Awake()
        {
            input = GetComponent<PlayerFlightInput>();
            stateMachine = stateMachine ? stateMachine : GetComponent<PlayerFlightStateMachine>();
            flightMotor = flightMotor ? flightMotor : GetComponent<PlayerFlightMotor>();
            visual = visual ? visual : transform.Find("Craft Visual");
            visual = visual ? visual : transform;
            visualBaseScale = visual.localScale;
            EnsureExhaust(ref leftExhaust, "Left Exhaust", -0.28f);
            EnsureExhaust(ref rightExhaust, "Right Exhaust", 0.28f);
        }

        void Update()
        {
            if (stateMachine && !stateMachine.AcceptsPlayerInput)
            {
                squashTimer = 0f;
                AnimateExhaust(leftExhaust, 0f);
                AnimateExhaust(rightExhaust, 0f);
                UpdateThrustAudio(false);
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

            AnimateExhaust(leftExhaust, leftExhaustAmount);
            AnimateExhaust(rightExhaust, rightExhaustAmount);
            UpdateThrustAudio(!stabilizing && (thrust > 0.01f || turn > 0.01f));

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
            AnimateExhaust(leftExhaust, 0f);
            AnimateExhaust(rightExhaust, 0f);
            UpdateThrustAudio(false);

            if (!visual)
                return;

            Vector3 targetScale = visualBaseScale;
            if (flightMotor && !flightMotor.FacingRight)
                targetScale.x = -Mathf.Abs(targetScale.x);
            visual.localScale = targetScale;
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

        void AnimateExhaust(Transform exhaust, float amount)
        {
            LineRenderer line = exhaust.GetComponent<LineRenderer>();
            line.enabled = amount > 0.01f;
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, Vector3.down * Mathf.Lerp(0.08f, exhaustLength, amount));
            line.startWidth = Mathf.Lerp(0.04f, 0.14f, amount);
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

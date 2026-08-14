using UnityEngine;
using UnityEngine.InputSystem;
using ExtraterrestrialExhaust.Core;

namespace ExtraterrestrialExhaust.Player
{
    /// <summary>
    /// Translates the Unity Input System into a simple command for the flight motor.
    /// The motor never reads keyboard, gamepad, or UI input directly.
    /// </summary>
    public sealed class PlayerFlightInput : MonoBehaviour
    {
        [SerializeField] InputActionReference moveAction;
        [SerializeField] InputActionReference flipAction;
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] string moveActionName = "Player/Move";
        [SerializeField] string flipActionName = "Player/Flip";
        [SerializeField] PlayerFlightStateMachine stateMachine;
        [SerializeField] GameStateMachine gameState;
        [Header("EE5 Keyboard Compatibility")]
        [Tooltip("Keeps the realScene Q/E rotation and C stabilization controls available when an imported action asset omits them.")]
        [SerializeField] bool includeEe5KeyboardFallback = true;

        [Header("Analog Hygiene")]
        [Tooltip("Suppresses controller stick drift without changing full-strength keyboard commands.")]
        [SerializeField, Range(0f, 0.25f)] float turnDeadzone = Ee5SliceProfile.PlayerTurnDeadzone;
        [SerializeField, Range(0f, 0.25f)] float thrustDeadzone = Ee5SliceProfile.PlayerThrustDeadzone;

        public Vector2 Move { get; private set; }
        public bool WasFlipPressed { get; private set; }

        bool flipRequestLatched;

        InputAction ResolvedMoveAction => moveAction != null
            ? moveAction.action
            : inputActions != null ? inputActions.FindAction(moveActionName) : null;

        InputAction ResolvedFlipAction => flipAction != null
            ? flipAction.action
            : inputActions != null ? inputActions.FindAction(flipActionName) : null;

        void Reset()
        {
            stateMachine = GetComponent<PlayerFlightStateMachine>();
        }

        void Awake()
        {
            if (!stateMachine)
                stateMachine = GetComponent<PlayerFlightStateMachine>();
            if (!gameState)
                gameState = FindFirstObjectByType<GameStateMachine>();
        }

        void OnEnable()
        {
            ResolvedMoveAction?.Enable();
            ResolvedFlipAction?.Enable();
        }

        void OnDisable()
        {
            ResolvedMoveAction?.Disable();
            ResolvedFlipAction?.Disable();
            ClearInputState();
        }

        void Update()
        {
            bool canReadInput = stateMachine != null
                && stateMachine.AcceptsPlayerInput
                && (!gameState || gameState.IsPlaying);
            InputAction resolvedMoveAction = ResolvedMoveAction;
            bool hasMoveAction = resolvedMoveAction != null;
            Move = canReadInput && hasMoveAction
                ? resolvedMoveAction.ReadValue<Vector2>()
                : Vector2.zero;
            Move = SanitizeMove(Move);

            if (canReadInput && includeEe5KeyboardFallback && Keyboard.current != null)
            {
                Keyboard keyboard = Keyboard.current;
                Vector2 keyboardCommand = Move;
                bool turnNegativePressed = keyboard.aKey.isPressed
                    || keyboard.leftArrowKey.isPressed
                    || keyboard.qKey.isPressed;
                bool turnPositivePressed = keyboard.dKey.isPressed
                    || keyboard.rightArrowKey.isPressed
                    || keyboard.eKey.isPressed;
                bool turnKeyPressed = turnNegativePressed || turnPositivePressed;

                bool stabilizePressed = keyboard.sKey.isPressed
                    || keyboard.downArrowKey.isPressed
                    || keyboard.cKey.isPressed;
                bool thrustPressed = keyboard.wKey.isPressed
                    || keyboard.upArrowKey.isPressed
                    || keyboard.spaceKey.isPressed;

                // The generated action asset uses a normalized Dpad
                // composite, so W+D arrives as approximately .707/.707.
                // EE5 reads these keys as independent booleans and applies
                // full thrust plus full torque. Restore that authored command
                // contract whenever a digital keyboard axis is actually down,
                // while leaving a gamepad-only command analog.
                if (turnKeyPressed)
                {
                    keyboardCommand.x = KeyboardAxis(
                        turnNegativePressed,
                        turnPositivePressed);
                }

                // Stabilization is an exclusive EE5 branch. S/C must win over
                // W/Space rather than algebraically cancelling to neutral.
                if (stabilizePressed || thrustPressed)
                {
                    keyboardCommand.y = stabilizePressed ? -1f : 1f;
                }

                // Re-apply deadzones after the digital compatibility command is
                // merged with any analog input.
                Move = SanitizeMove(keyboardCommand);
            }

            if (!canReadInput)
            {
                flipRequestLatched = false;
            }
            else if ((ResolvedFlipAction != null && ResolvedFlipAction.WasPressedThisFrame())
                || (includeEe5KeyboardFallback
                    && Keyboard.current != null
                    && Keyboard.current.xKey.wasPressedThisFrame))
            {
                // EE5's X flip was read through the legacy input path. Keep
                // that keyboard contract alive even when an imported action
                // asset has the Flip action but no serialized X binding.
                // Keep the edge alive until the motor consumes it because
                // Unity does not guarantee sibling Update order.
                flipRequestLatched = true;
            }

            WasFlipPressed = flipRequestLatched;
        }

        static float KeyboardAxis(bool negativePressed, bool positivePressed)
        {
            return (positivePressed ? 1f : 0f)
                - (negativePressed ? 1f : 0f);
        }

        /// <summary>
        /// Clears the command edge when a gameplay volume or scripted state
        /// takes control. The next Update may sample held hardware again, but
        /// no stale flip pulse or thrust vector survives the hand-off.
        /// </summary>
        public void ClearInputState()
        {
            Move = Vector2.zero;
            flipRequestLatched = false;
            WasFlipPressed = false;
        }

        /// <summary>
        /// Consumes one flip edge without making presentation depend on script
        /// execution order. The public property remains available for visual
        /// systems that need to observe the same frame's request.
        /// </summary>
        public bool ConsumeFlipRequest()
        {
            if (!flipRequestLatched)
                return false;

            flipRequestLatched = false;
            WasFlipPressed = false;
            return true;
        }

        Vector2 SanitizeMove(Vector2 raw)
        {
            return new Vector2(
                ApplyAxisDeadzone(Mathf.Clamp(raw.x, -1f, 1f), turnDeadzone),
                ApplyAxisDeadzone(Mathf.Clamp(raw.y, -1f, 1f), thrustDeadzone));
        }

        static float ApplyAxisDeadzone(float value, float deadzone)
        {
            float magnitude = Mathf.Abs(value);
            if (magnitude <= deadzone)
                return 0f;

            float remapped = Mathf.InverseLerp(deadzone, 1f, magnitude);
            return Mathf.Sign(value) * remapped;
        }
    }
}

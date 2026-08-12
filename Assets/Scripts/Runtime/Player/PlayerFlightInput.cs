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
                float legacyTurn = 0f;
                float legacyThrust = 0f;
                if (Keyboard.current.aKey.isPressed)
                    legacyTurn -= 1f;
                if (Keyboard.current.dKey.isPressed)
                    legacyTurn += 1f;
                if (Keyboard.current.qKey.isPressed)
                    legacyTurn -= 1f;
                if (Keyboard.current.eKey.isPressed)
                    legacyTurn += 1f;

                if (Keyboard.current.wKey.isPressed || Keyboard.current.spaceKey.isPressed)
                    legacyThrust += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.cKey.isPressed)
                    legacyThrust -= 1f;

                // Add instead of replacing the action value so the old EE5
                // keyboard contract remains usable alongside a gamepad or a
                // partially authored action asset. Clamping preserves the
                // expected full-strength button response.
                Move = new Vector2(
                    Mathf.Clamp(Move.x + legacyTurn, -1f, 1f),
                    Mathf.Clamp(Move.y + legacyThrust, -1f, 1f));

                // Re-apply deadzones after the legacy keys are combined with
                // the action asset. Keyboard values remain full strength.
                Move = SanitizeMove(Move);
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

        /// <summary>
        /// Used by editor tooling to bind a generated test scene without exposing
        /// Unity serialization details to gameplay systems.
        /// </summary>
        public void ConfigureInputAsset(InputActionAsset asset, string actionName = "Player/Move")
        {
            InputAction previousMoveAction = ResolvedMoveAction;
            InputAction previousFlipAction = ResolvedFlipAction;
            previousMoveAction?.Disable();
            previousFlipAction?.Disable();

            inputActions = asset;
            moveActionName = actionName;
            if (isActiveAndEnabled)
            {
                ResolvedMoveAction?.Enable();
                ResolvedFlipAction?.Enable();
            }
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

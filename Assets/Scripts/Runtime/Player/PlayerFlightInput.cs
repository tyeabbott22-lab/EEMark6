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

        public Vector2 Move { get; private set; }
        public bool WasFlipPressed { get; private set; }

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

            if (canReadInput && includeEe5KeyboardFallback && Keyboard.current != null)
            {
                float legacyTurn = 0f;
                if (Keyboard.current.qKey.isPressed)
                    legacyTurn -= 1f;
                if (Keyboard.current.eKey.isPressed)
                    legacyTurn += 1f;

                if (!Mathf.Approximately(legacyTurn, 0f))
                {
                    // Add instead of replacing the action value so Q/E remain
                    // compatible with A/D and gamepad rotation, including the
                    // authored cancellation behavior when opposite keys overlap.
                    Move = new Vector2(
                        Mathf.Clamp(Move.x + legacyTurn, -1f, 1f),
                        Move.y);
                }

                if (Keyboard.current.cKey.isPressed)
                    Move = new Vector2(Move.x, Mathf.Min(Move.y, -1f));
            }

            WasFlipPressed = canReadInput && ResolvedFlipAction != null && ResolvedFlipAction.WasPressedThisFrame();
        }

        /// <summary>
        /// Clears the command edge when a gameplay volume or scripted state
        /// takes control. The next Update may sample held hardware again, but
        /// no stale flip pulse or thrust vector survives the hand-off.
        /// </summary>
        public void ClearInputState()
        {
            Move = Vector2.zero;
            WasFlipPressed = false;
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
    }
}

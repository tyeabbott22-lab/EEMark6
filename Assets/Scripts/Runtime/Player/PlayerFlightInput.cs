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
            Move = Vector2.zero;
            WasFlipPressed = false;
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
            WasFlipPressed = canReadInput && ResolvedFlipAction != null && ResolvedFlipAction.WasPressedThisFrame();
        }

        /// <summary>
        /// Used by editor tooling to bind a generated test scene without exposing
        /// Unity serialization details to gameplay systems.
        /// </summary>
        public void ConfigureInputAsset(InputActionAsset asset, string actionName = "Player/Move")
        {
            inputActions = asset;
            moveActionName = actionName;
        }
    }
}

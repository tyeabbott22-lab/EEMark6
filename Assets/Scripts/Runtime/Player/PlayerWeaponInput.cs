using UnityEngine;
using UnityEngine.InputSystem;
using ExtraterrestrialExhaust.Core;

namespace ExtraterrestrialExhaust.Player
{
    /// <summary>
    /// Owns weapon input state, including hold-to-fire UI input.
    /// The weapon itself only asks whether it should fire.
    /// </summary>
    public sealed class PlayerWeaponInput : MonoBehaviour
    {
        [SerializeField] InputActionReference attackAction;
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] string attackActionName = "Player/Attack";
        [SerializeField] PlayerFlightStateMachine stateMachine;
        [SerializeField] GameStateMachine gameState;

        int uiHoldCount;
        bool uiHeld;

        InputAction ResolvedAttackAction => attackAction != null
            ? attackAction.action
            : inputActions != null ? inputActions.FindAction(attackActionName) : null;

        public bool IsFiring
        {
            get
            {
                InputAction action = ResolvedAttackAction;
                bool actionHeld = action != null && action.IsPressed();
                if (stateMachine != null && !stateMachine.AcceptsPlayerInput)
                    return false;
                if (gameState && !gameState.IsPlaying)
                    return false;

                return actionHeld || uiHeld;
            }
        }

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

        void OnEnable() => ResolvedAttackAction?.Enable();

        void OnDisable()
        {
            ResolvedAttackAction?.Disable();
            ClearUIInput();
        }

        public void StartFiring()
        {
            uiHoldCount++;
            uiHeld = true;
        }

        public void StopFiring()
        {
            uiHoldCount = Mathf.Max(0, uiHoldCount - 1);
            uiHeld = uiHoldCount > 0;
        }

        public void ClearUIInput()
        {
            uiHoldCount = 0;
            uiHeld = false;
        }

        public void ConfigureInputAsset(InputActionAsset asset, string actionName = "Player/Attack")
        {
            inputActions = asset;
            attackActionName = actionName;
        }
    }
}

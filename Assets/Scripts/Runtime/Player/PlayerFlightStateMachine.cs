using System;
using UnityEngine;

namespace ExtraterrestrialExhaust.Player
{
    /// <summary>
    /// Owns the player's high-level flight state and exposes one place for systems
    /// such as cutscenes, hazards, and respawning to pause simulation.
    /// </summary>
    public sealed class PlayerFlightStateMachine : MonoBehaviour
    {
        [SerializeField] PlayerFlightState initialState = PlayerFlightState.FreeFlight;

        public PlayerFlightState CurrentState { get; private set; }
        public bool AcceptsPlayerInput => CurrentState == PlayerFlightState.FreeFlight;
        public event Action<PlayerFlightState, PlayerFlightState> StateChanged;

        void Awake()
        {
            CurrentState = initialState;
        }

        public bool TrySetState(PlayerFlightState nextState)
        {
            if (CurrentState == nextState)
                return false;

            PlayerFlightState previousState = CurrentState;
            CurrentState = nextState;
            StateChanged?.Invoke(previousState, nextState);
            return true;
        }
    }
}

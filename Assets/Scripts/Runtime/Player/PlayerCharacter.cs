using UnityEngine;
using ExtraterrestrialExhaust.Combat;
using ExtraterrestrialExhaust.Core;

namespace ExtraterrestrialExhaust.Player
{
    /// <summary>
    /// Composition root for the playable character.
    /// Other systems can depend on this stable identity instead of searching for
    /// individual movement, health, or weapon components.
    /// </summary>
    [RequireComponent(typeof(PlayerFlightStateMachine))]
    [RequireComponent(typeof(PlayerFlightInput))]
    [RequireComponent(typeof(PlayerFlightMotor))]
    [RequireComponent(typeof(HealthComponent))]
    public sealed class PlayerCharacter : MonoBehaviour
    {
        public PlayerFlightStateMachine FlightState { get; private set; }
        public PlayerFlightInput FlightInput { get; private set; }
        public PlayerFlightMotor FlightMotor { get; private set; }
        public HealthComponent Health { get; private set; }
        public GameStateMachine GameState { get; private set; }
        public bool CanReceiveGameplayInput => Health && Health.IsAlive
            && (!FlightState || FlightState.AcceptsPlayerInput)
            && (!GameState || GameState.IsPlaying);

        void Awake()
        {
            FlightState = GetComponent<PlayerFlightStateMachine>();
            FlightInput = GetComponent<PlayerFlightInput>();
            FlightMotor = GetComponent<PlayerFlightMotor>();
            Health = GetComponent<HealthComponent>();
            GameState = FindFirstObjectByType<GameStateMachine>();
        }
    }
}

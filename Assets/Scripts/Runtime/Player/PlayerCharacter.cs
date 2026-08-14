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
        /// <summary>
        /// Physics-space position for systems that update in FixedUpdate.
        /// Reading an interpolated Transform from a physics tick can introduce
        /// a visible one-frame chase correction in carried objectives.
        /// </summary>
        public Vector2 PhysicsPosition => FlightMotor && FlightMotor.Body
            ? FlightMotor.Body.position
            : (Vector2)transform.position;
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

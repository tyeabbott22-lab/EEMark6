using System;
using UnityEngine;
using ExtraterrestrialExhaust.Player;

namespace ExtraterrestrialExhaust.Core
{
    public enum ScoreReason
    {
        Speed,
        EnemyDamaged,
        EnemyDefeated,
        ObjectiveCollected,
        GateDeactivated,
        WallBroken,
        LevelCompleted
    }

    /// <summary>Event-driven arcade score service for combat and objective feedback.</summary>
    public sealed class ScoreSystem : MonoBehaviour
    {
        [SerializeField] int startingScore;
        [Header("Movement Credits")]
        [SerializeField] PlayerCharacter player;
        [SerializeField] GameStateMachine gameState;
        [SerializeField, Min(0f)] float speedCreditThreshold = 11f;
        [SerializeField, Min(0f)] float speedCreditCooldown = 0.45f;
        [SerializeField, Min(0)] int speedCreditPoints = 25;

        public int CurrentScore { get; private set; }
        public event Action<int, int, ScoreReason> ScoreChanged;
        float nextSpeedCreditTime;

        void Awake()
        {
            CurrentScore = startingScore;
            ResolveReferences();
        }

        void Update()
        {
            ResolveReferences();
            if (!player || !player.CanReceiveGameplayInput
                || (gameState && !gameState.IsPlaying)
                || !player.FlightMotor || !player.FlightInput
                || player.FlightInput.Move.sqrMagnitude <= 0.001f)
                return;

            Rigidbody2D body = player.FlightMotor.Body;
            if (!body || body.linearVelocity.magnitude < speedCreditThreshold
                || Time.time < nextSpeedCreditTime)
                return;

            nextSpeedCreditTime = Time.time + speedCreditCooldown;
            AddScore(speedCreditPoints, ScoreReason.Speed);
        }

        public void AddScore(int points, ScoreReason reason)
        {
            if (points <= 0)
                return;

            CurrentScore += points;
            ScoreChanged?.Invoke(CurrentScore, points, reason);
        }

        public void ResetScore()
        {
            CurrentScore = startingScore;
            ScoreChanged?.Invoke(CurrentScore, 0, ScoreReason.LevelCompleted);
        }

        void ResolveReferences()
        {
            if (!player)
                player = FindFirstObjectByType<PlayerCharacter>();
            if (!gameState)
                gameState = FindFirstObjectByType<GameStateMachine>();
        }
    }
}

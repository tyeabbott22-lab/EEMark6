using System;
using UnityEngine;
using ExtraterrestrialExhaust.Player;

namespace ExtraterrestrialExhaust.Core
{
    public enum ScoreReason
    {
        Speed,
        Flip,
        EnemyDamaged,
        EnemyDefeated,
        NearMiss,
        ObjectiveCollected,
        GateDeactivated,
        WallBroken,
        LevelCompleted
    }

    /// <summary>
    /// Event-driven arcade score service for combat, movement, and objective
    /// feedback. The score is cumulative; the chain is the time-sensitive
    /// EE5-style layer that rewards keeping the flight loop active.
    /// </summary>
    public sealed class ScoreSystem : MonoBehaviour
    {
        [Header("Points")]
        [SerializeField, Min(0)] int startingScore;
        [SerializeField, Min(0)] int speedCreditPoints = 25;
        [SerializeField, Min(0)] int flipPoints = 100;
        [SerializeField, Min(0)] int enemyDamagePoints = 75;
        [SerializeField, Min(0)] int enemyDefeatPoints = 300;
        [SerializeField, Min(0)] int nearMissPoints = 150;
        [SerializeField, Min(0)] int objectiveCollectedPoints = 175;
        [SerializeField, Min(0)] int gateDeactivatedPoints = 250;
        [SerializeField, Min(0)] int wallBrokenPoints = 200;
        [SerializeField, Min(0)] int levelCompletedPoints = 500;

        [Header("Movement Credits")]
        [SerializeField] PlayerCharacter player;
        [SerializeField] GameStateMachine gameState;
        [SerializeField, Min(0f)] float speedCreditThreshold = 11f;
        [SerializeField, Min(0f)] float speedCreditCooldown = 0.45f;

        [Header("Arcade Chain")]
        [SerializeField, Min(0.25f)] float startingComboSeconds = 4f;
        [SerializeField, Min(0.25f)] float minimumComboSeconds = 1.25f;
        [SerializeField, Min(1)] int comboCreditsToMinimum = 18;
        [SerializeField, Min(0f)] float timeMultiplierPerSecond = 0.85f;
        [SerializeField, Min(1f)] float exponentialMultiplierGrowth = 1.34f;
        [SerializeField, Min(1f)] float chainMultiplierGrowth = 1.16f;
        [SerializeField, Min(1f)] float lateChainSurgeGrowth = 1.08f;
        [SerializeField, Min(1)] int lateChainSurgeStartsAt = 20;
        [SerializeField, Min(1f)] float maximumMultiplier = 50f;

        public int CurrentScore { get; private set; }
        public int ComboCredits { get; private set; }
        public float CurrentMultiplier { get; private set; } = 1f;
        public float ComboTimeRemaining { get; private set; }
        public event Action<int, int, ScoreReason> ScoreChanged;
        public event Action ComboBroken;

        float nextSpeedCreditTime;
        float scoreStartedTime = -999f;
        PlayerFlightMotor subscribedMotor;

        void Awake()
        {
            CurrentScore = startingScore;
            ResolveReferences();
        }

        void OnDisable()
        {
            UnsubscribeFromPlayerMotor();
        }

        void Update()
        {
            ResolveReferences();
            if (!gameState || gameState.IsPlaying)
                UpdateComboTimer();

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
            Award(ScoreReason.Speed);
        }

        /// <summary>Applies the authored base value for a gameplay beat.</summary>
        public void Award(ScoreReason reason)
        {
            AddScore(GetBasePoints(reason), reason);
        }

        public void AddScore(int points, ScoreReason reason)
        {
            // Completion is awarded immediately before the state transitions
            // to GameOver; every other score beat belongs to active play.
            if (points <= 0
                || (gameState && !gameState.IsPlaying
                    && reason != ScoreReason.LevelCompleted))
                return;

            BeginOrContinueCombo();
            int awarded = Mathf.Max(1, Mathf.RoundToInt(points * CurrentMultiplier));
            long total = (long)CurrentScore + awarded;
            CurrentScore = total >= int.MaxValue ? int.MaxValue : (int)total;
            ScoreChanged?.Invoke(CurrentScore, awarded, reason);
        }

        public void ResetScore()
        {
            CurrentScore = startingScore;
            ResetComboState();
            ScoreChanged?.Invoke(CurrentScore, 0, ScoreReason.LevelCompleted);
        }

        void HandlePlayerFlipped(bool facingRight) => Award(ScoreReason.Flip);

        void UpdateComboTimer()
        {
            if (ComboCredits <= 0 || ComboTimeRemaining <= 0f)
                return;

            ComboTimeRemaining = Mathf.Max(0f, ComboTimeRemaining - Time.deltaTime);
            if (ComboTimeRemaining > 0f)
                return;

            ResetComboState();
            ComboBroken?.Invoke();
        }

        void BeginOrContinueCombo()
        {
            if (ComboCredits <= 0)
                scoreStartedTime = Time.time;

            ComboCredits++;
            CurrentMultiplier = CalculateMultiplier();
            ComboTimeRemaining = GetCurrentComboSeconds();
        }

        void ResetComboState()
        {
            ComboCredits = 0;
            CurrentMultiplier = 1f;
            ComboTimeRemaining = 0f;
            scoreStartedTime = -999f;
        }

        float GetCurrentComboSeconds()
        {
            float progress = comboCreditsToMinimum > 0
                ? Mathf.Clamp01(ComboCredits / (float)comboCreditsToMinimum)
                : 1f;
            float easedProgress = 1f - Mathf.Pow(1f - progress, 1.6f);
            return Mathf.Max(
                0.25f,
                Mathf.Lerp(startingComboSeconds, minimumComboSeconds, easedProgress));
        }

        float CalculateMultiplier()
        {
            float activeSeconds = Mathf.Max(0f, Time.time - scoreStartedTime);
            float creditBonus = Mathf.Pow(
                ComboCredits / Mathf.Max(1f, comboCreditsToMinimum),
                1.35f);
            float timeBonus = activeSeconds * timeMultiplierPerSecond;
            float timeGrowth = Mathf.Pow(exponentialMultiplierGrowth, activeSeconds);
            float chainGrowth = Mathf.Pow(chainMultiplierGrowth, ComboCredits);
            float lateChain = Mathf.Max(0f, ComboCredits - lateChainSurgeStartsAt);
            float lateSurge = lateChain > 0f
                ? Mathf.Pow(lateChainSurgeGrowth, Mathf.Pow(lateChain, 1.25f))
                : 1f;
            return Mathf.Clamp(
                (1f + creditBonus + timeBonus) * timeGrowth * chainGrowth * lateSurge,
                1f,
                maximumMultiplier);
        }

        int GetBasePoints(ScoreReason reason)
        {
            switch (reason)
            {
                case ScoreReason.Speed:
                    return speedCreditPoints;
                case ScoreReason.Flip:
                    return flipPoints;
                case ScoreReason.EnemyDamaged:
                    return enemyDamagePoints;
                case ScoreReason.EnemyDefeated:
                    return enemyDefeatPoints;
                case ScoreReason.NearMiss:
                    return nearMissPoints;
                case ScoreReason.ObjectiveCollected:
                    return objectiveCollectedPoints;
                case ScoreReason.GateDeactivated:
                    return gateDeactivatedPoints;
                case ScoreReason.WallBroken:
                    return wallBrokenPoints;
                case ScoreReason.LevelCompleted:
                    return levelCompletedPoints;
                default:
                    return 0;
            }
        }

        void ResolveReferences()
        {
            if (!player)
                player = FindFirstObjectByType<PlayerCharacter>();
            if (!gameState)
                gameState = FindFirstObjectByType<GameStateMachine>();

            PlayerFlightMotor nextMotor = player ? player.FlightMotor : null;
            if (subscribedMotor == nextMotor)
                return;

            UnsubscribeFromPlayerMotor();
            subscribedMotor = nextMotor;
            if (subscribedMotor)
                subscribedMotor.Flipped += HandlePlayerFlipped;
        }

        void UnsubscribeFromPlayerMotor()
        {
            if (subscribedMotor)
                subscribedMotor.Flipped -= HandlePlayerFlipped;
            subscribedMotor = null;
        }
    }
}

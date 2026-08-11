using System;
using UnityEngine;

namespace ExtraterrestrialExhaust.Core
{
    public enum ScoreReason
    {
        EnemyDefeated,
        ObjectiveCollected,
        GateDeactivated,
        LevelCompleted
    }

    /// <summary>Event-driven arcade score service for combat and objective feedback.</summary>
    public sealed class ScoreSystem : MonoBehaviour
    {
        [SerializeField] int startingScore;

        public int CurrentScore { get; private set; }
        public event Action<int, int, ScoreReason> ScoreChanged;

        void Awake() => CurrentScore = startingScore;

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
    }
}

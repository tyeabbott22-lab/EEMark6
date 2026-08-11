using System;
using UnityEngine;
using ExtraterrestrialExhaust.Enemy;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// Tracks a bounded combat encounter and exposes a single completion signal
    /// for doors, exits, UI, and level flow.
    /// </summary>
    public sealed class EncounterController : MonoBehaviour
    {
        [SerializeField] EnemyController[] encounterEnemies;

        EnemyController[] enemies;
        ScoreSystem scoreSystem;

        public bool IsComplete { get; private set; }
        public event Action Completed;

        void Awake()
        {
            enemies = encounterEnemies != null && encounterEnemies.Length > 0
                ? encounterEnemies
                : FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
            scoreSystem = FindFirstObjectByType<ScoreSystem>();
            if (enemies.Length == 0)
                CompleteEncounter();
        }

        void OnEnable()
        {
            if (enemies == null)
                return;

            foreach (EnemyController enemy in enemies)
                if (enemy) enemy.Defeated += HandleEnemyDefeated;
        }

        void OnDisable()
        {
            if (enemies == null)
                return;

            foreach (EnemyController enemy in enemies)
                if (enemy) enemy.Defeated -= HandleEnemyDefeated;
        }

        void HandleEnemyDefeated(EnemyController defeatedEnemy)
        {
            scoreSystem?.AddScore(100, ScoreReason.EnemyDefeated);
            foreach (EnemyController enemy in enemies)
                if (enemy && enemy.State != EnemyState.Defeated)
                    return;

            CompleteEncounter();
        }

        void CompleteEncounter()
        {
            if (IsComplete)
                return;

            IsComplete = true;
            Completed?.Invoke();
        }
    }
}

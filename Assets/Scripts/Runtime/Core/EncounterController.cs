using System;
using UnityEngine;
using ExtraterrestrialExhaust.Combat;
using ExtraterrestrialExhaust.Enemy;
using ExtraterrestrialExhaust.Player;

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
            // Evaluate the authored roster immediately. This matters when a
            // scene is re-enabled or loaded additively after its enemies have
            // already been defeated; waiting for another Defeated event would
            // leave the objective director permanently in ClearEncounter.
            EvaluateCompletion();
        }

        void EvaluateCompletion()
        {
            if (IsComplete)
                return;

            if (enemies == null || enemies.Length == 0)
            {
                CompleteEncounter();
                return;
            }

            foreach (EnemyController enemy in enemies)
                if (enemy && enemy.State != EnemyState.Defeated)
                    return;

            CompleteEncounter();
        }

        void OnEnable()
        {
            if (enemies == null)
                return;

            foreach (EnemyController enemy in enemies)
                if (enemy)
                {
                    enemy.Defeated += HandleEnemyDefeated;
                    enemy.Damaged += HandleEnemyDamaged;
                }

            EvaluateCompletion();
        }

        void OnDisable()
        {
            if (enemies == null)
                return;

            foreach (EnemyController enemy in enemies)
                if (enemy)
                {
                    enemy.Defeated -= HandleEnemyDefeated;
                    enemy.Damaged -= HandleEnemyDamaged;
                }
        }

        void HandleEnemyDamaged(EnemyController damagedEnemy, DamageInfo damage)
        {
            if (!damage.Source || !damage.Source.GetComponentInParent<PlayerCharacter>())
                return;

            scoreSystem?.Award(ScoreReason.EnemyDamaged);
        }

        void HandleEnemyDefeated(EnemyController defeatedEnemy)
        {
            scoreSystem?.Award(ScoreReason.EnemyDefeated);
            EvaluateCompletion();
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

using System;
using UnityEngine;

namespace ExtraterrestrialExhaust.Combat
{
    /// <summary>
    /// Reusable health implementation for the player, enemies, and destructible props.
    /// Death behavior belongs to the owning object; this component only owns health rules.
    /// </summary>
    public sealed class HealthComponent : MonoBehaviour, IDamageable
    {
        [SerializeField, Min(1f)] float maxHealth = 10f;
        [SerializeField, Min(0f)] float invulnerabilityDuration = 0.5f;
        [SerializeField] bool destroyOnDeath;

        public float CurrentHealth { get; private set; }
        public float MaxHealth => maxHealth;
        public float InvulnerabilityDuration => invulnerabilityDuration;
        public bool IsAlive => CurrentHealth > 0f;
        public bool IsInvulnerable => invulnerabilityTimer > 0f;

        public event Action<DamageInfo> Damaged;
        public event Action<float> HealthChanged;
        public event Action Died;

        float invulnerabilityTimer;

        void Awake()
        {
            ResetHealth();
        }

        void Update()
        {
            if (invulnerabilityTimer > 0f)
                invulnerabilityTimer -= Time.deltaTime;
        }

        public bool TryTakeDamage(DamageInfo damage)
        {
            if (!IsAlive || IsInvulnerable || damage.Amount <= 0f)
                return false;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - damage.Amount);
            invulnerabilityTimer = invulnerabilityDuration;
            Damaged?.Invoke(damage);
            HealthChanged?.Invoke(CurrentHealth);

            if (CurrentHealth > 0f)
                return true;

            Died?.Invoke();
            if (destroyOnDeath)
                Destroy(gameObject);

            return true;
        }

        public bool TryRestore(float amount)
        {
            if (!IsAlive || amount <= 0f)
                return false;

            float previousHealth = CurrentHealth;
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            if (Mathf.Approximately(previousHealth, CurrentHealth))
                return false;

            HealthChanged?.Invoke(CurrentHealth);
            return true;
        }

        public void ResetHealth()
        {
            CurrentHealth = maxHealth;
            invulnerabilityTimer = 0f;
            HealthChanged?.Invoke(CurrentHealth);
        }

        /// <summary>
        /// Applies a role-specific maximum before the actor enters combat.
        /// Keeping this explicit makes health setup independent of scene
        /// placement and presentation configuration.
        /// </summary>
        public void ConfigureMaxHealth(float value)
        {
            maxHealth = Mathf.Max(1f, value);
            ResetHealth();
        }

        /// <summary>
        /// Applies both halves of an actor's combat contract together. A role
        /// can therefore define its health and its post-hit protection window
        /// without exposing a partially configured state.
        /// </summary>
        public void ConfigureDamageRules(float healthValue, float invulnerabilityValue)
        {
            maxHealth = Mathf.Max(1f, healthValue);
            invulnerabilityDuration = Mathf.Max(0f, invulnerabilityValue);
            ResetHealth();
        }
    }
}

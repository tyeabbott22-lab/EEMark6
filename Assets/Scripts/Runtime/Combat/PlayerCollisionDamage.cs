using UnityEngine;
using ExtraterrestrialExhaust.CameraSystem;
using ExtraterrestrialExhaust.Core;
using ExtraterrestrialExhaust.Player;

namespace ExtraterrestrialExhaust.Combat
{
    /// <summary>
    /// Optional experimental impact damage. It is deliberately disabled by
    /// the EE5 slice builder because the reference realScene does not damage
    /// the player for generic wall contact.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(HealthComponent))]
    public sealed class PlayerCollisionDamage : MonoBehaviour
    {
        [SerializeField, Min(0f)] float minimumImpactSpeed = 8f;
        [SerializeField, Min(0f)] float damageAtMinimumSpeed = 1f;
        [SerializeField, Min(0f)] float damageAtMaximumSpeed = 3f;
        [SerializeField, Min(0.01f)] float damageCooldown = 0.35f;
        [SerializeField, Min(0f)] float maximumImpactSpeed = 20f;

        HealthComponent health;
        PlayerCharacter player;
        float nextDamageTime;

        void Awake()
        {
            // The EE5 realScene player does not lose hull on ordinary wall
            // contact. Preserved FlightTest instances can carry an old
            // enabled override even when the prefab is correctly disabled;
            // enforce the gold slice contract before the first collision
            // callback so a dirty scene cannot invent a death path.
            if (!IsEe5CollisionDamageEnabled())
            {
                enabled = false;
                return;
            }

            health = GetComponent<HealthComponent>();
            player = GetComponent<PlayerCharacter>();
        }

        static bool IsEe5CollisionDamageEnabled()
        {
            return Ee5SliceProfile.PlayerCollisionDamageEnabled;
        }

        void OnCollisionEnter2D(Collision2D collision)
        {
            float impactSpeed = collision.relativeVelocity.magnitude;
            PlayerCameraFollow.Instance?.TryShakeForWallImpact(collision);

            // Unity sends collision callbacks to disabled behaviours so they
            // can opt back in. The EE5 profile disables this experiment in
            // Awake before caching health, so never enter its damage path from
            // one of those compatibility callbacks.
            if (!enabled || !health)
                return;

            if (impactSpeed < minimumImpactSpeed || Time.time < nextDamageTime)
                return;

            if (player && !player.CanReceiveGameplayInput)
                return;

            float impactT = Mathf.InverseLerp(minimumImpactSpeed, maximumImpactSpeed, impactSpeed);
            float damage = Mathf.Lerp(damageAtMinimumSpeed, damageAtMaximumSpeed, impactT);
            if (health.TryTakeDamage(new DamageInfo(damage, DamageType.Collision, collision.gameObject)))
            {
                nextDamageTime = Time.time + damageCooldown;
                PlayerCameraFollow.Instance?.Shake(0.16f * impactT, 0.16f);
            }
        }
    }
}

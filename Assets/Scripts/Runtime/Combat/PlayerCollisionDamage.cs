using UnityEngine;
using ExtraterrestrialExhaust.CameraSystem;
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
            health = GetComponent<HealthComponent>();
            player = GetComponent<PlayerCharacter>();
        }

        void OnCollisionEnter2D(Collision2D collision)
        {
            float impactSpeed = collision.relativeVelocity.magnitude;
            PlayerCameraFollow.Instance?.TryShakeForWallImpact(collision);
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

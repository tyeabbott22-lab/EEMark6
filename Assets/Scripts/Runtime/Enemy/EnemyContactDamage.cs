using UnityEngine;
using ExtraterrestrialExhaust.Combat;
using ExtraterrestrialExhaust.Player;

namespace ExtraterrestrialExhaust.Enemy
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class EnemyContactDamage : MonoBehaviour
    {
        [SerializeField, Min(0f)] float damage = 1f;
        [SerializeField, Min(0.01f)] float cooldown = 0.75f;
        [SerializeField, Min(0f)] float knockback = 8f;
        float nextDamageTime;
        EnemyController controller;

        void Awake()
        {
            controller = GetComponent<EnemyController>();
        }

        void OnCollisionStay2D(Collision2D collision)
        {
            if (controller && !controller.CanAttack)
                return;

            PlayerCharacter player = collision.collider.GetComponentInParent<PlayerCharacter>();
            if (!player || !player.CanReceiveGameplayInput || Time.time < nextDamageTime)
                return;

            Vector2 direction = ((Vector2)player.transform.position - (Vector2)transform.position).normalized;
            if (direction.sqrMagnitude <= 0.001f && collision.contactCount > 0)
                direction = -collision.GetContact(0).normal;

            Vector2 hitPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : player.transform.position;
            DamageInfo damageInfo = new DamageInfo(
                damage,
                DamageType.Enemy,
                gameObject,
                hitPoint,
                direction,
                knockback);

            if (player.Health.TryTakeDamage(damageInfo))
            {
                // Health owns whether the hit was lethal. Only shove a player
                // who remains alive; the respawn controller owns dead-body reset.
                if (player.Health.IsAlive && player.FlightMotor && player.FlightMotor.Body)
                    player.FlightMotor.Body.linearVelocity += direction * knockback;
                nextDamageTime = Time.time + cooldown;
            }
        }
    }
}

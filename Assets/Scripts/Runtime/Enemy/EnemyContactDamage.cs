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

        void FixedUpdate()
        {
            // EE5's melee enemy dealt contact damage while physically
            // overlapping the player. EE6 deliberately keeps the navigation
            // body just outside that overlap to eliminate rigidbody tug-of-war
            // and the resulting jitter. Preserve the readable attack timing
            // with a range hit instead of reintroducing that collision loop.
            if (!controller || !controller.IsMelee || !controller.CanAttack)
                return;

            PlayerCharacter player = controller.Target;
            if (!player || !player.CanReceiveGameplayInput)
                return;

            Vector2 enemyPosition = controller.PhysicsPosition;
            Vector2 playerPosition = player.PhysicsPosition;
            if (Vector2.Distance(enemyPosition, playerPosition) > controller.ContactDamageReach)
                return;

            Vector2 direction = (playerPosition - enemyPosition).normalized;
            if (direction.sqrMagnitude <= 0.001f)
                direction = Vector2.right;

            TryDamagePlayer(player, playerPosition, direction);
        }

        void OnCollisionStay2D(Collision2D collision)
        {
            // EE5 attaches this contact contract to both enemyNormal and
            // enemyGun. Do not gate it on the controller's ranged/melee attack
            // state; physical contact remains dangerous during the intro.
            PlayerCharacter player = collision.collider.GetComponentInParent<PlayerCharacter>();
            if (!player || !player.CanReceiveGameplayInput || Time.time < nextDamageTime)
                return;

            Vector2 direction = (player.PhysicsPosition - controller.PhysicsPosition).normalized;
            if (direction.sqrMagnitude <= 0.001f && collision.contactCount > 0)
                direction = -collision.GetContact(0).normal;

            Vector2 hitPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : player.transform.position;

            TryDamagePlayer(player, hitPoint, direction);
        }

        void TryDamagePlayer(PlayerCharacter player, Vector2 hitPoint, Vector2 direction)
        {
            if (!player || !player.CanReceiveGameplayInput || Time.time < nextDamageTime)
                return;

            DamageInfo damageInfo = new DamageInfo(
                damage,
                DamageType.Enemy,
                gameObject,
                hitPoint,
                direction,
                knockback);

            controller?.RegisterPlayerContact();
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

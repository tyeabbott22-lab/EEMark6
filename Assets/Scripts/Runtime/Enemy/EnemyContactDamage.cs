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
            // Keep the solid-body EE5 contact contract for the ranged gunner
            // and older authored scenes. The repaired melee role uses a
            // trigger navigation body, so its near-contact FixedUpdate path
            // is the deterministic replacement for this callback.
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
                controller?.RegisterAttackImpact();
                // Health owns whether the hit was lethal. Only shove a player
                // who remains alive; the respawn controller owns dead-body reset.
                if (player.Health.IsAlive && player.FlightMotor && player.FlightMotor.Body)
                    ApplyEe5ContactImpulse(player.FlightMotor.Body, direction);
                nextDamageTime = Time.time + cooldown;
            }
        }

        /// <summary>
        /// Mirrors the original EE5 meleeEnemy contract: first remove velocity
        /// aimed into the hunter, then add the authored outward impulse. Without
        /// the first step a fast player can remain inside the melee reach for a
        /// frame, forcing the kinematic hunter to chase, brake, and re-aim in a
        /// visible popcorn loop.
        /// </summary>
        void ApplyEe5ContactImpulse(Rigidbody2D playerBody, Vector2 direction)
        {
            if (!playerBody)
                return;

            float speedIntoEnemy = Vector2.Dot(playerBody.linearVelocity, -direction);
            if (speedIntoEnemy > 0f)
                playerBody.linearVelocity += direction * speedIntoEnemy;

            playerBody.linearVelocity += direction * knockback;
        }
    }
}

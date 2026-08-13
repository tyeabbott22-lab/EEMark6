using UnityEngine;
using ExtraterrestrialExhaust.Combat;
using ExtraterrestrialExhaust.CameraSystem;
using ExtraterrestrialExhaust.Player;

namespace ExtraterrestrialExhaust.Enemy
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class EnemyContactDamage : MonoBehaviour
    {
        [SerializeField, Min(0f)] float damage = 1f;
        [SerializeField, Min(0.01f)] float cooldown = 0.75f;
        [SerializeField, Min(0f)] float knockback = 8f;
        float cooldownRemaining;

        EnemyController controller;

        void Awake()
        {
            controller = GetComponent<EnemyController>();
        }

        void FixedUpdate()
        {
            cooldownRemaining = Mathf.Max(
                0f,
                cooldownRemaining - Time.fixedDeltaTime);

            // Trigger callbacks are the normal EE5 path, but they can be
            // skipped when a dirty prefab has an old layer matrix or when the
            // player and the repaired trigger body cross between physics
            // steps. Re-test the actual collider pair on the fixed clock so
            // hitboxes remain authoritative without inventing a larger damage
            // radius.
            TryDamageResolvedContact();
        }

        void TryDamageResolvedContact()
        {
            if (!controller || !controller.IsMelee || !controller.CanAttack)
                return;

            PlayerCharacter player = controller.Target;
            if (!player)
                player = FindFirstObjectByType<PlayerCharacter>();

            if (!player || !player.CanReceiveGameplayInput || cooldownRemaining > 0f)
                return;

            if (!controller.IsWithinMeleeContact(player))
                return;

            Vector2 direction = (player.PhysicsPosition - controller.PhysicsPosition).normalized;
            if (direction.sqrMagnitude <= 0.001f)
                direction = Vector2.right;

            Collider2D playerCollider = player.GetComponent<Collider2D>();
            Vector2 hitPoint = playerCollider
                ? playerCollider.ClosestPoint(controller.PhysicsAnchorPosition)
                : player.PhysicsPosition;
            TryDamagePlayer(player, hitPoint, direction);
        }

        void OnTriggerEnter2D(Collider2D other) => TryDamageFromCollider(other);

        void OnTriggerStay2D(Collider2D other) => TryDamageFromCollider(other);

        void TryDamageFromCollider(Collider2D other)
        {
            if (!controller || !controller.IsMelee || !controller.CanAttack)
                return;

            PlayerCharacter player = other
                ? other.GetComponentInParent<PlayerCharacter>()
                : null;
            if (!player || !player.CanReceiveGameplayInput || cooldownRemaining > 0f)
                return;

            Vector2 direction = (player.PhysicsPosition - controller.PhysicsPosition).normalized;
            if (direction.sqrMagnitude <= 0.001f)
                direction = Vector2.right;

            Vector2 hitPoint = other.ClosestPoint(controller.PhysicsAnchorPosition);
            TryDamagePlayer(player, hitPoint, direction);
        }

        void OnCollisionStay2D(Collision2D collision)
        {
            // The reusable component remains on older solid-body prefabs, but
            // contact damage is a melee attack contract. The EE5 gunner has
            // projectile pressure, not a hidden collision attack. The repaired
            // melee role normally reaches this component through the trigger
            // callbacks above; this solid-body path keeps old hand-authored
            // scenes compatible without changing their contact authority.
            if (!controller || !controller.IsMelee || !controller.CanAttack)
                return;

            PlayerCharacter player = collision.collider.GetComponentInParent<PlayerCharacter>();
            if (!player || !player.CanReceiveGameplayInput || cooldownRemaining > 0f)
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
            if (!player || !player.CanReceiveGameplayInput || cooldownRemaining > 0f)
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
                controller?.RegisterAttackImpact(direction);
                // Keep this feedback at the same authority as damage. A
                // navigation pass or a blocked overlap cannot accidentally
                // play a fake swing, while the player still gets a crisp
                // strike read before the knockback carries them away.
                MeleeAttackBurst.Spawn(
                    hitPoint,
                    direction,
                    new Color(1f, 0.22f, 0.86f, 0.95f));
                PlayerCameraFollow.Instance?.Shake(0.028f, 0.045f);
                // Health owns whether the hit was lethal. Only shove a player
                // who remains alive; the respawn controller owns dead-body reset.
                if (player.Health.IsAlive && player.FlightMotor && player.FlightMotor.Body)
                    ApplyEe5ContactImpulse(player.FlightMotor.Body, direction);
                // This component is driven by physics callbacks; use the same
                // clock as navigation and attack recovery so a render hitch
                // cannot shorten or stretch the authored melee cadence.
                cooldownRemaining = cooldown;
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

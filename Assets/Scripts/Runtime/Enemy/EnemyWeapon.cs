using UnityEngine;
using ExtraterrestrialExhaust.Combat;
using ExtraterrestrialExhaust.Player;
using ExtraterrestrialExhaust.Core;

namespace ExtraterrestrialExhaust.Enemy
{
    /// <summary>
    /// Simple ranged pressure for the vertical slice. It reuses the projectile
    /// contract while keeping enemy firing decisions separate from player weapons.
    /// </summary>
    public sealed class EnemyWeapon : MonoBehaviour
    {
        [SerializeField] PlayerProjectile projectilePrefab;
        [SerializeField] Transform firePoint;
        // The EE5 white gunner fires at 2 Hz with a readable, deliberate
        // projectile travel speed. The builder serializes this same profile.
        [SerializeField, Min(0.05f)] float fireCooldown = 0.5f;
        [SerializeField, Min(0f)] float attackRange = 7f;
        [SerializeField, Min(0f)] float projectileSpeed = 9.5f;
        [SerializeField, Min(0f)] float projectileKnockback = 2.5f;
        [SerializeField] Color projectileTint = new Color(0.05f, 1f, 0.16f, 1f);
        [SerializeField] PlayerCharacter target;
        [SerializeField] GameStateMachine gameState;

        float cooldownRemaining;
        EnemyController controller;

        void Awake()
        {
            controller = GetComponent<EnemyController>();
            gameState = FindFirstObjectByType<GameStateMachine>();
        }

        void Update()
        {
            cooldownRemaining -= Time.deltaTime;
            if (gameState && !gameState.IsPlaying)
                return;
            if (controller && !controller.CanAttack)
                return;
            if (!target)
                target = FindFirstObjectByType<PlayerCharacter>();

            if (!target || !target.CanReceiveGameplayInput
                || !projectilePrefab || cooldownRemaining > 0f)
                return;

            Vector2 toTarget = (Vector2)target.transform.position - (Vector2)transform.position;
            if (toTarget.magnitude > attackRange)
                return;

            Fire(toTarget.normalized);
        }

        void Fire(Vector2 direction)
        {
            Transform origin = firePoint ? firePoint : transform;
            PlayerProjectile projectile = Instantiate(projectilePrefab, origin.position, Quaternion.identity);
            projectile.SetTeam(ProjectileTeam.Enemy);
            projectile.SetKnockback(projectileKnockback);
            projectile.SetTint(projectileTint);
            projectile.Launch(direction, gameObject, projectileSpeed);
            cooldownRemaining = fireCooldown;
        }
    }
}

using UnityEngine;
using ExtraterrestrialExhaust.Enemy;
using ExtraterrestrialExhaust.Player;
using ExtraterrestrialExhaust.Core;

namespace ExtraterrestrialExhaust.Combat
{
    /// <summary>
    /// A small self-contained projectile used by the vertical slice.
    /// Movement, ownership, lifetime, and impact rules live here; visuals can be
    /// added to the prefab without changing the combat contract.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public sealed class PlayerProjectile : MonoBehaviour
    {
        // Match the authored EE5 player bullet. Enemy projectiles still use
        // their own source override when the encounter needs different pacing.
        [SerializeField, Min(0f)] float speed = 30f;
        [SerializeField, Min(0.01f)] float lifetime = 2f;
        [SerializeField, Min(0f)] float damage = 1f;
        [SerializeField, Min(0f)] float knockback;
        [SerializeField] bool destroyOnUnrecognizedCollision;
        [SerializeField] ProjectileTeam team = ProjectileTeam.Player;
        [SerializeField] bool enforceEe5Profile = true;

        Rigidbody2D body;
        SpriteRenderer spriteRenderer;
        LineRenderer trailRenderer;
        GameObject owner;
        Vector2 direction;
        float lifetimeRemaining;

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            if (enforceEe5Profile)
                speed = Ee5SliceProfile.PlayerProjectileSpeed;

            spriteRenderer = GetComponent<SpriteRenderer>();
            trailRenderer = GetComponent<LineRenderer>();
            body.gravityScale = 0f;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        void Update()
        {
            lifetimeRemaining -= Time.deltaTime;
            if (lifetimeRemaining <= 0f)
                Destroy(gameObject);
        }

        public void Launch(Vector2 launchDirection, GameObject source, float speedOverride = -1f)
        {
            direction = launchDirection.sqrMagnitude > 0.001f
                ? launchDirection.normalized
                : Vector2.right;
            owner = source;
            lifetimeRemaining = lifetime;
            body.linearVelocity = direction * (speedOverride > 0f ? speedOverride : speed);
            transform.right = direction;
        }

        /// <summary>Applies source-specific presentation without changing impact rules.</summary>
        public void SetTint(Color color)
        {
            if (!spriteRenderer)
                spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer)
                spriteRenderer.color = color;

            if (!trailRenderer)
                trailRenderer = GetComponent<LineRenderer>();
            if (trailRenderer)
            {
                Color trailEnd = color;
                trailEnd.a *= 0.15f;
                trailRenderer.startColor = color;
                trailRenderer.endColor = trailEnd;
            }
        }

        public void SetTeam(ProjectileTeam projectileTeam) => team = projectileTeam;

        public void SetKnockback(float value) => knockback = Mathf.Max(0f, value);

        void OnTriggerEnter2D(Collider2D other)
        {
            if (IsOwnerCollider(other))
                return;

            IDamageable damageable = other.GetComponentInParent<IDamageable>();
            if (damageable != null && CanDamage(other))
            {
                Vector2 hitPoint = other.ClosestPoint(transform.position);
                DamageInfo damageInfo = new DamageInfo(
                    damage,
                    DamageType.Projectile,
                    owner,
                    hitPoint,
                    direction,
                    knockback);
                if (damageable.TryTakeDamage(damageInfo) && knockback > 0f)
                {
                    PlayerCharacter targetPlayer = other.GetComponentInParent<PlayerCharacter>();
                    bool targetCanBePushed = !targetPlayer
                        || !targetPlayer.Health
                        || targetPlayer.Health.IsAlive;
                    // A lethal hit already handed control to respawn. Do not
                    // add velocity after the death handler stopped the body.
                    if (targetCanBePushed)
                    {
                        Rigidbody2D targetBody = other.attachedRigidbody
                            ? other.attachedRigidbody
                            : other.GetComponentInParent<Rigidbody2D>();
                        if (targetBody)
                            targetBody.AddForce(direction * knockback, ForceMode2D.Impulse);
                    }
                }
                ProjectileImpactBurst.Spawn(hitPoint, team == ProjectileTeam.Enemy
                    ? new Color(0.05f, 1f, 0.16f)
                    : Color.white);
                Destroy(gameObject);
                return;
            }

            if (destroyOnUnrecognizedCollision || other.CompareTag("Wall"))
            {
                ProjectileImpactBurst.Spawn(transform.position, new Color(1f, 0.25f, 0.1f));
                Destroy(gameObject);
            }
        }

        bool IsOwnerCollider(Collider2D other)
        {
            if (!owner)
                return false;

            return other.gameObject == owner
                || other.transform.IsChildOf(owner.transform)
                || owner.transform.IsChildOf(other.transform);
        }

        bool CanDamage(Collider2D other)
        {
            if (team == ProjectileTeam.Player)
                return other.GetComponentInParent<PlayerCharacter>() == null;

            return other.GetComponentInParent<EnemyController>() == null;
        }
    }
}

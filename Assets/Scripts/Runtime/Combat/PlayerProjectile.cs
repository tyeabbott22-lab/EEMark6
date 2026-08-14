using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ExtraterrestrialExhaust.Enemy;
using ExtraterrestrialExhaust.Player;
using ExtraterrestrialExhaust.Core;

namespace ExtraterrestrialExhaust.Combat
{
    /// <summary>
    /// A small self-contained projectile used by the vertical slice.
    /// Movement, ownership, team filtering, lifetime, and impact rules live
    /// here; visuals can be added to the prefab without changing the combat
    /// contract. The historical PlayerProjectile name is retained because the
    /// generated prefab and existing Unity references still use it; EnemyWeapon
    /// configures the same component through the explicit enemy-shot API below.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public sealed class PlayerProjectile : MonoBehaviour
    {
        // Match the authored EE5 player bullet. Enemy shots apply their own
        // profile through ConfigureEnemyShot after instantiation.
        [SerializeField, Min(0f)] float speed = 30f;
        [SerializeField, Min(0.01f)] float lifetime = Ee5SliceProfile.PlayerProjectileLifetime;
        [SerializeField, Min(0f)] float damage = Ee5SliceProfile.PlayerProjectileDamage;
        [SerializeField, Min(0f)] float knockback = Ee5SliceProfile.PlayerProjectileKnockback;
        [SerializeField] bool destroyOnUnrecognizedCollision = Ee5SliceProfile.PlayerProjectileDestroysOnUnknownCollision;
        [SerializeField] ProjectileTeam team = ProjectileTeam.Player;
        [SerializeField] bool enforceEe5Profile = true;
        [Header("Trail")]
        [SerializeField, Min(1)] int maxTrailPoints = 6;
        [SerializeField, Min(0.001f)] float pointSpacing = 0.03f;
        [SerializeField] bool useImpactFade = true;
        [SerializeField, Min(0.01f)] float impactFadeTime = 0.08f;
        [SerializeField] Color trailStartColor = Color.white;
        [SerializeField] Color trailEndColor = new Color(1f, 0.1f, 0.04f, 1f);
        [Header("Enemy Near Miss")]
        [SerializeField, Min(0f)] float nearMissDistance = Ee5SliceProfile.ProjectileNearMissDistance;
        [Header("Player Hit Assist")]
        [Tooltip("Small swept forgiveness for enlarged EE6 enemy art. This affects player projectiles only; terrain remains the first blocking impact.")]
        [SerializeField, Min(0f)] float playerHitAssistRadius = Ee5SliceProfile.PlayerProjectileHitAssistRadius;

        Rigidbody2D body;
        SpriteRenderer spriteRenderer;
        LineRenderer trailRenderer;
        ProjectileExhaustPresentation exhaustPresentation;
        ProjectileSpriteTrailPresentation spriteTrailPresentation;
        GameObject owner;
        PlayerCharacter nearMissTarget;
        Vector2 direction;
        float lifetimeRemaining;
        bool nearPlayer;
        bool nearMissAwarded;
        bool hitPlayer;
        bool dying;
        Material trailMaterial;
        readonly List<Vector3> trailPoints = new();
        readonly RaycastHit2D[] hitAssistHits = new RaycastHit2D[12];
        Vector2 lastPhysicsPosition;
        bool hasPhysicsPosition;

        public ProjectileTeam Team => team;

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            if (enforceEe5Profile)
            {
                speed = Ee5SliceProfile.PlayerProjectileSpeed;
                lifetime = Ee5SliceProfile.PlayerProjectileLifetime;
                damage = Ee5SliceProfile.PlayerProjectileDamage;
                knockback = Ee5SliceProfile.PlayerProjectileKnockback;
                destroyOnUnrecognizedCollision = Ee5SliceProfile.PlayerProjectileDestroysOnUnknownCollision;
                nearMissDistance = Ee5SliceProfile.ProjectileNearMissDistance;
                playerHitAssistRadius = Ee5SliceProfile.PlayerProjectileHitAssistRadius;
            }

            spriteRenderer = GetComponent<SpriteRenderer>();
            trailRenderer = GetComponent<LineRenderer>();
            exhaustPresentation = GetComponent<ProjectileExhaustPresentation>();
            if (!exhaustPresentation)
                exhaustPresentation = gameObject.AddComponent<ProjectileExhaustPresentation>();
            spriteTrailPresentation = GetComponent<ProjectileSpriteTrailPresentation>();
            if (!spriteTrailPresentation)
                spriteTrailPresentation = gameObject.AddComponent<ProjectileSpriteTrailPresentation>();
            ConfigureTrail();
            body.gravityScale = 0f;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            lastPhysicsPosition = body.position;
            hasPhysicsPosition = true;
        }

        void OnDestroy()
        {
            if (trailMaterial)
                Destroy(trailMaterial);
        }

        void Update()
        {
            if (dying)
                return;

            CheckNearMiss();
            AddTrailPoint(transform.position);
            lifetimeRemaining -= Time.deltaTime;
            if (lifetimeRemaining <= 0f)
                BeginLifetimeEnd();
        }

        public void Launch(Vector2 launchDirection, GameObject source, float speedOverride = -1f)
        {
            direction = launchDirection.sqrMagnitude > 0.001f
                ? launchDirection.normalized
                : Vector2.right;
            owner = source;
            lifetimeRemaining = lifetime;
            nearMissTarget = null;
            nearPlayer = false;
            nearMissAwarded = false;
            hitPlayer = false;
            dying = false;
            body.linearVelocity = direction * (speedOverride > 0f ? speedOverride : speed);
            transform.right = direction;
            lastPhysicsPosition = body.position;
            hasPhysicsPosition = true;
            ResetTrail();
        }

        void FixedUpdate()
        {
            if (!body)
                return;

            Vector2 currentPosition = body.position;
            if (!hasPhysicsPosition)
            {
                lastPhysicsPosition = currentPosition;
                hasPhysicsPosition = true;
                return;
            }

            if (!dying && team == ProjectileTeam.Player && playerHitAssistRadius > 0f)
                CheckPlayerHitAssist(lastPhysicsPosition, currentPosition);

            lastPhysicsPosition = currentPosition;
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
                trailStartColor = color;
                trailEndColor = color;
                trailEndColor.a *= 0.15f;
                ApplyTrailColors(1f);
            }

            exhaustPresentation?.SetColorTheme(color);
        }

        public void SetTeam(ProjectileTeam projectileTeam) => team = projectileTeam;

        /// <summary>
        /// Applies the vertical-slice enemy-bullet contract in one place.
        /// Keeping this grouped prevents a future enemy weapon from changing
        /// team filtering while forgetting the EE5 lifetime or wall behavior.
        /// </summary>
        public void ConfigureEnemyShot(
            float shotLifetime,
            float shotDamage,
            float shotKnockback,
            bool destroyOnUnknownCollision)
        {
            team = ProjectileTeam.Enemy;
            lifetime = Mathf.Max(0.01f, shotLifetime);
            damage = Mathf.Max(0f, shotDamage);
            knockback = Mathf.Max(0f, shotKnockback);
            this.destroyOnUnrecognizedCollision = destroyOnUnknownCollision;
        }

        public void SetKnockback(float value) => knockback = Mathf.Max(0f, value);

        public void SetDamage(float value) => damage = Mathf.Max(0f, value);

        public void SetDestroyOnUnrecognizedCollision(bool value) => destroyOnUnrecognizedCollision = value;

        /// <summary>
        /// Source-specific lifetime override for non-profile experiments.
        /// Gold enemy shots should use ConfigureEnemyShot so their complete
        /// collision contract is applied together.
        /// </summary>
        public void SetLifetime(float value) => lifetime = Mathf.Max(0.01f, value);

        // The generated FlightTest projectile uses a trigger collider, but
        // the imported EE5 bullet variants are not guaranteed to keep that
        // flag after a prefab/scene refresh. Keep one impact authority and
        // accept either Unity callback so a stale collider mode cannot make
        // an otherwise valid shot pass through an enemy or wall.
        void OnTriggerEnter2D(Collider2D other) => HandleImpact(other);

        void OnCollisionEnter2D(Collision2D collision)
        {
            if (collision != null)
                HandleImpact(collision.collider);
        }

        void HandleImpact(Collider2D other)
        {
            if (dying || !other)
                return;

            if (IsOwnerCollider(other))
                return;

            if (team == ProjectileTeam.Enemy
                && other.GetComponentInParent<PlayerCharacter>())
                hitPlayer = true;

            IDamageable damageable = other.GetComponentInParent<IDamageable>();
            if (damageable != null && CanDamage(other))
            {
                Vector2 hitPoint = other.ClosestPoint(transform.position);
                Vector2 impactNormal = ((Vector2)transform.position - hitPoint).normalized;
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
                    : Color.white, impactNormal);
                BeginImpactEnd();
                return;
            }

            if (destroyOnUnrecognizedCollision || Ee5SliceProfile.IsWallCollider(other))
            {
                Vector2 hitPoint = other.ClosestPoint(transform.position);
                Vector2 impactNormal = ((Vector2)transform.position - hitPoint).normalized;
                ProjectileImpactBurst.Spawn(
                    hitPoint,
                    new Color(1f, 0.25f, 0.1f),
                    impactNormal);
                BeginImpactEnd();
            }
        }

        void CheckPlayerHitAssist(Vector2 from, Vector2 to)
        {
            Vector2 travel = to - from;
            float distance = travel.magnitude;
            if (distance <= 0.0001f)
                return;

#pragma warning disable CS0618
            int hitCount = Physics2D.CircleCastNonAlloc(
                from,
                playerHitAssistRadius,
                travel / distance,
                hitAssistHits,
                distance);
#pragma warning restore CS0618

            RaycastHit2D closestImpact = default;
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = hitAssistHits[i];
                Collider2D hitCollider = hit.collider;
                if (!hitCollider || IsOwnerCollider(hitCollider))
                    continue;

                bool blocksShot = Ee5SliceProfile.IsWallCollider(hitCollider);
                bool damagesEnemy = CanDamage(hitCollider);
                if ((!blocksShot && !damagesEnemy) || hit.distance >= closestDistance)
                    continue;

                closestDistance = hit.distance;
                closestImpact = hit;
            }

            // Send the chosen collider through the normal impact authority.
            // This preserves damage, effects, score, and destruction behavior
            // while ensuring a wall selected before an enemy still blocks it.
            if (closestImpact.collider)
                HandleImpact(closestImpact.collider);
        }

        void ConfigureTrail()
        {
            if (!trailRenderer)
                return;

            trailRenderer.useWorldSpace = true;
            trailRenderer.positionCount = 0;
            trailRenderer.startWidth = 0.01f;
            trailRenderer.endWidth = 0.1f;
            trailRenderer.numCapVertices = 8;
            trailRenderer.numCornerVertices = 4;
            if (!trailRenderer.sharedMaterial)
            {
                trailMaterial = RuntimeVisualMaterial.Create("Runtime Projectile Trail");
                if (trailMaterial)
                    trailRenderer.sharedMaterial = trailMaterial;
            }

            ApplyTrailColors(1f);
        }

        void ResetTrail()
        {
            trailPoints.Clear();
            if (trailRenderer)
                trailRenderer.positionCount = 0;
            AddTrailPoint(transform.position);
            ApplyTrailColors(1f);
        }

        void AddTrailPoint(Vector3 point)
        {
            if (!trailRenderer)
                return;

            if (trailPoints.Count > 0
                && Vector3.Distance(trailPoints[trailPoints.Count - 1], point)
                    < Mathf.Max(0.001f, pointSpacing))
                return;

            trailPoints.Add(point);
            int pointLimit = Mathf.Max(1, maxTrailPoints);
            while (trailPoints.Count > pointLimit)
                trailPoints.RemoveAt(0);

            trailRenderer.positionCount = trailPoints.Count;
            for (int i = 0; i < trailPoints.Count; i++)
                trailRenderer.SetPosition(i, trailPoints[i]);
        }

        void ApplyTrailColors(float alpha)
        {
            if (!trailRenderer)
                return;

            Color start = trailStartColor;
            Color end = trailEndColor;
            start.a *= Mathf.Clamp01(alpha);
            end.a *= Mathf.Clamp01(alpha);
            trailRenderer.startColor = start;
            trailRenderer.endColor = end;
        }

        void BeginImpactEnd()
        {
            if (dying)
                return;

            AwardNearMissIfNeeded();
            dying = true;
            body.linearVelocity = Vector2.zero;
            Collider2D projectileCollider = GetComponent<Collider2D>();
            if (projectileCollider)
                projectileCollider.enabled = false;
            if (spriteRenderer)
                spriteRenderer.enabled = false;
            spriteTrailPresentation?.FinishTrail();
            exhaustPresentation?.StopExhaust();
            AddTrailPoint(transform.position);
            StartTrailFadeOrDestroy();
        }

        void BeginLifetimeEnd()
        {
            if (dying)
                return;

            AwardNearMissIfNeeded();
            dying = true;
            body.linearVelocity = Vector2.zero;
            Collider2D projectileCollider = GetComponent<Collider2D>();
            if (projectileCollider)
                projectileCollider.enabled = false;
            if (spriteRenderer)
                spriteRenderer.enabled = false;
            spriteTrailPresentation?.FinishTrail();
            exhaustPresentation?.StopExhaust();
            AddTrailPoint(transform.position);
            StartTrailFadeOrDestroy();
        }

        void StartTrailFadeOrDestroy()
        {
            if (!useImpactFade || !trailRenderer || trailPoints.Count == 0)
            {
                Destroy(gameObject);
                return;
            }

            StartCoroutine(FadeTrailRoutine());
        }

        IEnumerator FadeTrailRoutine()
        {
            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, impactFadeTime);
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                ApplyTrailColors(1f - Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            Destroy(gameObject);
        }

        void CheckNearMiss()
        {
            if (team != ProjectileTeam.Enemy || nearMissAwarded || hitPlayer)
                return;

            if (!nearMissTarget)
                nearMissTarget = FindFirstObjectByType<PlayerCharacter>();
            if (!nearMissTarget
                || !nearMissTarget.Health
                || !nearMissTarget.Health.IsAlive
                || !nearMissTarget.CanReceiveGameplayInput)
            {
                nearPlayer = false;
                return;
            }

            float distance = Vector2.Distance(transform.position, nearMissTarget.transform.position);
            if (distance <= nearMissDistance)
            {
                nearPlayer = true;
                return;
            }

            AwardNearMissIfNeeded();
        }

        void AwardNearMissIfNeeded()
        {
            if (team != ProjectileTeam.Enemy
                || nearMissAwarded
                || hitPlayer
                || !nearPlayer
                || !nearMissTarget
                || !nearMissTarget.CanReceiveGameplayInput)
                return;

            nearMissAwarded = true;
            FindFirstObjectByType<ScoreSystem>()?.Award(ScoreReason.NearMiss);
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
            {
                EnemyController enemy = other
                    ? other.GetComponentInParent<EnemyController>()
                    : null;
                if (!enemy || !enemy.IsDamageable)
                    return false;

                // The canonical BoxCollider2D still defines the normal
                // hurtbox. Accept another enabled collider in that same enemy
                // hierarchy as a compatibility path, though: preserved scene
                // instances can retain a scaled visual/contact collider while
                // their controller rebuilds the root box on Awake. A projectile
                // destroys itself after this first accepted hit, so this cannot
                // create a double-damage path.
                return true;
            }

            // EE5 EnemyBullet only has a player impact path. Do not let an
            // enemy shot accidentally damage a future destructible prop,
            // gate, or another neutral IDamageable in the room.
            return other.GetComponentInParent<PlayerCharacter>() != null;
        }
    }
}

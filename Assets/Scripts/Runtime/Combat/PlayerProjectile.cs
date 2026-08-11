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
        [Header("Trail")]
        [SerializeField, Min(1)] int maxTrailPoints = 6;
        [SerializeField, Min(0.001f)] float pointSpacing = 0.03f;
        [SerializeField] bool useImpactFade = true;
        [SerializeField, Min(0.01f)] float impactFadeTime = 0.08f;
        [SerializeField] Color trailStartColor = Color.white;
        [SerializeField] Color trailEndColor = new Color(1f, 0.1f, 0.04f, 1f);
        [Header("Enemy Near Miss")]
        [SerializeField, Min(0f)] float nearMissDistance = 1.35f;

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

        public ProjectileTeam Team => team;

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            if (enforceEe5Profile)
                speed = Ee5SliceProfile.PlayerProjectileSpeed;

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
            ResetTrail();
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

        public void SetKnockback(float value) => knockback = Mathf.Max(0f, value);

        void OnTriggerEnter2D(Collider2D other)
        {
            if (dying)
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

            if (destroyOnUnrecognizedCollision || other.CompareTag("Wall"))
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
                Shader shader = Shader.Find("Sprites/Default");
                if (shader)
                {
                    trailMaterial = new Material(shader);
                    trailMaterial.name = "Runtime Projectile Trail";
                    trailRenderer.sharedMaterial = trailMaterial;
                }
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
            if (!nearMissTarget || !nearMissTarget.Health || !nearMissTarget.Health.IsAlive)
                return;

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
            if (team != ProjectileTeam.Enemy || nearMissAwarded || hitPlayer || !nearPlayer)
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
                return other.GetComponentInParent<PlayerCharacter>() == null;

            return other.GetComponentInParent<EnemyController>() == null;
        }
    }
}

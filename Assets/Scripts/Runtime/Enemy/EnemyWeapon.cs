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
        [Header("Attack Telegraph")]
        [SerializeField] bool drawAimTelegraph = true;
        [SerializeField, Min(0f)] float telegraphDuration = 0.18f;
        [SerializeField, Min(0f)] float telegraphMinWidth = 0.018f;
        [SerializeField, Min(0f)] float telegraphMaxWidth = 0.085f;
        [SerializeField] Color telegraphColor = new Color(0.1f, 1f, 0.3f, 0.5f);
        [SerializeField] int telegraphSortingOrder = 75;
        [SerializeField] PlayerCharacter target;
        [SerializeField] GameStateMachine gameState;

        float cooldownRemaining;
        EnemyController controller;
        LineRenderer telegraph;
        Material telegraphMaterial;
        readonly RaycastHit2D[] lineOfSightHits = new RaycastHit2D[16];

        void Awake()
        {
            controller = GetComponent<EnemyController>();
            gameState = FindFirstObjectByType<GameStateMachine>();
        }

        void OnEnable()
        {
            if (controller)
                controller.StateChanged += HandleStateChanged;
        }

        void OnDisable()
        {
            if (controller)
                controller.StateChanged -= HandleStateChanged;
            HideTelegraph();
        }

        void OnDestroy()
        {
            if (telegraphMaterial)
                Destroy(telegraphMaterial);
        }

        void Update()
        {
            cooldownRemaining -= Time.deltaTime;
            if (gameState && !gameState.IsPlaying)
            {
                HideTelegraph();
                return;
            }
            if (controller && !controller.CanAttack)
            {
                HideTelegraph();
                return;
            }
            if (!target)
                target = FindFirstObjectByType<PlayerCharacter>();

            if (!target || !target.CanReceiveGameplayInput
                || !projectilePrefab || cooldownRemaining > 0f)
            {
                UpdateTelegraphIfReady();
                return;
            }

            Vector2 toTarget = (Vector2)target.transform.position - (Vector2)transform.position;
            if (toTarget.magnitude > attackRange)
            {
                HideTelegraph();
                return;
            }

            if (!HasLineOfSight(out Vector2 aimEnd))
            {
                HideTelegraph();
                return;
            }

            HideTelegraph();
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

        void HandleStateChanged(EnemyController source, EnemyState nextState)
        {
            if (nextState == EnemyState.Attacking)
            {
                // EE5 does not fire on the same frame an enemy wakes into its
                // attack state. Give the player one readable cadence beat.
                cooldownRemaining = Mathf.Max(cooldownRemaining, fireCooldown);
                return;
            }

            HideTelegraph();
        }

        void UpdateTelegraphIfReady()
        {
            if (!drawAimTelegraph || cooldownRemaining > telegraphDuration)
            {
                HideTelegraph();
                return;
            }

            if (!target || !target.CanReceiveGameplayInput || !HasLineOfSight(out Vector2 aimEnd))
            {
                HideTelegraph();
                return;
            }

            Transform originTransform = firePoint ? firePoint : transform;
            Vector2 origin = originTransform.position;
            SetTelegraph(origin, aimEnd);
        }

        bool HasLineOfSight(out Vector2 visibleEnd)
        {
            Transform originTransform = firePoint ? firePoint : transform;
            Vector2 origin = originTransform.position;
            Vector2 targetPoint = target ? (Vector2)target.transform.position : origin;
            Vector2 toTarget = targetPoint - origin;
            float distance = toTarget.magnitude;
            visibleEnd = targetPoint;
            if (distance <= 0.001f)
                return true;

#pragma warning disable CS0618
            int hitCount = Physics2D.RaycastNonAlloc(
                origin,
                toTarget / distance,
                lineOfSightHits,
                distance);
#pragma warning restore CS0618

            float closestWallDistance = float.PositiveInfinity;
            bool blocked = false;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = lineOfSightHits[i];
                if (!hit.collider || hit.collider.isTrigger
                    || IsOwnerCollider(hit.collider)
                    || (target && hit.collider.transform.IsChildOf(target.transform)))
                    continue;

                if (!IsWallCollider(hit.collider) || hit.distance >= closestWallDistance)
                    continue;

                closestWallDistance = hit.distance;
                visibleEnd = hit.point;
                blocked = true;
            }

            return !blocked;
        }

        void SetTelegraph(Vector2 start, Vector2 end)
        {
            EnsureTelegraph();
            if (!telegraph)
                return;

            float progress = telegraphDuration > 0f
                ? Mathf.Clamp01(1f - cooldownRemaining / telegraphDuration)
                : 1f;
            float pulse = 0.75f + Mathf.Sin(Time.time * 26f) * 0.25f;
            Color color = telegraphColor;
            color.a *= Mathf.Lerp(0.35f, 1f, progress) * pulse;
            telegraph.enabled = true;
            telegraph.startWidth = Mathf.Lerp(telegraphMinWidth, telegraphMaxWidth, progress);
            telegraph.endWidth = telegraph.startWidth * 0.08f;
            telegraph.startColor = color;
            telegraph.endColor = color;
            telegraph.SetPosition(0, new Vector3(start.x, start.y, transform.position.z - 0.04f));
            telegraph.SetPosition(1, new Vector3(end.x, end.y, transform.position.z - 0.04f));
        }

        void EnsureTelegraph()
        {
            if (!drawAimTelegraph || telegraph)
                return;

            GameObject telegraphObject = new GameObject("Enemy Aim Telegraph");
            telegraphObject.transform.SetParent(transform, false);
            telegraph = telegraphObject.AddComponent<LineRenderer>();
            telegraph.useWorldSpace = true;
            telegraph.positionCount = 2;
            telegraph.numCapVertices = 6;
            telegraph.numCornerVertices = 2;
            telegraph.sortingOrder = telegraphSortingOrder;
            telegraph.startWidth = telegraphMinWidth;
            telegraph.endWidth = telegraphMinWidth * 0.08f;
            telegraphMaterial = new Material(Shader.Find("Sprites/Default"));
            telegraphMaterial.name = "Enemy Aim Telegraph";
            telegraph.sharedMaterial = telegraphMaterial;
            telegraph.enabled = false;
        }

        void HideTelegraph()
        {
            if (telegraph)
                telegraph.enabled = false;
        }

        bool IsOwnerCollider(Collider2D other)
        {
            return other.gameObject == gameObject
                || other.transform.IsChildOf(transform)
                || transform.IsChildOf(other.transform);
        }

        static bool IsWallCollider(Collider2D other)
        {
            if (!other)
                return false;

            if (other.CompareTag("Wall"))
                return true;

            Transform current = other.transform.parent;
            while (current)
            {
                if (current.CompareTag("Wall"))
                    return true;
                current = current.parent;
            }

            return false;
        }
    }
}

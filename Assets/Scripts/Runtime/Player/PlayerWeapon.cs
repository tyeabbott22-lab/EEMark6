using UnityEngine;
using UnityEngine.InputSystem;
using ExtraterrestrialExhaust.Combat;
using ExtraterrestrialExhaust.Core;
using ExtraterrestrialExhaust.Enemy;

namespace ExtraterrestrialExhaust.Player
{
    /// <summary>
    /// Player weapon orchestration: input, cooldowns, projectile spawning, and recoil.
    /// Projectile collision rules remain in <see cref="PlayerProjectile"/>.
    /// </summary>
    [RequireComponent(typeof(PlayerWeaponInput))]
    public sealed class PlayerWeapon : MonoBehaviour
    {
        [SerializeField] PlayerFlightStateMachine stateMachine;
        [SerializeField] PlayerFlightMotor flightMotor;
        [SerializeField] PlayerWeaponInput input;
        [SerializeField] GameStateMachine gameState;
        [SerializeField] PlayerProjectile projectilePrefab;
        [SerializeField] Transform firePoint;
        // Match the authored EE5 sniper prefab: deliberate one-second shots
        // and a strong recoil impulse that remains part of the flight rhythm.
        [SerializeField, Min(0.01f)] float fireCooldown = 1f;
        [SerializeField, Min(0f)] float recoilForce = 12f;

        [Header("Aim Line")]
        [SerializeField] bool drawAimLine;
        [SerializeField, Min(0.1f)] float aimLineMaxDistance = 120f;
        [SerializeField, Min(0f)] float aimLineWidth = 0.035f;
        [SerializeField] Color aimLineColor = new Color(1f, 1f, 1f, 0.32f);
        [SerializeField] Color aimLineEnemyColor = new Color(1f, 0.08f, 0.04f, 0.58f);
        [SerializeField] int aimLineSortingOrder = 5000;

        float cooldownRemaining;
        float fireRateMultiplier = 1f;
        float fireRateBoostRemaining;
        LineRenderer aimLine;
        Material aimLineMaterial;

        public bool CanFire => cooldownRemaining <= 0f;
        public float FireRateMultiplier => fireRateMultiplier;

        void Reset()
        {
            ResolveReferences();
        }

        void Awake()
        {
            ResolveReferences();
            EnsureAimLine();
        }

        void OnDestroy()
        {
            if (aimLineMaterial)
                Destroy(aimLineMaterial);
        }

        void Update()
        {
            if (gameState && !gameState.IsPlaying)
            {
                HideAimLine();
                return;
            }

            cooldownRemaining -= Time.deltaTime;

            if (fireRateBoostRemaining > 0f)
            {
                fireRateBoostRemaining -= Time.deltaTime;
                if (fireRateBoostRemaining <= 0f)
                    fireRateMultiplier = 1f;
            }

            UpdateAimLine();
            if (input.IsFiring)
                TryFire();
        }

        public bool TryFire()
        {
            if (!CanFire || !projectilePrefab || !IsAllowedToFire())
                return false;

            GetFirePose(out Vector2 spawnPosition, out Vector2 direction);

            PlayerProjectile projectile = Instantiate(projectilePrefab, spawnPosition, Quaternion.identity);
            projectile.Launch(direction, gameObject);

            cooldownRemaining = fireCooldown / fireRateMultiplier;
            if (flightMotor && flightMotor.Body)
                flightMotor.Body.AddForce(-direction * recoilForce, ForceMode2D.Impulse);

            return true;
        }

        public void ApplyFireRateBoost(float duration, float multiplier)
        {
            fireRateMultiplier = Mathf.Max(1f, multiplier);
            fireRateBoostRemaining = Mathf.Max(fireRateBoostRemaining, duration);
        }

        void ResolveReferences()
        {
            if (!stateMachine)
                stateMachine = GetComponent<PlayerFlightStateMachine>();
            if (!flightMotor)
                flightMotor = GetComponent<PlayerFlightMotor>();
            if (!input)
                input = GetComponent<PlayerWeaponInput>();
            if (!gameState)
                gameState = FindFirstObjectByType<GameStateMachine>();
        }

        bool IsAllowedToFire()
        {
            return (stateMachine == null || stateMachine.AcceptsPlayerInput)
                && (!gameState || gameState.IsPlaying);
        }

        void GetFirePose(out Vector2 spawnPosition, out Vector2 direction)
        {
            Transform origin = firePoint ? firePoint : transform;
            spawnPosition = origin.position;
            direction = origin.right;

            if (!flightMotor)
                return;

            // The craft visual flips independently of the physics body. Mirror the
            // fire point in body-local space so shots and recoil follow the visual.
            Vector3 localOffset = transform.InverseTransformPoint(origin.position);
            localOffset.x = Mathf.Abs(localOffset.x) * (flightMotor.FacingRight ? 1f : -1f);
            spawnPosition = transform.TransformPoint(localOffset);
            direction = ((Vector2)spawnPosition - (Vector2)transform.position).normalized;
            if (direction.sqrMagnitude < 0.001f)
                direction = transform.right * (flightMotor.FacingRight ? 1f : -1f);
        }

        void EnsureAimLine()
        {
            if (!drawAimLine || aimLine)
                return;

            GameObject lineObject = new GameObject("Player Aim Line");
            lineObject.transform.SetParent(transform, false);
            aimLine = lineObject.AddComponent<LineRenderer>();
            aimLine.useWorldSpace = true;
            aimLine.positionCount = 2;
            aimLine.numCapVertices = 4;
            aimLine.sortingOrder = aimLineSortingOrder;
            aimLine.startWidth = aimLineWidth;
            aimLine.endWidth = aimLineWidth;
            aimLine.enabled = false;
            aimLineMaterial = new Material(Shader.Find("Sprites/Default"));
            aimLine.sharedMaterial = aimLineMaterial;
        }

        void UpdateAimLine()
        {
            if (!drawAimLine || !firePoint || !aimLine || !IsAllowedToFire())
            {
                HideAimLine();
                return;
            }

            GetFirePose(out Vector2 start, out Vector2 direction);
            if (direction.sqrMagnitude <= 0.001f)
            {
                HideAimLine();
                return;
            }

            RaycastHit2D hit = Physics2D.Raycast(
                start,
                direction,
                Mathf.Max(0.1f, aimLineMaxDistance));
            bool isWall = hit.collider && hit.collider.CompareTag("Wall");
            bool isEnemy = hit.collider
                && hit.collider.GetComponentInParent<EnemyController>();
            bool hitTarget = hit.collider
                && hit.distance > 0.001f
                && !IsOwnerCollider(hit.collider)
                && (isWall || isEnemy);
            Vector2 end = hitTarget
                ? hit.point
                : start + direction * Mathf.Max(0.1f, aimLineMaxDistance);
            Color color = hitTarget && isEnemy && !isWall
                ? aimLineEnemyColor
                : aimLineColor;
            aimLine.enabled = color.a > 0.01f;
            aimLine.startColor = color;
            aimLine.endColor = color;
            aimLine.startWidth = aimLineWidth;
            aimLine.endWidth = aimLineWidth;
            aimLine.SetPosition(0, start);
            aimLine.SetPosition(1, end);
        }

        void HideAimLine()
        {
            if (aimLine)
                aimLine.enabled = false;
        }

        bool IsOwnerCollider(Collider2D other)
        {
            return other.gameObject == gameObject
                || other.transform.IsChildOf(transform)
                || transform.IsChildOf(other.transform);
        }
    }
}

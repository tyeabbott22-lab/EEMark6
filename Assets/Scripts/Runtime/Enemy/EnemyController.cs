using UnityEngine;
using System;
using ExtraterrestrialExhaust.Combat;
using ExtraterrestrialExhaust.Player;
using ExtraterrestrialExhaust.CameraSystem;

namespace ExtraterrestrialExhaust.Enemy
{
    /// <summary>
    /// Small vertical-slice enemy: wake near the player, chase within a leash,
    /// steer around authored walls, stop inside attack range, and become inert
    /// when defeated.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(HealthComponent))]
    public sealed class EnemyController : MonoBehaviour
    {
        [SerializeField, Min(0f)] float detectionRange = 12f;
        [SerializeField, Min(0f)] float wakeDistance = 6f;
        [SerializeField, Min(0f)] float wakeDuration = 1.35f;
        [SerializeField, Min(0f)] float attackRange = 1.2f;
        [SerializeField, Min(0f)] float chaseSpeed = 2.5f;
        [SerializeField] PlayerCharacter target;

        [Header("Chase Steering")]
        [SerializeField] string wallTag = "Wall";
        [SerializeField, Min(0f)] float wallBuffer = 0.03f;
        [SerializeField] bool steerAroundWalls = true;
        [SerializeField, Min(0f)] float steeringCommitTime = 0.28f;
        [SerializeField, Range(5f, 85f)] float steeringProbeAngle = 55f;
        [SerializeField, Min(0f)] float steeringProbeDistance = 1.15f;
        [SerializeField, Range(0f, 1f)] float steeringPlayerBias = 0.35f;
        [SerializeField, Min(0f)] float wallSlideCommitTime = 0.42f;
        [SerializeField, Range(0f, 1f)] float wallSlideNormalPush = 0.22f;
        [SerializeField, Min(0.01f)] float stuckSampleTime = 0.28f;
        [SerializeField, Min(0f)] float stuckMinProgress = 0.06f;
        [SerializeField, Min(0f)] float stuckEscapeCommitTime = 0.5f;
        [SerializeField] bool blockOtherEnemies = true;
        [SerializeField, Min(0f)] float otherEnemyBuffer = 0.025f;

        [Header("Attack Movement")]
        [SerializeField] bool orbitWhileAttacking;
        [SerializeField, Min(0f)] float orbitRadius = 1.5f;
        [SerializeField, Min(0f)] float orbitMoveSpeed = 2f;
        [SerializeField, Min(0f)] float orbitAngularSpeed = 100f;
        [SerializeField] float orbitDirection = 1f;

        [Header("Facing")]
        [SerializeField, Min(0f)] float faceTurnSpeed = 5f;
        [SerializeField] bool keepSpriteUpright = true;

        Rigidbody2D body;
        Collider2D bodyCollider;
        HealthComponent health;
        SpriteRenderer spriteRenderer;
        readonly RaycastHit2D[] castHits = new RaycastHit2D[8];
        Vector2 chaseSteerDirection;
        float chaseSteerRemaining;
        Vector2 lastChasePosition;
        float chaseProgressTimer;
        Vector2 homePosition;
        Vector2 orbitCenter;
        float orbitAngle;
        float wakeTimer;

        public EnemyState State { get; private set; }
        public PlayerCharacter Target => target;
        public bool CanAttack => State == EnemyState.Attacking;
        public float WakeProgress => State == EnemyState.Waking && wakeDuration > 0f
            ? Mathf.Clamp01(wakeTimer / wakeDuration)
            : 0f;
        public event Action<EnemyController> Defeated;
        public event Action<EnemyController, EnemyState> StateChanged;

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            bodyCollider = GetComponent<Collider2D>();
            health = GetComponent<HealthComponent>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            State = EnemyState.Dormant;
            lastChasePosition = body.position;
            homePosition = body.position;
        }

        void OnEnable()
        {
            if (health)
                health.Died += HandleDefeated;
        }

        void OnDisable()
        {
            if (health)
                health.Died -= HandleDefeated;
        }

        void Update()
        {
            if (State == EnemyState.Defeated)
                return;

            if (!target)
                target = FindFirstObjectByType<PlayerCharacter>();

            if (!target || !target.CanReceiveGameplayInput)
            {
                SetState(EnemyState.Dormant);
                return;
            }

            float distance = Vector2.Distance(transform.position, target.transform.position);
            if (distance > detectionRange)
            {
                SetState(EnemyState.Dormant);
                return;
            }

            if (State == EnemyState.Dormant)
            {
                if (distance <= wakeDistance)
                    SetState(EnemyState.Waking);
                return;
            }

            if (State == EnemyState.Waking)
            {
                if (distance > wakeDistance)
                {
                    SetState(EnemyState.Dormant);
                    return;
                }

                wakeTimer += Time.deltaTime;
                if (wakeTimer < wakeDuration)
                    return;
            }

            SetState(distance > attackRange ? EnemyState.Chasing : EnemyState.Attacking);
        }

        void FixedUpdate()
        {
            if (!target || !bodyCollider)
            {
                body.linearVelocity = Vector2.zero;
                return;
            }

            if (State == EnemyState.Chasing)
            {
                HandleChaseMovement();
                return;
            }

            if (State == EnemyState.Attacking && orbitWhileAttacking)
            {
                HandleOrbitMovement();
                return;
            }

            body.linearVelocity = Vector2.zero;
        }

        void HandleChaseMovement()
        {
            body.linearVelocity = Vector2.zero;
            Vector2 toTarget = (Vector2)target.transform.position - body.position;
            if (toTarget.sqrMagnitude <= 0.0001f)
                return;

            Vector2 directDirection = toTarget.normalized;
            FaceTarget(directDirection);

            bool stuck = IsChaseStuck();
            RaycastHit2D wallHit = default;
            Vector2 move = directDirection * chaseSpeed * Time.fixedDeltaTime;

            if (steerAroundWalls && chaseSteerRemaining > 0f)
            {
                chaseSteerRemaining -= Time.fixedDeltaTime;
                if (MoveClamped(chaseSteerDirection * chaseSpeed * Time.fixedDeltaTime, out _))
                    return;

                chaseSteerRemaining = 0f;
            }

            if (!stuck && MoveClamped(move, out wallHit))
                return;

            if (!steerAroundWalls)
                return;

            if (!stuck && TryChooseWallSlide(wallHit, directDirection, out Vector2 slideDirection))
            {
                chaseSteerDirection = slideDirection;
                chaseSteerRemaining = wallSlideCommitTime;
                MoveClamped(chaseSteerDirection * chaseSpeed * Time.fixedDeltaTime, out _);
                return;
            }

            if (stuck && TryChooseEscapeSteer(directDirection, out Vector2 escapeDirection))
            {
                chaseSteerDirection = escapeDirection;
                chaseSteerRemaining = stuckEscapeCommitTime;
                MoveClamped(chaseSteerDirection * chaseSpeed * Time.fixedDeltaTime, out _);
                return;
            }

            if (TryChooseChaseSteer(directDirection, out Vector2 steerDirection))
            {
                chaseSteerDirection = steerDirection;
                chaseSteerRemaining = steeringCommitTime;
                MoveClamped(chaseSteerDirection * chaseSpeed * Time.fixedDeltaTime, out _);
            }
        }

        void HandleOrbitMovement()
        {
            body.linearVelocity = Vector2.zero;
            Vector2 toTarget = (Vector2)target.transform.position - body.position;
            if (toTarget.sqrMagnitude > 0.0001f)
                FaceTarget(toTarget.normalized);

            orbitAngle += orbitDirection * orbitAngularSpeed * Time.fixedDeltaTime;
            Vector2 orbitTarget = orbitCenter + new Vector2(
                Mathf.Cos(orbitAngle * Mathf.Deg2Rad),
                Mathf.Sin(orbitAngle * Mathf.Deg2Rad)) * orbitRadius;
            Vector2 move = orbitTarget - body.position;
            if (move.sqrMagnitude > 0.0001f)
                MoveClamped(Vector2.ClampMagnitude(move, orbitMoveSpeed * Time.fixedDeltaTime), out _);
        }

        void HandleDefeated()
        {
            SetState(EnemyState.Defeated);
            body.linearVelocity = Vector2.zero;
            body.simulated = false;
            Defeated?.Invoke(this);
            PlayerCameraFollow.Instance?.Shake(0.12f, 0.14f);

            foreach (Collider2D collider in GetComponentsInChildren<Collider2D>(true))
                collider.enabled = false;
        }

        void SetState(EnemyState nextState)
        {
            if (State == nextState)
                return;

            State = nextState;
            if (nextState == EnemyState.Chasing)
            {
                lastChasePosition = body.position;
                chaseProgressTimer = 0f;
            }
            else if (nextState == EnemyState.Waking)
            {
                wakeTimer = 0f;
                chaseSteerRemaining = 0f;
                chaseProgressTimer = 0f;
            }
            else if (nextState == EnemyState.Attacking && orbitWhileAttacking)
            {
                // EE5 ranged enemies orbit their authored spawn center rather
                // than creating a new patrol anchor after chasing the player.
                orbitCenter = homePosition;
                orbitAngle = UnityEngine.Random.Range(0f, 360f);
                chaseSteerRemaining = 0f;
                chaseProgressTimer = 0f;
            }
            else
            {
                chaseSteerRemaining = 0f;
                chaseProgressTimer = 0f;
            }

            StateChanged?.Invoke(this, nextState);
        }

        bool MoveClamped(Vector2 move, out RaycastHit2D blockingWall)
        {
            blockingWall = default;
            float distance = move.magnitude;
            if (distance <= 0.00001f)
                return true;

            Vector2 direction = move / distance;
            int hitCount = bodyCollider.Cast(direction, castHits, distance + wallBuffer);
            float allowedDistance = distance;
            float closestWallDistance = float.PositiveInfinity;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = castHits[i];
                if (hit.collider == null)
                    continue;

                if (!IsWallCollider(hit.collider))
                {
                    if (!IsBlockingEnemyCollider(hit.collider))
                        continue;

                    allowedDistance = Mathf.Min(
                        allowedDistance,
                        Mathf.Max(0f, hit.distance - otherEnemyBuffer));
                    continue;
                }

                if (hit.distance < closestWallDistance)
                {
                    closestWallDistance = hit.distance;
                    blockingWall = hit;
                }

                allowedDistance = Mathf.Min(
                    allowedDistance,
                    Mathf.Max(0f, hit.distance - wallBuffer));
            }

            if (allowedDistance > 0f)
                body.MovePosition(body.position + direction * allowedDistance);

            return allowedDistance >= distance * 0.65f;
        }

        bool IsChaseStuck()
        {
            chaseProgressTimer += Time.fixedDeltaTime;
            if (chaseProgressTimer < stuckSampleTime)
                return false;

            float progress = Vector2.Distance(body.position, lastChasePosition);
            lastChasePosition = body.position;
            chaseProgressTimer = 0f;
            return progress < stuckMinProgress;
        }

        bool TryChooseWallSlide(
            RaycastHit2D wallHit,
            Vector2 directDirection,
            out Vector2 slideDirection)
        {
            slideDirection = Vector2.zero;
            if (wallHit.collider == null)
                return false;

            Vector2 normal = wallHit.normal.sqrMagnitude > 0.0001f
                ? wallHit.normal.normalized
                : -directDirection;
            Vector2 tangent = new Vector2(-normal.y, normal.x);
            Vector2 towardTarget = target
                ? ((Vector2)target.transform.position - body.position).normalized
                : directDirection;

            if (Vector2.Dot(tangent, towardTarget) < Vector2.Dot(-tangent, towardTarget))
                tangent = -tangent;

            Vector2 primary = (tangent + normal * wallSlideNormalPush).normalized;
            Vector2 secondary = (-tangent + normal * wallSlideNormalPush).normalized;
            if (CanMove(primary, steeringProbeDistance))
            {
                slideDirection = primary;
                return true;
            }

            if (CanMove(secondary, steeringProbeDistance))
            {
                slideDirection = secondary;
                return true;
            }

            return false;
        }

        bool TryChooseEscapeSteer(Vector2 directDirection, out Vector2 escapeDirection)
        {
            escapeDirection = Vector2.zero;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < 8; i++)
            {
                float angleOffset = i * 45f;
                Vector2 candidate = Quaternion.Euler(0f, 0f, angleOffset) * directDirection;
                if (!CanMove(candidate, steeringProbeDistance))
                    continue;

                float score = Vector2.Dot(candidate, directDirection);
                score += i == 0 ? 0.35f : 0f;
                score -= Mathf.Abs(Mathf.DeltaAngle(0f, angleOffset)) * 0.002f;
                if (score <= bestScore)
                    continue;

                bestScore = score;
                escapeDirection = candidate.normalized;
            }

            return bestScore > float.NegativeInfinity;
        }

        bool TryChooseChaseSteer(Vector2 directDirection, out Vector2 steerDirection)
        {
            steerDirection = Vector2.zero;
            Vector2 left = Quaternion.Euler(0f, 0f, steeringProbeAngle) * directDirection;
            Vector2 right = Quaternion.Euler(0f, 0f, -steeringProbeAngle) * directDirection;
            bool leftOpen = CanMove(left, steeringProbeDistance);
            bool rightOpen = CanMove(right, steeringProbeDistance);
            if (!leftOpen && !rightOpen)
                return false;

            Vector2 towardTarget = target
                ? ((Vector2)target.transform.position - body.position).normalized
                : directDirection;
            if (leftOpen && rightOpen)
            {
                steerDirection = Vector2.Dot(left, towardTarget) >= Vector2.Dot(right, towardTarget)
                    ? left
                    : right;
            }
            else
            {
                steerDirection = leftOpen ? left : right;
            }

            steerDirection = Vector2.Lerp(steerDirection, directDirection, steeringPlayerBias).normalized;
            return steerDirection.sqrMagnitude > 0.001f;
        }

        bool CanMove(Vector2 direction, float distance)
        {
            if (direction.sqrMagnitude <= 0.0001f)
                return false;

            int hitCount = bodyCollider.Cast(
                direction.normalized,
                castHits,
                distance + wallBuffer);
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = castHits[i];
                if (hit.collider != null
                    && (IsWallCollider(hit.collider) || IsBlockingEnemyCollider(hit.collider)))
                    return false;
            }

            return true;
        }

        bool IsBlockingEnemyCollider(Collider2D hitCollider)
        {
            if (!blockOtherEnemies || hitCollider == null)
                return false;

            EnemyController other = hitCollider.GetComponentInParent<EnemyController>();
            return other && other != this;
        }

        bool IsWallCollider(Collider2D hitCollider)
        {
            if (hitCollider == null)
                return false;

            string hitTag = hitCollider.tag;
            return string.Equals(hitTag, wallTag, StringComparison.OrdinalIgnoreCase)
                || string.Equals(hitTag, "Wall", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hitTag, "wall", StringComparison.OrdinalIgnoreCase);
        }

        void FaceTarget(Vector2 direction)
        {
            if (direction.sqrMagnitude <= 0.0001f)
                return;

            float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            float turnT = 1f - Mathf.Exp(-faceTurnSpeed * Time.fixedDeltaTime);
            body.MoveRotation(Mathf.LerpAngle(body.rotation, targetAngle, turnT));

            if (spriteRenderer && keepSpriteUpright)
            {
                float signedAngle = Mathf.DeltaAngle(0f, targetAngle);
                spriteRenderer.flipY = signedAngle > 90f || signedAngle < -90f;
            }
        }
    }
}

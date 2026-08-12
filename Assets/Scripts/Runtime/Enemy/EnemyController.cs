using UnityEngine;
using System;
using ExtraterrestrialExhaust.Combat;
using ExtraterrestrialExhaust.Core;
using ExtraterrestrialExhaust.Player;

namespace ExtraterrestrialExhaust.Enemy
{
    public enum EnemyMovementMode
    {
        Chase,
        Wander
    }

    /// <summary>
    /// Small vertical-slice enemy: wake near the player, then either chase into
    /// contact range or preserve an authored wander pattern, steer around room
    /// walls, and become inert when defeated.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(HealthComponent))]
    public sealed class EnemyController : MonoBehaviour
    {
        [Header("Authored Movement")]
        [SerializeField] EnemyMovementMode movementMode = EnemyMovementMode.Chase;
        [SerializeField, Min(0f)] float wanderRadius = Ee5SliceProfile.EnemyGunnerWanderRadius;
        [SerializeField, Min(0f)] float wanderDurationMin = Ee5SliceProfile.EnemyGunnerWanderDurationMin;
        [SerializeField, Min(0f)] float wanderDurationMax = Ee5SliceProfile.EnemyGunnerWanderDurationMax;

        [SerializeField, Min(0f)] float detectionRange = 12f;
        [SerializeField, Min(0f)] float wakeDistance = 6f;
        // EE5's authored six-unit value is the base trigger for the wider
        // line-of-sight wake envelope. The actual alert duration is randomized
        // below so the dormant enemy does not trigger on an identical beat.
        [SerializeField, Min(0f)] float wakeDuration = Ee5SliceProfile.EnemyWakeBuildupDuration;
        [SerializeField, Min(0f)] float wakeIdleDurationMin = Ee5SliceProfile.EnemyWakeIdleDurationMin;
        [SerializeField, Min(0f)] float wakeIdleDurationMax = Ee5SliceProfile.EnemyWakeIdleDurationMax;
        [SerializeField, Min(0f)] float wakeScreamDuration = Ee5SliceProfile.EnemyWakeScreamDuration;
        [SerializeField] bool requireLineOfSightToWake = true;
        // EE5's intro line uses the authored six-unit base trigger multiplied
        // by four. The signal can therefore arm across the room while the
        // enemy remains in its dormant sprite loop; once the line charge is
        // complete, the idle/scream intro commits and the enemy cannot cancel
        // it halfway through because the player crossed a wall or moved away.
        [SerializeField, Min(1f)] float wakeSignalDistanceMultiplier = 4f;
        [SerializeField, Min(0.01f)] float wakeSignalChargeDuration = Ee5SliceProfile.EnemyWakeSignalChargeDuration;
        [SerializeField, Min(0f)] float wakeSignalChargeDecay = Ee5SliceProfile.EnemyWakeSignalChargeDecay;
        [SerializeField, Min(0f)] float wakeSignalChargeSpeedAtEdge = Ee5SliceProfile.EnemyWakeSignalChargeSpeedAtEdge;
        [SerializeField, Min(0f)] float wakeSignalChargeSpeedAtClose = Ee5SliceProfile.EnemyWakeSignalChargeSpeedAtClose;
        [SerializeField, Min(0f)] float wakeFinalWarningDuration = Ee5SliceProfile.EnemyWakeFinalWarningDuration;
        [SerializeField, Min(0f)] float attackRange = 1.2f;
        [Tooltip("Melee attack remains latched until this wider radius is crossed, preventing state chatter at the stopping distance.")]
        [SerializeField, Min(0f)] float attackExitRange = Ee5SliceProfile.EnemyMeleeAttackExitRange;
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
        [Tooltip("Extra separation from the player so contact damage does not become a physics tug-of-war.")]
        [SerializeField, Min(0f)] float targetBuffer = 0.04f;

        [Header("Attack Movement")]
        [SerializeField] bool orbitWhileAttacking;
        [SerializeField, Min(0f)] float orbitRadius = 1.5f;
        [SerializeField, Min(0f)] float orbitMoveSpeed = 2f;
        [SerializeField, Min(0f)] float orbitAngularSpeed = 100f;
        [SerializeField] float orbitDirection = 1f;

        [Header("Near Miss")]
        [SerializeField, Min(0f)] float nearMissDistance = 1.65f;
        [SerializeField, Min(0f)] float nearMissExitDistance = 2.15f;

        [Header("Facing")]
        [SerializeField, Min(0f)] float faceTurnSpeed = 5f;
        [Tooltip("The authored ranged gunner points along local negative X, matching EE5's enemyGun prefab.")]
        [SerializeField] bool forwardIsLocalNegativeX;
        [SerializeField] bool keepSpriteUpright = true;
        [SerializeField] GameStateMachine gameState;

        Rigidbody2D body;
        Collider2D bodyCollider;
        HealthComponent health;
        SpriteRenderer spriteRenderer;
        readonly RaycastHit2D[] castHits = new RaycastHit2D[8];
        readonly RaycastHit2D[] lineOfSightHits = new RaycastHit2D[16];
        Vector2 chaseSteerDirection;
        float chaseSteerRemaining;
        Vector2 lastChasePosition;
        float chaseProgressTimer;
        Vector2 homePosition;
        Vector2 wanderTarget;
        Vector2 orbitCenter;
        float orbitAngle;
        float wakeTimer;
        float wakeTotalDuration;
        float wakeSignalCharge;
        float wanderTimer;
        bool nearPlayer;
        bool touchedPlayerDuringNearPass;

        public EnemyState State { get; private set; }
        public PlayerCharacter Target => target;
        public bool ForwardIsLocalNegativeX => forwardIsLocalNegativeX;
        public bool CanAttack => State == EnemyState.Attacking;
        public bool IsCombatActive => State == EnemyState.Chasing || State == EnemyState.Attacking;
        public float WakeProgress => State == EnemyState.Waking && wakeTotalDuration > 0f
            ? Mathf.Clamp01(wakeTimer / Mathf.Max(0.01f, wakeTotalDuration))
            : 0f;
        public float WakeSignalChargeProgress => wakeSignalChargeDuration > 0f
            ? Mathf.Clamp01(wakeSignalCharge / wakeSignalChargeDuration)
            : 0f;
        public bool WakeSignalVisible { get; private set; }
        public bool WakeSignalHasClearSight { get; private set; }
        public Vector2 WakeSignalEnd { get; private set; }
        public bool IsWakeFinalWarning => State == EnemyState.Waking
            && wakeFinalWarningDuration > 0f
            && wakeTotalDuration > 0f
            && WakeProgress >= Mathf.InverseLerp(
                0f,
                Mathf.Max(0.01f, wakeTotalDuration),
                Mathf.Max(0f, wakeTotalDuration - GetWakeFinalPhaseDuration()));
        public event Action<EnemyController> Defeated;
        public event Action<EnemyController, DamageInfo> Damaged;
        public event Action<EnemyController, EnemyState> StateChanged;

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            bodyCollider = GetComponent<Collider2D>();
            health = GetComponent<HealthComponent>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            ApplyEe5PhysicsProfile();
            if (!gameState)
                gameState = FindFirstObjectByType<GameStateMachine>();
            State = EnemyState.Dormant;
            lastChasePosition = body.position;
            homePosition = body.position;
            ResetWander();
        }

        void ApplyEe5PhysicsProfile()
        {
            if (!body)
                return;

            // EnemyController advances with Rigidbody2D.MovePosition and
            // MoveRotation. EE5's prefabs are kinematic/interpolated; allowing
            // dynamic contact resolution here makes the melee body fight its
            // scripted stop and visibly jitter against the player.
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.linearDamping = 0f;
            body.angularDamping = 0.05f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            // The EE5 enemy prefabs all use the same deliberate five-degree
            // turn response. Runtime repair keeps a stale inspector override
            // from reintroducing a twitchy melee presentation.
            faceTurnSpeed = Ee5SliceProfile.EnemyFaceTurnSpeed;
        }

        void OnEnable()
        {
            if (health)
            {
                health.Died += HandleDefeated;
                health.Damaged += HandleDamaged;
            }
        }

        void OnDisable()
        {
            if (health)
            {
                health.Died -= HandleDefeated;
                health.Damaged -= HandleDamaged;
            }
        }

        void HandleDamaged(DamageInfo damage)
        {
            Damaged?.Invoke(this, damage);
        }

        void Update()
        {
            if (State == EnemyState.Defeated)
                return;

            // Pausing must suspend the current enemy state rather than making
            // a wake-capable enemy forget its progress. Game-over likewise
            // freezes the room while the extraction handoff finishes.
            if (gameState && !gameState.IsPlaying)
            {
                if (gameState.CurrentState == GameState.GameOver)
                    ClearWakeSignal();
                return;
            }

            if (!target)
                target = FindFirstObjectByType<PlayerCharacter>();

            if (!target || !target.CanReceiveGameplayInput)
            {
                ClearWakeSignal();
                SetState(EnemyState.Dormant);
                return;
            }

            float distance = Vector2.Distance(transform.position, target.transform.position);
            UpdateNearMiss(distance);
            UpdateWakeSignal(distance);

            float behaviorRange = Mathf.Max(detectionRange, GetWakeSignalDistance());
            // EE5's EnemyAI has no post-wake leash. Once the intro commits,
            // the enemy remains an authored room threat until death or reload.
            if (State == EnemyState.Dormant && distance > behaviorRange)
            {
                return;
            }

            if (State == EnemyState.Dormant)
            {
                // The six-unit value is the base of the EE5 four-times wake
                // envelope, not a second close-range gate. Starting the
                // authored intro when the charged signal completes keeps the
                // line, idle strip, scream strip, and combat handoff in one
                // deterministic sequence.
                if (WakeSignalVisible
                    && (!requireLineOfSightToWake || WakeSignalHasClearSight)
                    && WakeSignalChargeProgress >= 0.999f)
                    SetState(EnemyState.Waking);
                return;
            }

            if (State == EnemyState.Waking)
            {
                wakeTimer += Time.deltaTime;
                // EE5 commits to the alert once its wake line has armed. A
                // later wall crossing must not cancel the authored scream
                // beat halfway through.
                if (wakeTimer < wakeTotalDuration)
                    return;
            }

            if (movementMode == EnemyMovementMode.Wander)
            {
                // The white gunner is a persistent wandering shooter in
                // realScene2. Its weapon uses Attacking as the firing contract,
                // while its body patrols independently of player distance.
                SetState(EnemyState.Attacking);
                return;
            }

            float attackStartRange = Mathf.Max(0f, attackRange);
            float attackStopRange = Mathf.Max(
                attackStartRange,
                Mathf.Max(0f, attackExitRange));
            bool stayInAttack = State == EnemyState.Attacking
                && distance <= attackStopRange;
            SetState(
                stayInAttack || distance <= attackStartRange
                    ? EnemyState.Attacking
                    : EnemyState.Chasing);
        }

        void UpdateWakeSignal(float distance)
        {
            // The line is an activation telegraph, not a permanent combat
            // tether. Once the enemy is active, the attack and movement
            // presentations own threat readability instead.
            if (State != EnemyState.Dormant && State != EnemyState.Waking)
            {
                ClearWakeSignal();
                return;
            }

            if (State == EnemyState.Waking)
            {
                WakeSignalVisible = target != null;
                WakeSignalHasClearSight = target != null;
                WakeSignalEnd = target ? GetWakeSignalTargetPoint() : transform.position;
                wakeSignalCharge = wakeSignalChargeDuration;
                return;
            }

            float signalDistance = GetWakeSignalDistance();
            WakeSignalVisible = distance <= signalDistance;
            WakeSignalEnd = target ? GetWakeSignalTargetPoint() : transform.position;

            if (!WakeSignalVisible)
            {
                WakeSignalHasClearSight = false;
                wakeSignalCharge = Mathf.MoveTowards(
                    wakeSignalCharge,
                    0f,
                    wakeSignalChargeDecay * Time.deltaTime);
                return;
            }

            Vector2 origin = bodyCollider ? bodyCollider.bounds.center : transform.position;
            Vector2 targetPoint = GetWakeSignalTargetPoint();
            Vector2 lineEnd = targetPoint;
            WakeSignalHasClearSight = !requireLineOfSightToWake
                || HasClearLineOfSight(origin, targetPoint, out lineEnd);
            if (requireLineOfSightToWake && !WakeSignalHasClearSight)
                WakeSignalEnd = lineEnd;

            if (WakeSignalHasClearSight)
            {
                // Match EE5's EnemyAI pressure curve: a distant clear line
                // takes longer to commit, while a close approach accelerates
                // the final charge and makes the scream handoff feel earned.
                float proximity = Mathf.Clamp01(1f - distance / Mathf.Max(0.01f, signalDistance));
                float chargeSpeed = Mathf.Lerp(
                    wakeSignalChargeSpeedAtEdge,
                    wakeSignalChargeSpeedAtClose,
                    proximity);
                wakeSignalCharge = Mathf.MoveTowards(
                    wakeSignalCharge,
                    wakeSignalChargeDuration,
                    Mathf.Max(0f, chargeSpeed) * Time.deltaTime);
            }
            else
            {
                wakeSignalCharge = Mathf.MoveTowards(
                    wakeSignalCharge,
                    0f,
                    wakeSignalChargeDecay * Time.deltaTime);
            }
        }

        void ClearWakeSignal()
        {
            WakeSignalVisible = false;
            WakeSignalHasClearSight = false;
            WakeSignalEnd = transform.position;
            wakeSignalCharge = 0f;
        }

        float GetWakeSignalDistance() =>
            wakeDistance * Mathf.Max(1f, wakeSignalDistanceMultiplier);

        Vector2 GetWakeSignalTargetPoint()
        {
            if (!target)
                return transform.position;

            Collider2D targetCollider = target.GetComponent<Collider2D>();
            return targetCollider ? targetCollider.bounds.center : (Vector2)target.transform.position;
        }

        bool HasClearLineOfSight(Vector2 origin, Vector2 targetPoint, out Vector2 lineEnd)
        {
            Vector2 toTarget = targetPoint - origin;
            float distance = toTarget.magnitude;
            lineEnd = targetPoint;
            if (distance <= 0.0001f)
                return true;

            // Unity 6 marks the allocation-free overload obsolete while the
            // contact-filter replacement is still not available in every
            // supported 2D package version used by this project.
#pragma warning disable CS0618
            int hitCount = Physics2D.RaycastNonAlloc(
                origin,
                toTarget / distance,
                lineOfSightHits,
                distance);
#pragma warning restore CS0618
            float closestBlockDistance = float.PositiveInfinity;
            bool blocked = false;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = lineOfSightHits[i];
                if (hit.collider == null || hit.collider.isTrigger)
                    continue;

                if (hit.collider == bodyCollider || hit.collider.transform.IsChildOf(transform))
                    continue;

                if (target && hit.collider.transform.IsChildOf(target.transform))
                    continue;

                if (!IsWallCollider(hit.collider) || hit.distance >= closestBlockDistance)
                    continue;

                closestBlockDistance = hit.distance;
                lineEnd = hit.point;
                blocked = true;
            }

            return !blocked;
        }

        void FixedUpdate()
        {
            if (gameState && !gameState.IsPlaying)
            {
                body.linearVelocity = Vector2.zero;
                return;
            }

            if (!target || !bodyCollider)
            {
                body.linearVelocity = Vector2.zero;
                return;
            }

            if (movementMode == EnemyMovementMode.Wander
                && (State == EnemyState.Chasing || State == EnemyState.Attacking))
            {
                HandleWanderMovement();
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

            // EE5 keeps facing the player while a melee hunter is in its
            // contact-attack state. Without this branch the body stops moving
            // at the attack radius but its sprite remains aimed at the last
            // chase direction, which reads as a backwards enemy.
            if (State == EnemyState.Attacking)
            {
                Vector2 toTarget = (Vector2)target.transform.position - body.position;
                if (toTarget.sqrMagnitude > 0.0001f)
                    FaceTarget(toTarget.normalized);
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

        void HandleWanderMovement()
        {
            body.linearVelocity = Vector2.zero;
            Vector2 toTarget = (Vector2)target.transform.position - body.position;
            if (toTarget.sqrMagnitude > 0.0001f)
                FaceTarget(toTarget.normalized);

            wanderTimer -= Time.fixedDeltaTime;
            if (wanderTimer <= 0f
                || Vector2.Distance(body.position, wanderTarget) <= 0.1f)
            {
                ResetWander();
            }

            Vector2 move = Vector2.MoveTowards(
                body.position,
                wanderTarget,
                chaseSpeed * Time.fixedDeltaTime) - body.position;
            if (move.sqrMagnitude > 0.0001f)
                MoveClamped(move, out _);
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
            ResolveNearMiss();
            ClearWakeSignal();
            SetState(EnemyState.Defeated);
            body.linearVelocity = Vector2.zero;
            body.simulated = false;
            // EE5 briefly slows the world on a confirmed defeat. The game
            // state machine owns time so this remains compatible with pause.
            // Apply it before broadcasting the death event, matching EE5's
            // EnemyHealth.Die ordering so the burst and key handoff begin
            // inside the same readable hit-stop beat.
            FindFirstObjectByType<GameStateMachine>()?.TriggerEnemyDefeatSlowdown();
            Defeated?.Invoke(this);

            foreach (Collider2D collider in GetComponentsInChildren<Collider2D>(true))
                collider.enabled = false;
        }

        /// <summary>
        /// Contact damage marks a close pass as a hit so the same enemy cannot
        /// award both a collision and a near-miss credit.
        /// </summary>
        public void RegisterPlayerContact() => touchedPlayerDuringNearPass = true;

        void UpdateNearMiss(float distance)
        {
            if (State == EnemyState.Dormant || State == EnemyState.Defeated)
            {
                nearPlayer = false;
                touchedPlayerDuringNearPass = false;
                return;
            }

            if (!nearPlayer && distance <= nearMissDistance)
            {
                nearPlayer = true;
                touchedPlayerDuringNearPass = false;
                return;
            }

            if (!nearPlayer || distance < nearMissExitDistance)
                return;

            ResolveNearMiss();
        }

        void ResolveNearMiss()
        {
            if (!nearPlayer)
                return;

            if (!touchedPlayerDuringNearPass)
                FindFirstObjectByType<ScoreSystem>()?.Award(ScoreReason.NearMiss);

            nearPlayer = false;
            touchedPlayerDuringNearPass = false;
        }

        void ResetWander()
        {
            float minDuration = Mathf.Max(0f, wanderDurationMin);
            float maxDuration = Mathf.Max(minDuration, wanderDurationMax);
            wanderTarget = homePosition
                + UnityEngine.Random.insideUnitCircle * Mathf.Max(0f, wanderRadius);
            wanderTimer = UnityEngine.Random.Range(minDuration, maxDuration);
        }

        void SetState(EnemyState nextState)
        {
            if (State == nextState)
                return;

            State = nextState;
            if (nextState != EnemyState.Dormant && nextState != EnemyState.Waking)
                ClearWakeSignal();

            if (nextState == EnemyState.Chasing)
            {
                lastChasePosition = body.position;
                chaseProgressTimer = 0f;
            }
            else if (nextState == EnemyState.Waking)
            {
                wakeTimer = 0f;
                wakeTotalDuration = GetWakeTotalDuration();
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

            if (nextState == EnemyState.Dormant)
                wakeTotalDuration = 0f;

            StateChanged?.Invoke(this, nextState);
        }

        float GetWakeTotalDuration()
        {
            float minimumAlertDuration = Mathf.Max(0f, wakeDuration)
                + Mathf.Max(0f, wakeFinalWarningDuration);
            float minimumIdle = Mathf.Max(0f, wakeIdleDurationMin);
            float maximumIdle = Mathf.Max(minimumIdle, wakeIdleDurationMax);
            float randomizedAlert = UnityEngine.Random.Range(minimumIdle, maximumIdle);
            float alertDuration = Mathf.Max(minimumAlertDuration, randomizedAlert);
            return alertDuration + Mathf.Max(
                0f,
                wakeScreamDuration - wakeFinalWarningDuration);
        }

        float GetWakeFinalPhaseDuration()
        {
            return wakeScreamDuration > 0f
                ? wakeScreamDuration
                : wakeFinalWarningDuration;
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

                if (IsBlockingTargetCollider(hit.collider))
                {
                    allowedDistance = Mathf.Min(
                        allowedDistance,
                        Mathf.Max(0f, hit.distance - targetBuffer));
                    continue;
                }

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
                    && (IsWallCollider(hit.collider)
                        || IsBlockingEnemyCollider(hit.collider)
                        || IsBlockingTargetCollider(hit.collider)))
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

        bool IsBlockingTargetCollider(Collider2D hitCollider)
        {
            if (!target || !hitCollider)
                return false;

            PlayerCharacter hitPlayer = hitCollider.GetComponentInParent<PlayerCharacter>();
            return hitPlayer == target;
        }

        bool IsWallCollider(Collider2D hitCollider)
        {
            return Ee5SliceProfile.IsWallCollider(hitCollider, wallTag);
        }

        void FaceTarget(Vector2 direction)
        {
            if (direction.sqrMagnitude <= 0.0001f)
                return;

            float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            if (forwardIsLocalNegativeX)
                targetAngle += 180f;
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

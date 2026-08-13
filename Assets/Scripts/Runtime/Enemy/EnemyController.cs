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
    [DefaultExecutionOrder(-200)]
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
        [Tooltip("Actual melee damage reach. Keep this below the attack-state brake radius so knockback does not create a chase/attack tug-of-war.")]
        [SerializeField, Min(0f)] float contactDamageRange = Ee5SliceProfile.EnemyMeleeContactRange;
        [Tooltip("Post-hit hold that lets the contact knockback read before the hunter resumes pursuit.")]
        [SerializeField, Min(0f)] float attackRecoveryDuration = Ee5SliceProfile.EnemyMeleeAttackRecoveryDuration;
        [Tooltip("Melee aim stays committed inside this angular band before it re-aims, preventing a single-frame target correction from making the sword pop.")]
        [SerializeField, Min(0f)] float attackFacingRefreshDegrees = Ee5SliceProfile.EnemyMeleeAttackFacingRefreshDegrees;
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
        float attackRecoveryRemaining;
        Vector2 attackFacingDirection = Vector2.right;
        bool nearPlayer;
        bool touchedPlayerDuringNearPass;
        bool spriteFlippedUpright;
        EnemyWeapon weapon;
        EnemyContactDamage contactDamage;
        bool roleResolved;
        bool meleeRole;

        public EnemyState State { get; private set; }
        public PlayerCharacter Target => target;
        /// <summary>Authoritative Rigidbody position for physics-step followers.</summary>
        public Vector2 PhysicsPosition => body ? body.position : (Vector2)transform.position;
        /// <summary>
        /// Shared fixed-step anchor for effects that belong to the enemy body.
        /// Wake and defeat presentation must not alternate between an
        /// interpolated Transform and the controller's physics position.
        /// </summary>
        public Vector3 PhysicsAnchorPosition => body
            ? (Vector3)body.position
            : transform.position;
        /// <summary>
        /// Reprojects a child anchor from the rendered hierarchy onto the
        /// current fixed-step body pose. Kinematic enemies are interpolated
        /// for display, so using child.position during FixedUpdate can leave a
        /// projectile or telegraph one render behind the actual enemy.
        /// </summary>
        public Vector2 PhysicsPoint(Transform child)
        {
            if (!child)
                return PhysicsPosition;

            if (!body || !child.IsChildOf(transform))
                return child.position;

            Vector2 localPoint = transform.InverseTransformPoint(child.position);
            Vector2 scaledLocalPoint = Vector2.Scale(
                localPoint,
                new Vector2(transform.localScale.x, transform.localScale.y));
            Vector2 rotatedPoint = Quaternion.Euler(0f, 0f, body.rotation) * scaledLocalPoint;
            return body.position + rotatedPoint;
        }
        // Prefer the authored movement enum, but recover an older EE5 melee
        // prefab when it still says Wander. The melee prefab has contact
        // damage and no weapon; the gunner has both, so the weapon component
        // is the stronger role signal when serialized values disagree.
        public bool IsMelee => ResolveMeleeRole();
        public bool ForwardIsLocalNegativeX => IsMelee ? false : forwardIsLocalNegativeX;
        /// <summary>
        /// Exposes the same upright-side decision used by the body sprite so
        /// the ranged weapon can mirror its authored muzzle offset in lockstep.
        /// </summary>
        public bool IsSpriteFlippedUpright => spriteFlippedUpright;
        public bool CanAttack => State == EnemyState.Attacking;
        /// <summary>
        /// Legacy hand-authored melee prefabs may still use a trigger navigation
        /// body. The gold EE5 close-bruiser path uses a solid box; exposing this
        /// flag keeps the contact fallback explicit for both contracts.
        /// </summary>
        public bool UsesTriggerContactBody => IsMelee && bodyCollider && bodyCollider.isTrigger;
        /// <summary>
        /// The range at which this role is allowed to own the attack state.
        /// Ranged enemies use their authored aim range. Melee exposes its
        /// serialized center-distance fallback for gizmos/legacy scenes, while
        /// generated trigger scenes use IsWithinMeleeContact as the authority.
        /// </summary>
        public float AttackReach => IsMelee ? ContactDamageReach : attackRange;
        public float ContactDamageReach => Mathf.Min(
            Mathf.Max(0f, attackRange),
            Mathf.Max(0f, contactDamageRange));
        public bool IsAttackRecoveryActive => IsMelee && attackRecoveryRemaining > 0f;
        public bool IsCombatActive => State == EnemyState.Chasing || State == EnemyState.Attacking;

        /// <summary>
        /// Tests the authored player/enemy collider pair. EE5's melee damage
        /// came from overlap, so both the solid close-bruiser body and older
        /// trigger scenes enter their attack state at the physical contact
        /// moment instead of at an arbitrary center radius.
        /// </summary>
        public bool IsWithinMeleeContact(PlayerCharacter candidate)
        {
            if (!IsMelee || !candidate)
                return false;

            Collider2D candidateCollider = candidate.GetComponent<Collider2D>();
            if (bodyCollider && candidateCollider)
            {
                ColliderDistance2D separation = Physics2D.Distance(
                    bodyCollider,
                    candidateCollider);
                return separation.distance <= Mathf.Max(0f, targetBuffer);
            }

            // Keep older hand-authored scenes playable if one side lost its
            // collider. The serialized range is deliberately only a recovery
            // path; generated scenes use the collider-pair test above.
            return Vector2.Distance(PhysicsPosition, candidate.PhysicsPosition)
                <= ContactDamageReach;
        }
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
            spriteRenderer = ResolveVisibleSpriteRenderer();
            ResolveMeleeRole();
            if (meleeRole)
                movementMode = EnemyMovementMode.Chase;
            if (health)
            {
                health.ConfigureMaxHealth(
                    meleeRole
                        ? Ee5SliceProfile.EnemyMeleeMaxHealth
                        : Ee5SliceProfile.EnemyGunnerMaxHealth);
            }
            ApplyEe5PhysicsProfile();
            if (!gameState)
                gameState = FindFirstObjectByType<GameStateMachine>();
            State = EnemyState.Dormant;
            lastChasePosition = body.position;
            homePosition = body.position;
            ResetWander();
        }

        void Start()
        {
            // HealthComponent and EnemyController are independent components;
            // Unity does not promise their Awake order. Re-assert the role
            // health contract after every component has initialized so a stale
            // prefab value cannot make the one-hit bruiser or five-pip gunner
            // change behavior between scene launches.
            float expectedHealth = ResolveMeleeRole()
                ? Ee5SliceProfile.EnemyMeleeMaxHealth
                : Ee5SliceProfile.EnemyGunnerMaxHealth;
            if (health && !Mathf.Approximately(health.MaxHealth, expectedHealth))
                health.ConfigureMaxHealth(expectedHealth);
        }

        SpriteRenderer ResolveVisibleSpriteRenderer()
        {
            SpriteRenderer direct = GetComponent<SpriteRenderer>();
            if (direct && direct.sprite)
                return direct;

            SpriteRenderer fallback = null;
            foreach (SpriteRenderer candidate in GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (!candidate || candidate == direct)
                    continue;

                // Health strips and blank compatibility renderers should not
                // become the authority for upright-facing flips.
                if (candidate.sprite && !candidate.name.Contains("Health"))
                    return candidate;

                if (!fallback && candidate.sprite)
                    fallback = candidate;
            }

            return fallback ? fallback : direct;
        }

        void ApplyEe5PhysicsProfile()
        {
            if (!body)
                return;

            bool isMelee = ResolveMeleeRole();

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

            // The purple EE5 melee prefab is authored with a mirrored root,
            // not a SpriteRenderer flip. Restoring it here keeps the sword,
            // health display, and wake handoff on the same coordinate basis
            // even when an older scene instance lost its prefab override.
            float authoredScaleX = isMelee
                ? Ee5SliceProfile.EnemyMeleeRootScaleX
                : Ee5SliceProfile.EnemyGunnerRootScaleX;
            Vector3 rootScale = transform.localScale;
            rootScale.x = Mathf.Sign(authoredScaleX) * Mathf.Max(0.0001f, Mathf.Abs(rootScale.x));
            transform.localScale = rootScale;

            // Enforce the role contract at runtime as a compatibility bridge.
            // FlightTest is intentionally allowed to keep dirty prefab/scene
            // values, but those values must not make the playable slice drift
            // from the EE5 close-bruiser and enemyGun references.
            //
            // The serialized movement enum is intentionally not trusted here:
            // preserved realScene instances can carry an old Chase/Wander value
            // after their component graph has been repaired. EE5's role is
            // structural—EnemyWeapon means persistent wandering gunner, while
            // EnemyContactDamage means close-bruiser chase—so make that choice
            // once before the state machine evaluates its first fixed step.
            movementMode = isMelee
                ? EnemyMovementMode.Chase
                : EnemyMovementMode.Wander;
            chaseSpeed = isMelee
                ? Ee5SliceProfile.EnemyMeleeChaseSpeed
                : Ee5SliceProfile.EnemyGunnerChaseSpeed;
            faceTurnSpeed = isMelee
                ? Ee5SliceProfile.EnemyMeleeFaceTurnSpeed
                : Ee5SliceProfile.EnemyGunnerFaceTurnSpeed;

            // EE5's ranged gunner aims from local negative X. The purple
            // melee hunter keeps the authored controller basis and relies on
            // its mirrored root scale above. Derive this role flag here so
            // an older prefab cannot leave the sprite presentation inverted.
            forwardIsLocalNegativeX = !isMelee;
            contactDamageRange = isMelee
                ? Ee5SliceProfile.EnemyMeleeContactRange
                : 0f;
            attackRecoveryDuration = isMelee
                ? Ee5SliceProfile.EnemyMeleeAttackRecoveryDuration
                : 0f;
            attackFacingRefreshDegrees = isMelee
                ? Ee5SliceProfile.EnemyMeleeAttackFacingRefreshDegrees
                : 0f;
            if (bodyCollider)
            {
                ConfigureEe5Hitbox(isMelee);
            }
        }

        void OnDrawGizmosSelected()
        {
            BoxCollider2D hitbox = GetComponent<BoxCollider2D>();
            if (!hitbox)
                return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = IsMelee
                ? new Color(1f, 0.22f, 0.86f, 0.85f)
                : new Color(0.05f, 1f, 0.16f, 0.85f);
            Gizmos.DrawWireCube(hitbox.offset, hitbox.size);
            if (IsMelee)
            {
                Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.32f);
                Gizmos.DrawWireSphere(Vector3.zero, ContactDamageReach);
            }
            Gizmos.matrix = previousMatrix;
        }

        void ConfigureEe5Hitbox(bool isMelee)
        {
            // The imported EE6 prefabs were originally built with centered
            // circles. EE5's enemyFast and enemyGun use small, offset boxes
            // aligned to the actual sprite silhouette. Rebuild that shape at
            // runtime as a compatibility bridge for existing scene instances;
            // the editor builder writes the same BoxCollider2D for new scenes.
            BoxCollider2D authoredBox = GetComponent<BoxCollider2D>();
            if (!authoredBox)
                authoredBox = gameObject.AddComponent<BoxCollider2D>();

            authoredBox.offset = isMelee
                ? Ee5SliceProfile.EnemyMeleeHitboxOffset
                : Ee5SliceProfile.EnemyGunnerHitboxOffset;
            authoredBox.size = isMelee
                ? Ee5SliceProfile.EnemyMeleeHitboxSize
                : Ee5SliceProfile.EnemyGunnerHitboxSize;
            authoredBox.isTrigger = isMelee
                && Ee5SliceProfile.EnemyMeleeUsesTriggerBody;

            foreach (Collider2D collider in GetComponents<Collider2D>())
            {
                if (collider != authoredBox)
                    collider.enabled = false;
            }

            bodyCollider = authoredBox;
        }

        bool ResolveMeleeRole()
        {
            if (roleResolved)
                return meleeRole;

            weapon = GetComponent<EnemyWeapon>();
            contactDamage = GetComponent<EnemyContactDamage>();
            meleeRole = weapon == null
                && (movementMode == EnemyMovementMode.Chase || contactDamage != null);
            roleResolved = true;
            return meleeRole;
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
            }
        }

        void EvaluateCombatState()
        {
            // Both sides of the encounter are simulated in FixedUpdate. Read
            // the player's Rigidbody position here so an interpolated render
            // transform cannot make the melee state chatter at its stop radius.
            float distance = Vector2.Distance(body.position, target.PhysicsPosition);
            UpdateNearMiss(distance);
            UpdateWakeSignal(distance);

            float behaviorRange = Mathf.Max(detectionRange, GetWakeSignalDistance());
            // EE5's EnemyAI has no post-wake leash. Once the intro commits,
            // the enemy remains an authored room threat until death or reload.
            if (State == EnemyState.Dormant && distance > behaviorRange)
                return;

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
                wakeTimer += Time.fixedDeltaTime;
                // EE5 commits to the alert once its wake line has armed. A
                // later wall crossing must not cancel the authored scream
                // beat halfway through.
                if (wakeTimer < wakeTotalDuration)
                    return;
            }

            if (State == EnemyState.Attacking && attackRecoveryRemaining > 0f)
            {
                // A confirmed melee hit owns this short recovery beat. Keep
                // the attack state latched while the player knockback travels
                // outward; otherwise the distance threshold can make the
                // hunter chase and brake again before the hit is readable.
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

            // Ranged enemies use their authored attack range as a state
            // contract. The melee hunter enters attack on the actual collider
            // contact, matching EE5's OnTriggerStay/OnCollisionStay contract.
            // Its wider exit band still prevents state chatter after knockback.
            bool meleeContact = IsMelee && IsWithinMeleeContact(target);
            float attackStartRange = Mathf.Max(0f, attackRange);
            float attackStopRange = Mathf.Max(
                IsMelee ? 0f : attackStartRange,
                Mathf.Max(0f, attackExitRange));
            bool stayInAttack = State == EnemyState.Attacking
                && distance <= attackStopRange;
            SetState(
                meleeContact || stayInAttack || (!IsMelee && distance <= attackStartRange)
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
                WakeSignalEnd = target ? GetWakeSignalTargetPoint() : PhysicsAnchorPosition;
                wakeSignalCharge = wakeSignalChargeDuration;
                return;
            }

            float signalDistance = GetWakeSignalDistance();
            WakeSignalVisible = distance <= signalDistance;
            WakeSignalEnd = target ? GetWakeSignalTargetPoint() : PhysicsAnchorPosition;

            if (!WakeSignalVisible)
            {
                WakeSignalHasClearSight = false;
                wakeSignalCharge = Mathf.MoveTowards(
                    wakeSignalCharge,
                    0f,
                    wakeSignalChargeDecay * Time.fixedDeltaTime);
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
                    Mathf.Max(0f, chargeSpeed) * Time.fixedDeltaTime);
            }
            else
            {
                wakeSignalCharge = Mathf.MoveTowards(
                    wakeSignalCharge,
                    0f,
                    wakeSignalChargeDecay * Time.fixedDeltaTime);
            }
        }

        void ClearWakeSignal()
        {
            WakeSignalVisible = false;
            WakeSignalHasClearSight = false;
            WakeSignalEnd = PhysicsAnchorPosition;
            wakeSignalCharge = 0f;
        }

        float GetWakeSignalDistance() =>
            wakeDistance * Mathf.Max(1f, wakeSignalDistanceMultiplier);

        Vector2 GetWakeSignalTargetPoint()
        {
            if (!target)
                return PhysicsAnchorPosition;

            Collider2D targetCollider = target.GetComponent<Collider2D>();
            return targetCollider ? targetCollider.bounds.center : target.PhysicsPosition;
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

            if (!target)
                target = FindFirstObjectByType<PlayerCharacter>();

            if (!target || !bodyCollider)
            {
                body.linearVelocity = Vector2.zero;
                return;
            }

            if (!target.CanReceiveGameplayInput)
            {
                ClearWakeSignal();
                SetState(EnemyState.Dormant);
                body.linearVelocity = Vector2.zero;
                return;
            }

            // Contact damage, the attack-facing lock, and navigation all run
            // on the physics clock. Decaying recovery here prevents a render
            // frame-rate change from altering the distance at which the melee
            // hunter resumes pursuit.
            if (State == EnemyState.Attacking && attackRecoveryRemaining > 0f)
            {
                attackRecoveryRemaining = Mathf.Max(
                    0f,
                    attackRecoveryRemaining - Time.fixedDeltaTime);
            }

            EvaluateCombatState();

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

            if (State == EnemyState.Attacking && attackRecoveryRemaining > 0f)
            {
                // A confirmed strike owns this short presentation window. Do
                // not let the player's knockback make the hunter re-aim every
                // fixed tick; that angle tug-of-war is the popcorn/jitter that
                // the original EE5 contact beat never showed.
                FaceTarget(attackFacingDirection);
                body.linearVelocity = Vector2.zero;
                return;
            }

            // EE5 keeps facing the player while a melee hunter is in its
            // contact-attack state. Without this branch the body stops moving
            // at the attack radius but its sprite remains aimed at the last
            // chase direction, which reads as a backwards enemy.
            if (State == EnemyState.Attacking)
            {
                Vector2 toTarget = target.PhysicsPosition - body.position;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    Vector2 targetDirection = toTarget.normalized;
                    if (!IsMelee || ShouldRefreshAttackFacing(targetDirection))
                        attackFacingDirection = targetDirection;

                    FaceTarget(IsMelee ? attackFacingDirection : targetDirection);
                }
            }

            body.linearVelocity = Vector2.zero;
        }

        void HandleChaseMovement()
        {
            body.linearVelocity = Vector2.zero;
            Vector2 toTarget = target.PhysicsPosition - body.position;
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
            Vector2 toTarget = target.PhysicsPosition - body.position;
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
            Vector2 toTarget = target.PhysicsPosition - body.position;
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

            // Make the defeat authoritative before external systems observe it.
            // EncounterController, EnergyKey, and trigger callbacks can all
            // react to Defeated in the same physics step; leaving a collider
            // enabled until after the event creates a one-frame dead-but-solid
            // enemy that can double-hit the player or block the key handoff.
            foreach (Collider2D collider in GetComponentsInChildren<Collider2D>(true))
                collider.enabled = false;

            Defeated?.Invoke(this);
        }

        /// <summary>
        /// Contact damage marks a close pass as a hit so the same enemy cannot
        /// award both a collision and a near-miss credit.
        /// </summary>
        public void RegisterPlayerContact() => touchedPlayerDuringNearPass = true;

        /// <summary>
        /// Holds a melee hunter in its attack state for the authored recovery
        /// beat after a successful contact hit. This is behavior timing, not a
        /// second damage path; EnemyContactDamage remains the damage authority.
        /// </summary>
        public void RegisterAttackImpact() => RegisterAttackImpact(Vector2.zero);

        public void RegisterAttackImpact(Vector2 direction)
        {
            if (!IsMelee || attackRecoveryDuration <= 0f)
                return;

            if (direction.sqrMagnitude > 0.001f)
                attackFacingDirection = direction.normalized;

            attackRecoveryRemaining = Mathf.Max(
                attackRecoveryRemaining,
                attackRecoveryDuration);
        }

        bool ShouldRefreshAttackFacing(Vector2 targetDirection)
        {
            if (!IsMelee || targetDirection.sqrMagnitude <= 0.0001f)
                return true;

            if (attackFacingDirection.sqrMagnitude <= 0.0001f)
                return true;

            return Vector2.Angle(
                attackFacingDirection,
                targetDirection) >= Mathf.Max(0f, attackFacingRefreshDegrees);
        }

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
            if (nextState != EnemyState.Attacking)
                attackRecoveryRemaining = 0f;
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
            else if (nextState == EnemyState.Attacking)
            {
                Vector2 toTarget = target
                    ? target.PhysicsPosition - body.position
                    : Vector2.zero;
                if (toTarget.sqrMagnitude > 0.001f)
                    attackFacingDirection = toTarget.normalized;
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
                    // EE5's close bruiser is a solid kinematic body. Keep a
                    // small separation buffer for that restored contract so
                    // MovePosition cannot tunnel through the dynamic player.
                    // Trigger-based legacy experiments may still cross and
                    // rely on the collider-pair contact fallback instead.
                    if (!IsMelee || !UsesTriggerContactBody)
                    {
                        allowedDistance = Mathf.Min(
                            allowedDistance,
                            Mathf.Max(0f, hit.distance - targetBuffer));
                    }
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
                ? (target.PhysicsPosition - body.position).normalized
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
                ? (target.PhysicsPosition - body.position).normalized
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
            float angleError = Mathf.DeltaAngle(body.rotation, targetAngle);
            // Hold inside the deadband instead of copying a moving target angle
            // directly into the body. The latter looks like sprite jitter when
            // the player hovers beside a melee hunter; EE5's turn response stays
            // damped all the way to rest.
            if (Mathf.Abs(angleError) > Ee5SliceProfile.EnemyFacingDeadbandDegrees)
                body.MoveRotation(Mathf.LerpAngle(body.rotation, targetAngle, turnT));

            if (spriteRenderer && keepSpriteUpright)
            {
                float signedAngle = Mathf.DeltaAngle(0f, targetAngle);
                float absoluteAngle = Mathf.Abs(signedAngle);
                float hysteresis = Ee5SliceProfile.EnemyFacingFlipHysteresisDegrees;
                if (!spriteFlippedUpright && absoluteAngle >= 90f + hysteresis)
                    spriteFlippedUpright = true;
                else if (spriteFlippedUpright && absoluteAngle <= 90f - hysteresis)
                    spriteFlippedUpright = false;

                spriteRenderer.flipY = spriteFlippedUpright;
            }
        }
    }
}

using System;
using UnityEngine;
using ExtraterrestrialExhaust.Enemy;
using ExtraterrestrialExhaust.Player;

namespace ExtraterrestrialExhaust.Core
{
    public enum EnergyKeyState
    {
        AttachedToEnemy,
        OrbitingPlayer,
        FollowingPlayer,
        FlyingToGate,
        Consumed
    }

    /// <summary>
    /// EE5-style objective key: orbit an encounter carrier, release when that
    /// carrier is defeated, orbit the player, follow the player, then fly into
    /// the gate. Other enemies remain active pressure while the carrier
    /// objective progresses.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class EnergyKey : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] bool enforceEe5Profile = true;
        [SerializeField] EncounterController requiredEncounter;
        [SerializeField] EnemyController enemyTarget;
        [SerializeField] EnergyGate targetGate;

        [Header("Enemy Orbit")]
        [SerializeField] Vector3 enemyOffset = Ee5SliceProfile.EnergyKeyEnemyOffset;
        [SerializeField, Min(0f)] float enemyOrbitRadius = Ee5SliceProfile.EnergyKeyEnemyOrbitRadius;
        [SerializeField, Min(0f)] float enemyOrbitSpeed = Ee5SliceProfile.EnergyKeyEnemyOrbitSpeed;
        [SerializeField, Min(0f)] float enemyOrbitSharpness = Ee5SliceProfile.EnergyKeyEnemyOrbitSharpness;

        [Header("Player Orbit")]
        [SerializeField, Min(0f)] float orbitRadiusX = Ee5SliceProfile.EnergyKeyOrbitRadiusX;
        [SerializeField, Min(0f)] float orbitRadiusY = Ee5SliceProfile.EnergyKeyOrbitRadiusY;
        [SerializeField, Min(0f)] float orbitSpeed = Ee5SliceProfile.EnergyKeyOrbitSpeed;
        [SerializeField, Min(0f)] float orbitSharpness = Ee5SliceProfile.EnergyKeyOrbitSharpness;
        [SerializeField] float orbitRotationSpeed;
        [SerializeField, Min(0f)] float radiusEase = Ee5SliceProfile.EnergyKeyRadiusEase;
        [SerializeField, Min(0f)] float centerFollowSharpness = Ee5SliceProfile.EnergyKeyCenterFollowSharpness;

        [Header("Collection")]
        [SerializeField, Min(0f)] float collectDistance = Ee5SliceProfile.EnergyKeyCollectDistance;
        [SerializeField, Min(0f)] float collectionArmDelay;
        [SerializeField, Min(0f)] float minRadiusBeforeCollect;
        [SerializeField] Vector3 playerOffset = Ee5SliceProfile.EnergyKeyPlayerOffset;
        [SerializeField, Min(0f)] float playerFollowSharpness = Ee5SliceProfile.EnergyKeyPlayerFollowSharpness;

        [Header("Gate")]
        [SerializeField, Min(0f)] float gateUnlockRange = Ee5SliceProfile.EnergyKeyGateUnlockRange;
        [SerializeField, Min(0f)] float gateFlySpeed = Ee5SliceProfile.EnergyKeyGateFlySpeed;

        [Header("Visual")]
        [SerializeField] Transform visual;
        [SerializeField, Min(0f)] float rotateSpeed = 180f;
        [SerializeField, Min(0f)] float pulseSpeed = 8f;
        [SerializeField, Range(0f, 0.5f)] float pulseAmount = 0.08f;
        [SerializeField] Color lockedColor = new Color(0.45f, 0.45f, 0.5f, 0.55f);
        [SerializeField] Color availableColor = new Color(1f, 0.85f, 0.15f, 1f);
        [SerializeField, Min(0f)] float releasePulseDuration = 0.28f;
        [SerializeField, Min(1f)] float releasePulseScale = 1.28f;

        EnergyKeyState state = EnergyKeyState.AttachedToEnemy;
        Rigidbody2D body;
        Collider2D keyCollider;
        SpriteRenderer spriteRenderer;
        LineRenderer line;
        PlayerCharacter player;
        Vector3 baseScale;
        Quaternion baseVisualRotation;
        Vector2 orbitCenter;
        float phase;
        float currentRadiusX;
        float currentRadiusY;
        float releasedAtTime;
        float releasePulseRemaining;
        EnemyController subscribedEnemyTarget;
        EncounterController subscribedRequiredEncounter;

        public EnergyKeyState State => state;
        public bool IsAvailable => state == EnergyKeyState.OrbitingPlayer;
        // Consumed is still part of the collected phase: the key is destroyed only
        // after it has triggered the gate, so objective listeners must not rewind
        // to CLEAR ENCOUNTER during that handoff.
        public bool IsCollected => state == EnergyKeyState.FollowingPlayer ||
                                   state == EnergyKeyState.FlyingToGate ||
                                   state == EnergyKeyState.Consumed;
        public PlayerCharacter CurrentPlayer => player;
        public EnemyController EnemyTarget => enemyTarget;
        public EnergyGate TargetGate => targetGate;
        /// <summary>
        /// World-space anchor for presentation systems. The gameplay root stays
        /// centered for trigger/range checks while the imported key artwork gets
        /// its EE5 canvas correction on the child visual.
        /// </summary>
        public Vector3 VisualPosition => visual ? visual.position : transform.position;
        /// <summary>
        /// Stable world-space anchor for gameplay-linked effects. Do not use the
        /// corrected child visual for tethers or sockets: its imported canvas
        /// offset intentionally rotates with the artwork and would make those
        /// effects appear to jitter around the key.
        /// </summary>
        public Vector3 GameplayPosition => body ? (Vector3)body.position : transform.position;
        /// <summary>
        /// Interpolated root pose for render-time effects. The child artwork
        /// carries a deliberate source-canvas correction, while the root pose
        /// remains centered and visually aligned with the Rigidbody2D's
        /// interpolation.
        /// </summary>
        public Vector3 PresentationPosition => transform.position;
        public event Action<EnergyKeyState, EnergyKeyState> StateChanged;

        void Reset() => GetComponent<Collider2D>().isTrigger = true;

        void Awake()
        {
            if (enforceEe5Profile)
                ApplyEe5Profile();

            ResolveCarrierReferences();

            body = GetComponent<Rigidbody2D>();
            keyCollider = GetComponent<Collider2D>();
            if (!visual)
                visual = transform.Find("Key Visual");
            if (!visual)
                visual = transform;

            // Earlier FlightTest builders placed a square debug outline on
            // the visual root to stand in for the key artwork. EE5's keyFollow
            // prefab presents the authored key sprite directly; keeping the
            // legacy line enabled makes the objective read like a placeholder
            // and exaggerates the source-canvas offset during orbit motion.
            if (visual != transform)
            {
                LineRenderer legacyVisualOutline = visual.GetComponent<LineRenderer>();
                if (legacyVisualOutline)
                    legacyVisualOutline.enabled = false;
            }

            if (enforceEe5Profile && visual != transform)
            {
                // The imported key sheet has a non-centered source canvas. Keep
                // the visual correction next to the runtime profile so prefab
                // instances and older hand-built scenes share the same visible
                // center without moving the gameplay trigger.
                visual.localPosition = Ee5SliceProfile.EnergyKeyVisualOffset;
                visual.localScale = Vector3.one * Ee5SliceProfile.EnergyKeyVisualScale;
            }

            spriteRenderer = visual.GetComponent<SpriteRenderer>();
            if (!spriteRenderer)
                spriteRenderer = GetComponent<SpriteRenderer>();
            line = GetComponent<LineRenderer>();
            baseScale = visual.localScale;
            baseVisualRotation = visual.localRotation;

            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            // EE5's scene-authored key is a kinematic body with interpolation
            // enabled. This component moves on the fixed step, so retaining
            // that transport setting is what keeps the delicate orbit and
            // gate-flight motion smooth at render rates above the physics tick.
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.simulated = true;
            keyCollider.isTrigger = true;
            ResolvePlayer();
            UpdateAvailabilityVisual();
        }

        void ApplyEe5Profile()
        {
            enemyOffset = Ee5SliceProfile.EnergyKeyEnemyOffset;
            enemyOrbitRadius = Ee5SliceProfile.EnergyKeyEnemyOrbitRadius;
            enemyOrbitSpeed = Ee5SliceProfile.EnergyKeyEnemyOrbitSpeed;
            enemyOrbitSharpness = Ee5SliceProfile.EnergyKeyEnemyOrbitSharpness;
            orbitRadiusX = Ee5SliceProfile.EnergyKeyOrbitRadiusX;
            orbitRadiusY = Ee5SliceProfile.EnergyKeyOrbitRadiusY;
            orbitSpeed = Ee5SliceProfile.EnergyKeyOrbitSpeed;
            orbitSharpness = Ee5SliceProfile.EnergyKeyOrbitSharpness;
            orbitRotationSpeed = 0f;
            radiusEase = Ee5SliceProfile.EnergyKeyRadiusEase;
            centerFollowSharpness = Ee5SliceProfile.EnergyKeyCenterFollowSharpness;
            collectDistance = Ee5SliceProfile.EnergyKeyCollectDistance;
            playerOffset = Ee5SliceProfile.EnergyKeyPlayerOffset;
            playerFollowSharpness = Ee5SliceProfile.EnergyKeyPlayerFollowSharpness;
            gateUnlockRange = Ee5SliceProfile.EnergyKeyGateUnlockRange;
            gateFlySpeed = Ee5SliceProfile.EnergyKeyGateFlySpeed;
        }

        void OnEnable()
        {
            RefreshCarrierSubscriptions();

            // A scene can be enabled after its carrier has already been
            // defeated. The event path is authoritative, but this keeps
            // prefab-preview and additive-scene loading deterministic.
            TryReleaseFromCarrier();
        }

        void OnDisable()
        {
            UnsubscribeFromCarrier();
        }

        void Update()
        {
            ResolvePlayer();
            ResolveCarrierReferences();
            RefreshCarrierSubscriptions();

            if (state != EnergyKeyState.Consumed)
            {
                // Keep presentation off the interpolated transport root. This
                // prevents the sprite rotation/pulse from making the key's
                // trigger appear to jitter during delicate handoffs.
                bool hasSeparateVisual = visual && visual != transform;
                if (hasSeparateVisual)
                {
                    // Rebuild the authored pose from absolute time instead
                    // of accumulating floating-point rotation deltas. The
                    // gameplay root remains a fixed-step transport body while
                    // only this child receives the presentation spin.
                    visual.localRotation = baseVisualRotation
                        * Quaternion.Euler(0f, 0f, Time.time * rotateSpeed);
                }

                float pulse = hasSeparateVisual
                    ? 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount
                    : 1f;
                if (releasePulseRemaining > 0f)
                {
                    releasePulseRemaining = Mathf.Max(
                        0f,
                        releasePulseRemaining - Time.deltaTime);
                    float releaseT = releasePulseDuration > 0f
                        ? 1f - releasePulseRemaining / releasePulseDuration
                        : 1f;
                    float releaseScale = Mathf.Lerp(
                        releasePulseScale,
                        1f,
                        Mathf.SmoothStep(0f, 1f, releaseT));
                    pulse *= releaseScale;
                }

                if (hasSeparateVisual)
                    visual.localScale = baseScale * pulse;
            }

            switch (state)
            {
                case EnergyKeyState.AttachedToEnemy:
                    // Keep the poll as a recovery path for references that are
                    // resolved late; normal gameplay releases from the carrier
                    // event so the objective handoff has no frame of drift.
                    TryReleaseFromCarrier();
                    break;
            }
        }

        void FixedUpdate()
        {
            // The key is a kinematic Rigidbody2D. Keeping all physical motion
            // on the fixed step prevents orbit and gate-flight jitter when the
            // render frame rate differs from the physics rate.
            switch (state)
            {
                case EnergyKeyState.AttachedToEnemy:
                    FollowEnemyOrbit();
                    break;
                case EnergyKeyState.OrbitingPlayer:
                    FollowPlayerOrbit();
                    break;
                case EnergyKeyState.FollowingPlayer:
                    FollowPlayer();
                    // Keep the handoff decision on the same physics clock as
                    // the carried key and player anchors. Checking this from
                    // Update mixed a fixed-step key pose with a render-step
                    // gate pose, which could make the flight-to-gate state
                    // begin one frame early or late at the unlock threshold.
                    CheckGate();
                    break;
                case EnergyKeyState.FlyingToGate:
                    FlyToGate();
                    break;
            }
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            TryCollect(other);
        }

        void OnTriggerStay2D(Collider2D other)
        {
            TryCollect(other);
        }

        void FollowEnemyOrbit()
        {
            if (!enemyTarget)
                return;

            phase += enemyOrbitSpeed * Time.fixedDeltaTime;
            Vector3 center = (Vector3)enemyTarget.PhysicsPosition + enemyOffset;
            Vector3 offset = new Vector3(Mathf.Cos(phase), Mathf.Sin(phase), 0f) * enemyOrbitRadius;
            body.MovePosition(Vector2.Lerp(
                body.position,
                center + offset,
                1f - Mathf.Exp(-enemyOrbitSharpness * Time.fixedDeltaTime)));
        }

        void FollowPlayerOrbit()
        {
            if (!player)
                return;

            float centerT = 1f - Mathf.Exp(-centerFollowSharpness * Time.fixedDeltaTime);
            orbitCenter = Vector2.Lerp(orbitCenter, player.PhysicsPosition, centerT);
            phase += orbitSpeed * Time.fixedDeltaTime;
            currentRadiusX = Mathf.Lerp(currentRadiusX, orbitRadiusX, radiusEase * Time.fixedDeltaTime);
            currentRadiusY = Mathf.Lerp(currentRadiusY, orbitRadiusY, radiusEase * Time.fixedDeltaTime);

            Vector2 offset = new Vector2(
                Mathf.Cos(phase) * currentRadiusX,
                Mathf.Sin(phase) * currentRadiusY);

            if (orbitRotationSpeed != 0f)
            {
                float rotation = Time.time * orbitRotationSpeed;
                float cos = Mathf.Cos(rotation);
                float sin = Mathf.Sin(rotation);
                offset = new Vector2(
                    offset.x * cos - offset.y * sin,
                    offset.x * sin + offset.y * cos);
            }

            Vector2 target = orbitCenter + offset;
            float orbitT = 1f - Mathf.Exp(-orbitSharpness * Time.fixedDeltaTime);
            body.MovePosition(Vector2.Lerp(body.position, target, orbitT));
        }

        void FollowPlayer()
        {
            if (player && player.CanReceiveGameplayInput)
            {
                Vector3 target = (Vector3)player.PhysicsPosition + playerOffset;
                body.MovePosition(Vector2.Lerp(
                    body.position,
                    target,
                    1f - Mathf.Exp(-playerFollowSharpness * Time.fixedDeltaTime)));
            }
        }

        void CheckGate()
        {
            // The key should not complete the objective behind the player's
            // back while a capture, death, or other scripted state owns the
            // craft. The player contract is the single authority for that
            // handoff eligibility.
            if (!player || !player.CanReceiveGameplayInput || !targetGate)
                return;

            if (Vector2.Distance(player.PhysicsPosition, targetGate.KeyTarget.position) <= gateUnlockRange)
            {
                SetState(EnergyKeyState.FlyingToGate);
                keyCollider.enabled = false;
                UpdateAvailabilityVisual();
            }
        }

        void FlyToGate()
        {
            if (!targetGate)
                return;

            Vector2 gateTarget = targetGate.KeyTarget.position;
            Vector2 nextPosition = Vector2.MoveTowards(
                body.position,
                gateTarget,
                gateFlySpeed * Time.fixedDeltaTime);
            body.MovePosition(nextPosition);

            if (Vector2.Distance(nextPosition, gateTarget) > 0.15f)
                return;

            targetGate.DisableGate();
            FindFirstObjectByType<ScoreSystem>()?.Award(ScoreReason.GateDeactivated);
            SetState(EnergyKeyState.Consumed);
            Destroy(gameObject);
        }

        void ReleaseFromEnemy()
        {
            SetState(EnergyKeyState.OrbitingPlayer);
            orbitCenter = player ? player.PhysicsPosition : body.position;
            releasedAtTime = Time.time;

            Vector2 fromPlayer = body.position - orbitCenter;
            if (fromPlayer.sqrMagnitude > 0.001f)
            {
                // Preserve the release point on the ellipse, then let the
                // normal radius easing grow it into the authored EE5 orbit.
                float ellipseScale = Mathf.Sqrt(
                    Mathf.Pow(fromPlayer.x / Mathf.Max(0.001f, orbitRadiusX), 2f)
                    + Mathf.Pow(fromPlayer.y / Mathf.Max(0.001f, orbitRadiusY), 2f));
                currentRadiusX = orbitRadiusX * ellipseScale;
                currentRadiusY = orbitRadiusY * ellipseScale;
                phase = Mathf.Atan2(
                    fromPlayer.y / Mathf.Max(0.001f, orbitRadiusY),
                    fromPlayer.x / Mathf.Max(0.001f, orbitRadiusX));
            }
            else
            {
                currentRadiusX = orbitRadiusX;
                currentRadiusY = orbitRadiusY;
                phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            }

            keyCollider.enabled = true;
            UpdateAvailabilityVisual();
        }

        void TryCollect(Collider2D other)
        {
            if (state != EnergyKeyState.OrbitingPlayer)
                return;

            PlayerCharacter otherPlayer = other.GetComponentInParent<PlayerCharacter>();
            if (!otherPlayer || !player || !otherPlayer.CanReceiveGameplayInput)
                return;

            // Use the collider's owning player for the proximity check. The
            // resolved field normally points at the same object, but keeping
            // the event-local identity correct makes this reusable in tests
            // and additive scenes with more than one player candidate.
            if (Time.time - releasedAtTime < collectionArmDelay)
                return;

            if (currentRadiusX < minRadiusBeforeCollect
                || currentRadiusY < minRadiusBeforeCollect)
                return;

            if (Vector2.Distance(body.position, otherPlayer.PhysicsPosition) > collectDistance)
                return;

            player = otherPlayer;
            SetState(EnergyKeyState.FollowingPlayer);
            keyCollider.enabled = false;
            FindFirstObjectByType<ScoreSystem>()?.Award(ScoreReason.ObjectiveCollected);
            UpdateAvailabilityVisual();
        }

        bool CanReleaseFromEnemy()
        {
            if (enemyTarget)
                return enemyTarget.State == EnemyState.Defeated
                    || (requiredEncounter && requiredEncounter.IsComplete);

            // A missing carrier reference is not permission to release at
            // scene start. Old hand-authored scenes can resolve references a
            // frame late; holding the attached state keeps the objective from
            // silently teleporting until the carrier is known or the authored
            // encounter explicitly completes.
            return requiredEncounter && requiredEncounter.IsComplete;
        }

        void HandleEnemyDefeated(EnemyController defeatedEnemy)
        {
            TryReleaseFromCarrier();
        }

        void HandleEncounterCompleted()
        {
            TryReleaseFromCarrier();
        }

        void TryReleaseFromCarrier()
        {
            if (state == EnergyKeyState.AttachedToEnemy && CanReleaseFromEnemy())
                ReleaseFromEnemy();
        }

        void ResolvePlayer()
        {
            if (!player)
                player = FindFirstObjectByType<PlayerCharacter>();
        }

        void ResolveCarrierReferences()
        {
            if (!requiredEncounter)
                requiredEncounter = FindFirstObjectByType<EncounterController>();

            if (enemyTarget)
                return;

            EnemyController[] candidates = FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
            if (candidates == null || candidates.Length == 0)
                return;

            Vector2 keyPosition = body ? body.position : (Vector2)transform.position;
            EnemyController nearestRanged = null;
            float nearestRangedDistance = float.PositiveInfinity;
            EnemyController nearestFallback = null;
            float nearestFallbackDistance = float.PositiveInfinity;
            for (int i = 0; i < candidates.Length; i++)
            {
                EnemyController candidate = candidates[i];
                if (!candidate)
                    continue;

                float distance = (candidate.PhysicsPosition - keyPosition).sqrMagnitude;
                if (distance < nearestFallbackDistance)
                {
                    nearestFallbackDistance = distance;
                    nearestFallback = candidate;
                }

                // EE5's objective carrier is the ranged gunner. Prefer that
                // role during recovery instead of letting scene enumeration
                // order or a stale spawn position silently bind the key to the
                // melee hunter.
                if (candidate.GetComponent<EnemyWeapon>()
                    && distance < nearestRangedDistance)
                {
                    nearestRangedDistance = distance;
                    nearestRanged = candidate;
                }
            }

            enemyTarget = nearestRanged ? nearestRanged : nearestFallback;
        }

        void RefreshCarrierSubscriptions()
        {
            if (subscribedEnemyTarget != enemyTarget)
            {
                if (subscribedEnemyTarget)
                    subscribedEnemyTarget.Defeated -= HandleEnemyDefeated;

                subscribedEnemyTarget = enemyTarget;
                if (subscribedEnemyTarget)
                    subscribedEnemyTarget.Defeated += HandleEnemyDefeated;
            }

            if (subscribedRequiredEncounter != requiredEncounter)
            {
                if (subscribedRequiredEncounter)
                    subscribedRequiredEncounter.Completed -= HandleEncounterCompleted;

                subscribedRequiredEncounter = requiredEncounter;
                if (subscribedRequiredEncounter)
                    subscribedRequiredEncounter.Completed += HandleEncounterCompleted;
            }
        }

        void UnsubscribeFromCarrier()
        {
            if (subscribedEnemyTarget)
                subscribedEnemyTarget.Defeated -= HandleEnemyDefeated;
            if (subscribedRequiredEncounter)
                subscribedRequiredEncounter.Completed -= HandleEncounterCompleted;

            subscribedEnemyTarget = null;
            subscribedRequiredEncounter = null;
        }

        void UpdateAvailabilityVisual()
        {
            bool available = state == EnergyKeyState.OrbitingPlayer || state == EnergyKeyState.FollowingPlayer;
            Color color = available ? availableColor : lockedColor;
            if (keyCollider)
                keyCollider.enabled = state == EnergyKeyState.OrbitingPlayer;
            if (spriteRenderer)
                spriteRenderer.color = color;
            if (line)
            {
                line.startColor = color;
                line.endColor = color;
            }
        }

        void SetState(EnergyKeyState nextState)
        {
            if (state == nextState)
                return;

            EnergyKeyState previousState = state;
            state = nextState;
            if (nextState == EnergyKeyState.OrbitingPlayer)
                releasePulseRemaining = releasePulseDuration;
            StateChanged?.Invoke(previousState, nextState);
        }
    }
}

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
        [SerializeField] EncounterController requiredEncounter;
        [SerializeField] EnemyController enemyTarget;
        [SerializeField] EnergyGate targetGate;

        [Header("Enemy Orbit")]
        [SerializeField] Vector3 enemyOffset = Ee5SliceProfile.EnergyKeyEnemyOffset;
        [SerializeField, Min(0f)] float enemyOrbitRadius = Ee5SliceProfile.EnergyKeyEnemyOrbitRadius;
        [SerializeField, Min(0f)] float enemyOrbitSpeed = Ee5SliceProfile.EnergyKeyEnemyOrbitSpeed;
        [SerializeField, Min(0f)] float enemyOrbitSharpness = Ee5SliceProfile.EnergyKeyEnemyOrbitSharpness;

        [Header("Player Orbit")]
        [SerializeField, Min(0f)] float orbitRadiusX = 4.4f;
        [SerializeField, Min(0f)] float orbitRadiusY = 1.9f;
        [SerializeField, Min(0f)] float orbitSpeed = 2f;
        [SerializeField, Min(0f)] float orbitSharpness = 8f;
        [SerializeField] float orbitRotationSpeed;
        [SerializeField, Min(0f)] float radiusEase = 3.5f;
        [SerializeField, Min(0f)] float centerFollowSharpness = 5.5f;

        [Header("Collection")]
        [SerializeField, Min(0f)] float collectDistance = Ee5SliceProfile.EnergyKeyCollectDistance;
        [SerializeField, Min(0f)] float collectionArmDelay;
        [SerializeField, Min(0f)] float minRadiusBeforeCollect;
        [SerializeField] Vector3 playerOffset = Ee5SliceProfile.EnergyKeyPlayerOffset;
        [SerializeField, Min(0f)] float playerFollowSharpness = Ee5SliceProfile.EnergyKeyPlayerFollowSharpness;

        [Header("Gate")]
        [SerializeField, Min(0f)] float gateUnlockRange = Ee5SliceProfile.EnergyKeyGateUnlockRange;
        [SerializeField, Min(0f)] float gateFlySpeed = 14f;

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
        Vector2 orbitCenter;
        float phase;
        float currentRadiusX;
        float currentRadiusY;
        float releasedAtTime;
        float releasePulseRemaining;

        public EnergyKeyState State => state;
        public bool IsAvailable => state == EnergyKeyState.OrbitingPlayer;
        public bool IsCollected => state == EnergyKeyState.FollowingPlayer || state == EnergyKeyState.FlyingToGate;
        public PlayerCharacter CurrentPlayer => player;
        public EnemyController EnemyTarget => enemyTarget;
        public EnergyGate TargetGate => targetGate;
        public event Action<EnergyKeyState, EnergyKeyState> StateChanged;

        void Reset() => GetComponent<Collider2D>().isTrigger = true;

        void Awake()
        {
            if (!requiredEncounter)
                requiredEncounter = FindFirstObjectByType<EncounterController>();
            if (!enemyTarget)
                enemyTarget = FindFirstObjectByType<EnemyController>();

            body = GetComponent<Rigidbody2D>();
            keyCollider = GetComponent<Collider2D>();
            if (!visual)
                visual = transform.Find("Key Visual");
            if (!visual)
                visual = transform;
            spriteRenderer = visual.GetComponent<SpriteRenderer>();
            if (!spriteRenderer)
                spriteRenderer = GetComponent<SpriteRenderer>();
            line = GetComponent<LineRenderer>();
            baseScale = visual.localScale;

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

        void OnEnable()
        {
            if (enemyTarget)
                enemyTarget.Defeated += HandleEnemyDefeated;
            if (requiredEncounter)
                requiredEncounter.Completed += HandleEncounterCompleted;

            // A scene can be enabled after its carrier has already been
            // defeated. The event path is authoritative, but this keeps
            // prefab-preview and additive-scene loading deterministic.
            TryReleaseFromCarrier();
        }

        void OnDisable()
        {
            if (enemyTarget)
                enemyTarget.Defeated -= HandleEnemyDefeated;
            if (requiredEncounter)
                requiredEncounter.Completed -= HandleEncounterCompleted;
        }

        void Update()
        {
            ResolvePlayer();

            if (state != EnergyKeyState.Consumed)
            {
                // Keep presentation off the interpolated transport root. This
                // prevents the sprite rotation/pulse from making the key's
                // trigger appear to jitter during delicate handoffs.
                visual.Rotate(0f, 0f, rotateSpeed * Time.deltaTime);
                float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
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
                case EnergyKeyState.FollowingPlayer:
                    CheckGate();
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
            Vector3 center = enemyTarget.transform.position + enemyOffset;
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
            orbitCenter = Vector2.Lerp(orbitCenter, player.transform.position, centerT);
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
                Vector3 target = player.transform.position + playerOffset;
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

            if (Vector2.Distance(player.transform.position, targetGate.KeyTarget.position) <= gateUnlockRange)
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
            orbitCenter = player ? (Vector2)player.transform.position : body.position;
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

            if (Vector2.Distance(body.position, otherPlayer.transform.position) > collectDistance)
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

            return requiredEncounter == null || requiredEncounter.IsComplete;
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

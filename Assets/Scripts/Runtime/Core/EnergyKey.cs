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
        [SerializeField] Vector3 enemyOffset = new Vector3(0f, 1f, 0f);
        [SerializeField, Min(0f)] float enemyOrbitRadius = 1f;
        [SerializeField, Min(0f)] float enemyOrbitSpeed = 4f;
        [SerializeField, Min(0f)] float enemyOrbitSharpness = 8f;

        [Header("Player Orbit")]
        [SerializeField, Min(0f)] float orbitRadiusX = 4.4f;
        [SerializeField, Min(0f)] float orbitRadiusY = 1.9f;
        [SerializeField, Min(0f)] float orbitSpeed = 2f;
        [SerializeField, Min(0f)] float orbitSharpness = 8f;
        [SerializeField, Min(0f)] float radiusEase = 3.5f;
        [SerializeField, Min(0f)] float centerFollowSharpness = 5.5f;

        [Header("Collection")]
        [SerializeField, Min(0f)] float collectDistance = 0.65f;
        [SerializeField] Vector3 playerOffset = new Vector3(0.6f, 0.7f, 0f);
        [SerializeField, Min(0f)] float playerFollowSharpness = 14f;

        [Header("Gate")]
        [SerializeField, Min(0f)] float gateUnlockRange = 2f;
        [SerializeField, Min(0f)] float gateFlySpeed = 14f;

        [Header("Visual")]
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
        float releasePulseRemaining;

        public EnergyKeyState State => state;
        public bool IsAvailable => state == EnergyKeyState.OrbitingPlayer;
        public bool IsCollected => state == EnergyKeyState.FollowingPlayer || state == EnergyKeyState.FlyingToGate;
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
            spriteRenderer = GetComponent<SpriteRenderer>();
            line = GetComponent<LineRenderer>();
            baseScale = transform.localScale;

            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.simulated = true;
            keyCollider.isTrigger = true;
            ResolvePlayer();
            UpdateAvailabilityVisual();
        }

        void Update()
        {
            ResolvePlayer();

            if (state != EnergyKeyState.Consumed)
            {
                transform.Rotate(0f, 0f, rotateSpeed * Time.deltaTime);
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

                transform.localScale = baseScale * pulse;
            }

            switch (state)
            {
                case EnergyKeyState.AttachedToEnemy:
                    FollowEnemyOrbit();
                    if (CanReleaseFromEnemy())
                        ReleaseFromEnemy();
                    break;
                case EnergyKeyState.OrbitingPlayer:
                    FollowPlayerOrbit();
                    break;
                case EnergyKeyState.FollowingPlayer:
                    FollowPlayer();
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

            phase += enemyOrbitSpeed * Time.deltaTime;
            Vector3 center = enemyTarget.transform.position + enemyOffset;
            Vector3 offset = new Vector3(Mathf.Cos(phase), Mathf.Sin(phase), 0f) * enemyOrbitRadius;
            transform.position = Vector3.Lerp(
                transform.position,
                center + offset,
                1f - Mathf.Exp(-enemyOrbitSharpness * Time.deltaTime));
        }

        void FollowPlayerOrbit()
        {
            if (!player)
                return;

            float centerT = 1f - Mathf.Exp(-centerFollowSharpness * Time.deltaTime);
            orbitCenter = Vector2.Lerp(orbitCenter, player.transform.position, centerT);
            phase += orbitSpeed * Time.deltaTime;
            currentRadiusX = Mathf.Lerp(currentRadiusX, orbitRadiusX, radiusEase * Time.deltaTime);
            currentRadiusY = Mathf.Lerp(currentRadiusY, orbitRadiusY, radiusEase * Time.deltaTime);

            Vector2 target = orbitCenter + new Vector2(
                Mathf.Cos(phase) * currentRadiusX,
                Mathf.Sin(phase) * currentRadiusY);
            float orbitT = 1f - Mathf.Exp(-orbitSharpness * Time.deltaTime);
            body.MovePosition(Vector2.Lerp(body.position, target, orbitT));
        }

        void FollowPlayer()
        {
            if (player)
            {
                Vector3 target = player.transform.position + playerOffset;
                body.MovePosition(Vector2.Lerp(
                    body.position,
                    target,
                    1f - Mathf.Exp(-playerFollowSharpness * Time.deltaTime)));
            }
        }

        void CheckGate()
        {
            if (!player || !targetGate)
                return;

            if (Vector2.Distance(player.transform.position, targetGate.transform.position) <= gateUnlockRange)
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

            Vector2 nextPosition = Vector2.MoveTowards(
                body.position,
                targetGate.transform.position,
                gateFlySpeed * Time.deltaTime);
            body.MovePosition(nextPosition);

            if (Vector2.Distance(nextPosition, targetGate.transform.position) > 0.15f)
                return;

            targetGate.DisableGate();
            FindFirstObjectByType<ScoreSystem>()?.AddScore(50, ScoreReason.GateDeactivated);
            SetState(EnergyKeyState.Consumed);
            Destroy(gameObject);
        }

        void ReleaseFromEnemy()
        {
            SetState(EnergyKeyState.OrbitingPlayer);
            orbitCenter = player ? (Vector2)player.transform.position : body.position;

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

            if (Vector2.Distance(body.position, player.transform.position) > collectDistance)
                return;

            player = otherPlayer;
            SetState(EnergyKeyState.FollowingPlayer);
            keyCollider.enabled = false;
            FindFirstObjectByType<ScoreSystem>()?.AddScore(50, ScoreReason.ObjectiveCollected);
            UpdateAvailabilityVisual();
        }

        bool CanReleaseFromEnemy()
        {
            if (enemyTarget)
                return enemyTarget.State == EnemyState.Defeated
                    || (requiredEncounter && requiredEncounter.IsComplete);

            return requiredEncounter == null || requiredEncounter.IsComplete;
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

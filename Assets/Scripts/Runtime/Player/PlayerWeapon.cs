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
        [SerializeField] bool enforceEe5Profile = true;
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
        SpriteRenderer aimLineEndpoint;
        Sprite aimLineEndpointSprite;
        Texture2D aimLineEndpointTexture;
        readonly RaycastHit2D[] aimHits = new RaycastHit2D[16];
        readonly Collider2D[] aimOverlapHits = new Collider2D[8];

        public bool CanFire => cooldownRemaining <= 0f;
        public float FireRateMultiplier => fireRateMultiplier;

        void Reset()
        {
            ResolveReferences();
        }

        void Awake()
        {
            ResolveReferences();
            if (enforceEe5Profile)
            {
                fireCooldown = Ee5SliceProfile.PlayerFireCooldown;
                recoilForce = Ee5SliceProfile.PlayerRecoilForce;
            }
            EnsureAimLine();
        }

        void OnDestroy()
        {
            if (aimLineMaterial)
                Destroy(aimLineMaterial);
            if (aimLineEndpointSprite)
                Destroy(aimLineEndpointSprite);
            if (aimLineEndpointTexture)
                Destroy(aimLineEndpointTexture);
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
            aimLine.numCapVertices = 12;
            aimLine.numCornerVertices = 4;
            aimLine.alignment = LineAlignment.View;
            aimLine.textureMode = LineTextureMode.Stretch;
            aimLine.sortingOrder = aimLineSortingOrder;
            aimLine.startWidth = aimLineWidth;
            aimLine.endWidth = aimLineWidth;
            aimLine.enabled = false;

            Shader shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (!shader)
                shader = Shader.Find("Sprites/Default");
            if (shader)
            {
                aimLineMaterial = new Material(shader);
                aimLineMaterial.color = Color.white;
                aimLine.sharedMaterial = aimLineMaterial;
            }

            GameObject endpointObject = new GameObject("Player Aim Line Endpoint");
            endpointObject.transform.SetParent(transform, false);
            aimLineEndpoint = endpointObject.AddComponent<SpriteRenderer>();
            aimLineEndpointSprite = CreateAimLineEndpointSprite();
            aimLineEndpoint.sprite = aimLineEndpointSprite;
            aimLineEndpoint.sortingOrder = aimLineSortingOrder + 1;
            aimLineEndpoint.enabled = false;
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

            if (IsAimLineStartBlocked(start))
            {
                HideAimLine();
                return;
            }

            Vector2 end = FindAimLineEnd(start, direction, out bool hitEnemy);
            Color color = hitEnemy
                ? aimLineEnemyColor
                : aimLineColor;
            aimLine.enabled = color.a > 0.01f;
            aimLine.startColor = color;
            aimLine.endColor = color;
            aimLine.startWidth = aimLineWidth;
            aimLine.endWidth = aimLineWidth;
            aimLine.SetPosition(0, start);
            aimLine.SetPosition(1, end);

            if (aimLineEndpoint)
            {
                aimLineEndpoint.enabled = aimLine.enabled;
                aimLineEndpoint.color = color;
                aimLineEndpoint.transform.position = new Vector3(end.x, end.y, 0f);
                aimLineEndpoint.transform.localScale =
                    Vector3.one * Mathf.Max(0.06f, aimLineWidth * 1.45f);
            }
        }

        void HideAimLine()
        {
            if (aimLine)
                aimLine.enabled = false;
            if (aimLineEndpoint)
                aimLineEndpoint.enabled = false;
        }

        bool IsAimLineStartBlocked(Vector2 start)
        {
#pragma warning disable CS0618
            int hitCount = Physics2D.OverlapPointNonAlloc(start, aimOverlapHits);
#pragma warning restore CS0618

            for (int i = 0; i < hitCount; i++)
            {
                Collider2D hitCollider = aimOverlapHits[i];
                if (!hitCollider || IsOwnerCollider(hitCollider))
                    continue;

                if (IsWallAimTarget(hitCollider))
                    return true;
            }

            return false;
        }

        Vector2 FindAimLineEnd(Vector2 start, Vector2 direction, out bool hitEnemy)
        {
            hitEnemy = false;
            float maxDistance = Mathf.Max(0.1f, aimLineMaxDistance);
#pragma warning disable CS0618
            int hitCount = Physics2D.RaycastNonAlloc(start, direction, aimHits, maxDistance);
#pragma warning restore CS0618
            RaycastHit2D bestHit = default;
            float bestDistance = float.PositiveInfinity;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = aimHits[i];
                if (!hit.collider || hit.distance <= 0.001f || hit.distance >= bestDistance)
                    continue;

                if (!IsAimLineStop(hit.collider))
                    continue;

                bestHit = hit;
                bestDistance = hit.distance;
                hitEnemy = IsEnemyAimTarget(hit.collider);
            }

            return bestHit.collider
                ? bestHit.point
                : start + direction * maxDistance;
        }

        bool IsAimLineStop(Collider2D other)
        {
            if (IsOwnerCollider(other))
                return false;

            return IsWallAimTarget(other)
                || other.GetComponentInParent<EnemyController>() != null;
        }

        bool IsEnemyAimTarget(Collider2D other)
        {
            return !IsWallAimTarget(other)
                && other.GetComponentInParent<EnemyController>() != null;
        }

        bool IsWallAimTarget(Collider2D other)
        {
            return other.CompareTag("Wall")
                || HasTagInParents(other.transform, "Wall");
        }

        static bool HasTagInParents(Transform start, string tagName)
        {
            Transform current = start.parent;
            while (current)
            {
                if (current.CompareTag(tagName))
                    return true;

                current = current.parent;
            }

            return false;
        }

        Sprite CreateAimLineEndpointSprite()
        {
            const int size = 32;
            aimLineEndpointTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            aimLineEndpointTexture.name = "player_aim_line_endpoint_texture";
            aimLineEndpointTexture.hideFlags = HideFlags.DontSave;
            aimLineEndpointTexture.filterMode = FilterMode.Bilinear;
            aimLineEndpointTexture.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[size * size];
            Vector2 center = Vector2.one * (size - 1) * 0.5f;
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = Mathf.Clamp01(
                        1f - Mathf.InverseLerp(radius * 0.35f, radius, distance));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            aimLineEndpointTexture.SetPixels(pixels);
            aimLineEndpointTexture.Apply(false, true);
            Sprite endpoint = Sprite.Create(
                aimLineEndpointTexture,
                new Rect(0f, 0f, size, size),
                Vector2.one * 0.5f,
                size);
            endpoint.name = "player_aim_line_endpoint";
            endpoint.hideFlags = HideFlags.DontSave;
            return endpoint;
        }

        bool IsOwnerCollider(Collider2D other)
        {
            return other.gameObject == gameObject
                || other.transform.IsChildOf(transform)
                || transform.IsChildOf(other.transform);
        }
    }
}

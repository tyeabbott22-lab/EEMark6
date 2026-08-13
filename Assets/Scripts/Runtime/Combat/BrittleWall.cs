using System.Collections;
using UnityEngine;
using ExtraterrestrialExhaust.CameraSystem;
using ExtraterrestrialExhaust.Core;
using ExtraterrestrialExhaust.Player;

namespace ExtraterrestrialExhaust.Combat
{
    /// <summary>
    /// Compact EE5-style brittle room prop. Low-angle contact dents and
    /// scrapes the prop while a direct, thrust-assisted impact at speed breaks
    /// it; permanent arena boundaries remain ordinary static walls.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class BrittleWall : MonoBehaviour
    {
        [Header("Impact")]
        [SerializeField, Min(0f)] float dentSpeed = 0.15f;
        [SerializeField, Min(0f)] float breakSpeed = 14f;
        [SerializeField, Range(0f, 1f)] float minimumChipDirectness = 0.12f;
        [SerializeField, Range(0f, 1f)] float minimumDirectness = 0.68f;
        [SerializeField] bool requireThrustToChip;
        [SerializeField] bool requireThrustToBreak = true;
        [SerializeField, Min(1)] int chipsBeforeBreak = 8;
        [SerializeField, Range(0f, 1f)] float retainedVelocity = 0.94f;
        [SerializeField, Min(0f)] float followThroughNudge = 0.34f;
        [Tooltip("Physics-clock handoff that prevents the old wall contact from cancelling the break momentum.")]
        [SerializeField, Min(0f)] float followThroughAssistDuration =
            Ee5SliceProfile.BrittleFollowThroughAssistDuration;
        [SerializeField, Range(0f, 1f)] float angularVelocityRetain =
            Ee5SliceProfile.BrittleAngularVelocityRetention;
        [SerializeField, Min(0f)] float impactCooldown = 0.32f;
        [SerializeField, Min(0f)] float cameraShakeStrength = 0.14f;
        [SerializeField, Min(0f)] float cameraShakeDuration = 0.18f;
        [SerializeField, Min(0)] int breakScore = 150;
        [SerializeField] Color breakColor = new Color(0.95f, 0.12f, 1f, 1f);

        [Header("Scrape Expression")]
        [SerializeField, Min(0f)] float scrapeAnimationDuration = 0.18f;
        [SerializeField, Min(0f)] float scrapeSlideDistance = 0.075f;
        [SerializeField, Min(0f)] float scrapeShakeAngle = 4.5f;
        [SerializeField, Min(0f)] float scrapePulseScale = 0.045f;
        [SerializeField] Color scrapeFlashColor = new Color(1f, 0.62f, 1f, 1f);

        Collider2D wallCollider;
        bool broken;
        float nextImpactTime;
        int chipHits;
        Coroutine scrapeAnimation;
        bool scrapeAnimationActive;
        Vector3 scrapeBaseLocalPosition;
        Quaternion scrapeBaseLocalRotation;
        Vector3 scrapeBaseLocalScale;
        LineRenderer[] visualLines;
        Color[] visualLineStartColors;
        Color[] visualLineEndColors;
        SpriteRenderer[] visualSprites;
        Color[] visualSpriteColors;

        public bool IsBroken => broken;

        void Awake()
        {
            wallCollider = GetComponent<Collider2D>();
            CacheVisuals();
        }

        void OnDisable()
        {
            StopScrapeAnimation();
        }

        void OnCollisionEnter2D(Collision2D collision) => TryBreak(collision);

        void OnCollisionStay2D(Collision2D collision) => TryBreak(collision);

        void TryBreak(Collision2D collision)
        {
            if (broken || Time.time < nextImpactTime)
                return;

            PlayerCharacter player = collision.collider
                ? collision.collider.GetComponentInParent<PlayerCharacter>()
                : null;
            if (!player || !player.FlightMotor || !player.FlightInput)
                return;

            Rigidbody2D playerBody = player.FlightMotor.Body;
            if (!playerBody || !player.CanReceiveGameplayInput)
                return;

            Vector2 velocity = playerBody.linearVelocity;
            float speed = Mathf.Max(collision.relativeVelocity.magnitude, velocity.magnitude);
            Vector2 hitPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : transform.position;
            Vector2 travelDirection = velocity.sqrMagnitude > 0.001f
                ? velocity.normalized
                : (hitPoint - (Vector2)player.transform.position).normalized;
            if (travelDirection.sqrMagnitude <= 0.001f)
                travelDirection = Vector2.right;

            float directness = GetImpactDirectness(collision, travelDirection);
            bool thrusting = player.FlightInput.Move.y > 0.2f;
            bool canChip = speed >= dentSpeed
                && directness >= minimumChipDirectness
                && (!requireThrustToChip || thrusting);
            if (!canChip)
                return;

            nextImpactTime = Time.time + impactCooldown;
            chipHits++;

            bool directBreak = speed >= breakSpeed
                && directness >= minimumDirectness
                && (!requireThrustToBreak || thrusting);
            bool chipBreak = chipsBeforeBreak > 0 && chipHits >= chipsBeforeBreak;
            if (directBreak || chipBreak)
            {
                Break(player.FlightMotor, hitPoint, travelDirection, velocity);
                return;
            }

            playerBody.angularVelocity *= 0.18f;
            float chipStrength = Mathf.InverseLerp(
                dentSpeed,
                Mathf.Max(dentSpeed + 0.01f, breakSpeed * 0.58f),
                speed);
            PlayScrapeAnimation(travelDirection, chipStrength);
            ProjectileImpactBurst.Spawn(
                hitPoint,
                Color.Lerp(breakColor, scrapeFlashColor, 0.55f),
                travelDirection);
        }

        void CacheVisuals()
        {
            visualLines = GetComponentsInChildren<LineRenderer>(true);
            visualLineStartColors = new Color[visualLines.Length];
            visualLineEndColors = new Color[visualLines.Length];
            for (int i = 0; i < visualLines.Length; i++)
            {
                visualLineStartColors[i] = visualLines[i].startColor;
                visualLineEndColors[i] = visualLines[i].endColor;
            }

            visualSprites = GetComponentsInChildren<SpriteRenderer>(true);
            visualSpriteColors = new Color[visualSprites.Length];
            for (int i = 0; i < visualSprites.Length; i++)
                visualSpriteColors[i] = visualSprites[i].color;
        }

        static float GetImpactDirectness(Collision2D collision, Vector2 travelDirection)
        {
            if (collision.contactCount == 0 || travelDirection.sqrMagnitude <= 0.0001f)
                return 1f;

            float best = 0f;
            Vector2 direction = travelDirection.normalized;
            for (int i = 0; i < collision.contactCount; i++)
            {
                Vector2 normal = collision.GetContact(i).normal.normalized;
                best = Mathf.Max(best, Mathf.Abs(Vector2.Dot(direction, normal)));
            }

            return best;
        }

        void PlayScrapeAnimation(Vector2 worldDirection, float strength)
        {
            StopScrapeAnimation();
            scrapeAnimation = StartCoroutine(ScrapeAnimation(worldDirection, strength));
        }

        IEnumerator ScrapeAnimation(Vector2 worldDirection, float strength)
        {
            scrapeAnimationActive = true;
            scrapeBaseLocalPosition = transform.localPosition;
            scrapeBaseLocalRotation = transform.localRotation;
            scrapeBaseLocalScale = transform.localScale;

            Vector2 tangent = new Vector2(-worldDirection.y, worldDirection.x);
            Vector3 localTangent = transform.parent
                ? transform.parent.InverseTransformDirection(tangent).normalized
                : (Vector3)tangent.normalized;
            float duration = Mathf.Max(0.03f, scrapeAnimationDuration);
            float elapsed = 0f;

            while (elapsed < duration && !broken)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float envelope = 1f - t;
                float pulse = Mathf.Sin(t * Mathf.PI);
                float scratch = Mathf.Sin(t * Mathf.PI * 8f);
                float slide = pulse * scrapeSlideDistance
                    * Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(strength));

                transform.localPosition = scrapeBaseLocalPosition + localTangent * slide;
                transform.localRotation = scrapeBaseLocalRotation
                    * Quaternion.Euler(0f, 0f, scratch * scrapeShakeAngle * envelope * Mathf.Clamp01(strength));
                transform.localScale = scrapeBaseLocalScale
                    * (1f + pulse * scrapePulseScale * Mathf.Clamp01(strength));
                SetScrapeVisual(Mathf.Sin(t * Mathf.PI) * Mathf.Clamp01(strength));
                yield return null;
            }

            RestoreScrapePose();
            scrapeAnimation = null;
        }

        void SetScrapeVisual(float intensity)
        {
            for (int i = 0; i < visualLines.Length; i++)
            {
                if (!visualLines[i])
                    continue;

                visualLines[i].startColor = Color.Lerp(
                    visualLineStartColors[i],
                    scrapeFlashColor,
                    intensity);
                visualLines[i].endColor = Color.Lerp(
                    visualLineEndColors[i],
                    scrapeFlashColor,
                    intensity);
            }

            for (int i = 0; i < visualSprites.Length; i++)
                if (visualSprites[i])
                    visualSprites[i].color = Color.Lerp(
                        visualSpriteColors[i],
                        scrapeFlashColor,
                        intensity);
        }

        void StopScrapeAnimation()
        {
            if (scrapeAnimation != null)
            {
                StopCoroutine(scrapeAnimation);
                scrapeAnimation = null;
            }

            if (scrapeAnimationActive)
                RestoreScrapePose();
        }

        void RestoreScrapePose()
        {
            transform.localPosition = scrapeBaseLocalPosition;
            transform.localRotation = scrapeBaseLocalRotation;
            transform.localScale = scrapeBaseLocalScale;
            RestoreScrapeVisuals();
            scrapeAnimationActive = false;
        }

        void RestoreScrapeVisuals()
        {
            for (int i = 0; i < visualLines.Length; i++)
            {
                if (!visualLines[i])
                    continue;

                visualLines[i].startColor = visualLineStartColors[i];
                visualLines[i].endColor = visualLineEndColors[i];
            }

            for (int i = 0; i < visualSprites.Length; i++)
                if (visualSprites[i])
                    visualSprites[i].color = visualSpriteColors[i];
        }

        void Break(
            PlayerFlightMotor playerMotor,
            Vector2 hitPoint,
            Vector2 travelDirection,
            Vector2 velocity)
        {
            StopScrapeAnimation();
            broken = true;
            if (wallCollider)
                wallCollider.enabled = false;

            // The authored SpriteShape debris is represented by a reusable
            // burst until that heavier asset pipeline is migrated.
            ObjectiveSignalBurst.Spawn(hitPoint, breakColor, 1.35f);
            PlayerCameraFollow.Instance?.Shake(cameraShakeStrength, cameraShakeDuration);
            FindFirstObjectByType<ScoreSystem>()?.AddScore(breakScore, ScoreReason.WallBroken);

            Rigidbody2D playerBody = playerMotor ? playerMotor.Body : null;
            if (playerMotor && playerBody)
            {
                Vector2 retained = velocity * retainedVelocity;
                playerMotor.ApplyBrittleFollowThrough(
                    retained,
                    travelDirection * followThroughNudge,
                    followThroughAssistDuration,
                    angularVelocityRetain);
            }

            foreach (Collider2D childCollider in GetComponentsInChildren<Collider2D>(true))
                if (childCollider)
                    childCollider.enabled = false;

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                if (renderer)
                    renderer.enabled = false;

            gameObject.SetActive(false);
        }
    }
}

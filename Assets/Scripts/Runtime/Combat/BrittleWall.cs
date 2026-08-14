using System.Collections;
using UnityEngine;
using UnityEngine.U2D;
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

        [Header("Persistent Fracture")]
        [Tooltip("When this wall is backed by a SpriteShape, each chip permanently pushes the nearby authored contour inward.")]
        [SerializeField] bool deformSpriteShape = true;
        [SerializeField, Min(0.01f)] float splineDentDepth = 0.28f;
        [SerializeField, Min(0.05f)] float splineDentRadius = 0.9f;
        [Tooltip("Width of the physical V-notch cut into the contact edge. This is deliberately larger than a line scratch so a chip reads while flying past it.")]
        [SerializeField, Min(0.04f)] float splineNotchWidth = 0.44f;
        [SerializeField, Min(1)] int maxSplineNotches = 4;
        [SerializeField, Min(0.01f)] float minimumSplinePointSpacing = 0.08f;
        [SerializeField, Min(0.01f)] float fractureLineWidth = 0.045f;

        Collider2D wallCollider;
        SpriteShapeController spriteShape;
        PolygonCollider2D polygonCollider;
        Material fractureMaterial;
        bool broken;
        float nextImpactTime;
        int chipHits;
        int splineNotches;
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
        public int ChipHits => chipHits;
        public float DeformationMagnitude { get; private set; }

        void Awake()
        {
            wallCollider = GetComponent<Collider2D>();
            spriteShape = GetComponent<SpriteShapeController>();
            polygonCollider = GetComponent<PolygonCollider2D>();
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

            Vector2 bodyVelocity = playerBody.linearVelocity;
            // The solver may have already zeroed the player's normal velocity
            // by the time this static wall receives its callback. Preserve the
            // larger collision sample for both the break decision and the
            // post-break handoff, otherwise a successful smash feels like it
            // hits an invisible brake.
            Vector2 velocity = collision.relativeVelocity.sqrMagnitude > bodyVelocity.sqrMagnitude
                ? collision.relativeVelocity
                : bodyVelocity;
            float speed = velocity.magnitude;
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

            // A chip should settle collision chatter, not erase the craft's
            // manoeuvre. A real break receives the full follow-through below.
            playerBody.angularVelocity *= angularVelocityRetain;
            float chipStrength = Mathf.InverseLerp(
                dentSpeed,
                Mathf.Max(dentSpeed + 0.01f, breakSpeed * 0.58f),
                speed);
            ApplyPersistentFracture(hitPoint, travelDirection, chipStrength);
            PlayScrapeAnimation(travelDirection, chipStrength);
            ProjectileImpactBurst.Spawn(
                hitPoint,
                Color.Lerp(breakColor, scrapeFlashColor, 0.55f),
                travelDirection);
        }

        void ApplyPersistentFracture(Vector2 hitPoint, Vector2 worldDirection, float strength)
        {
            if (!deformSpriteShape || !spriteShape || spriteShape.spline == null)
                return;

            int pointCount = spriteShape.spline.GetPointCount();
            if (pointCount < 3)
                return;

            Vector2 localHit = transform.InverseTransformPoint(hitPoint);
            Vector2 localDirection = (Vector2)transform
                .InverseTransformDirection((Vector3)worldDirection).normalized;
            if (localDirection.sqrMagnitude < 0.0001f)
                localDirection = Vector2.right;

            float radius = Mathf.Max(0.05f, splineDentRadius);
            float depth = splineDentDepth * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(strength));
            bool changed = TryCutImpactNotch(
                localHit,
                localDirection,
                depth,
                strength);

            // After a crisp V-shaped cut, gently bow the surrounding edge
            // inward. This makes a light impact read as terrain deformation,
            // not merely a translated straight spline segment.
            pointCount = spriteShape.spline.GetPointCount();

            for (int i = 0; i < pointCount; i++)
            {
                Vector2 point = spriteShape.spline.GetPosition(i);
                float distance = Vector2.Distance(point, localHit);
                float influence = 1f - Mathf.Clamp01(distance / radius);
                if (influence <= 0.001f)
                    continue;

                Vector2 dented = point + localDirection
                    * (depth * 0.32f * influence * influence);
                spriteShape.spline.SetPosition(i, dented);
                changed = true;
            }

            if (!changed)
                return;

            // This builder intentionally owns a separate PolygonCollider2D
            // for deterministic EE5-like contact. Keep it in lockstep with
            // the visible contour so a permanent dent is gameplay-relevant,
            // not a cosmetic lie.
            SyncPolygonColliderToSpline();

            spriteShape.RefreshSpriteShape();
            DeformationMagnitude += depth;
            AddFractureVisual(localHit, localDirection, strength);
        }

        bool TryCutImpactNotch(
            Vector2 localHit,
            Vector2 localDirection,
            float depth,
            float strength)
        {
            if (splineNotches >= maxSplineNotches)
                return false;

            int count = spriteShape.spline.GetPointCount();
            if (count < 3)
                return false;

            int segmentStart = -1;
            float segmentT = 0f;
            float closestDistance = float.PositiveInfinity;
            Vector2 segmentA = default;
            Vector2 segmentB = default;
            for (int i = 0; i < count; i++)
            {
                Vector2 a = spriteShape.spline.GetPosition(i);
                Vector2 b = spriteShape.spline.GetPosition((i + 1) % count);
                Vector2 closest = ClosestPointOnSegment(a, b, localHit, out float t);
                float distance = Vector2.Distance(closest, localHit);
                if (distance >= closestDistance)
                    continue;

                segmentStart = i;
                segmentT = t;
                closestDistance = distance;
                segmentA = a;
                segmentB = b;
            }

            if (segmentStart < 0 || closestDistance > splineDentRadius * 1.45f)
                return false;

            Vector2 edge = segmentB - segmentA;
            float edgeLength = edge.magnitude;
            float minSpacing = Mathf.Max(0.01f, minimumSplinePointSpacing);
            if (edgeLength <= minSpacing * 4f)
                return false;

            Vector2 edgeDirection = edge / edgeLength;
            float halfWidth = Mathf.Max(
                minSpacing,
                splineNotchWidth * Mathf.Lerp(0.72f, 1.2f, Mathf.Clamp01(strength)) * 0.5f);
            float centerDistance = Mathf.Clamp(
                segmentT * edgeLength,
                minSpacing * 2f,
                edgeLength - minSpacing * 2f);
            float leftDistance = Mathf.Clamp(
                centerDistance - halfWidth,
                minSpacing,
                edgeLength - minSpacing * 3f);
            float rightDistance = Mathf.Clamp(
                centerDistance + halfWidth,
                minSpacing * 3f,
                edgeLength - minSpacing);
            if (rightDistance - leftDistance < minSpacing * 2f)
                return false;

            Vector2 left = segmentA + edgeDirection * leftDistance;
            Vector2 right = segmentA + edgeDirection * rightDistance;
            Vector2 notchBase = segmentA + edgeDirection * centerDistance;
            Vector2 notch = notchBase + localDirection
                * Mathf.Min(depth, halfWidth * 1.25f);
            if (!CanInsertNotch(segmentStart, left, notch, right, minSpacing))
                return false;

            int insertIndex = segmentStart + 1;
            spriteShape.spline.InsertPointAt(insertIndex, left);
            ConfigureNotchPoint(insertIndex);
            spriteShape.spline.InsertPointAt(insertIndex + 1, notch);
            ConfigureNotchPoint(insertIndex + 1);
            spriteShape.spline.InsertPointAt(insertIndex + 2, right);
            ConfigureNotchPoint(insertIndex + 2);
            splineNotches++;
            return true;
        }

        bool CanInsertNotch(
            int segmentStart,
            Vector2 left,
            Vector2 notch,
            Vector2 right,
            float minSpacing)
        {
            int count = spriteShape.spline.GetPointCount();
            int segmentEnd = (segmentStart + 1) % count;
            Vector2 a = spriteShape.spline.GetPosition(segmentStart);
            Vector2 b = spriteShape.spline.GetPosition(segmentEnd);
            if (Vector2.Distance(a, left) < minSpacing
                || Vector2.Distance(right, b) < minSpacing
                || Vector2.Distance(left, notch) < minSpacing
                || Vector2.Distance(notch, right) < minSpacing)
                return false;

            for (int i = 0; i < count; i++)
            {
                if (i == segmentStart || i == segmentEnd)
                    continue;

                Vector2 point = spriteShape.spline.GetPosition(i);
                if (Vector2.Distance(point, left) < minSpacing
                    || Vector2.Distance(point, notch) < minSpacing
                    || Vector2.Distance(point, right) < minSpacing)
                    return false;
            }

            return true;
        }

        void ConfigureNotchPoint(int index)
        {
            spriteShape.spline.SetTangentMode(index, ShapeTangentMode.Linear);
            spriteShape.spline.SetHeight(index, 0.12f);
            spriteShape.spline.SetCorner(index, true);
        }

        void SyncPolygonColliderToSpline()
        {
            if (!polygonCollider)
                return;

            int pointCount = spriteShape.spline.GetPointCount();
            Vector2[] contour = new Vector2[pointCount];
            for (int i = 0; i < pointCount; i++)
                contour[i] = spriteShape.spline.GetPosition(i);

            polygonCollider.pathCount = 1;
            polygonCollider.SetPath(0, contour);
        }

        static Vector2 ClosestPointOnSegment(
            Vector2 a,
            Vector2 b,
            Vector2 point,
            out float t)
        {
            Vector2 edge = b - a;
            float lengthSquared = edge.sqrMagnitude;
            t = lengthSquared > 0.0001f
                ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / lengthSquared)
                : 0f;
            return a + edge * t;
        }

        void AddFractureVisual(Vector2 localHit, Vector2 localDirection, float strength)
        {
            GameObject fracture = new GameObject($"Fracture Mark {chipHits:00}");
            fracture.transform.SetParent(transform, false);

            LineRenderer line = fracture.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 3;
            line.numCapVertices = 2;
            line.widthMultiplier = fractureLineWidth;
            line.sortingOrder = 6;
            line.sharedMaterial = GetFractureMaterial();
            Color start = Color.Lerp(scrapeFlashColor, breakColor, 0.5f);
            start.a = 0.9f;
            Color end = start;
            end.a = 0.1f;
            line.startColor = start;
            line.endColor = end;

            Vector2 tangent = new Vector2(-localDirection.y, localDirection.x);
            float size = Mathf.Lerp(0.22f, 0.42f, Mathf.Clamp01(strength));
            Vector2 root = localHit - localDirection * (size * 0.04f);
            line.SetPosition(0, root);
            line.SetPosition(1, root - localDirection * (size * 0.52f) + tangent * (size * 0.26f));
            line.SetPosition(2, root - localDirection * size - tangent * (size * 0.18f));
        }

        Material GetFractureMaterial()
        {
            if (!fractureMaterial)
                fractureMaterial = RuntimeVisualMaterial.Create("Brittle Wall Fracture (Runtime)");
            return fractureMaterial;
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

        void OnDestroy()
        {
            if (!fractureMaterial)
                return;

            if (Application.isPlaying)
                Destroy(fractureMaterial);
            else
                DestroyImmediate(fractureMaterial);
        }
    }
}

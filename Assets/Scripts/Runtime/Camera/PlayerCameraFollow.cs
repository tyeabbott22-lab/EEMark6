using UnityEngine;
using ExtraterrestrialExhaust.Player;
using ExtraterrestrialExhaust.Core;

namespace ExtraterrestrialExhaust.CameraSystem
{
    /// <summary>
    /// Camera follow with velocity lead, speed zoom, and reusable screen shake.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PlayerCameraFollow : MonoBehaviour
    {
        [System.Serializable]
        public sealed class ParallaxLayer
        {
            public Transform transform;
            [Range(0f, 1f)] public float strength = 0.18f;
        }

        public static PlayerCameraFollow Instance { get; private set; }
        [SerializeField] PlayerCharacter target;
        [SerializeField] bool enforceEe5Profile = true;
        [SerializeField, Min(0f)] float followSpeed = 12f;
        [SerializeField, Min(0f)] float velocityLead = 0.24f;
        [SerializeField, Min(0f)] float maxLeadDistance = 3.75f;
        [SerializeField, Min(0f)] float facingLead = 1.15f;
        [SerializeField, Min(0f)] float leadSmooth = 10f;
        [SerializeField, Min(0f)] float catchupDistance = 1.4f;
        [SerializeField, Min(1f)] float catchupBoost = 2.2f;
        [SerializeField, Min(0f)] float hardCatchupDistance = 5f;
        [SerializeField, Min(0f)] float closeEnoughSnap = 0.04f;
        [SerializeField, Min(0f)] float speedZoomStart = 6f;
        [SerializeField, Min(0f)] float speedZoomFull = 18f;
        [SerializeField, Min(0f)] float maxZoomOut = 2.25f;
        [SerializeField, Min(0f)] float zoomSmooth = 10f;
        [SerializeField, Min(0f)] float flipZoomOut = 1.4f;
        [SerializeField, Min(0f)] float flipZoomDuration = 0.45f;
        [SerializeField, Min(0f)] float shakeDamping = 10f;
        [SerializeField, Min(0f)] float shakeFrequency = 34f;
        [SerializeField] string wallTag = "Wall";
        [SerializeField, Min(0f)] float wallSlamMinSpeed = 4.5f;
        [SerializeField, Min(0f)] float wallSlamMaxSpeed = 18f;
        [SerializeField, Min(0f)] float wallSlamShakeStrength = 0.14f;
        [SerializeField, Min(0f)] float wallSlamShakeDuration = 0.18f;
        [SerializeField, Min(0f)] float wallSlamCooldown = 0.14f;
        [SerializeField] ParallaxLayer[] parallaxLayers;

        Camera cameraComponent;
        Rigidbody2D targetBody;
        Vector3 currentLead;
        float baseZoom;
        float zoomImpulse;
        float zoomImpulseRemaining;
        float zoomImpulseDuration;
        float shakeStrength;
        float shakeRemaining;
        float shakeSeedX;
        float shakeSeedY;
        float nextWallSlamShakeTime;

        public PlayerCharacter Target => target;

        void Awake()
        {
            Instance = this;
            cameraComponent = GetComponent<Camera>();
            if (enforceEe5Profile)
                ApplyEe5Profile();

            baseZoom = cameraComponent.orthographicSize;
            shakeSeedX = Random.Range(0f, 1000f);
            shakeSeedY = Random.Range(0f, 1000f);
            ResolveTarget();
        }

        void ApplyEe5Profile()
        {
            if (cameraComponent && cameraComponent.orthographic)
            {
                // Enforce the authored room frame at runtime as well as in the
                // builder. This keeps an older serialized FlightTest scene from
                // launching with the broader prototype view.
                cameraComponent.orthographicSize = Ee5SliceProfile.CameraOrthographicSize;
                cameraComponent.backgroundColor = Ee5SliceProfile.CameraBackgroundColor;
                cameraComponent.clearFlags = CameraClearFlags.SolidColor;
            }

            followSpeed = Ee5SliceProfile.CameraFollowSpeed;
            velocityLead = Ee5SliceProfile.CameraVelocityLead;
            maxLeadDistance = Ee5SliceProfile.CameraMaxLeadDistance;
            facingLead = Ee5SliceProfile.CameraFacingLead;
            leadSmooth = Ee5SliceProfile.CameraLeadSmooth;
            catchupDistance = Ee5SliceProfile.CameraCatchupDistance;
            catchupBoost = Ee5SliceProfile.CameraCatchupBoost;
            hardCatchupDistance = Ee5SliceProfile.CameraHardCatchupDistance;
            closeEnoughSnap = Ee5SliceProfile.CameraCloseEnoughSnap;
            speedZoomStart = Ee5SliceProfile.CameraSpeedZoomStart;
            speedZoomFull = Ee5SliceProfile.CameraSpeedZoomFull;
            maxZoomOut = Ee5SliceProfile.CameraMaxZoomOut;
            zoomSmooth = Ee5SliceProfile.CameraZoomSmooth;
            flipZoomOut = Ee5SliceProfile.CameraFlipZoomOut;
            flipZoomDuration = Ee5SliceProfile.CameraFlipZoomDuration;
            wallTag = Ee5SliceProfile.WallTag;
            wallSlamMinSpeed = Ee5SliceProfile.CameraWallSlamMinSpeed;
            wallSlamMaxSpeed = Ee5SliceProfile.CameraWallSlamMaxSpeed;
            wallSlamShakeStrength = Ee5SliceProfile.CameraWallSlamShakeStrength;
            wallSlamShakeDuration = Ee5SliceProfile.CameraWallSlamShakeDuration;
            wallSlamCooldown = Ee5SliceProfile.CameraWallSlamCooldown;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void LateUpdate()
        {
            ResolveTarget();
            if (!target)
                return;

            Vector3 previousCameraPosition = transform.position;

            Vector2 velocity = targetBody ? targetBody.linearVelocity : Vector2.zero;
            Vector2 velocityLeadVector = Vector2.ClampMagnitude(velocity * velocityLead, maxLeadDistance);
            Vector2 desiredLead = velocityLeadVector + (Vector2)target.transform.up * facingLead;
            float leadT = 1f - Mathf.Exp(-leadSmooth * Time.deltaTime);
            currentLead = Vector3.Lerp(currentLead, desiredLead, leadT);

            Vector3 desiredPosition = target.transform.position + currentLead;
            desiredPosition.z = transform.position.z;
            float distance = Vector3.Distance(transform.position, desiredPosition);
            float catchupT = Mathf.InverseLerp(catchupDistance, hardCatchupDistance, distance);
            float effectiveFollowSpeed = followSpeed * Mathf.Lerp(1f, catchupBoost, catchupT);
            float followT = 1f - Mathf.Exp(-effectiveFollowSpeed * Time.deltaTime);
            transform.position = distance <= closeEnoughSnap
                ? desiredPosition
                : Vector3.Lerp(transform.position, desiredPosition, followT);

            ApplyParallax(transform.position - previousCameraPosition);
            ApplyZoom(velocity.magnitude);
            ApplyShake();
        }

        public void Shake(float strength, float duration)
        {
            shakeStrength = Mathf.Max(shakeStrength, strength);
            shakeRemaining = Mathf.Max(shakeRemaining, duration);
        }

        /// <summary>
        /// Adds EE5-style impact feedback without coupling camera code to player
        /// damage rules. Non-damaging glancing hits can still communicate speed.
        /// </summary>
        public void TryShakeForWallImpact(Collision2D collision)
        {
            if (collision == null || collision.contactCount == 0
                || string.IsNullOrEmpty(wallTag)
                || !collision.collider.CompareTag(wallTag)
                || Time.unscaledTime < nextWallSlamShakeTime)
                return;

            float impactSpeed = collision.relativeVelocity.magnitude;
            if (impactSpeed < wallSlamMinSpeed)
                return;

            float impactT = Mathf.InverseLerp(wallSlamMinSpeed, wallSlamMaxSpeed, impactSpeed);
            Shake(
                wallSlamShakeStrength * impactT,
                Mathf.Lerp(wallSlamShakeDuration * 0.5f, wallSlamShakeDuration, impactT));
            nextWallSlamShakeTime = Time.unscaledTime + wallSlamCooldown;
        }

        public void ZoomForPlayerFlip()
        {
            zoomImpulse = Mathf.Max(zoomImpulse, flipZoomOut);
            zoomImpulseDuration = Mathf.Max(zoomImpulseDuration, flipZoomDuration);
            zoomImpulseRemaining = Mathf.Max(zoomImpulseRemaining, flipZoomDuration);
        }

        void ResolveTarget()
        {
            if (!target)
                target = FindFirstObjectByType<PlayerCharacter>();
            if (target && !targetBody)
                targetBody = target.GetComponent<Rigidbody2D>();
        }

        void ApplyZoom(float speed)
        {
            if (!cameraComponent.orthographic)
                return;

            float zoomT = Mathf.InverseLerp(speedZoomStart, speedZoomFull, speed);
            float targetZoom = baseZoom + maxZoomOut * zoomT;

            if (zoomImpulseRemaining > 0f)
            {
                zoomImpulseRemaining -= Time.deltaTime;
                float impulseT = zoomImpulseDuration > 0f
                    ? Mathf.Clamp01(zoomImpulseRemaining / zoomImpulseDuration)
                    : 0f;
                targetZoom += zoomImpulse * impulseT;

                if (zoomImpulseRemaining <= 0f)
                {
                    zoomImpulse = 0f;
                    zoomImpulseDuration = 0f;
                }
            }

            cameraComponent.orthographicSize = Mathf.Lerp(
                cameraComponent.orthographicSize,
                targetZoom,
                1f - Mathf.Exp(-zoomSmooth * Time.deltaTime));
        }

        void ApplyParallax(Vector3 cameraDelta)
        {
            if (parallaxLayers == null || cameraDelta.sqrMagnitude <= 0.000001f)
                return;

            for (int i = 0; i < parallaxLayers.Length; i++)
            {
                ParallaxLayer layer = parallaxLayers[i];
                if (layer == null || !layer.transform || layer.strength <= 0f)
                    continue;

                // Move the authored layer with the camera so it drifts more
                // slowly across the view, matching EE5's starfield language.
                layer.transform.position += cameraDelta * Mathf.Clamp01(layer.strength);
            }
        }

        void ApplyShake()
        {
            if (shakeRemaining <= 0f)
                return;

            shakeRemaining -= Time.unscaledDeltaTime;
            float fade = Mathf.Clamp01(shakeRemaining * shakeDamping);
            float sample = Time.unscaledTime * shakeFrequency;
            Vector2 noise = new Vector2(
                Mathf.PerlinNoise(shakeSeedX, sample) - 0.5f,
                Mathf.PerlinNoise(shakeSeedY, sample) - 0.5f) * 2f;
            transform.position += (Vector3)(noise * (shakeStrength * fade));
            shakeStrength = Mathf.MoveTowards(
                shakeStrength,
                0f,
                Time.unscaledDeltaTime * shakeDamping);
        }
    }
}

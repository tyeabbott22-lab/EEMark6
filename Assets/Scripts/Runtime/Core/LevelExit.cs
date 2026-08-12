using System.Collections;
using UnityEngine;
using ExtraterrestrialExhaust.CameraSystem;
using ExtraterrestrialExhaust.Player;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// Completes the vertical slice when the player reaches the exit after
    /// clearing the encounter.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class LevelExit : MonoBehaviour
    {
        [SerializeField] EncounterController encounter;
        [SerializeField] EnergyGate requiredGate;
        [SerializeField] GameStateMachine gameState;
        [SerializeField] Color lockedColor = new Color(0.35f, 0.35f, 0.45f);
        [SerializeField] Color unlockedColor = new Color(0.2f, 1f, 0.85f);
        [SerializeField, Min(0.25f)] float captureDuration = 1.95f;
        [SerializeField, Range(0f, 3f)] float spiralRevolutions = 1.25f;
        [SerializeField] bool clockwise = true;
        [SerializeField, Range(0f, 1f)] float arrivalRadius = 0.015f;
        [SerializeField, Range(0f, 720f)] float additionalPlayerSpin = 430f;
        [SerializeField, Range(0.02f, 1f)] float finalPlayerScale = 0.06f;
        [SerializeField, Range(0.5f, 0.95f)] float fadeStart = 0.76f;
        [SerializeField, Range(0f, 0.3f)] float squashAmount = 0.12f;
        [SerializeField] AnimationCurve inwardPull = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0.12f),
            new Keyframe(0.28f, 0.08f, 0.35f, 0.7f),
            new Keyframe(1f, 1f, 2.6f, 0f));
        [SerializeField] AnimationCurve angularProgress = null;
        LineRenderer exitRenderer;
        ExtractionPortalPresentation portalPresentation;
        bool capturing;
        bool extractionComplete;
        PlayerCharacter capturedPlayer;
        Rigidbody2D capturedBody;
        RigidbodyType2D capturedBodyType;
        RigidbodyConstraints2D capturedBodyConstraints;
        RigidbodyInterpolation2D capturedBodyInterpolation;
        CollisionDetectionMode2D capturedBodyCollisionMode;
        float capturedBodyGravityScale;
        float capturedBodyLinearDamping;
        float capturedBodyAngularDamping;
        bool capturedBodySimulated;
        Vector2 captureStartPosition;
        float captureStartBodyRotation;
        Vector3 captureStartVisualScale;
        Transform capturedVisual;
        bool capturedBodyState;
        Collider2D[] capturedColliders;
        bool[] capturedColliderStates;
        SpriteRenderer[] capturedRenderers;
        Color[] capturedRendererColors;
        public bool IsCapturing => capturing;
        public bool IsComplete => extractionComplete;
        // EE5's door state is the extraction gate. Once the delivered key has
        // disabled it, remaining enemies are pressure rather than a hidden
        // second exit condition.
        public bool IsUnlocked => requiredGate == null || requiredGate.IsDisabled;

        void Reset() => GetComponent<Collider2D>().isTrigger = true;

        void Awake()
        {
            if (!encounter)
                encounter = FindFirstObjectByType<EncounterController>();
            if (!gameState)
                gameState = FindFirstObjectByType<GameStateMachine>();
            exitRenderer = GetComponent<LineRenderer>();
            portalPresentation = GetComponent<ExtractionPortalPresentation>();
            UpdateVisual();
        }

        void OnEnable()
        {
            if (encounter)
                encounter.Completed += UpdateVisual;
            if (requiredGate)
                requiredGate.Disabled += UpdateVisual;
        }

        void OnDisable()
        {
            if (encounter)
                encounter.Completed -= UpdateVisual;
            if (requiredGate)
                requiredGate.Disabled -= UpdateVisual;

            if (capturing && !extractionComplete)
                CancelCapture();
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            TryStartCapture(other);
        }

        // The gate can unlock while the craft is already overlapping the portal.
        // Stay-based evaluation keeps that ordering from creating a soft lock.
        void OnTriggerStay2D(Collider2D other)
        {
            TryStartCapture(other);
        }

        void TryStartCapture(Collider2D other)
        {
            if (!IsUnlocked || capturing || extractionComplete)
                return;

            PlayerCharacter player = other.GetComponentInParent<PlayerCharacter>();
            if (player && player.CanReceiveGameplayInput)
                StartCoroutine(CapturePlayer(player));
        }

        IEnumerator CapturePlayer(PlayerCharacter player)
        {
            capturing = true;
            capturedPlayer = player;
            portalPresentation?.BeginCapture();
            player.FlightState.TrySetState(PlayerFlightState.Scripted);
            PlayerCameraFollow.Instance?.Shake(0.12f, captureDuration);

            Rigidbody2D body = player.FlightMotor ? player.FlightMotor.Body : null;
            Transform visual = player.FlightMotor && player.FlightMotor.Visual
                ? player.FlightMotor.Visual
                : player.transform;
            capturedVisual = visual;
            capturedColliders = player.GetComponentsInChildren<Collider2D>(true);
            capturedColliderStates = new bool[capturedColliders.Length];
            for (int i = 0; i < capturedColliders.Length; i++)
            {
                capturedColliderStates[i] = capturedColliders[i].enabled;
                capturedColliders[i].enabled = false;
            }

            capturedRenderers = player.GetComponentsInChildren<SpriteRenderer>(true);
            capturedRendererColors = new Color[capturedRenderers.Length];
            for (int i = 0; i < capturedRenderers.Length; i++)
                capturedRendererColors[i] = capturedRenderers[i].color;

            if (body)
            {
                capturedBody = body;
                capturedBodyType = body.bodyType;
                capturedBodyConstraints = body.constraints;
                capturedBodyInterpolation = body.interpolation;
                capturedBodyCollisionMode = body.collisionDetectionMode;
                capturedBodyGravityScale = body.gravityScale;
                capturedBodyLinearDamping = body.linearDamping;
                capturedBodyAngularDamping = body.angularDamping;
                capturedBodySimulated = body.simulated;
                capturedBodyState = true;
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.constraints = RigidbodyConstraints2D.None;
                body.gravityScale = 0f;
                body.linearDamping = 0f;
                body.angularDamping = 0f;
                body.interpolation = RigidbodyInterpolation2D.None;
                body.bodyType = RigidbodyType2D.Kinematic;
                body.simulated = true;
            }

            captureStartPosition = body ? body.position : player.transform.position;
            captureStartVisualScale = visual.localScale;
            float elapsed = 0f;
            float startAngle = Mathf.Atan2(
                captureStartPosition.y - transform.position.y,
                captureStartPosition.x - transform.position.x);
            captureStartBodyRotation = body ? body.rotation : player.transform.eulerAngles.z;
            float initialRadius = Mathf.Max(Vector2.Distance(captureStartPosition, transform.position), 0.001f);
            float direction = clockwise ? -1f : 1f;

            while (elapsed < captureDuration && player)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / captureDuration);
                float radialT = inwardPull != null && inwardPull.length > 0
                    ? Mathf.Clamp01(inwardPull.Evaluate(t))
                    : Mathf.SmoothStep(0f, 1f, t);
                float angleT = angularProgress != null && angularProgress.length > 0
                    ? Mathf.Clamp01(angularProgress.Evaluate(t))
                    : Mathf.SmoothStep(0f, 1f, t);
                float angle = startAngle + direction * spiralRevolutions * Mathf.PI * 2f * angleT;
                float radius = Mathf.Lerp(initialRadius, arrivalRadius, radialT);
                Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Vector2 capturePosition = (Vector2)transform.position + offset;
                if (body)
                {
                    body.position = capturePosition;
                    body.rotation = captureStartBodyRotation - additionalPlayerSpin * angleT;
                }
                else
                {
                    player.transform.position = capturePosition;
                    player.transform.rotation = Quaternion.Euler(
                        0f,
                        0f,
                        captureStartBodyRotation - additionalPlayerSpin * angleT);
                }

                float scale = Mathf.Lerp(1f, finalPlayerScale, Mathf.SmoothStep(0f, 1f, radialT));
                float squash = squashAmount * Mathf.SmoothStep(0f, 1f, t);
                visual.localScale = Vector3.Scale(
                    captureStartVisualScale * scale,
                    new Vector3(1f + squash, Mathf.Max(0.35f, 1f - squash), 1f));

                float fade = t <= fadeStart ? 0f : Mathf.InverseLerp(fadeStart, 1f, t);
                for (int i = 0; i < capturedRenderers.Length; i++)
                {
                    if (!capturedRenderers[i])
                        continue;
                    Color color = capturedRendererColors[i];
                    color.a *= 1f - fade;
                    capturedRenderers[i].color = color;
                }

                portalPresentation?.SetCaptureProgress(t);

                yield return null;
            }

            if (!player)
            {
                CancelCapture();
                yield break;
            }

            if (body)
            {
                body.position = transform.position;
                body.rotation = captureStartBodyRotation - additionalPlayerSpin;
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.bodyType = RigidbodyType2D.Kinematic;
                body.simulated = true;
            }

            portalPresentation?.SetCaptureProgress(1f);
            portalPresentation?.CompleteCapture();
            player.FlightState.TrySetState(PlayerFlightState.Disabled);
            FindFirstObjectByType<ScoreSystem>()?.Award(ScoreReason.LevelCompleted);
            extractionComplete = true;
            gameState?.EndGame(GameOverReason.ExtractionComplete);
            capturing = false;
            capturedBodyState = false;
            capturedPlayer = null;
        }

        void CancelCapture()
        {
            if (capturedBody && capturedBodyState)
            {
                capturedBody.position = captureStartPosition;
                capturedBody.rotation = captureStartBodyRotation;
                capturedBody.bodyType = capturedBodyType;
                capturedBody.constraints = capturedBodyConstraints;
                capturedBody.interpolation = capturedBodyInterpolation;
                capturedBody.collisionDetectionMode = capturedBodyCollisionMode;
                capturedBody.gravityScale = capturedBodyGravityScale;
                capturedBody.linearDamping = capturedBodyLinearDamping;
                capturedBody.angularDamping = capturedBodyAngularDamping;
                capturedBody.simulated = capturedBodySimulated;
                capturedBody.linearVelocity = Vector2.zero;
                capturedBody.angularVelocity = 0f;
            }
            else if (capturedPlayer)
            {
                capturedPlayer.transform.SetPositionAndRotation(
                    captureStartPosition,
                    Quaternion.Euler(0f, 0f, captureStartBodyRotation));
            }

            if (capturedVisual)
                capturedVisual.localScale = captureStartVisualScale;

            if (capturedColliders != null && capturedColliderStates != null)
            {
                for (int i = 0; i < capturedColliders.Length; i++)
                {
                    if (capturedColliders[i])
                        capturedColliders[i].enabled = capturedColliderStates[i];
                }
            }

            if (capturedRenderers != null && capturedRendererColors != null)
            {
                for (int i = 0; i < capturedRenderers.Length; i++)
                {
                    if (capturedRenderers[i])
                        capturedRenderers[i].color = capturedRendererColors[i];
                }
            }

            if (capturedPlayer && capturedPlayer.FlightState.CurrentState == PlayerFlightState.Scripted)
                capturedPlayer.FlightState.TrySetState(PlayerFlightState.FreeFlight);

            portalPresentation?.CancelCapture();
            capturedBodyState = false;
            capturedBody = null;
            capturedPlayer = null;
            capturing = false;
        }

        void UpdateVisual()
        {
            if (!exitRenderer)
                return;

            exitRenderer.startColor = IsUnlocked ? unlockedColor : lockedColor;
            exitRenderer.endColor = exitRenderer.startColor;
        }
    }
}

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
        [Tooltip("Leave disabled for the EE6 vertical slice. Enable only for a deliberately gate-free test room.")]
        [SerializeField] bool allowGateFreeExtraction;
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
        PlayerFlightPresentation capturedPresentation;
        EncounterController subscribedEncounter;
        EnergyGate subscribedGate;
        public bool IsCapturing => capturing;
        public bool IsComplete => extractionComplete;
        // EE5's door state is the extraction gate. Once the delivered key has
        // finished the authored lift and cleared the route, remaining enemies
        // are pressure rather than a hidden second exit condition.
        // A missing gate reference is a broken route contract, not permission
        // to skip the EE5 key/gate beat. Gate-free experiments can explicitly
        // opt out, but the presentable vertical slice fails closed while a
        // preserved prefab is still recovering its references.
        public bool IsUnlocked => requiredGate
            ? requiredGate.IsRouteClear
            : allowGateFreeExtraction;

        void Reset() => GetComponent<Collider2D>().isTrigger = true;

        void Awake()
        {
            ResolveReferences();
            exitRenderer = GetComponent<LineRenderer>();
            portalPresentation = GetComponent<ExtractionPortalPresentation>();
            HideLegacyExitOutline();
            UpdateVisual();
        }

        void OnEnable()
        {
            ResolveReferences();
            RefreshSubscriptions();
            UpdateVisual();
        }

        void OnDisable()
        {
            Unsubscribe();

            if (capturing && !extractionComplete)
                CancelCapture();
        }

        void ResolveReferences()
        {
            if (!encounter)
                encounter = FindFirstObjectByType<EncounterController>();
            if (!requiredGate)
                requiredGate = FindFirstObjectByType<EnergyGate>();
            if (!gameState)
                gameState = FindFirstObjectByType<GameStateMachine>();
        }

        void RefreshSubscriptions()
        {
            if (subscribedEncounter != encounter)
            {
                if (subscribedEncounter)
                    subscribedEncounter.Completed -= UpdateVisual;
                subscribedEncounter = encounter;
                if (subscribedEncounter)
                    subscribedEncounter.Completed += UpdateVisual;
            }

            if (subscribedGate != requiredGate)
            {
                if (subscribedGate)
                {
                    subscribedGate.Disabled -= UpdateVisual;
                    subscribedGate.RouteCleared -= UpdateVisual;
                }

                subscribedGate = requiredGate;
                if (subscribedGate)
                {
                    subscribedGate.Disabled += UpdateVisual;
                    subscribedGate.RouteCleared += UpdateVisual;
                }
            }
        }

        void Unsubscribe()
        {
            if (subscribedEncounter)
                subscribedEncounter.Completed -= UpdateVisual;
            if (subscribedGate)
            {
                subscribedGate.Disabled -= UpdateVisual;
                subscribedGate.RouteCleared -= UpdateVisual;
            }

            subscribedEncounter = null;
            subscribedGate = null;
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
            capturedPresentation = player.GetComponent<PlayerFlightPresentation>();
            capturedPresentation?.BeginExternalCapture();
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

            // Capture owns the player's Rigidbody pose. Advance that pose on the
            // physics clock, just like the key flight and gate lift, so a render
            // hitch cannot stretch the spiral or make the craft visibly fight
            // interpolation on its way into the portal.
            while (elapsed < captureDuration && player)
            {
                yield return new WaitForFixedUpdate();
                if (!capturing)
                    yield break;
                if (!player)
                    break;

                elapsed += Time.fixedDeltaTime;
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
                    Physics2D.SyncTransforms();
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
                Physics2D.SyncTransforms();
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
                Physics2D.SyncTransforms();
            }
            else if (capturedPlayer)
            {
                capturedPlayer.transform.SetPositionAndRotation(
                    captureStartPosition,
                    Quaternion.Euler(0f, 0f, captureStartBodyRotation));
            }

            if (capturedVisual)
                capturedVisual.localScale = captureStartVisualScale;

            capturedPresentation?.EndExternalCapture();

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
            capturedPresentation = null;
            capturing = false;
        }

        void UpdateVisual()
        {
            if (!exitRenderer)
                return;

            exitRenderer.startColor = IsUnlocked ? unlockedColor : lockedColor;
            exitRenderer.endColor = exitRenderer.startColor;
        }

        void HideLegacyExitOutline()
        {
            if (!exitRenderer || !portalPresentation)
                return;

            // Early FlightTest scenes used a four-point square LineRenderer as
            // a placeholder exit marker. The authored portal presentation now
            // supplies the locked/unlocked read, so suppress only that known
            // generated shape and leave hand-authored line art untouched.
            if (IsGeneratedSquareOutline())
                exitRenderer.enabled = false;
        }

        bool IsGeneratedSquareOutline()
        {
            if (exitRenderer.positionCount != 4)
                return false;

            Vector3 first = exitRenderer.GetPosition(0);
            Vector3 second = exitRenderer.GetPosition(1);
            Vector3 third = exitRenderer.GetPosition(2);
            Vector3 fourth = exitRenderer.GetPosition(3);
            const float tolerance = 0.02f;
            return Mathf.Abs(Mathf.Abs(first.x) - 0.45f) <= tolerance
                && Mathf.Abs(Mathf.Abs(first.y) - 0.45f) <= tolerance
                && Mathf.Abs(Mathf.Abs(second.x) - 0.45f) <= tolerance
                && Mathf.Abs(Mathf.Abs(second.y) - 0.45f) <= tolerance
                && Mathf.Abs(Mathf.Abs(third.x) - 0.45f) <= tolerance
                && Mathf.Abs(Mathf.Abs(third.y) - 0.45f) <= tolerance
                && Mathf.Abs(Mathf.Abs(fourth.x) - 0.45f) <= tolerance
                && Mathf.Abs(Mathf.Abs(fourth.y) - 0.45f) <= tolerance;
        }
    }
}

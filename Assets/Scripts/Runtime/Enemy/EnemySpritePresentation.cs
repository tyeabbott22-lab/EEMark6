using UnityEngine;
using ExtraterrestrialExhaust.Core;
using ExtraterrestrialExhaust.Player;

namespace ExtraterrestrialExhaust.Enemy
{
    /// <summary>
    /// Maps enemy simulation states to authored sprite frames.
    /// The controller owns movement and health; this component owns readability.
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class EnemySpritePresentation : MonoBehaviour
    {
        [SerializeField] SpriteRenderer spriteRenderer;
        [SerializeField] Sprite[] dormantSprites;
        [SerializeField] Sprite[] alertSprites;
        [SerializeField] Sprite[] activeSprites;
        [SerializeField] Sprite[] defeatedSprites;
        [SerializeField, Min(0.1f)] float animationFramesPerSecond = 10f;
        [SerializeField, Min(0.1f)] float dormantFramesPerSecond = 8f;
        [SerializeField, Min(0.1f)] float wakeFramesPerSecond = 14f;
        [SerializeField, Min(0f)] float defeatDisplayDuration = 0.3f;
        [SerializeField] bool pingPongDormantAnimation = true;
        [SerializeField] bool randomizeDormantStartFrame = true;
        [SerializeField] bool hideAfterDefeat = true;

        [Header("Dormant Facing")]
        [Tooltip("During EE5's dormant/wake intro, mirror this sprite toward the player before combat takes over. The melee prefab intentionally leaves this off: EE5 used a fixed intro mirror instead.")]
        [SerializeField] bool faceDormantTowardTarget;
        [Tooltip("Apply the EE5 fixed intro mirror to the dormant/wake strip, then restore the authored active flip on combat handoff.")]
        [SerializeField] bool invertDormantSpriteX;
        [Tooltip("The authored sprite points left in its unflipped pose, as the EE5 white gunner does.")]
        [SerializeField] bool forwardIsLocalNegativeX = true;
        [Tooltip("Restore the authored sprite flip as soon as the wake presentation hands control to combat.")]
        [SerializeField] bool restoreFacingAfterWake = true;
        [Tooltip("Horizontal deadband used while the dormant strip tracks the player, preventing left/right chatter at the midpoint.")]
        [SerializeField, Min(0f)] float dormantFacingHysteresis = Ee5SliceProfile.EnemyDormantFacingHysteresis;

        EnemyController controller;
        Renderer[] renderers;
        Sprite[] currentFrames;
        EnemyState currentState;
        int frameIndex;
        float frameTimer;
        float defeatTimer;
        bool hasWoken;
        bool waking;
        float wakeFrameTimer;
        int wakeFrameIndex;
        int dormantFrameDirection = 1;
        bool wakeAlertStarted;
        bool authoredFlipX;
        bool hasDormantFacing;
        bool dormantPlayerRight;

        void Reset()
        {
            controller = GetComponent<EnemyController>();
            spriteRenderer = ResolveVisibleSpriteRenderer();
        }

        void Awake()
        {
            if (!controller)
                controller = GetComponent<EnemyController>();
            if (!spriteRenderer || !spriteRenderer.sprite)
                spriteRenderer = ResolveVisibleSpriteRenderer();

            // Presentation used to carry its own copy of the authored forward
            // axis. If an older prefab says melee faces like the gunner, the
            // controller is the authoritative role-specific source at runtime.
            RefreshRoleFacingContract();

            renderers = GetComponentsInChildren<Renderer>(true);
            authoredFlipX = spriteRenderer && spriteRenderer.flipX;
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

                if (candidate.sprite && !candidate.name.Contains("Health"))
                    return candidate;

                if (!fallback && candidate.sprite)
                    fallback = candidate;
            }

            return fallback ? fallback : direct;
        }

        void Start()
        {
            // Unity does not guarantee Awake ordering between components on
            // one object. EnemyController may therefore finish its role
            // repair after this presentation's Awake. Re-read the contract at
            // Start so a stale serialized forward axis cannot leave the melee
            // sprite facing backward on its first wake.
            RefreshRoleFacingContract();
            ApplyDormantFacing();
            ApplyFrame();
        }

        void RefreshRoleFacingContract()
        {
            if (!controller)
                return;

            forwardIsLocalNegativeX = controller.ForwardIsLocalNegativeX;
            bool isMelee = controller.IsMelee;
            faceDormantTowardTarget = isMelee
                ? Ee5SliceProfile.EnemyMeleeFacesDormantTarget
                : true;
            invertDormantSpriteX = isMelee
                && Ee5SliceProfile.EnemyMeleeInvertsSpriteDuringIntro;
            dormantFacingHysteresis = Ee5SliceProfile.EnemyDormantFacingHysteresis;
        }

        void OnEnable()
        {
            if (controller)
            {
                controller.StateChanged += HandleStateChanged;
                ApplyState(controller.State);
            }
        }

        void OnDisable()
        {
            if (controller)
                controller.StateChanged -= HandleStateChanged;
        }

        void Update()
        {
            if (currentState == EnemyState.Defeated)
            {
                defeatTimer += Time.deltaTime;
                if (hideAfterDefeat && defeatTimer >= defeatDisplayDuration)
                    SetRenderersEnabled(false);
            }

            if (!hasWoken && !waking && currentState == EnemyState.Waking)
            {
                BeginWake();
            }

            if (waking)
            {
                UpdateDormantFacing();
                UpdateWakeAnimation();
                return;
            }

            UpdateDormantFacing();
            bool useDormantTiming = !hasWoken
                && currentState != EnemyState.Defeated;
            AdvanceRegularAnimation(
                useDormantTiming ? dormantFramesPerSecond : animationFramesPerSecond,
                useDormantTiming && pingPongDormantAnimation);
        }

        void HandleStateChanged(EnemyController source, EnemyState state)
        {
            ApplyState(state);
        }

        void ApplyState(EnemyState state)
        {
            EnemyState previousState = currentState;
            bool leavingWake = previousState == EnemyState.Waking
                && (state == EnemyState.Chasing || state == EnemyState.Attacking);
            bool preserveDormantFrame = state == EnemyState.Waking
                && previousState == EnemyState.Dormant
                && currentFrames != null
                && currentFrames.Length > 0;
            currentState = state;
            if (!preserveDormantFrame)
            {
                frameIndex = 0;
                frameTimer = 0f;
                dormantFrameDirection = 1;
            }
            defeatTimer = 0f;
            wakeAlertStarted = false;

            if (state == EnemyState.Dormant)
            {
                hasWoken = false;
                waking = false;
                hasDormantFacing = false;
                currentFrames = FirstAvailable(dormantSprites, activeSprites);
            }
            else if (state == EnemyState.Waking)
            {
                hasWoken = false;
                currentFrames = FirstAvailable(dormantSprites, activeSprites);
            }
            else if (state == EnemyState.Defeated)
            {
                waking = false;
                hasWoken = true;
                currentFrames = FirstAvailable(defeatedSprites, activeSprites);
            }
            else if (!hasWoken)
            {
                // Combat can begin at the detection leash, but the visual stays
                // dormant until the player reaches the authored wake distance.
                currentFrames = FirstAvailable(dormantSprites, activeSprites);
            }
            else
            {
                currentFrames = activeSprites;
            }

            if (state == EnemyState.Dormant
                && randomizeDormantStartFrame
                && currentFrames != null
                && currentFrames.Length > 0)
            {
                frameIndex = Random.Range(0, currentFrames.Length);
            }

            if (leavingWake)
            {
                // EnemyController commits the combat state on the physics
                // clock. Finish the authored intro on that same event instead
                // of leaving one render frame of idle/scream art visible
                // after the hunter has already started attacking.
                FinishWake();
                return;
            }

            if (restoreFacingAfterWake
                && state != EnemyState.Dormant
                && state != EnemyState.Waking
                && spriteRenderer)
            {
                spriteRenderer.flipX = authoredFlipX;
            }

            SetRenderersEnabled(true);
            ApplyDormantFacing();
            ApplyFrame();
            if (state == EnemyState.Waking)
                BeginWake();
        }

        void BeginWake()
        {
            waking = true;
            wakeFrameTimer = 0f;
            wakeFrameIndex = 0;
            // EE5 holds the authored idle strip through the buildup and only
            // enters the scream strip during the final warning window.
            currentFrames = FirstAvailable(dormantSprites, activeSprites);
            SetRenderersEnabled(true);
            ApplyDormantFacing();
            ApplyFrame();
        }

        void UpdateWakeAnimation()
        {
            if (!controller || controller.State != EnemyState.Waking)
            {
                FinishWake();
                return;
            }

            if (!wakeAlertStarted && controller.IsWakeFinalWarning)
            {
                wakeAlertStarted = true;
                wakeFrameTimer = 0f;
                wakeFrameIndex = 0;
                currentFrames = FirstAvailable(alertSprites, defeatedSprites);
                ApplyWakeFrame();
            }

            if (wakeAlertStarted)
                AdvanceWakeAnimation();
            else
                AdvanceRegularAnimation(dormantFramesPerSecond, pingPongDormantAnimation);
        }

        void AdvanceRegularAnimation(float framesPerSecond, bool pingPong)
        {
            if (currentFrames == null || currentFrames.Length <= 1)
                return;

            frameTimer += Time.deltaTime;
            float frameDuration = 1f / Mathf.Max(0.1f, framesPerSecond);
            while (frameTimer >= frameDuration)
            {
                frameTimer -= frameDuration;
                if (pingPong && currentFrames.Length > 2)
                {
                    if (frameIndex >= currentFrames.Length - 1)
                        dormantFrameDirection = -1;
                    else if (frameIndex <= 0)
                        dormantFrameDirection = 1;
                    frameIndex = Mathf.Clamp(
                        frameIndex + dormantFrameDirection,
                        0,
                        currentFrames.Length - 1);
                }
                else
                {
                    frameIndex = (frameIndex + 1) % currentFrames.Length;
                }

                ApplyFrame();
            }
        }

        void AdvanceWakeAnimation()
        {
            if (currentFrames == null || currentFrames.Length <= 1)
                return;

            wakeFrameTimer += Time.deltaTime;
            float frameDuration = 1f / Mathf.Max(0.1f, wakeFramesPerSecond);
            while (wakeFrameTimer >= frameDuration)
            {
                wakeFrameTimer -= frameDuration;
                wakeFrameIndex++;
                if (wakeFrameIndex >= currentFrames.Length)
                {
                    // The authored scream strip is allowed to hold its last
                    // frame until the controller finishes the wake state.
                    wakeFrameIndex = currentFrames.Length - 1;
                }
                ApplyWakeFrame();
            }
        }

        void FinishWake()
        {
            waking = false;
            hasWoken = true;
            frameIndex = 0;
            frameTimer = 0f;
            dormantFrameDirection = 1;
            currentFrames = activeSprites;
            if (restoreFacingAfterWake && spriteRenderer)
                spriteRenderer.flipX = authoredFlipX;
            ApplyFrame();
        }

        void UpdateDormantFacing()
        {
            if (!spriteRenderer || !controller)
                return;

            if (currentState != EnemyState.Dormant && currentState != EnemyState.Waking)
            {
                if (restoreFacingAfterWake)
                    spriteRenderer.flipX = authoredFlipX;
                return;
            }

            if (!faceDormantTowardTarget)
            {
                ApplyDormantFacing();
                return;
            }

            PlayerCharacter target = controller.Target;
            if (!target)
                return;

            // Enemy bodies and the player both interpolate for rendering. Use
            // their fixed-step positions for this role-facing decision so the
            // dormant/wake sprite cannot chatter when the player hovers on the
            // horizontal midpoint.
            bool playerIsRight = target.PhysicsPosition.x >= controller.PhysicsPosition.x;
            float horizontalDelta = target.PhysicsPosition.x - controller.PhysicsPosition.x;
            float hysteresis = Mathf.Max(0f, dormantFacingHysteresis);
            if (!hasDormantFacing)
            {
                dormantPlayerRight = horizontalDelta >= 0f;
                hasDormantFacing = true;
            }
            else if (dormantPlayerRight && horizontalDelta < -hysteresis)
            {
                dormantPlayerRight = false;
            }
            else if (!dormantPlayerRight && horizontalDelta > hysteresis)
            {
                dormantPlayerRight = true;
            }

            playerIsRight = dormantPlayerRight;
            bool flipFromDefault = forwardIsLocalNegativeX ? playerIsRight : !playerIsRight;
            spriteRenderer.flipX = authoredFlipX ^ flipFromDefault;
        }

        void ApplyDormantFacing()
        {
            if (!spriteRenderer || !controller)
                return;

            if (currentState == EnemyState.Dormant || currentState == EnemyState.Waking)
            {
                if (!faceDormantTowardTarget)
                    spriteRenderer.flipX = authoredFlipX ^ invertDormantSpriteX;
                return;
            }

            if (restoreFacingAfterWake)
                spriteRenderer.flipX = authoredFlipX;
        }

        void ApplyWakeFrame()
        {
            if (spriteRenderer && currentFrames != null && currentFrames.Length > 0)
                spriteRenderer.sprite = currentFrames[Mathf.Clamp(wakeFrameIndex, 0, currentFrames.Length - 1)];
        }

        void ApplyFrame()
        {
            if (spriteRenderer && currentFrames != null && currentFrames.Length > 0)
                spriteRenderer.sprite = currentFrames[Mathf.Clamp(frameIndex, 0, currentFrames.Length - 1)];
        }

        void SetRenderersEnabled(bool enabled)
        {
            if (renderers == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i]) renderers[i].enabled = enabled;
        }

        static Sprite[] FirstAvailable(Sprite[] preferred, Sprite[] fallback)
        {
            return preferred != null && preferred.Length > 0 ? preferred : fallback;
        }
    }
}

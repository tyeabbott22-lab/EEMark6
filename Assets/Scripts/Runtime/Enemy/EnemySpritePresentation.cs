using UnityEngine;

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
        [SerializeField, Min(0.1f)] float wakeFramesPerSecond = 14f;
        [SerializeField, Min(0f)] float defeatDisplayDuration = 0.3f;
        [SerializeField] bool hideAfterDefeat = true;

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

        void Reset()
        {
            controller = GetComponent<EnemyController>();
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        void Awake()
        {
            if (!controller)
                controller = GetComponent<EnemyController>();
            if (!spriteRenderer)
                spriteRenderer = GetComponent<SpriteRenderer>();
            renderers = GetComponentsInChildren<Renderer>(true);
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
                UpdateWakeAnimation();
                return;
            }

            if (currentFrames == null || currentFrames.Length <= 1)
                return;

            frameTimer += Time.deltaTime;
            float frameDuration = 1f / animationFramesPerSecond;
            if (frameTimer < frameDuration)
                return;

            frameTimer -= frameDuration;
            frameIndex = (frameIndex + 1) % currentFrames.Length;
            ApplyFrame();
        }

        void HandleStateChanged(EnemyController source, EnemyState state)
        {
            ApplyState(state);
        }

        void ApplyState(EnemyState state)
        {
            currentState = state;
            frameIndex = 0;
            frameTimer = 0f;
            defeatTimer = 0f;

            if (state == EnemyState.Dormant)
            {
                hasWoken = false;
                waking = false;
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

            SetRenderersEnabled(true);
            ApplyFrame();
            if (state == EnemyState.Waking)
                BeginWake();
        }

        void BeginWake()
        {
            waking = true;
            wakeFrameTimer = 0f;
            wakeFrameIndex = 0;
            currentFrames = FirstAvailable(alertSprites, defeatedSprites);
            SetRenderersEnabled(true);
            ApplyFrame();
        }

        void UpdateWakeAnimation()
        {
            wakeFrameTimer += Time.deltaTime;
            float frameDuration = 1f / wakeFramesPerSecond;
            if (currentFrames != null && currentFrames.Length > 1 && wakeFrameTimer >= frameDuration)
            {
                wakeFrameTimer -= frameDuration;
                wakeFrameIndex = (wakeFrameIndex + 1) % currentFrames.Length;
                ApplyWakeFrame();
            }

            if (controller && controller.State == EnemyState.Waking)
                return;

            waking = false;
            hasWoken = true;
            frameIndex = 0;
            frameTimer = 0f;
            currentFrames = activeSprites;
            ApplyFrame();
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

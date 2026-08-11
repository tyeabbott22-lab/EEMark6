using UnityEngine;

namespace ExtraterrestrialExhaust.Enemy
{
    /// <summary>
    /// Short-lived authored defeat animation. It is spawned as a separate object
    /// so the enemy's state and renderer cleanup cannot interrupt the effect.
    /// </summary>
    public sealed class EnemyDeathBurst : MonoBehaviour
    {
        SpriteRenderer spriteRenderer;
        Sprite[] frames;
        float frameDuration;
        int frameIndex;
        float frameTimer;

        public static void Spawn(
            Vector3 position,
            Sprite[] frames,
            float scale,
            int sortingOrder,
            Color color)
        {
            if (frames == null || frames.Length == 0)
                return;

            GameObject burstObject = new GameObject("Enemy Defeat Burst");
            burstObject.transform.position = position;
            burstObject.transform.localScale = Vector3.one * scale;

            SpriteRenderer renderer = burstObject.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = sortingOrder;
            renderer.color = color;

            EnemyDeathBurst burst = burstObject.AddComponent<EnemyDeathBurst>();
            burst.Initialize(renderer, frames, 0.05f);
        }

        void Initialize(SpriteRenderer renderer, Sprite[] sourceFrames, float secondsPerFrame)
        {
            spriteRenderer = renderer;
            frames = sourceFrames;
            frameDuration = Mathf.Max(0.001f, secondsPerFrame);
            ApplyFrame();
        }

        void Update()
        {
            if (!spriteRenderer || frames == null || frames.Length == 0)
            {
                Destroy(gameObject);
                return;
            }

            frameTimer += Time.deltaTime;
            while (frameTimer >= frameDuration)
            {
                frameTimer -= frameDuration;
                frameIndex++;
                if (frameIndex >= frames.Length)
                {
                    Destroy(gameObject);
                    return;
                }

                ApplyFrame();
            }
        }

        void ApplyFrame()
        {
            if (spriteRenderer && frames != null && frames.Length > 0)
                spriteRenderer.sprite = frames[Mathf.Clamp(frameIndex, 0, frames.Length - 1)];
        }
    }
}

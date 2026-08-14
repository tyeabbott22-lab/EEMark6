using System.Collections;
using UnityEngine;

namespace ExtraterrestrialExhaust.Combat
{
    /// <summary>Reusable hit flash for any line-rendered combat actor.</summary>
    [RequireComponent(typeof(HealthComponent))]
    public sealed class DamageFlashFeedback : MonoBehaviour
    {
        [SerializeField] Color flashColor = Color.white;
        [SerializeField, Min(0f)] float duration = 0.1f;

        HealthComponent health;
        LineRenderer[] renderers;
        Color[] originalColors;
        SpriteRenderer[] sprites;
        Color[] originalSpriteColors;
        Coroutine routine;

        void Awake()
        {
            health = GetComponent<HealthComponent>();
            renderers = GetComponentsInChildren<LineRenderer>(true);
            originalColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                originalColors[i] = renderers[i].startColor;
            sprites = GetComponentsInChildren<SpriteRenderer>(true);
            originalSpriteColors = new Color[sprites.Length];
            for (int i = 0; i < sprites.Length; i++)
                originalSpriteColors[i] = sprites[i].color;
        }

        void OnEnable()
        {
            if (health)
                health.Damaged += HandleDamaged;
        }

        void OnDisable()
        {
            if (health)
                health.Damaged -= HandleDamaged;

            // A pooled or temporarily disabled actor can leave the coroutine
            // halfway through its flash. Always restore authored colors so the
            // next encounter does not inherit a stale hit state.
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }
            RestoreColors();
        }

        void HandleDamaged(DamageInfo damage)
        {
            if (routine != null)
                StopCoroutine(routine);
            routine = StartCoroutine(FlashRoutine());
        }

        IEnumerator FlashRoutine()
        {
            SetColor(flashColor);
            yield return new WaitForSeconds(duration);
            RestoreColors();
            routine = null;
        }

        void SetColor(Color color)
        {
            foreach (LineRenderer line in renderers)
            {
                if (!line || line.gameObject.name == "Player Aim Line")
                    continue;
                line.startColor = color;
                line.endColor = color;
            }
            foreach (SpriteRenderer sprite in sprites)
                if (sprite)
                    sprite.color = color;
        }

        void RestoreColors()
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i] || renderers[i].gameObject.name == "Player Aim Line")
                    continue;
                renderers[i].startColor = originalColors[i];
                renderers[i].endColor = originalColors[i];
            }
            for (int i = 0; i < sprites.Length; i++)
                if (sprites[i])
                    sprites[i].color = originalSpriteColors[i];
        }
    }
}

using System.Collections;
using UnityEngine;
using ExtraterrestrialExhaust.Combat;
using ExtraterrestrialExhaust.CameraSystem;

namespace ExtraterrestrialExhaust.Player
{
    /// <summary>
    /// Translates health events into a short visual hit flash.
    /// Damage rules stay in HealthComponent; presentation stays here.
    /// </summary>
    [RequireComponent(typeof(HealthComponent))]
    public sealed class PlayerDamageFeedback : MonoBehaviour
    {
        [SerializeField] Color flashColor = new Color(1f, 0.01f, 0.01f, 1f);
        [SerializeField] Color alternateFlashColor = new Color(1f, 0.92f, 0.01f, 1f);
        [SerializeField, Min(0f)] float flashDuration = 0.4f;
        [SerializeField, Min(0.001f)] float flashInterval = 1f / 30f;

        HealthComponent health;
        LineRenderer[] renderers;
        Color[] startColors;
        SpriteRenderer[] sprites;
        Color[] startSpriteColors;
        Coroutine flashRoutine;

        void Awake()
        {
            health = GetComponent<HealthComponent>();
            renderers = GetComponentsInChildren<LineRenderer>(true);
            startColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                startColors[i] = renderers[i].startColor;
            sprites = GetComponentsInChildren<SpriteRenderer>(true);
            startSpriteColors = new Color[sprites.Length];
            for (int i = 0; i < sprites.Length; i++)
                startSpriteColors[i] = sprites[i].color;
        }

        void OnEnable()
        {
            if (health)
            {
                health.Damaged += HandleDamage;
                health.Died += HandleDeath;
            }
        }

        void OnDisable()
        {
            if (health)
            {
                health.Damaged -= HandleDamage;
                health.Died -= HandleDeath;
            }

            StopFlashAndRestore();
        }

        void HandleDamage(DamageInfo damage)
        {
            PlayerCameraFollow.Instance?.Shake(0.1f, 0.12f);
            if (flashRoutine != null)
                StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(FlashRoutine());
        }

        void HandleDeath()
        {
            // Death immediately owns the visual reset; respawn should never
            // inherit a hit flash from the previous life.
            StopFlashAndRestore();
        }

        IEnumerator FlashRoutine()
        {
            float elapsed = 0f;
            while (elapsed < flashDuration)
            {
                bool useAlternateColor = Mathf.FloorToInt(elapsed / flashInterval) % 2 == 1;
                SetColor(useAlternateColor ? alternateFlashColor : flashColor);
                elapsed += Time.deltaTime;
                yield return null;
            }

            RestoreColors();
            flashRoutine = null;
        }

        void StopFlashAndRestore()
        {
            if (flashRoutine != null)
            {
                StopCoroutine(flashRoutine);
                flashRoutine = null;
            }

            RestoreColors();
        }

        void SetColor(Color color)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (IsAimLine(renderers[i]))
                    continue;
                renderers[i].startColor = color;
                renderers[i].endColor = color;
            }
            for (int i = 0; i < sprites.Length; i++)
                sprites[i].color = color;
        }

        void RestoreColors()
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (IsAimLine(renderers[i]))
                    continue;
                renderers[i].startColor = startColors[i];
                renderers[i].endColor = startColors[i];
            }
            for (int i = 0; i < sprites.Length; i++)
                sprites[i].color = startSpriteColors[i];
        }

        static bool IsAimLine(LineRenderer renderer) =>
            renderer && renderer.gameObject.name == "Player Aim Line";
    }
}

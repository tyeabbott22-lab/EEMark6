using UnityEngine;
using ExtraterrestrialExhaust.Combat;

namespace ExtraterrestrialExhaust.Player
{
    /// <summary>
    /// Displays health without owning health rules. Sprite index zero represents
    /// full health; later entries represent progressively damaged states.
    /// </summary>
    [RequireComponent(typeof(HealthComponent))]
    public sealed class PlayerHealthDisplay : MonoBehaviour
    {
        [SerializeField] SpriteRenderer displayRenderer;
        [SerializeField] Sprite[] healthSprites;
        [SerializeField, Min(0f)] float showOnStartDuration = 0.8f;
        [SerializeField, Min(0f)] float showOnHitDuration = 0.6f;
        [SerializeField, Min(0f)] float fadeSpeed = 6f;

        HealthComponent health;
        float visibleRemaining;
        float alpha;

        void Awake()
        {
            health = GetComponent<HealthComponent>();
            if (!displayRenderer)
                displayRenderer = GetComponentInChildren<SpriteRenderer>(true);

            alpha = 0f;
            ApplyAlpha();
        }

        void Start()
        {
            // HealthComponent initializes during Awake; Start guarantees the first
            // visual read observes the authoritative current value regardless of
            // component ordering on the player.
            if (!health)
                return;

            UpdateDisplay(health.CurrentHealth);
            ShowFor(showOnStartDuration);
        }

        void OnEnable()
        {
            if (health)
            {
                health.Damaged += HandleDamaged;
                health.HealthChanged += UpdateDisplay;
                health.Died += HandleDied;
            }
        }

        void OnDisable()
        {
            if (health)
            {
                health.Damaged -= HandleDamaged;
                health.HealthChanged -= UpdateDisplay;
                health.Died -= HandleDied;
            }
        }

        void Update()
        {
            if (!health || !displayRenderer)
                return;

            visibleRemaining = Mathf.Max(0f, visibleRemaining - Time.deltaTime);
            float targetAlpha = health.IsAlive && visibleRemaining > 0f ? 1f : 0f;
            alpha = Mathf.MoveTowards(alpha, targetAlpha, fadeSpeed * Time.deltaTime);
            ApplyAlpha();
        }

        void HandleDamaged(DamageInfo damage)
        {
            ShowFor(showOnHitDuration);
        }

        void HandleDied()
        {
            visibleRemaining = 0f;
            alpha = 0f;
            ApplyAlpha();
        }

        void ShowFor(float duration)
        {
            visibleRemaining = Mathf.Max(visibleRemaining, duration);
        }

        void UpdateDisplay(float currentHealth)
        {
            if (!displayRenderer || healthSprites == null || healthSprites.Length == 0)
                return;

            int damageIndex = Mathf.RoundToInt(health.MaxHealth - currentHealth);
            damageIndex = Mathf.Clamp(damageIndex, 0, healthSprites.Length - 1);
            displayRenderer.sprite = healthSprites[damageIndex];
            ShowFor(showOnHitDuration);
        }

        void ApplyAlpha()
        {
            if (!displayRenderer)
                return;

            displayRenderer.enabled = alpha > 0.001f;
            Color color = displayRenderer.color;
            color.a = alpha;
            displayRenderer.color = color;
        }
    }
}

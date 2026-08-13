using UnityEngine;
using ExtraterrestrialExhaust.Combat;

namespace ExtraterrestrialExhaust.Enemy
{
    /// <summary>
    /// Presents enemy health briefly at spawn and after damage.
    /// HealthComponent remains authoritative; this component only selects frames
    /// and fades the world-space display.
    /// </summary>
    [RequireComponent(typeof(HealthComponent))]
    public sealed class EnemyHealthDisplay : MonoBehaviour
    {
        [SerializeField] SpriteRenderer displayRenderer;
        [SerializeField] Sprite[] healthSprites;
        [SerializeField, Min(0f)] float showOnStartDuration = 0.8f;
        [SerializeField, Min(0f)] float showOnHitDuration = 0.6f;
        [SerializeField, Min(0f)] float fadeSpeed = 6f;
        [SerializeField] float rotationSpeed = 90f;

        HealthComponent health;
        float visibleRemaining;
        float alpha;

        void Awake()
        {
            health = GetComponent<HealthComponent>();
            if (!displayRenderer)
            {
                // The EE5 visual-size bridge adds a SpriteRenderer child at
                // runtime. Prefer the authored health socket before falling
                // back to a broad child search, otherwise an older prefab
                // with a missing serialized reference can accidentally bind
                // the disabled source/enemy renderer as its health display.
                Transform healthObject = transform.Find("Health Display");
                if (healthObject)
                    displayRenderer = healthObject.GetComponent<SpriteRenderer>();

                if (!displayRenderer)
                    displayRenderer = GetComponentInChildren<SpriteRenderer>(true);
            }

            alpha = 0f;
            ApplyAlpha();
        }

        void Start()
        {
            if (!health)
                return;

            UpdateSprite(health.CurrentHealth);
            ShowFor(showOnStartDuration);
        }

        void OnEnable()
        {
            if (!health)
                return;

            health.Damaged += HandleDamaged;
            health.HealthChanged += UpdateSprite;
            health.Died += HandleDied;
        }

        void OnDisable()
        {
            if (!health)
                return;

            health.Damaged -= HandleDamaged;
            health.HealthChanged -= UpdateSprite;
            health.Died -= HandleDied;
        }

        void Update()
        {
            if (!health || !displayRenderer)
                return;

            visibleRemaining = Mathf.Max(0f, visibleRemaining - Time.deltaTime);
            float targetAlpha = health.IsAlive && visibleRemaining > 0f ? 1f : 0f;
            alpha = Mathf.MoveTowards(alpha, targetAlpha, fadeSpeed * Time.deltaTime);
            ApplyAlpha();

            if (displayRenderer && Mathf.Abs(rotationSpeed) > 0.001f)
                displayRenderer.transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);
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

        void UpdateSprite(float currentHealth)
        {
            if (!displayRenderer || healthSprites == null || healthSprites.Length == 0 || !health)
                return;

            // EE5 used a six-frame sheet for enemies with different max-health
            // values. Map the actual health pool onto that sheet instead of
            // assuming one sprite frame equals one point of damage.
            int maxVisiblePips = healthSprites.Length - 1;
            int visiblePips = currentHealth <= maxVisiblePips
                ? Mathf.CeilToInt(currentHealth)
                : Mathf.CeilToInt(currentHealth / health.MaxHealth * maxVisiblePips);
            int damageIndex = currentHealth <= 0f
                ? maxVisiblePips
                : maxVisiblePips - visiblePips;
            damageIndex = Mathf.Clamp(damageIndex, 0, maxVisiblePips);
            displayRenderer.sprite = healthSprites[damageIndex];
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

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

        HealthComponent health;

        void Awake()
        {
            health = GetComponent<HealthComponent>();
            if (!displayRenderer)
                displayRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }

        void Start()
        {
            // HealthComponent initializes during Awake; Start guarantees the first
            // visual read observes the authoritative current value regardless of
            // component ordering on the player.
            if (health)
                UpdateDisplay(health.CurrentHealth);
        }

        void OnEnable()
        {
            if (health)
                health.HealthChanged += UpdateDisplay;
        }

        void OnDisable()
        {
            if (health)
                health.HealthChanged -= UpdateDisplay;
        }

        void UpdateDisplay(float currentHealth)
        {
            if (!displayRenderer || healthSprites == null || healthSprites.Length == 0)
                return;

            int damageIndex = Mathf.RoundToInt(health.MaxHealth - currentHealth);
            damageIndex = Mathf.Clamp(damageIndex, 0, healthSprites.Length - 1);
            displayRenderer.sprite = healthSprites[damageIndex];
            displayRenderer.enabled = currentHealth < health.MaxHealth;
        }
    }
}

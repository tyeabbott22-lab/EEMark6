using UnityEngine;

namespace ExtraterrestrialExhaust.Player
{
    /// <summary>
    /// Shared idle and collection presentation for optional room pickups.
    /// Pickup rules remain in HealthPickup and FireRatePickup; this component
    /// only makes a successful pickup legible to the player.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class PickupPresentation : MonoBehaviour
    {
        [SerializeField] Color accentColor = new Color(0.2f, 1f, 0.45f, 1f);
        [SerializeField, Min(0f)] float bobHeight = 0.1f;
        [SerializeField, Min(0f)] float bobSpeed = 2.8f;
        [SerializeField, Min(0f)] float rotateSpeed = 36f;
        [SerializeField, Range(0f, 0.5f)] float pulseAmount = 0.06f;

        HealthPickup healthPickup;
        FireRatePickup fireRatePickup;
        SpriteRenderer spriteRenderer;
        Vector3 baseLocalPosition;
        Vector3 baseScale;
        float phase;
        bool collected;

        void Awake()
        {
            healthPickup = GetComponent<HealthPickup>();
            fireRatePickup = GetComponent<FireRatePickup>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            baseLocalPosition = transform.localPosition;
            baseScale = transform.localScale;
            phase = Random.Range(0f, Mathf.PI * 2f);
        }

        void OnEnable()
        {
            if (healthPickup)
                healthPickup.Collected += HandleCollected;
            if (fireRatePickup)
                fireRatePickup.Collected += HandleCollected;
        }

        void OnDisable()
        {
            if (healthPickup)
                healthPickup.Collected -= HandleCollected;
            if (fireRatePickup)
                fireRatePickup.Collected -= HandleCollected;
        }

        void Update()
        {
            if (collected)
                return;

            float time = Time.time + phase;
            transform.localPosition = baseLocalPosition + Vector3.up *
                (Mathf.Sin(time * bobSpeed) * bobHeight);
            transform.Rotate(0f, 0f, rotateSpeed * Time.deltaTime);
            transform.localScale = baseScale *
                (1f + Mathf.Sin(time * bobSpeed * 1.7f) * pulseAmount);
        }

        void HandleCollected()
        {
            if (collected)
                return;

            collected = true;
            foreach (Collider2D collider in GetComponentsInChildren<Collider2D>(true))
                if (collider)
                    collider.enabled = false;

            PickupCollectionBurst.Spawn(
                transform.position,
                accentColor,
                spriteRenderer ? spriteRenderer.sprite : null);
        }
    }
}

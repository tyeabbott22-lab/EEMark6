using UnityEngine;

namespace ExtraterrestrialExhaust.Player
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class FireRatePickup : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] float duration = 5f;
        [SerializeField, Min(1f)] float multiplier = 2f;

        void Reset() => GetComponent<Collider2D>().isTrigger = true;

        void OnTriggerEnter2D(Collider2D other)
        {
            PlayerWeapon weapon = other.GetComponentInParent<PlayerWeapon>();
            PlayerCharacter player = other.GetComponentInParent<PlayerCharacter>();
            if (!weapon || !player || !player.CanReceiveGameplayInput)
                return;

            weapon.ApplyFireRateBoost(duration, multiplier);
            Destroy(gameObject);
        }
    }
}

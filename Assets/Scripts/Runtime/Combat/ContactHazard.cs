using UnityEngine;
using ExtraterrestrialExhaust.Player;

namespace ExtraterrestrialExhaust.Combat
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class ContactHazard : MonoBehaviour
    {
        [SerializeField, Min(0f)] float damage = 1f;
        [SerializeField, Min(0.01f)] float damageCooldown = 0.5f;
        float nextDamageTime;

        void Reset() => GetComponent<Collider2D>().isTrigger = true;

        void OnTriggerStay2D(Collider2D other)
        {
            PlayerCharacter player = other.GetComponentInParent<PlayerCharacter>();
            if (!player || !player.CanReceiveGameplayInput || Time.time < nextDamageTime)
                return;

            if (player.Health.TryTakeDamage(new DamageInfo(
                damage, DamageType.Hazard, gameObject, other.ClosestPoint(transform.position))))
            {
                nextDamageTime = Time.time + damageCooldown;
            }
        }
    }
}

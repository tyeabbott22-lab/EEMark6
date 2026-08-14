using System;
using UnityEngine;
using ExtraterrestrialExhaust.Player;

namespace ExtraterrestrialExhaust.Combat
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class ContactHazard : MonoBehaviour
    {
        [SerializeField, Min(0f)] float damage = 1f;
        [SerializeField, Min(0.01f)] float damageCooldown = 0.5f;
        [SerializeField, Min(0f)] float knockback = 7f;
        float nextDamageTime;

        public event Action<DamageInfo> PlayerDamaged;

        void Reset() => GetComponent<Collider2D>().isTrigger = true;

        void OnTriggerStay2D(Collider2D other)
        {
            PlayerCharacter player = other.GetComponentInParent<PlayerCharacter>();
            if (!player || !player.CanReceiveGameplayInput || Time.time < nextDamageTime)
                return;

            Vector2 direction = ((Vector2)player.transform.position -
                (Vector2)transform.position).normalized;
            if (direction.sqrMagnitude <= 0.001f)
                direction = Vector2.up;

            DamageInfo damageInfo = new DamageInfo(
                damage,
                DamageType.Hazard,
                gameObject,
                other.ClosestPoint(transform.position),
                direction,
                knockback);

            if (player.Health.TryTakeDamage(damageInfo))
            {
                if (player.Health.IsAlive && player.FlightMotor && player.FlightMotor.Body)
                    player.FlightMotor.Body.linearVelocity += direction * knockback;

                nextDamageTime = Time.time + damageCooldown;
                PlayerDamaged?.Invoke(damageInfo);
            }
        }
    }
}

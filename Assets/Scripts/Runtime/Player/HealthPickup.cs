using System;
using UnityEngine;

namespace ExtraterrestrialExhaust.Player
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class HealthPickup : MonoBehaviour
    {
        [SerializeField, Min(1f)] float healAmount = 3f;

        public event Action Collected;

        void Reset() => GetComponent<Collider2D>().isTrigger = true;

        void OnTriggerEnter2D(Collider2D other)
        {
            PlayerCharacter player = other.GetComponentInParent<PlayerCharacter>();
            if (!CanCollect(player) || !player.Health.TryRestore(healAmount))
                return;

            Collected?.Invoke();
            Destroy(gameObject);
        }

        static bool CanCollect(PlayerCharacter player)
        {
            return player && player.CanReceiveGameplayInput;
        }
    }
}

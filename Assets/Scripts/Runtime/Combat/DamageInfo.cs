using UnityEngine;

namespace ExtraterrestrialExhaust.Combat
{
    /// <summary>
    /// Immutable context passed through the damage system.
    /// Adding metadata here keeps damage receivers small and consistent.
    /// </summary>
    public readonly struct DamageInfo
    {
        public DamageInfo(
            float amount,
            DamageType type,
            GameObject source = null,
            Vector2 hitPoint = default,
            Vector2 direction = default,
            float knockback = 0f)
        {
            Amount = Mathf.Max(0f, amount);
            Type = type;
            Source = source;
            HitPoint = hitPoint;
            Direction = direction;
            Knockback = Mathf.Max(0f, knockback);
        }

        public float Amount { get; }
        public DamageType Type { get; }
        public GameObject Source { get; }
        public Vector2 HitPoint { get; }
        public Vector2 Direction { get; }
        public float Knockback { get; }
    }
}

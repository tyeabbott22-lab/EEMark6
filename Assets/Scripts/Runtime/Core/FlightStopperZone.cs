using UnityEngine;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// Identifies an authored no-flight volume. The player motor owns the
    /// control-state transition; the environment owns which colliders grant
    /// that rule, avoiding a scene-wide dependency on object names or tags.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class FlightStopperZone : MonoBehaviour
    {
        Collider2D zoneCollider;

        public Collider2D ZoneCollider => zoneCollider
            ? zoneCollider
            : zoneCollider = GetComponent<Collider2D>();
        public bool IsTriggerVolume => ZoneCollider && ZoneCollider.isTrigger;

        void Reset()
        {
            Collider2D collider = GetComponent<Collider2D>();
            if (collider)
                collider.isTrigger = true;
        }

        void Awake()
        {
            zoneCollider = GetComponent<Collider2D>();
        }
    }
}

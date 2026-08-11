using System.Collections;
using System;
using UnityEngine;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>Reusable blocking energy gate that lifts away when deactivated.</summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class EnergyGate : MonoBehaviour
    {
        // EE5's DoorController clears the route by lifting the authored door,
        // not by scaling it out. Keep the motion readable for the key-to-exit
        // handoff and let the collider disable immediately at activation.
        [SerializeField, Min(0f)] float liftDistance = 12f;
        [SerializeField, Min(0.01f)] float liftSpeed = 6f;
        [SerializeField] Color activeColor = new Color(0.2f, 0.55f, 1f);
        [SerializeField] Color disabledColor = new Color(0.2f, 1f, 0.85f);

        BoxCollider2D gateCollider;
        LineRenderer line;
        Vector3 targetPosition;
        bool disabled;

        public bool IsDisabled => disabled;
        public event Action Disabled;

        void Awake()
        {
            gateCollider = GetComponent<BoxCollider2D>();
            line = GetComponent<LineRenderer>();
            targetPosition = transform.position + Vector3.up * liftDistance;
            UpdateVisual(activeColor);
        }

        public void DisableGate()
        {
            if (disabled)
                return;

            disabled = true;
            gateCollider.enabled = false;
            Disabled?.Invoke();
            StartCoroutine(LiftRoutine());
        }

        IEnumerator LiftRoutine()
        {
            while (Vector3.Distance(transform.position, targetPosition) > 0.01f)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    targetPosition,
                    liftSpeed * Time.deltaTime);
                yield return null;
            }

            transform.position = targetPosition;
            UpdateVisual(disabledColor);
        }

        void UpdateVisual(Color color)
        {
            if (!line)
                return;

            line.startColor = color;
            line.endColor = color;
        }
    }
}

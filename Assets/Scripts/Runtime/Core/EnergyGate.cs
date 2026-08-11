using System.Collections;
using System;
using UnityEngine;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>Reusable blocking energy gate that retreats when deactivated.</summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class EnergyGate : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] float retreatDuration = 0.5f;
        [SerializeField] Color activeColor = new Color(0.2f, 0.55f, 1f);
        [SerializeField] Color disabledColor = new Color(0.2f, 1f, 0.85f);

        BoxCollider2D gateCollider;
        LineRenderer line;
        Vector3 initialScale;
        bool disabled;

        public bool IsDisabled => disabled;
        public event Action Disabled;

        void Awake()
        {
            gateCollider = GetComponent<BoxCollider2D>();
            line = GetComponent<LineRenderer>();
            initialScale = transform.localScale;
            UpdateVisual(activeColor);
        }

        public void DisableGate()
        {
            if (disabled)
                return;

            disabled = true;
            gateCollider.enabled = false;
            Disabled?.Invoke();
            StartCoroutine(RetreatRoutine());
        }

        IEnumerator RetreatRoutine()
        {
            float elapsed = 0f;
            while (elapsed < retreatDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / retreatDuration);
                transform.localScale = Vector3.Lerp(initialScale, Vector3.zero, t);
                if (line)
                    UpdateVisual(Color.Lerp(activeColor, disabledColor, t));
                yield return null;
            }

            transform.localScale = Vector3.zero;
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

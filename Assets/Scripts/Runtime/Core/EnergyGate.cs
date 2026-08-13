using System.Collections;
using System;
using UnityEngine;

namespace ExtraterrestrialExhaust.Core
{
    public enum EnergyGateState
    {
        Closed,
        Opening,
        Open
    }

    /// <summary>Reusable blocking energy gate that lifts away when deactivated.</summary>
    // The gate owns the blocking collider and its physics-clock lift. Run its
    // handoff before the key transport so the key always reads the current
    // socket/route state rather than a one-frame-old barrier pose.
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class EnergyGate : MonoBehaviour
    {
        // EE5's DoorController clears the route by lifting the authored door,
        // not by scaling it out. Keep the motion readable for the key-to-exit
        // handoff and keep collision active until the lift has finished.
        [SerializeField] bool enforceEe5Profile = true;
        [SerializeField, Min(0f)] float liftDistance = Ee5SliceProfile.EnergyGateLiftDistance;
        [SerializeField, Min(0.01f)] float liftSpeed = Ee5SliceProfile.EnergyGateLiftSpeed;

        [Header("Key Handoff")]
        [SerializeField] Transform keyTarget;

        [SerializeField] Color activeColor = new Color(0.2f, 0.55f, 1f);
        [SerializeField] Color disabledColor = new Color(0.2f, 1f, 0.85f);

        BoxCollider2D gateCollider;
        LineRenderer line;
        Vector3 closedPosition;
        Vector3 targetPosition;
        EnergyGateState state = EnergyGateState.Closed;
        Coroutine liftRoutine;

        public EnergyGateState State => state;
        public bool IsDisabled => state != EnergyGateState.Closed;
        /// <summary>
        /// True during the authored lift window: the key has activated the
        /// gate, but the blocking collider has not finished leaving the route.
        /// </summary>
        public bool IsOpening => state == EnergyGateState.Opening;
        /// <summary>
        /// True only after the authored lift finishes and the blocking collider
        /// is actually disabled. Objective flow and extraction use this rather
        /// than treating key impact as an instantaneous teleport.
        /// </summary>
        public bool IsRouteClear => state == EnergyGateState.Open;
        public float OpeningProgress
        {
            get
            {
                if (state == EnergyGateState.Open)
                    return 1f;
                if (state == EnergyGateState.Closed || liftDistance <= 0f)
                    return 0f;

                return Mathf.Clamp01(1f - Vector3.Distance(transform.position, closedPosition) / liftDistance);
            }
        }
        public float BarrierHeight => gateCollider ? gateCollider.size.y : 0f;
        public Transform KeyTarget => keyTarget ? keyTarget : transform;
        public event Action Disabled;
        public event Action RouteCleared;

        void Awake()
        {
            gateCollider = GetComponent<BoxCollider2D>();

            if (enforceEe5Profile)
            {
                liftDistance = Ee5SliceProfile.EnergyGateLiftDistance;
                liftSpeed = Ee5SliceProfile.EnergyGateLiftSpeed;
                if (gateCollider)
                {
                    gateCollider.size = Ee5SliceProfile.VerticalSliceGateColliderSize;
                    gateCollider.offset = Vector2.zero;
                    gateCollider.isTrigger = false;
                }
            }

            ResolveKeyTarget();
            line = GetComponent<LineRenderer>();
            closedPosition = transform.position;
            targetPosition = closedPosition + Vector3.up * liftDistance;
            if (gateCollider && !gateCollider.enabled)
                state = EnergyGateState.Open;
            UpdateVisual(state == EnergyGateState.Open ? disabledColor : activeColor);
        }

        void ResolveKeyTarget()
        {
            if (!keyTarget)
                keyTarget = transform.Find("Key Target");

            if (!keyTarget && enforceEe5Profile)
            {
                GameObject targetObject = new GameObject("Key Target");
                keyTarget = targetObject.transform;
                keyTarget.SetParent(transform, false);
            }

            if (!keyTarget || !enforceEe5Profile)
                return;

            // The key socket belongs to the gate, not the room root. Repairing
            // its local pose here keeps older hand-built scenes aligned with
            // the builder and prevents a missing/stale reference from making
            // the key fly into empty space.
            keyTarget.localPosition = Ee5SliceProfile.VerticalSliceGateKeyTarget;
            keyTarget.localRotation = Quaternion.identity;
            keyTarget.localScale = Vector3.one;
        }

        void OnDisable()
        {
            if (liftRoutine != null)
                StopCoroutine(liftRoutine);
            liftRoutine = null;

            // A disabled scene object can interrupt the lift coroutine. Reset
            // that half-open state so re-enabling the room cannot leave the
            // collider, visual, and objective director disagreeing.
            if (state == EnergyGateState.Opening)
            {
                transform.position = closedPosition;
                targetPosition = closedPosition + Vector3.up * liftDistance;
                if (gateCollider)
                    gateCollider.enabled = true;
                state = EnergyGateState.Closed;
            }
        }

        /// <summary>
        /// Accepts the key handoff once. Returning the acceptance result lets
        /// the key keep its transport state if a scene re-enable or another
        /// caller catches the gate between its closed and opening states.
        /// </summary>
        public bool TryDisableGate()
        {
            if (state == EnergyGateState.Opening || state == EnergyGateState.Open)
                return true;

            if (state != EnergyGateState.Closed)
                return false;

            state = EnergyGateState.Opening;
            // EE5's DoorController opens by moving the wall; it does not
            // turn the collider off at the key impact. Keep the collider on
            // while the authored lift is visible so the physical route and
            // the presentation cannot disagree during the handoff.
            Disabled?.Invoke();
            liftRoutine = StartCoroutine(LiftRoutine());
            return true;
        }

        // Keep the original scene/script entry point intact for hand-authored
        // callers. Gameplay code that owns a transport object should use the
        // result-returning method above before consuming that object.
        public void DisableGate() => TryDisableGate();

        IEnumerator LiftRoutine()
        {
            // EnergyKey transports on FixedUpdate and resolves this moving
            // socket from the gate hierarchy. Advance the gate on the same
            // physics clock so the blocker, socket, and key never disagree
            // for a render frame during the authored lift.
            while (Vector3.Distance(transform.position, targetPosition) > 0.01f)
            {
                yield return new WaitForFixedUpdate();

                if (state != EnergyGateState.Opening)
                    yield break;

                transform.position = Vector3.MoveTowards(
                    transform.position,
                    targetPosition,
                    liftSpeed * Time.fixedDeltaTime);
                // This gate is an authored moving BoxCollider2D without a
                // Rigidbody2D. Explicitly synchronize the physics scene so the
                // blocker, key socket, and visual lift agree during the handoff
                // even when Unity's automatic transform sync is disabled.
                Physics2D.SyncTransforms();
            }

            transform.position = targetPosition;
            Physics2D.SyncTransforms();
            if (gateCollider)
                gateCollider.enabled = false;
            liftRoutine = null;
            state = EnergyGateState.Open;
            RouteCleared?.Invoke();
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

using UnityEngine;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// Makes the EE5 key handoff readable without owning any key rules:
    /// release, collection, and gate flight each receive a distinct beat, and
    /// the active key keeps a soft tether to its current gameplay target.
    /// </summary>
    [RequireComponent(typeof(EnergyKey))]
    public sealed class EnergyKeyPresentation : MonoBehaviour
    {
        [SerializeField] Color availableColor = new Color(1f, 0.85f, 0.15f, 1f);
        [SerializeField] Color tetherColor = new Color(1f, 0.92f, 0.3f, 0.7f);
        [SerializeField, Min(0f)] float tetherWidth = 0.035f;
        [SerializeField, Min(0f)] float tetherPulseSpeed = 7f;
        [SerializeField, Range(0f, 1f)] float tetherMinAlpha = 0.2f;
        [SerializeField, Range(0f, 1f)] float tetherMaxAlpha = 0.72f;

        EnergyKey energyKey;
        LineRenderer tether;

        void Awake()
        {
            energyKey = GetComponent<EnergyKey>();
            EnsureTether();
        }

        void OnEnable()
        {
            if (energyKey)
            {
                energyKey.StateChanged += HandleStateChanged;
                SyncCurrentState();
            }
        }

        void OnDisable()
        {
            if (energyKey)
                energyKey.StateChanged -= HandleStateChanged;
        }

        void LateUpdate()
        {
            if (!tether || !energyKey)
                return;

            Transform target = ResolveTetherTarget();
            if (!target)
            {
                tether.enabled = false;
                return;
            }

            tether.enabled = true;
            tether.SetPosition(0, energyKey.VisualPosition);
            tether.SetPosition(1, target.position);

            float pulse = 0.5f + Mathf.Sin(Time.time * tetherPulseSpeed) * 0.5f;
            Color color = tetherColor;
            color.a = Mathf.Lerp(tetherMinAlpha, tetherMaxAlpha, pulse);
            tether.startColor = color;
            tether.endColor = color;
            tether.startWidth = tetherWidth * Mathf.Lerp(0.8f, 1.2f, pulse);
            tether.endWidth = tether.startWidth * 0.12f;
        }

        void HandleStateChanged(EnergyKeyState previous, EnergyKeyState next)
        {
            switch (next)
            {
                case EnergyKeyState.OrbitingPlayer:
                    ObjectiveSignalBurst.Spawn(transform.position, availableColor, 0.8f);
                    break;
                case EnergyKeyState.FollowingPlayer:
                    ObjectiveSignalBurst.Spawn(transform.position, availableColor, 1f);
                    break;
                case EnergyKeyState.FlyingToGate:
                    ObjectiveSignalBurst.Spawn(transform.position, availableColor, 1.15f);
                    if (energyKey.TargetGate)
                    {
                        EnergyGatePresentation gatePresentation =
                            energyKey.TargetGate.GetComponent<EnergyGatePresentation>();
                        gatePresentation?.BeginKeyApproach();
                    }
                    break;
            }
        }

        void SyncCurrentState()
        {
            // EnergyKey can resolve an already-defeated carrier in OnEnable
            // before this companion receives its event subscription. Recover
            // only the persistent approach cue here; transient signal bursts
            // should remain event-driven and must not replay on scene reload.
            if (energyKey.State != EnergyKeyState.FlyingToGate || !energyKey.TargetGate)
                return;

            energyKey.TargetGate.GetComponent<EnergyGatePresentation>()?.BeginKeyApproach();
        }

        Transform ResolveTetherTarget()
        {
            if (energyKey.State == EnergyKeyState.FollowingPlayer)
                return energyKey.CurrentPlayer ? energyKey.CurrentPlayer.transform : null;

            if (energyKey.State == EnergyKeyState.FlyingToGate)
                return energyKey.TargetGate
                    ? energyKey.TargetGate.KeyTarget
                    : null;

            return null;
        }

        void EnsureTether()
        {
            GameObject tetherObject = new GameObject("Energy Key Tether");
            tetherObject.transform.SetParent(transform, false);
            tether = tetherObject.AddComponent<LineRenderer>();
            tether.useWorldSpace = true;
            tether.positionCount = 2;
            tether.startWidth = tetherWidth;
            tether.endWidth = tetherWidth * 0.12f;
            tether.startColor = tetherColor;
            tether.endColor = tetherColor;
            tether.sortingOrder = 17;
            tether.sharedMaterial = RuntimeVisualMaterial.Create("Energy Key Tether");
            tether.enabled = false;
        }

        void OnDestroy()
        {
            if (tether && tether.sharedMaterial)
                Destroy(tether.sharedMaterial);
        }
    }
}

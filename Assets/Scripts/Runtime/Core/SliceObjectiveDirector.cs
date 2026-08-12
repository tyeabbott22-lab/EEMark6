using System;
using UnityEngine;

namespace ExtraterrestrialExhaust.Core
{
    public enum SliceObjectiveState
    {
        ClearEncounter,
        CollectEnergyKey,
        OpenExtractionGate,
        ReachExtraction,
        ExtractionComplete
    }

    /// <summary>
    /// Owns the authored EE5 vertical-slice objective sequence.
    /// Gameplay systems publish facts; this component turns those facts into
    /// one stable state for HUD, presentation, and future level flow.
    /// </summary>
    public sealed class SliceObjectiveDirector : MonoBehaviour
    {
        [SerializeField] EncounterController encounter;
        [SerializeField] EnergyKey energyKey;
        [SerializeField] EnergyGate gate;
        [SerializeField] LevelExit exit;
        [SerializeField] GameStateMachine gameState;

        public SliceObjectiveState CurrentState { get; private set; } = SliceObjectiveState.ClearEncounter;
        public string CurrentObjective => GetObjectiveLabel(CurrentState);
        public event Action<SliceObjectiveState, SliceObjectiveState> ObjectiveChanged;

        void Awake()
        {
            ResolveReferences();
            Refresh();
        }

        void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        void OnDisable() => Unsubscribe();

        public void Refresh()
        {
            ResolveReferences();

            SliceObjectiveState nextState;
            if (exit && exit.IsComplete)
            {
                nextState = SliceObjectiveState.ExtractionComplete;
            }
            else if (!exit && gameState && gameState.CurrentState == GameState.GameOver)
            {
                // A scene without an authored exit can still use this director
                // as a lightweight state adapter. Generated FlightTest scenes
                // always have an exit, so their completion state comes from the
                // portal capture contract above rather than GameOver alone.
                nextState = SliceObjectiveState.ExtractionComplete;
            }
            else if (exit && exit.IsUnlocked)
            {
                nextState = SliceObjectiveState.ReachExtraction;
            }
            else if (energyKey && energyKey.IsCollected)
            {
                nextState = SliceObjectiveState.OpenExtractionGate;
            }
            else if (energyKey && energyKey.IsAvailable)
            {
                nextState = SliceObjectiveState.CollectEnergyKey;
            }
            else
            {
                nextState = SliceObjectiveState.ClearEncounter;
            }

            SetState(nextState);
        }

        void HandleEncounterCompleted() => Refresh();
        void HandleGateDisabled() => Refresh();
        void HandleKeyStateChanged(EnergyKeyState previous, EnergyKeyState next) => Refresh();
        void HandleGameStateChanged(GameState previous, GameState next) => Refresh();

        void ResolveReferences()
        {
            if (!encounter)
                encounter = FindFirstObjectByType<EncounterController>();
            if (!energyKey)
                energyKey = FindFirstObjectByType<EnergyKey>();
            if (!gate)
                gate = FindFirstObjectByType<EnergyGate>();
            if (!exit)
                exit = FindFirstObjectByType<LevelExit>();
            if (!gameState)
                gameState = FindFirstObjectByType<GameStateMachine>();
        }

        void Subscribe()
        {
            if (encounter)
                encounter.Completed += HandleEncounterCompleted;
            if (energyKey)
                energyKey.StateChanged += HandleKeyStateChanged;
            if (gate)
                gate.Disabled += HandleGateDisabled;
            if (gameState)
                gameState.StateChanged += HandleGameStateChanged;
        }

        void Unsubscribe()
        {
            if (encounter)
                encounter.Completed -= HandleEncounterCompleted;
            if (energyKey)
                energyKey.StateChanged -= HandleKeyStateChanged;
            if (gate)
                gate.Disabled -= HandleGateDisabled;
            if (gameState)
                gameState.StateChanged -= HandleGameStateChanged;
        }

        void SetState(SliceObjectiveState nextState)
        {
            if (CurrentState == nextState)
                return;

            SliceObjectiveState previousState = CurrentState;
            CurrentState = nextState;
            ObjectiveChanged?.Invoke(previousState, nextState);
        }

        static string GetObjectiveLabel(SliceObjectiveState state)
        {
            switch (state)
            {
                case SliceObjectiveState.CollectEnergyKey:
                    return "COLLECT ENERGY KEY";
                case SliceObjectiveState.OpenExtractionGate:
                    return "OPEN EXTRACTION GATE";
                case SliceObjectiveState.ReachExtraction:
                    return "REACH EXTRACTION";
                case SliceObjectiveState.ExtractionComplete:
                    return "EXTRACTION COMPLETE";
                default:
                    return "CLEAR ENCOUNTER";
            }
        }
    }
}

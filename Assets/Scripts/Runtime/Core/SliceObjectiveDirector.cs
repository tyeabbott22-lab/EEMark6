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

        EncounterController subscribedEncounter;
        EnergyKey subscribedEnergyKey;
        EnergyGate subscribedGate;
        GameStateMachine subscribedGameState;

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

        void Start()
        {
            // Awake/OnEnable order is not a gameplay contract. Re-evaluate once
            // every scene object has finished initialization so a rebuilt room
            // cannot briefly report CLEAR ENCOUNTER before its key, gate, and
            // exit references have settled.
            Refresh();
        }

        void OnDisable() => Unsubscribe();

        public void Refresh()
        {
            ResolveReferences();
            RefreshSubscriptions();

            SliceObjectiveState nextState;
            if (exit && exit.IsComplete)
            {
                nextState = SliceObjectiveState.ExtractionComplete;
            }
            else if (exit && exit.IsUnlocked)
            {
                nextState = SliceObjectiveState.ReachExtraction;
            }
            else if (energyKey && energyKey.IsCollected && gate && !gate.IsRouteClear)
            {
                nextState = SliceObjectiveState.OpenExtractionGate;
            }
            else if (energyKey && energyKey.IsCollected)
            {
                nextState = SliceObjectiveState.ReachExtraction;
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
        void HandleGateRouteCleared() => Refresh();
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
            RefreshSubscriptions();
        }

        void Unsubscribe()
        {
            if (subscribedEncounter)
                subscribedEncounter.Completed -= HandleEncounterCompleted;
            if (subscribedEnergyKey)
                subscribedEnergyKey.StateChanged -= HandleKeyStateChanged;
            if (subscribedGate)
            {
                subscribedGate.Disabled -= HandleGateDisabled;
                subscribedGate.RouteCleared -= HandleGateRouteCleared;
            }
            if (subscribedGameState)
                subscribedGameState.StateChanged -= HandleGameStateChanged;

            subscribedEncounter = null;
            subscribedEnergyKey = null;
            subscribedGate = null;
            subscribedGameState = null;
        }

        void RefreshSubscriptions()
        {
            if (subscribedEncounter != encounter)
            {
                if (subscribedEncounter)
                    subscribedEncounter.Completed -= HandleEncounterCompleted;
                subscribedEncounter = encounter;
                if (subscribedEncounter)
                    subscribedEncounter.Completed += HandleEncounterCompleted;
            }

            if (subscribedEnergyKey != energyKey)
            {
                if (subscribedEnergyKey)
                    subscribedEnergyKey.StateChanged -= HandleKeyStateChanged;
                subscribedEnergyKey = energyKey;
                if (subscribedEnergyKey)
                    subscribedEnergyKey.StateChanged += HandleKeyStateChanged;
            }

            if (subscribedGate != gate)
            {
                if (subscribedGate)
                {
                    subscribedGate.Disabled -= HandleGateDisabled;
                    subscribedGate.RouteCleared -= HandleGateRouteCleared;
                }
                subscribedGate = gate;
                if (subscribedGate)
                {
                    subscribedGate.Disabled += HandleGateDisabled;
                    subscribedGate.RouteCleared += HandleGateRouteCleared;
                }
            }

            if (subscribedGameState != gameState)
            {
                if (subscribedGameState)
                    subscribedGameState.StateChanged -= HandleGameStateChanged;
                subscribedGameState = gameState;
                if (subscribedGameState)
                    subscribedGameState.StateChanged += HandleGameStateChanged;
            }
        }

        void SetState(SliceObjectiveState nextState)
        {
            if (CurrentState == nextState)
                return;

            // Objective facts can arrive in different Unity callback orders,
            // especially while a scene is being re-enabled or while the key
            // destroys itself during gate flight. Never let a transient
            // missing reference make the HUD walk backward from OPEN GATE to
            // CLEAR ENCOUNTER; a scene reload creates a fresh director when a
            // real gameplay reset is intended.
            if (GetStateOrder(nextState) < GetStateOrder(CurrentState))
                return;

            SliceObjectiveState previousState = CurrentState;
            CurrentState = nextState;
            ObjectiveChanged?.Invoke(previousState, nextState);
        }

        static int GetStateOrder(SliceObjectiveState state)
        {
            switch (state)
            {
                case SliceObjectiveState.CollectEnergyKey:
                    return 1;
                case SliceObjectiveState.OpenExtractionGate:
                    return 2;
                case SliceObjectiveState.ReachExtraction:
                    return 3;
                case SliceObjectiveState.ExtractionComplete:
                    return 4;
                default:
                    return 0;
            }
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

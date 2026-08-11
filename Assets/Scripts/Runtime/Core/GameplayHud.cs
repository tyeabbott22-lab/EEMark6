using UnityEngine;
using UnityEngine.UI;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// Minimal vertical-slice HUD. It reads gameplay contracts and owns only text presentation.
    /// </summary>
    public sealed class GameplayHud : MonoBehaviour
    {
        [SerializeField] Text statusLabel;
        [SerializeField] ScoreSystem scoreSystem;
        [SerializeField] EncounterController encounter;
        [SerializeField] EnergyKey energyKey;
        [SerializeField] LevelExit exit;
        [SerializeField] GameStateMachine gameState;
        [SerializeField] SliceObjectiveDirector objectiveDirector;

        float nextRefreshTime;

        void Awake()
        {
            if (!scoreSystem)
                scoreSystem = FindFirstObjectByType<ScoreSystem>();
            if (!encounter)
                encounter = FindFirstObjectByType<EncounterController>();
            if (!energyKey)
                energyKey = FindFirstObjectByType<EnergyKey>();
            if (!exit)
                exit = FindFirstObjectByType<LevelExit>();
            if (!gameState)
                gameState = FindFirstObjectByType<GameStateMachine>();
            if (!objectiveDirector)
                objectiveDirector = FindFirstObjectByType<SliceObjectiveDirector>();
        }

        void OnEnable()
        {
            if (scoreSystem)
                scoreSystem.ScoreChanged += HandleScoreChanged;
            if (gameState)
                gameState.StateChanged += HandleStateChanged;
            if (objectiveDirector)
                objectiveDirector.ObjectiveChanged += HandleObjectiveChanged;
            Refresh();
        }

        void OnDisable()
        {
            if (scoreSystem)
                scoreSystem.ScoreChanged -= HandleScoreChanged;
            if (gameState)
                gameState.StateChanged -= HandleStateChanged;
            if (objectiveDirector)
                objectiveDirector.ObjectiveChanged -= HandleObjectiveChanged;
        }

        void Update()
        {
            if (Time.unscaledTime < nextRefreshTime)
                return;

            nextRefreshTime = Time.unscaledTime + 0.2f;
            Refresh();
        }

        void HandleScoreChanged(int score, int awarded, ScoreReason reason) => Refresh();
        void HandleStateChanged(GameState previous, GameState next) => Refresh();
        void HandleObjectiveChanged(SliceObjectiveState previous, SliceObjectiveState next) => Refresh();

        void Refresh()
        {
            if (!statusLabel)
                return;

            int score = scoreSystem ? scoreSystem.CurrentScore : 0;
            if (gameState && gameState.CurrentState == GameState.Paused)
            {
                statusLabel.text = $"SCORE  {score:0000}\nPAUSED  PRESS ESC TO RESUME";
                return;
            }

            if (gameState && gameState.CurrentState == GameState.GameOver)
            {
                statusLabel.text = $"SCORE  {score:0000}\nEXTRACTION COMPLETE";
                return;
            }

            string objective = objectiveDirector
                ? objectiveDirector.CurrentObjective
                : ResolveFallbackObjective();

            statusLabel.text = $"SCORE  {score:0000}\nOBJECTIVE  {objective}";
        }

        string ResolveFallbackObjective()
        {
            if (exit && exit.IsUnlocked)
                return "REACH EXTRACTION";
            if (energyKey && energyKey.IsCollected)
                return "OPEN EXTRACTION GATE";
            if (energyKey && energyKey.IsAvailable)
                return "COLLECT ENERGY KEY";
            return "CLEAR ENCOUNTER";
        }
    }
}

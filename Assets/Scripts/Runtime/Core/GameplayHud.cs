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
        }

        void OnEnable()
        {
            if (scoreSystem)
                scoreSystem.ScoreChanged += HandleScoreChanged;
            if (gameState)
                gameState.StateChanged += HandleStateChanged;
            Refresh();
        }

        void OnDisable()
        {
            if (scoreSystem)
                scoreSystem.ScoreChanged -= HandleScoreChanged;
            if (gameState)
                gameState.StateChanged -= HandleStateChanged;
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

        void Refresh()
        {
            if (!statusLabel)
                return;

            int score = scoreSystem ? scoreSystem.CurrentScore : 0;
            if (gameState && gameState.CurrentState == GameState.GameOver)
            {
                statusLabel.text = $"SCORE  {score:0000}\nEXTRACTION COMPLETE";
                return;
            }

            string objective = "CLEAR ENCOUNTER";
            if (exit && exit.IsUnlocked)
            {
                objective = "REACH EXTRACTION";
            }
            else if (energyKey && energyKey.IsCollected)
            {
                objective = "OPEN EXTRACTION GATE";
            }
            else if (energyKey && energyKey.IsAvailable)
            {
                objective = "COLLECT ENERGY KEY";
            }
            else if (encounter && encounter.IsComplete)
            {
                objective = "COLLECT ENERGY KEY";
            }

            statusLabel.text = $"SCORE  {score:0000}\nOBJECTIVE  {objective}";
        }
    }
}

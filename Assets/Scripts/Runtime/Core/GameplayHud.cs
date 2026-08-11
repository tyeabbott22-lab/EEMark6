using System.Collections;
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
        [SerializeField] Text objectiveBannerLabel;
        [SerializeField] CanvasGroup objectiveBannerGroup;
        [SerializeField, Min(0f)] float objectiveBannerDuration = 1.35f;
        [SerializeField] ScoreSystem scoreSystem;
        [SerializeField] EncounterController encounter;
        [SerializeField] EnergyKey energyKey;
        [SerializeField] LevelExit exit;
        [SerializeField] GameStateMachine gameState;
        [SerializeField] SliceObjectiveDirector objectiveDirector;

        float nextRefreshTime;
        Coroutine bannerRoutine;
        Vector3 bannerBaseScale = Vector3.one;

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
            if (objectiveBannerLabel)
            {
                if (!objectiveBannerGroup)
                    objectiveBannerGroup = objectiveBannerLabel.GetComponent<CanvasGroup>();
                bannerBaseScale = objectiveBannerLabel.transform.localScale;
            }
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
            if (objectiveDirector)
                ShowObjectiveBanner(objectiveDirector.CurrentObjective);
        }

        void OnDisable()
        {
            if (scoreSystem)
                scoreSystem.ScoreChanged -= HandleScoreChanged;
            if (gameState)
                gameState.StateChanged -= HandleStateChanged;
            if (objectiveDirector)
                objectiveDirector.ObjectiveChanged -= HandleObjectiveChanged;

            if (bannerRoutine != null)
            {
                StopCoroutine(bannerRoutine);
                bannerRoutine = null;
            }
            SetBannerVisible(false, 0f);
        }

        void Update()
        {
            if (Time.unscaledTime < nextRefreshTime)
                return;

            nextRefreshTime = Time.unscaledTime + 0.2f;
            Refresh();
        }

        void HandleScoreChanged(int score, int awarded, ScoreReason reason) => Refresh();
        void HandleStateChanged(GameState previous, GameState next)
        {
            Refresh();
            if (next == GameState.GameOver)
                ShowObjectiveBanner("EXTRACTION COMPLETE");
        }
        void HandleObjectiveChanged(SliceObjectiveState previous, SliceObjectiveState next)
        {
            Refresh();
            ShowObjectiveBanner(GetObjectiveLabel(next));
        }

        void ShowObjectiveBanner(string objective)
        {
            if (!objectiveBannerLabel)
                return;

            if (bannerRoutine != null)
                StopCoroutine(bannerRoutine);
            bannerRoutine = StartCoroutine(ObjectiveBannerRoutine(objective));
        }

        IEnumerator ObjectiveBannerRoutine(string objective)
        {
            objectiveBannerLabel.text = objective;
            SetBannerVisible(true, 0f);

            const float introDuration = 0.16f;
            const float outroDuration = 0.28f;
            float holdDuration = Mathf.Max(
                0f,
                objectiveBannerDuration - introDuration - outroDuration);

            float elapsed = 0f;
            while (elapsed < introDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / introDuration);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                SetBannerVisible(true, eased);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < holdDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetBannerVisible(true, 1f);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < outroDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / outroDuration);
                SetBannerVisible(true, 1f - Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }

            SetBannerVisible(false, 0f);
            bannerRoutine = null;
        }

        void SetBannerVisible(bool visible, float alpha)
        {
            if (!objectiveBannerLabel)
                return;

            objectiveBannerLabel.transform.localScale = bannerBaseScale *
                Mathf.Lerp(0.86f, 1f, Mathf.Clamp01(alpha));
            if (objectiveBannerGroup)
            {
                objectiveBannerGroup.alpha = visible ? alpha : 0f;
                objectiveBannerGroup.interactable = false;
                objectiveBannerGroup.blocksRaycasts = false;
                return;
            }

            Color color = objectiveBannerLabel.color;
            color.a = visible ? alpha : 0f;
            objectiveBannerLabel.color = color;
        }

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

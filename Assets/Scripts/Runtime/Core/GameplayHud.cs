using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using ExtraterrestrialExhaust.Combat;
using ExtraterrestrialExhaust.Player;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// Minimal vertical-slice HUD. It reads gameplay contracts and owns only text presentation.
    /// </summary>
    public sealed class GameplayHud : MonoBehaviour
    {
        [SerializeField] Text statusLabel;
        [SerializeField] Text healthLabel;
        [SerializeField] Text actionCalloutLabel;
        [SerializeField] CanvasGroup actionCalloutGroup;
        [SerializeField, Min(0f)] float actionCalloutDuration = 0.9f;
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
        Coroutine actionCalloutRoutine;
        Vector3 bannerBaseScale = Vector3.one;
        Vector3 actionCalloutBaseScale = Vector3.one;
        HealthComponent playerHealth;

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
            PlayerCharacter player = FindFirstObjectByType<PlayerCharacter>();
            playerHealth = player ? player.Health : FindFirstObjectByType<HealthComponent>();
            if (objectiveBannerLabel)
            {
                if (!objectiveBannerGroup)
                    objectiveBannerGroup = objectiveBannerLabel.GetComponent<CanvasGroup>();
                bannerBaseScale = objectiveBannerLabel.transform.localScale;
            }
            if (actionCalloutLabel)
            {
                if (!actionCalloutGroup)
                    actionCalloutGroup = actionCalloutLabel.GetComponent<CanvasGroup>();
                actionCalloutBaseScale = actionCalloutLabel.transform.localScale;
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
            if (playerHealth)
            {
                playerHealth.HealthChanged += HandleHealthChanged;
                playerHealth.Died += HandlePlayerDied;
            }
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
            if (playerHealth)
            {
                playerHealth.HealthChanged -= HandleHealthChanged;
                playerHealth.Died -= HandlePlayerDied;
            }

            if (bannerRoutine != null)
            {
                StopCoroutine(bannerRoutine);
                bannerRoutine = null;
            }
            if (actionCalloutRoutine != null)
            {
                StopCoroutine(actionCalloutRoutine);
                actionCalloutRoutine = null;
            }
            SetBannerVisible(false, 0f);
            SetActionCalloutVisible(false, 0f);
        }

        void Update()
        {
            if (Time.unscaledTime < nextRefreshTime)
                return;

            nextRefreshTime = Time.unscaledTime + 0.2f;
            Refresh();
        }

        void HandleScoreChanged(int score, int awarded, ScoreReason reason)
        {
            Refresh();
            if (awarded > 0)
                ShowActionCallout($"+{awarded:000}  {GetScoreReasonLabel(reason)}", GetScoreReasonColor(reason));
        }
        void HandleHealthChanged(float currentHealth) => Refresh();
        void HandlePlayerDied() => ShowActionCallout("HULL LOST", new Color(1f, 0.14f, 0.1f, 1f));
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

        void ShowActionCallout(string message, Color color)
        {
            if (!actionCalloutLabel)
                return;

            if (actionCalloutRoutine != null)
                StopCoroutine(actionCalloutRoutine);
            actionCalloutLabel.text = message;
            actionCalloutLabel.color = color;
            actionCalloutRoutine = StartCoroutine(ActionCalloutRoutine());
        }

        IEnumerator ActionCalloutRoutine()
        {
            const float introDuration = 0.08f;
            const float outroDuration = 0.22f;
            float holdDuration = Mathf.Max(
                0f,
                actionCalloutDuration - introDuration - outroDuration);

            float elapsed = 0f;
            SetActionCalloutVisible(true, 0f);
            while (elapsed < introDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetActionCalloutVisible(
                    true,
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / introDuration)));
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < holdDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetActionCalloutVisible(true, 1f);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < outroDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetActionCalloutVisible(
                    true,
                    1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / outroDuration)));
                yield return null;
            }

            SetActionCalloutVisible(false, 0f);
            actionCalloutRoutine = null;
        }

        void SetActionCalloutVisible(bool visible, float alpha)
        {
            if (!actionCalloutLabel)
                return;

            actionCalloutLabel.transform.localScale = actionCalloutBaseScale *
                Mathf.Lerp(0.88f, 1f, Mathf.Clamp01(alpha));
            if (actionCalloutGroup)
            {
                actionCalloutGroup.alpha = visible ? alpha : 0f;
                actionCalloutGroup.interactable = false;
                actionCalloutGroup.blocksRaycasts = false;
                return;
            }

            Color color = actionCalloutLabel.color;
            color.a = visible ? alpha : 0f;
            actionCalloutLabel.color = color;
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
            int score = scoreSystem ? scoreSystem.CurrentScore : 0;
            RefreshHealthLabel();
            if (!statusLabel)
                return;

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

        void RefreshHealthLabel()
        {
            if (!healthLabel)
                return;

            if (!playerHealth)
            {
                healthLabel.text = "HULL  --";
                return;
            }

            int current = Mathf.Max(0, Mathf.CeilToInt(playerHealth.CurrentHealth));
            int maximum = Mathf.Max(1, Mathf.CeilToInt(playerHealth.MaxHealth));
            healthLabel.text = $"HULL  {current}/{maximum}";
            float healthT = Mathf.Clamp01(current / (float)maximum);
            healthLabel.color = Color.Lerp(
                new Color(1f, 0.1f, 0.08f, 1f),
                new Color(1f, 0.82f, 0.18f, 1f),
                healthT);
        }

        static string GetScoreReasonLabel(ScoreReason reason)
        {
            switch (reason)
            {
                case ScoreReason.EnemyDefeated:
                    return "KILL";
                case ScoreReason.ObjectiveCollected:
                    return "KEY";
                case ScoreReason.GateDeactivated:
                    return "GATE";
                case ScoreReason.WallBroken:
                    return "WALL";
                case ScoreReason.LevelCompleted:
                    return "EXTRACTION";
                default:
                    return "SCORE";
            }
        }

        static Color GetScoreReasonColor(ScoreReason reason)
        {
            switch (reason)
            {
                case ScoreReason.EnemyDefeated:
                    return new Color(1f, 0.28f, 0.2f, 1f);
                case ScoreReason.ObjectiveCollected:
                    return new Color(1f, 0.86f, 0.18f, 1f);
                case ScoreReason.GateDeactivated:
                    return new Color(0.2f, 1f, 0.85f, 1f);
                case ScoreReason.WallBroken:
                    return new Color(1f, 0.2f, 0.92f, 1f);
                case ScoreReason.LevelCompleted:
                    return new Color(0.55f, 0.9f, 1f, 1f);
                default:
                    return Color.white;
            }
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

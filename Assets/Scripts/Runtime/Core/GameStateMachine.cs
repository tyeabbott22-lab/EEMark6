using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// Coordinates the application-level state without knowing about menus,
    /// UI, or a particular level implementation.
    /// </summary>
    public sealed class GameStateMachine : MonoBehaviour
    {
        [SerializeField] GameState initialState = GameState.Boot;
        [SerializeField] bool pauseTimeWhenPaused = true;
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] string pauseActionName = "Player/Pause";
        [SerializeField] bool allowPause = true;
        [SerializeField] bool enableEnemyDefeatSlowdown = true;
        [SerializeField, Range(0.05f, 1f)] float enemyDefeatTimeScale = 0.16f;
        [SerializeField, Min(0f)] float enemyDefeatSlowdownDuration = 0.07f;

        public GameState CurrentState { get; private set; }
        public bool IsPlaying => CurrentState == GameState.Playing;
        public event Action<GameState, GameState> StateChanged;

        InputAction pauseAction;
        Coroutine enemyDefeatSlowdownRoutine;

        void Awake()
        {
            ResolvePauseAction();
            CurrentState = initialState;
            ApplyTimeScale(CurrentState);
        }

        void OnEnable()
        {
            pauseAction?.Enable();
        }

        void OnDisable()
        {
            pauseAction?.Disable();
        }

        void Update()
        {
            if (allowPause && pauseAction != null && pauseAction.WasPressedThisFrame())
                TogglePause();
        }

        void OnDestroy()
        {
            // A destroyed manager should never leave the editor or next scene paused.
            if (pauseTimeWhenPaused)
                Time.timeScale = 1f;
        }

        public bool TrySetState(GameState nextState)
        {
            if (CurrentState == nextState)
                return false;

            GameState previousState = CurrentState;
            CurrentState = nextState;
            ApplyTimeScale(nextState);
            StateChanged?.Invoke(previousState, nextState);
            return true;
        }

        public void StartGame() => TrySetState(GameState.Playing);
        public void PauseGame() => TrySetState(GameState.Paused);
        public void ResumeGame() => TrySetState(GameState.Playing);
        public void EndGame() => TrySetState(GameState.GameOver);

        public void TogglePause()
        {
            if (CurrentState == GameState.Playing)
                PauseGame();
            else if (CurrentState == GameState.Paused)
                ResumeGame();
        }

        /// <summary>
        /// Reproduces the short EE5 defeat hit-stop without letting an enemy or
        /// camera script write directly to global time. Pause and game-over
        /// transitions remain authoritative when the pulse ends.
        /// </summary>
        public void TriggerEnemyDefeatSlowdown()
        {
            if (!enableEnemyDefeatSlowdown
                || CurrentState != GameState.Playing
                || enemyDefeatSlowdownDuration <= 0f)
                return;

            if (enemyDefeatSlowdownRoutine != null)
                StopCoroutine(enemyDefeatSlowdownRoutine);

            enemyDefeatSlowdownRoutine = StartCoroutine(EnemyDefeatSlowdownRoutine());
        }

        /// <summary>
        /// Editor-generated scenes bind the shared Input System asset here so
        /// pause remains a project-level flow concern rather than a keyboard
        /// lookup hidden inside the state machine.
        /// </summary>
        public void ConfigureInputAsset(InputActionAsset asset, string actionName = "Player/Pause")
        {
            if (pauseAction != null)
                pauseAction.Disable();

            inputActions = asset;
            pauseActionName = actionName;
            ResolvePauseAction();
            if (isActiveAndEnabled)
                pauseAction?.Enable();
        }

        void ResolvePauseAction()
        {
            pauseAction = inputActions != null
                ? inputActions.FindAction(pauseActionName, false)
                : null;
        }

        IEnumerator EnemyDefeatSlowdownRoutine()
        {
            Time.timeScale = Mathf.Min(Time.timeScale, enemyDefeatTimeScale);
            yield return new WaitForSecondsRealtime(enemyDefeatSlowdownDuration);

            enemyDefeatSlowdownRoutine = null;
            ApplyTimeScale(CurrentState);
        }

        void ApplyTimeScale(GameState state)
        {
            if (pauseTimeWhenPaused)
                Time.timeScale = state == GameState.Paused ? 0f : 1f;
        }
    }
}

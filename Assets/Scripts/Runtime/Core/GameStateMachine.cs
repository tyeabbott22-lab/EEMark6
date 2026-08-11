using System;
using UnityEngine;

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

        public GameState CurrentState { get; private set; }
        public bool IsPlaying => CurrentState == GameState.Playing;
        public event Action<GameState, GameState> StateChanged;

        void Awake()
        {
            CurrentState = initialState;
            ApplyTimeScale(CurrentState);
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

        void ApplyTimeScale(GameState state)
        {
            if (pauseTimeWhenPaused)
                Time.timeScale = state == GameState.Paused ? 0f : 1f;
        }
    }
}

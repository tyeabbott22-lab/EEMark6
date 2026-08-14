using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using ExtraterrestrialExhaust.Combat;
using ExtraterrestrialExhaust.Core;

namespace ExtraterrestrialExhaust.Player
{
    /// <summary>
    /// Owns player death recovery. Health reports that the player died; this
    /// component decides when and where the player returns.
    /// </summary>
    [RequireComponent(typeof(PlayerCharacter))]
    public sealed class PlayerRespawnController : MonoBehaviour
    {
        [SerializeField] Transform respawnPoint;
        // EE5 resets the authored room on death so encounter and objective
        // state start cleanly on the next life. In-place recovery remains
        // available for isolated prefab tests and future checkpoint work.
        [SerializeField] bool reloadSceneOnDeath = true;
        [SerializeField, Min(0f)] float reloadDelay;
        [SerializeField, Min(0f)] float respawnDelay = 1f;
        [SerializeField] bool respawnAutomatically;

        PlayerCharacter character;
        PlayerFlightPresentation presentation;
        PlayerWeapon weapon;
        PlayerWeaponInput weaponInput;
        Rigidbody2D body;
        Coroutine respawnRoutine;
        Vector3 initialPosition;
        Quaternion initialRotation;
        bool sceneReloadRequested;

        public bool IsRespawning => respawnRoutine != null;

        void Awake()
        {
            character = GetComponent<PlayerCharacter>();
            presentation = GetComponent<PlayerFlightPresentation>();
            weapon = GetComponent<PlayerWeapon>();
            weaponInput = GetComponent<PlayerWeaponInput>();
            body = GetComponent<Rigidbody2D>();
            initialPosition = transform.position;
            initialRotation = transform.rotation;
        }

        void OnEnable()
        {
            if (character && character.Health)
                character.Health.Died += HandleDeath;
        }

        void OnDisable()
        {
            if (character && character.Health)
                character.Health.Died -= HandleDeath;
        }

        void HandleDeath()
        {
            // EE5 reloads the authored room after hull loss. Marking the
            // transition first makes the short reload window deterministic:
            // combat, input, and HUD all know this is failure rather than a
            // successful extraction. In-place recovery re-enters Playing below.
            character.GameState?.EndGame(GameOverReason.HullLost);
            character.FlightState.TrySetState(PlayerFlightState.Disabled);
            // A touch/UI hold is not a physical input state. Clear it when a
            // life ends so an in-place recovery never fires on the first frame
            // of the next life because the old button press survived death.
            weaponInput?.ClearUIInput();
            StopBody();

            if (reloadSceneOnDeath)
            {
                if (!sceneReloadRequested)
                    respawnRoutine = StartCoroutine(ReloadSceneRoutine());
                return;
            }

            if (respawnAutomatically && respawnRoutine == null)
                respawnRoutine = StartCoroutine(RespawnRoutine());
        }

        IEnumerator ReloadSceneRoutine()
        {
            sceneReloadRequested = true;
            yield return new WaitForSecondsRealtime(reloadDelay);

            Scene activeScene = SceneManager.GetActiveScene();
            AsyncOperation load = activeScene.buildIndex >= 0
                ? SceneManager.LoadSceneAsync(activeScene.buildIndex)
                : SceneManager.LoadSceneAsync(activeScene.path);

            if (load != null)
                yield break;

            Debug.LogError("Player death could not reload the active scene; falling back to in-place recovery.", this);
            sceneReloadRequested = false;
            respawnRoutine = null;
            if (respawnAutomatically)
                respawnRoutine = StartCoroutine(RespawnRoutine());
        }

        IEnumerator RespawnRoutine()
        {
            // Recovery is lifecycle work, not gameplay simulation. It should
            // not remain stranded if a pause transition happens during the
            // delay, so use unscaled time just like the scene-reload path.
            yield return new WaitForSecondsRealtime(respawnDelay);

            Transform target = respawnPoint;
            transform.SetPositionAndRotation(
                target ? target.position : initialPosition,
                target ? target.rotation : initialRotation);

            StopBody();
            character.FlightMotor?.ResetFacingForRespawn();
            presentation?.ResetPresentation();
            weaponInput?.ClearUIInput();
            weapon?.ResetForRespawn();
            character.Health.ResetHealth();
            character.GameState?.StartGame();
            character.FlightState.TrySetState(PlayerFlightState.FreeFlight);
            respawnRoutine = null;
        }

        void StopBody()
        {
            if (!body)
                return;

            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }
}

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using ExtraterrestrialExhaust.Combat;

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
        [SerializeField] bool reloadSceneOnDeath;
        [SerializeField, Min(0f)] float reloadDelay;
        [SerializeField, Min(0f)] float respawnDelay = 1f;
        [SerializeField] bool respawnAutomatically = true;

        PlayerCharacter character;
        PlayerFlightPresentation presentation;
        PlayerWeapon weapon;
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
            character.FlightState.TrySetState(PlayerFlightState.Disabled);
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
            yield return new WaitForSeconds(respawnDelay);

            Transform target = respawnPoint;
            transform.SetPositionAndRotation(
                target ? target.position : initialPosition,
                target ? target.rotation : initialRotation);

            StopBody();
            character.FlightMotor?.ResetFacingForRespawn();
            presentation?.ResetPresentation();
            weapon?.ResetForRespawn();
            character.Health.ResetHealth();
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

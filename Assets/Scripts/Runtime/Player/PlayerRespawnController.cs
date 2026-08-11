using System.Collections;
using UnityEngine;
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
        [SerializeField, Min(0f)] float respawnDelay = 1f;
        [SerializeField] bool respawnAutomatically = true;

        PlayerCharacter character;
        PlayerFlightPresentation presentation;
        Rigidbody2D body;
        Coroutine respawnRoutine;
        Vector3 initialPosition;
        Quaternion initialRotation;

        public bool IsRespawning => respawnRoutine != null;

        void Awake()
        {
            character = GetComponent<PlayerCharacter>();
            presentation = GetComponent<PlayerFlightPresentation>();
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

            if (respawnAutomatically && respawnRoutine == null)
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
            presentation?.ResetPresentation();
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

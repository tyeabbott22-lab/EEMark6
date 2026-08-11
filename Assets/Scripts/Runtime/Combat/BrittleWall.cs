using UnityEngine;
using ExtraterrestrialExhaust.CameraSystem;
using ExtraterrestrialExhaust.Core;
using ExtraterrestrialExhaust.Player;

namespace ExtraterrestrialExhaust.Combat
{
    /// <summary>
    /// Compact EE5-style brittle room prop. A direct, thrust-assisted impact
    /// at speed breaks the wall and preserves the player's follow-through;
    /// permanent arena boundaries remain ordinary static walls.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class BrittleWall : MonoBehaviour
    {
        [SerializeField, Min(0f)] float breakSpeed = 14f;
        [SerializeField, Range(0f, 1f)] float minimumDirectness = 0.68f;
        [SerializeField] bool requireThrustToBreak = true;
        [SerializeField, Range(0f, 1f)] float retainedVelocity = 0.94f;
        [SerializeField, Min(0f)] float followThroughNudge = 0.34f;
        [SerializeField, Min(0f)] float impactCooldown = 0.32f;
        [SerializeField, Min(0f)] float cameraShakeStrength = 0.14f;
        [SerializeField, Min(0f)] float cameraShakeDuration = 0.18f;
        [SerializeField, Min(0)] int breakScore = 150;
        [SerializeField] Color breakColor = new Color(0.95f, 0.12f, 1f, 1f);

        Collider2D wallCollider;
        bool broken;
        float nextImpactTime;

        public bool IsBroken => broken;

        void Awake()
        {
            wallCollider = GetComponent<Collider2D>();
        }

        void OnCollisionEnter2D(Collision2D collision) => TryBreak(collision);

        void OnCollisionStay2D(Collision2D collision) => TryBreak(collision);

        void TryBreak(Collision2D collision)
        {
            if (broken || Time.time < nextImpactTime)
                return;

            PlayerCharacter player = collision.collider
                ? collision.collider.GetComponentInParent<PlayerCharacter>()
                : null;
            if (!player || !player.FlightMotor || !player.FlightInput)
                return;

            Rigidbody2D playerBody = player.FlightMotor.Body;
            if (!playerBody || !player.CanReceiveGameplayInput)
                return;

            Vector2 velocity = playerBody.linearVelocity;
            float speed = Mathf.Max(collision.relativeVelocity.magnitude, velocity.magnitude);
            if (speed < breakSpeed)
                return;

            if (requireThrustToBreak && player.FlightInput.Move.y <= 0.2f)
                return;

            Vector2 hitPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : transform.position;
            Vector2 travelDirection = velocity.sqrMagnitude > 0.001f
                ? velocity.normalized
                : (hitPoint - (Vector2)player.transform.position).normalized;
            Vector2 directionToWall = hitPoint - (Vector2)player.transform.position;
            if (directionToWall.sqrMagnitude <= 0.001f)
                directionToWall = (Vector2)transform.position - (Vector2)player.transform.position;
            directionToWall.Normalize();

            float directness = Vector2.Dot(travelDirection, directionToWall);
            if (directness < minimumDirectness)
                return;

            nextImpactTime = Time.time + impactCooldown;
            Break(playerBody, hitPoint, travelDirection, velocity);
        }

        void Break(
            Rigidbody2D playerBody,
            Vector2 hitPoint,
            Vector2 travelDirection,
            Vector2 velocity)
        {
            broken = true;
            if (wallCollider)
                wallCollider.enabled = false;

            // The authored SpriteShape debris is represented by a reusable
            // burst until that heavier asset pipeline is migrated.
            ObjectiveSignalBurst.Spawn(hitPoint, breakColor, 1.35f);
            PlayerCameraFollow.Instance?.Shake(cameraShakeStrength, cameraShakeDuration);
            FindFirstObjectByType<ScoreSystem>()?.AddScore(breakScore, ScoreReason.WallBroken);

            if (playerBody)
            {
                playerBody.linearVelocity = velocity * retainedVelocity;
                playerBody.angularVelocity *= 0.18f;
                playerBody.position += travelDirection * followThroughNudge;
            }

            foreach (Collider2D childCollider in GetComponentsInChildren<Collider2D>(true))
                if (childCollider)
                    childCollider.enabled = false;

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                if (renderer)
                    renderer.enabled = false;

            gameObject.SetActive(false);
        }
    }
}

using UnityEngine;
using ExtraterrestrialExhaust.CameraSystem;

namespace ExtraterrestrialExhaust.Enemy
{
    /// <summary>
    /// Translates the enemy's defeated event into authored VFX and audio.
    /// Combat and encounter rules remain owned by their respective systems.
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public sealed class EnemyDeathPresentation : MonoBehaviour
    {
        [SerializeField] Sprite[] burstFrames;
        [SerializeField] AudioClip defeatAudio;
        [SerializeField, Min(0.01f)] float burstScale = 3f;
        [SerializeField] int burstSortingOrder = 40;
        [SerializeField] Color burstColor = Color.white;
        [SerializeField, Range(0f, 1f)] float audioVolume = 0.65f;

        EnemyController controller;
        bool hasPlayed;

        void Reset()
        {
            controller = GetComponent<EnemyController>();
        }

        void Awake()
        {
            if (!controller)
                controller = GetComponent<EnemyController>();
        }

        void OnEnable()
        {
            if (controller)
                controller.Defeated += HandleDefeated;
        }

        void OnDisable()
        {
            if (controller)
                controller.Defeated -= HandleDefeated;
        }

        void HandleDefeated(EnemyController defeatedEnemy)
        {
            if (hasPlayed)
                return;

            hasPlayed = true;
            PlayerCameraFollow.Instance?.Shake(0.13f, 0.18f);
            EnemyDeathBurst.Spawn(
                controller.PhysicsAnchorPosition,
                burstFrames,
                burstScale,
                burstSortingOrder,
                burstColor);

            if (defeatAudio)
                AudioSource.PlayClipAtPoint(defeatAudio, transform.position, audioVolume);
        }
    }
}

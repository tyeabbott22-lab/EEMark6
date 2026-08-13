using UnityEngine;
using ExtraterrestrialExhaust.Player;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// Trigger-driven instruction prompt matching the EE5 realScene flow.
    /// Prompts identify the player through PlayerCharacter rather than tags or
    /// legacy controller names, so generated scenes and future prefabs agree.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class SliceInstructionTrigger : MonoBehaviour
    {
        [SerializeField, TextArea(2, 6)] string message;
        [SerializeField] bool hideOnExit = true;
        [SerializeField] bool onlyTriggerOnce;

        BoxCollider2D trigger;
        SliceInstructionDisplay display;
        string sourceId;
        bool hasTriggered;

        void Awake()
        {
            trigger = GetComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            sourceId = GetInstanceID().ToString();
            display = FindFirstObjectByType<SliceInstructionDisplay>();
        }

        void OnEnable()
        {
            if (!display)
                display = FindFirstObjectByType<SliceInstructionDisplay>();
        }

        void OnTriggerEnter2D(Collider2D other) => TryShow(other);

        void OnTriggerStay2D(Collider2D other)
        {
            if (!hasTriggered || !onlyTriggerOnce)
                TryShow(other);
        }

        void OnTriggerExit2D(Collider2D other)
        {
            if (hideOnExit && IsPlayer(other))
            {
                // Unity's destroyed-object wrapper is not null to C#'s
                // null-conditional operator. Use Unity's overloaded bool so a
                // scene refresh cannot call into a display that is already
                // being torn down.
                if (display)
                    display.Hide(sourceId);
            }
        }

        /// <summary>Editor builders use this to author prompts without exposing serialized details.</summary>
        public void ConfigureForBuilder(string newMessage, Vector2 triggerSize)
        {
            message = newMessage;
            if (!trigger)
                trigger = GetComponent<BoxCollider2D>();
            trigger.size = triggerSize;
            trigger.offset = Vector2.zero;
            trigger.isTrigger = true;
        }

        void TryShow(Collider2D other)
        {
            if (!IsPlayer(other) || (onlyTriggerOnce && hasTriggered))
                return;

            if (!display)
                display = FindFirstObjectByType<SliceInstructionDisplay>();
            if (!display)
                return;

            hasTriggered = true;
            display.Show(sourceId, message);
        }

        static bool IsPlayer(Collider2D other)
        {
            return other && other.GetComponentInParent<PlayerCharacter>();
        }
    }
}

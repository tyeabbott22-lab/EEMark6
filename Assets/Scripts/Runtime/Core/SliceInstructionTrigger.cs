using UnityEngine;
using ExtraterrestrialExhaust.Player;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// Trigger-driven instruction prompt matching the EE5 realScene flow.
    /// Prompts identify the player through PlayerCharacter rather than tags or
    /// scene-specific controller names.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class SliceInstructionTrigger : MonoBehaviour
    {
        [SerializeField, TextArea(2, 6)] string message;
        [SerializeField] bool hideOnExit = true;
        [SerializeField] bool onlyTriggerOnce;
        [SerializeField] SliceObjectiveDirector objectiveDirector;
        [SerializeField] SliceObjectiveState requiredObjectiveState =
            SliceObjectiveState.ClearEncounter;

        BoxCollider2D trigger;
        SliceInstructionDisplay display;
        string sourceId;
        bool hasTriggered;

        void Awake()
        {
            trigger = GetComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            sourceId = GetInstanceID().ToString();
            ResolveReferences();
        }

        void OnEnable()
        {
            ResolveReferences();
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

            // A trigger volume describes a location; the objective director
            // decides whether that location's instruction is currently true.
            // Keep the trigger armed when the player arrives early so a later
            // objective transition can show the prompt through OnTriggerStay2D.
            if (objectiveDirector
                && !objectiveDirector.HasReached(requiredObjectiveState))
                return;

            if (!display)
                ResolveReferences();
            if (!display)
                return;

            hasTriggered = true;
            display.Show(sourceId, message);
        }

        void ResolveReferences()
        {
            if (!display)
                display = FindFirstObjectByType<SliceInstructionDisplay>();
            if (!objectiveDirector)
                objectiveDirector = FindFirstObjectByType<SliceObjectiveDirector>();
        }

        static bool IsPlayer(Collider2D other)
        {
            return other && other.GetComponentInParent<PlayerCharacter>();
        }
    }
}

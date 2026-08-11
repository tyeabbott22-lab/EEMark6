using UnityEngine;

namespace ExtraterrestrialExhaust.Enemy
{
    /// <summary>
    /// Makes the waking state legible without duplicating enemy timing rules.
    /// The controller owns progress; this component only renders the telegraph.
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public sealed class EnemyWakePresentation : MonoBehaviour
    {
        [SerializeField] LineRenderer wakeLine;
        [SerializeField] Color chargingColor = new Color(0.45f, 0.1f, 1f, 0.22f);
        [SerializeField] Color readyColor = new Color(0.08f, 1f, 0.34f, 0.9f);
        [SerializeField, Min(0f)] float minWidth = 0.014f;
        [SerializeField, Min(0f)] float maxWidth = 0.11f;
        [SerializeField] int sortingOrder = 16;

        EnemyController controller;
        Material lineMaterial;

        void Awake()
        {
            controller = GetComponent<EnemyController>();
            EnsureLine();
        }

        void OnDestroy()
        {
            if (lineMaterial)
                Destroy(lineMaterial);
        }

        void Update()
        {
            if (!wakeLine || !controller || controller.State != EnemyState.Waking
                || !controller.Target)
            {
                if (wakeLine)
                    wakeLine.enabled = false;
                return;
            }

            float progress = controller.WakeProgress;
            Color color = Color.Lerp(chargingColor, readyColor, progress);
            wakeLine.enabled = color.a > 0.01f;
            wakeLine.startColor = color;
            wakeLine.endColor = color;
            wakeLine.startWidth = Mathf.Lerp(minWidth, maxWidth, progress);
            wakeLine.endWidth = wakeLine.startWidth * 0.08f;
            wakeLine.SetPosition(0, transform.position);
            wakeLine.SetPosition(1, controller.Target.transform.position);
        }

        void EnsureLine()
        {
            if (!wakeLine)
            {
                GameObject lineObject = new GameObject("Wake Telegraph");
                lineObject.transform.SetParent(transform, false);
                wakeLine = lineObject.AddComponent<LineRenderer>();
            }

            wakeLine.useWorldSpace = true;
            wakeLine.positionCount = 2;
            wakeLine.numCapVertices = 4;
            wakeLine.sortingOrder = sortingOrder;
            wakeLine.startWidth = minWidth;
            wakeLine.endWidth = minWidth * 0.08f;
            wakeLine.enabled = false;
            lineMaterial = new Material(Shader.Find("Sprites/Default"));
            wakeLine.sharedMaterial = lineMaterial;
        }
    }
}

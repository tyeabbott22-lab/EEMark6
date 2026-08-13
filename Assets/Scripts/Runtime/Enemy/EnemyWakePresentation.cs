using UnityEngine;
using ExtraterrestrialExhaust.Core;

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
        [SerializeField] Color blockedColor = new Color(0.5f, 0.14f, 1f, 0.18f);
        [SerializeField] Color readyColor = new Color(0.08f, 1f, 0.34f, 0.9f);
        [SerializeField] Color flashYellow = new Color(1f, 0.9f, 0.05f, 0.95f);
        [SerializeField] Color flashRed = new Color(1f, 0.04f, 0.02f, 0.95f);
        [SerializeField, Min(0f)] float minWidth = 0.014f;
        [SerializeField, Min(0f)] float maxWidth = 0.11f;
        [SerializeField, Min(0.001f)] float endpointSmoothTime = 0.055f;
        [SerializeField, Min(0f)] float glanceRadius = 0.56f;
        [SerializeField, Min(0f)] float glanceSpeed = 15f;
        [SerializeField, Range(0f, 1f)] float enemyEndAlphaMultiplier = 0.5f;
        [SerializeField, Range(0f, 1f)] float playerEndAlphaMultiplier;
        [SerializeField, Min(0f)] float endWidthMultiplier = 0.08f;
        [SerializeField] int sortingOrder = 80;

        EnemyController controller;
        Material lineMaterial;
        Vector3 smoothedStart;
        Vector3 smoothedEnd;
        Vector3 startVelocity;
        Vector3 endVelocity;
        bool hasSmoothedEndpoints;
        float noiseSeed;

        void Awake()
        {
            controller = GetComponent<EnemyController>();
            noiseSeed = Random.Range(0f, 1000f);
            EnsureLine();
        }

        void OnDestroy()
        {
            if (lineMaterial)
                Destroy(lineMaterial);
        }

        void Update()
        {
            if (!wakeLine || !controller || !controller.WakeSignalVisible)
            {
                if (wakeLine)
                    HideLine();
                return;
            }

            float chargeProgress = controller.WakeSignalChargeProgress;
            bool clearSight = controller.WakeSignalHasClearSight;
            Color color;
            float width;
            float glanceIntensity;

            if (controller.IsWakeFinalWarning)
            {
                float flash = Mathf.PingPong(Time.time * 8f, 1f);
                color = Color.Lerp(flashYellow, flashRed, flash);
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 28f);
                width = maxWidth * Mathf.Lerp(0.85f, 1.35f, pulse);
                glanceIntensity = 3.75f;
            }
            else
            {
                float intensity = Mathf.Clamp01(chargeProgress * 0.72f);
                color = Color.Lerp(
                    blockedColor,
                    readyColor,
                    clearSight ? intensity : intensity * 0.35f);
                color.a = clearSight
                    ? Mathf.Lerp(0.2f, readyColor.a, intensity)
                    : Mathf.Lerp(0.07f, blockedColor.a, Mathf.Max(0.15f, intensity));
                float pulse = clearSight
                    ? 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(8f, 18f, intensity))
                    : 0f;
                width = Mathf.Lerp(minWidth, maxWidth, intensity)
                    + pulse * intensity * 0.025f;
                glanceIntensity = intensity;
            }

            wakeLine.enabled = color.a > 0.01f;
            // The controller samples wake state on the physics clock. Use the
            // same anchor for the line origin so interpolation cannot make the
            // telegraph visibly detach or chatter during a close approach.
            SetEndpoints(controller.PhysicsAnchorPosition, controller.WakeSignalEnd, glanceIntensity);
            wakeLine.startWidth = width;
            wakeLine.endWidth = Mathf.Max(0.001f, width * endWidthMultiplier);
            wakeLine.startColor = new Color(
                color.r,
                color.g,
                color.b,
                color.a * enemyEndAlphaMultiplier);
            wakeLine.endColor = new Color(
                color.r,
                color.g,
                color.b,
                color.a * playerEndAlphaMultiplier);
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
            wakeLine.numCornerVertices = 2;
            wakeLine.alignment = LineAlignment.View;
            wakeLine.textureMode = LineTextureMode.Stretch;
            wakeLine.sortingOrder = sortingOrder;
            wakeLine.startWidth = minWidth;
            wakeLine.endWidth = minWidth * 0.08f;
            wakeLine.enabled = false;
            lineMaterial = RuntimeVisualMaterial.Create("Enemy Wake Telegraph");
            if (lineMaterial)
                wakeLine.sharedMaterial = lineMaterial;
        }

        void SetEndpoints(Vector2 start, Vector2 end, float intensity)
        {
            Vector3 targetStart = new Vector3(start.x, start.y, transform.position.z - 0.02f);
            Vector2 glanceOffset = GetGlanceOffset(intensity);
            Vector3 targetEnd = new Vector3(
                end.x + glanceOffset.x,
                end.y + glanceOffset.y,
                transform.position.z - 0.02f);

            if (!hasSmoothedEndpoints)
            {
                smoothedStart = targetStart;
                smoothedEnd = targetEnd;
                startVelocity = Vector3.zero;
                endVelocity = Vector3.zero;
                hasSmoothedEndpoints = true;
            }
            else
            {
                smoothedStart = Vector3.SmoothDamp(
                    smoothedStart,
                    targetStart,
                    ref startVelocity,
                    endpointSmoothTime);
                smoothedEnd = Vector3.SmoothDamp(
                    smoothedEnd,
                    targetEnd,
                    ref endVelocity,
                    endpointSmoothTime);
            }

            wakeLine.SetPosition(0, smoothedStart);
            wakeLine.SetPosition(1, smoothedEnd);
        }

        Vector2 GetGlanceOffset(float intensity)
        {
            if (intensity <= 0.001f || glanceRadius <= 0f)
                return Vector2.zero;

            float t = Time.time * glanceSpeed + noiseSeed;
            Vector2 offset = new Vector2(
                Mathf.PerlinNoise(t, noiseSeed) * 2f - 1f,
                Mathf.PerlinNoise(noiseSeed, t * 1.17f) * 2f - 1f);
            if (offset.sqrMagnitude > 1f)
                offset.Normalize();

            return offset * glanceRadius * intensity;
        }

        void HideLine()
        {
            wakeLine.enabled = false;
            hasSmoothedEndpoints = false;
            startVelocity = Vector3.zero;
            endVelocity = Vector3.zero;
        }
    }
}

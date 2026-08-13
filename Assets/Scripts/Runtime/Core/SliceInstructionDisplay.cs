using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// EE5-style center-screen instruction surface. Triggers own when a prompt
    /// is relevant; this component only resolves source ownership and presents
    /// the active message without competing with the persistent HUD objective.
    /// </summary>
    public sealed class SliceInstructionDisplay : MonoBehaviour
    {
        [SerializeField] Color backingColor = new Color(0f, 0f, 0f, 0.48f);
        [SerializeField] Color textColor = Color.white;
        [SerializeField] Color outlineColor = new Color(0f, 0f, 0f, 0.9f);
        [SerializeField, Min(0f)] float fadeDuration = 0.16f;
        [SerializeField, Min(0f)] float hideDuration = 0.2f;
        [SerializeField, Min(0.1f)] float visibleScale = 1f;

        Canvas canvas;
        CanvasGroup group;
        RectTransform panel;
        Text messageLabel;
        Coroutine transitionRoutine;
        string activeSourceId;
        Vector3 baseScale = Vector3.one;

        void Awake()
        {
            EnsureDisplay();
            HideImmediate();
        }

        public void Show(string sourceId, string message)
        {
            if (!this)
                return;

            EnsureDisplay();
            if (!messageLabel || !panel)
                return;

            activeSourceId = sourceId;
            messageLabel.text = message;
            panel.gameObject.SetActive(true);
            StartTransition(true);
        }

        public void Hide(string sourceId)
        {
            if (!this)
                return;

            if (!string.IsNullOrEmpty(activeSourceId) && activeSourceId != sourceId)
                return;

            activeSourceId = null;
            StartTransition(false);
        }

        void StartTransition(bool visible)
        {
            if (!this)
                return;

            if (transitionRoutine != null)
                StopCoroutine(transitionRoutine);
            transitionRoutine = StartCoroutine(TransitionRoutine(visible));
        }

        IEnumerator TransitionRoutine(bool visible)
        {
            float duration = visible ? fadeDuration : hideDuration;
            float startAlpha = group ? group.alpha : 0f;
            float endAlpha = visible ? 1f : 0f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
                float eased = Mathf.SmoothStep(0f, 1f, t);
                SetPresentation(Mathf.Lerp(startAlpha, endAlpha, eased));
                yield return null;
            }

            SetPresentation(endAlpha);
            if (!visible && panel)
                panel.gameObject.SetActive(false);
            transitionRoutine = null;
        }

        void EnsureDisplay()
        {
            if (messageLabel)
                return;

            canvas = GetComponent<Canvas>();
            if (!canvas)
            {
                GameObject canvasObject = new GameObject(
                    "Slice Instruction Canvas",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler));
                canvasObject.transform.SetParent(transform, false);
                canvas = canvasObject.GetComponent<Canvas>();
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 9000;

            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (!scaler)
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject panelObject = new GameObject(
                "Instruction Panel",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            panelObject.transform.SetParent(canvas.transform, false);
            panel = panelObject.GetComponent<RectTransform>();
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = new Vector2(0f, 58f);
            panel.sizeDelta = new Vector2(980f, 230f);
            baseScale = panel.localScale;

            Image backing = panelObject.GetComponent<Image>();
            backing.color = backingColor;
            backing.raycastTarget = false;
            group = panelObject.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            GameObject textObject = new GameObject(
                "Instruction Text",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            textObject.transform.SetParent(panel, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(28f, 18f);
            textRect.offsetMax = new Vector2(-28f, -18f);

            messageLabel = textObject.GetComponent<Text>();
            messageLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            messageLabel.fontSize = 34;
            messageLabel.fontStyle = FontStyle.Bold;
            messageLabel.alignment = TextAnchor.MiddleCenter;
            messageLabel.color = textColor;
            messageLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            messageLabel.verticalOverflow = VerticalWrapMode.Overflow;
            messageLabel.raycastTarget = false;

            Outline outline = textObject.AddComponent<Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = new Vector2(2.5f, -2.5f);
        }

        void SetPresentation(float alpha)
        {
            if (!group || !panel)
                return;

            group.alpha = Mathf.Clamp01(alpha);
            panel.localScale = baseScale *
                Mathf.Lerp(0.94f, visibleScale, Mathf.Clamp01(alpha));
        }

        void HideImmediate()
        {
            if (!panel || !group)
                return;

            activeSourceId = null;
            SetPresentation(0f);
            panel.gameObject.SetActive(false);
        }

        void OnDisable()
        {
            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
                transitionRoutine = null;
            }
        }
    }
}

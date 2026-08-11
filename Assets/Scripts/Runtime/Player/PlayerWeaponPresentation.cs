using UnityEngine;
using ExtraterrestrialExhaust.CameraSystem;

namespace ExtraterrestrialExhaust.Player
{
    /// <summary>
    /// Gives the deliberate EE5 sniper shot an immediate visual punctuation.
    /// Weapon rules still live in PlayerWeapon; this component only listens to
    /// the successful-fire event and presents the muzzle beat.
    /// </summary>
    [RequireComponent(typeof(PlayerWeapon))]
    public sealed class PlayerWeaponPresentation : MonoBehaviour
    {
        [SerializeField] Transform firePoint;
        [SerializeField, Min(0f)] float flashDuration = 0.08f;
        [SerializeField, Min(0f)] float flashLength = 0.34f;
        [SerializeField, Min(0f)] float flashWidth = 0.11f;
        [SerializeField, Min(0f)] float sideFlashLength = 0.22f;
        [SerializeField, Min(0f)] float sideFlashWidth = 0.12f;
        [SerializeField] Color flashColor = new Color(1f, 0.95f, 0.42f, 1f);
        [SerializeField] Color flashEdgeColor = new Color(1f, 0.16f, 0.04f, 0f);
        [SerializeField] int sortingOrder = 34;
        [SerializeField, Min(0f)] float cameraShakeStrength = 0.025f;
        [SerializeField, Min(0f)] float cameraShakeDuration = 0.05f;

        PlayerWeapon weapon;
        LineRenderer coreFlash;
        LineRenderer upperFlash;
        LineRenderer lowerFlash;
        Material flashMaterial;
        Vector2 flashPosition;
        Vector2 flashDirection = Vector2.right;
        float flashRemaining;

        void Awake()
        {
            weapon = GetComponent<PlayerWeapon>();
            if (!firePoint)
                firePoint = weapon ? weapon.FirePoint : null;
            EnsureFlashLines();
            SetFlashVisible(false, 0f);
        }

        void OnEnable()
        {
            if (weapon)
                weapon.Fired += HandleFired;
        }

        void OnDisable()
        {
            if (weapon)
                weapon.Fired -= HandleFired;
            flashRemaining = 0f;
            SetFlashVisible(false, 0f);
        }

        void OnDestroy()
        {
            if (flashMaterial)
                Destroy(flashMaterial);
        }

        void Update()
        {
            if (flashRemaining <= 0f)
                return;

            flashRemaining -= Time.deltaTime;
            float progress = flashDuration > 0f
                ? Mathf.Clamp01(flashRemaining / flashDuration)
                : 0f;
            SetFlashVisible(true, progress);
            if (flashRemaining <= 0f)
                SetFlashVisible(false, 0f);
        }

        void HandleFired(Vector2 position, Vector2 direction)
        {
            flashPosition = position;
            flashDirection = direction.sqrMagnitude > 0.001f
                ? direction.normalized
                : Vector2.right;
            flashRemaining = Mathf.Max(flashRemaining, flashDuration);

            if (cameraShakeStrength > 0f && cameraShakeDuration > 0f)
                PlayerCameraFollow.Instance?.Shake(cameraShakeStrength, cameraShakeDuration);
        }

        void EnsureFlashLines()
        {
            if (coreFlash)
                return;

            flashMaterial = new Material(Shader.Find("Sprites/Default"));
            flashMaterial.name = "Player Muzzle Flash";
            coreFlash = CreateFlashLine("Muzzle Flash Core");
            upperFlash = CreateFlashLine("Muzzle Flash Upper");
            lowerFlash = CreateFlashLine("Muzzle Flash Lower");
        }

        LineRenderer CreateFlashLine(string objectName)
        {
            GameObject flashObject = new GameObject(objectName);
            flashObject.transform.SetParent(transform, false);
            LineRenderer line = flashObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 4;
            line.sortingOrder = sortingOrder;
            line.sharedMaterial = flashMaterial;
            line.enabled = false;
            return line;
        }

        void SetFlashVisible(bool visible, float alpha)
        {
            if (!coreFlash || !upperFlash || !lowerFlash)
                return;

            Vector2 direction = flashDirection;
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            Vector2 origin = firePoint ? (Vector2)firePoint.position : flashPosition;
            if (flashPosition.sqrMagnitude > 0.0001f)
                origin = flashPosition;

            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(alpha));
            Vector2 tip = origin + direction * (flashLength * eased);
            Vector2 upperTip = origin + direction * (sideFlashLength * eased)
                + perpendicular * (sideFlashWidth * eased);
            Vector2 lowerTip = origin + direction * (sideFlashLength * eased)
                - perpendicular * (sideFlashWidth * eased);

            SetLine(coreFlash, origin - direction * 0.035f, tip, flashWidth * eased, visible, alpha);
            SetLine(upperFlash, origin, upperTip, flashWidth * 0.48f, visible, alpha);
            SetLine(lowerFlash, origin, lowerTip, flashWidth * 0.48f, visible, alpha);
        }

        void SetLine(
            LineRenderer line,
            Vector2 start,
            Vector2 end,
            float width,
            bool visible,
            float alpha)
        {
            line.enabled = visible && alpha > 0.001f;
            line.startWidth = width;
            line.endWidth = width * 0.12f;
            Color startColor = flashColor;
            startColor.a *= alpha;
            Color endColor = flashEdgeColor;
            endColor.a *= alpha;
            line.startColor = startColor;
            line.endColor = endColor;
            line.SetPosition(0, new Vector3(start.x, start.y, transform.position.z - 0.04f));
            line.SetPosition(1, new Vector3(end.x, end.y, transform.position.z - 0.04f));
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace ExtraterrestrialExhaust.Combat
{
    /// <summary>
    /// Recreates the EE5 enemy-bullet sprite ghosts without coupling trail
    /// lifetime to the projectile object. Ghosts can finish fading after the
    /// projectile has already been disabled by an impact.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerProjectile), typeof(SpriteRenderer))]
    public sealed class ProjectileSpriteTrailPresentation : MonoBehaviour
    {
        [SerializeField] bool enemyOnly = true;
        [SerializeField, Min(1)] int maxGhosts = 7;
        [SerializeField, Min(0.005f)] float spawnInterval = 0.025f;
        [SerializeField, Min(0.01f)] float ghostLifetime = 0.18f;
        [SerializeField, Range(0f, 1f)] float startAlpha = 0.45f;
        [SerializeField, Range(0f, 1f)] float endScale = 0.72f;
        [SerializeField, Range(0f, 1f)] float finalGhostAlphaBonus = 0.2f;

        PlayerProjectile projectile;
        SpriteRenderer source;
        float nextSpawnTime;
        bool spawning;
        readonly List<SpriteRenderer> ghosts = new();

        void Awake()
        {
            projectile = GetComponent<PlayerProjectile>();
            source = GetComponent<SpriteRenderer>();
        }

        void OnEnable()
        {
            nextSpawnTime = 0f;
            spawning = true;
        }

        void Update()
        {
            PruneGhosts();

            if (!spawning && ghosts.Count == 0)
            {
                enabled = false;
                return;
            }

            if (!spawning || !source || !source.enabled || Time.time < nextSpawnTime)
                return;

            if (enemyOnly && (!projectile || projectile.Team != ProjectileTeam.Enemy))
                return;

            if (!source.sprite)
                return;

            nextSpawnTime = Time.time + spawnInterval;
            SpawnGhost(startAlpha);
        }

        /// <summary>
        /// Stops spawning and leaves one last ghost at the committed endpoint.
        /// Existing ghosts are independent objects and continue fading.
        /// </summary>
        public void FinishTrail()
        {
            if (!spawning)
                return;

            if (source && source.sprite
                && (!enemyOnly || (projectile && projectile.Team == ProjectileTeam.Enemy)))
            {
                SpawnGhost(Mathf.Clamp01(startAlpha + finalGhostAlphaBonus));
            }

            spawning = false;
        }

        void SpawnGhost(float alpha)
        {
            while (ghosts.Count >= Mathf.Max(1, maxGhosts))
                DestroyOldestGhost();

            GameObject ghostObject = new GameObject("Projectile Sprite Trail Ghost");
            ghostObject.transform.position = transform.position;
            ghostObject.transform.rotation = transform.rotation;
            SpriteRenderer ghostRenderer = ghostObject.AddComponent<SpriteRenderer>();
            ghostRenderer.sprite = source.sprite;
            ghostRenderer.sharedMaterial = source.sharedMaterial;
            ghostRenderer.sortingLayerID = source.sortingLayerID;
            ghostRenderer.sortingOrder = source.sortingOrder - 1;
            ghostRenderer.flipX = source.flipX;
            ghostRenderer.flipY = source.flipY;
            ghostRenderer.drawMode = source.drawMode;
            ghostRenderer.size = source.size;
            ghostRenderer.maskInteraction = source.maskInteraction;
            ghostRenderer.transform.localScale = transform.lossyScale;

            Color color = source.color;
            color.a *= Mathf.Clamp01(alpha);
            ghostRenderer.color = color;

            ProjectileTrailGhost fade = ghostObject.AddComponent<ProjectileTrailGhost>();
            fade.Initialize(
                color,
                ghostRenderer.transform.localScale,
                ghostLifetime,
                endScale);
            ghosts.Add(ghostRenderer);
        }

        void PruneGhosts()
        {
            for (int i = ghosts.Count - 1; i >= 0; i--)
            {
                if (!ghosts[i])
                    ghosts.RemoveAt(i);
            }
        }

        void DestroyOldestGhost()
        {
            if (ghosts.Count == 0)
                return;

            if (ghosts[0])
                Destroy(ghosts[0].gameObject);
            ghosts.RemoveAt(0);
        }
    }

    /// <summary>Independent fade driver so ghosts survive projectile cleanup.</summary>
    [DisallowMultipleComponent]
    sealed class ProjectileTrailGhost : MonoBehaviour
    {
        SpriteRenderer spriteRenderer;
        Color baseColor;
        Vector3 startScale;
        float remaining;
        float lifetime;
        float endScale;

        public void Initialize(
            Color color,
            Vector3 initialScale,
            float duration,
            float scaleAtEnd)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            baseColor = color;
            startScale = initialScale;
            lifetime = Mathf.Max(0.01f, duration);
            remaining = lifetime;
            endScale = Mathf.Clamp01(scaleAtEnd);
        }

        void Update()
        {
            remaining -= Time.deltaTime;
            float t = Mathf.Clamp01(remaining / lifetime);
            if (spriteRenderer)
            {
                Color color = baseColor;
                color.a *= t;
                spriteRenderer.color = color;
                spriteRenderer.transform.localScale = Vector3.Lerp(
                    startScale * endScale,
                    startScale,
                    t);
            }

            if (remaining <= 0f)
                Destroy(gameObject);
        }
    }
}

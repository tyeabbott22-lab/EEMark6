using UnityEngine;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// Self-contained EE5-style extraction visual.
    ///
    /// The reference exit used a portal sheet plus several procedural helpers.
    /// EE6 keeps that composition reusable: if a portal sheet is present in
    /// Resources it becomes the focal layer; otherwise the generated core,
    /// halo, streams, and inward particles provide a deliberate fallback.
    /// </summary>
    [RequireComponent(typeof(LevelExit))]
    public sealed class ExtractionPortalPresentation : MonoBehaviour
    {
        const int PortalColumns = 8;
        const int PortalRows = 8;
        const int PortalFrameCount = PortalColumns * PortalRows;

        [Header("Portal Shape")]
        [SerializeField, Min(0.5f)] float portalDiameter = 3.8f;
        [SerializeField, Range(24, 128)] int ringSegments = 80;
        [SerializeField, Min(0f)] float rotationSpeed = 32f;
        [SerializeField, Min(0f)] float pulseSpeed = 2.3f;
        [SerializeField, Min(0.1f)] float coreRadius = 0.48f;

        [Header("Colors")]
        [SerializeField] Color coreColor = new Color(0.015f, 0.008f, 0.04f, 0.98f);
        [SerializeField] Color rimColor = new Color(0.64f, 0.24f, 1f, 0.9f);
        [SerializeField] Color innerRingColor = new Color(0.9f, 0.72f, 1f, 0.74f);
        [SerializeField] Color outerRingColor = new Color(0.22f, 0.68f, 1f, 0.5f);

        [Header("Optional Portal Sheet")]
        [SerializeField] string portalSheetResource = "Portal/portal_blue_amber_sheet";
        [SerializeField, Min(1f)] float portalFramesPerSecond = 30f;

        LevelExit levelExit;
        Transform generatedRoot;
        SpriteRenderer coreRenderer;
        SpriteRenderer haloRenderer;
        SpriteRenderer seedRenderer;
        SpriteRenderer portalRenderer;
        LineRenderer innerRing;
        LineRenderer middleRing;
        LineRenderer outerRing;
        ParticleSystem inwardParticles;
        ParticleSystem rimParticles;
        Material spriteMaterial;
        Material ringMaterial;
        Material particleMaterial;
        Texture2D coreTexture;
        Texture2D haloTexture;
        Sprite coreSprite;
        Sprite haloSprite;
        Sprite[] portalFrames;
        float portalTime;
        float captureProgress;
        float captureCollapse;
        float captureCollapseTarget;

        /// <summary>Starts the authored extraction handoff from gameplay into the portal.</summary>
        public void BeginCapture()
        {
            captureProgress = 0f;
            captureCollapse = 0f;
            captureCollapseTarget = 0f;
            if (inwardParticles)
                inwardParticles.Play();
            if (rimParticles)
                rimParticles.Play();
        }

        /// <summary>Feeds the player spiral progress into the portal presentation.</summary>
        public void SetCaptureProgress(float progress)
        {
            captureProgress = Mathf.Clamp01(progress);
        }

        /// <summary>Begins the brief portal collapse after the player is absorbed.</summary>
        public void CompleteCapture()
        {
            captureProgress = 1f;
            captureCollapseTarget = 1f;
        }

        /// <summary>Restores the idle portal if the capture is interrupted.</summary>
        public void CancelCapture()
        {
            captureProgress = 0f;
            captureCollapse = 0f;
            captureCollapseTarget = 0f;
        }

        void Awake()
        {
            levelExit = GetComponent<LevelExit>();
            EnsureHierarchy();
        }

        void OnEnable()
        {
            EnsureHierarchy();
        }

        void OnDestroy()
        {
            if (spriteMaterial)
                Destroy(spriteMaterial);
            if (ringMaterial)
                Destroy(ringMaterial);
            if (particleMaterial)
                Destroy(particleMaterial);
            if (coreSprite)
                Destroy(coreSprite);
            if (haloSprite)
                Destroy(haloSprite);
            if (coreTexture)
                Destroy(coreTexture);
            if (haloTexture)
                Destroy(haloTexture);
            if (portalFrames != null)
                for (int i = 0; i < portalFrames.Length; i++)
                    if (portalFrames[i])
                        Destroy(portalFrames[i]);
        }

        void Update()
        {
            if (!levelExit || !generatedRoot)
                return;

            float stateIntensity = levelExit.IsCapturing
                ? Mathf.Lerp(1.35f, 2f, Mathf.SmoothStep(0f, 1f, captureProgress))
                : levelExit.IsUnlocked ? 1f : 0.35f;
            float pulse = 0.94f + Mathf.Sin(Time.time * pulseSpeed) * 0.06f;
            float dt = Time.deltaTime;
            captureCollapse = Mathf.MoveTowards(
                captureCollapse,
                captureCollapseTarget,
                dt * 5.5f);
            float collapseScale = Mathf.Lerp(1f, 0.06f, Mathf.SmoothStep(0f, 1f, captureCollapse));
            generatedRoot.localScale = Vector3.one * collapseScale;

            // Separate stream rotation keeps the silhouette alive without
            // making the whole portal look like one rigid spinning ring.
            innerRing.transform.Rotate(0f, 0f, rotationSpeed * stateIntensity * dt);
            middleRing.transform.Rotate(0f, 0f, -rotationSpeed * 0.72f * stateIntensity * dt);
            outerRing.transform.Rotate(0f, 0f, rotationSpeed * 0.4f * stateIntensity * dt);

            float scalePulse = 1f + Mathf.Sin(Time.time * pulseSpeed * 0.7f) * 0.025f;
            haloRenderer.transform.localScale = Vector3.one * (1.7f * scalePulse);
            seedRenderer.transform.Rotate(0f, 0f, -24f * stateIntensity * dt);
            seedRenderer.transform.localScale = Vector3.one *
                (coreRadius * 0.3f * (1f + Mathf.Sin(Time.time * 5.5f) * 0.08f));

            SetSpriteState(coreRenderer, coreColor, pulse * stateIntensity);
            SetSpriteState(haloRenderer, new Color(rimColor.r, rimColor.g, rimColor.b, 0.5f), stateIntensity);
            SetSpriteState(seedRenderer, new Color(1f, 0.94f, 1f, 0.98f), pulse * stateIntensity);
            SetRingState(innerRing, innerRingColor, pulse * stateIntensity);
            SetRingState(middleRing, rimColor, pulse * stateIntensity);
            SetRingState(outerRing, outerRingColor, pulse * stateIntensity);

            ParticleSystem.EmissionModule inwardEmission = inwardParticles.emission;
            inwardEmission.rateOverTime = Mathf.Lerp(4f, 18f, Mathf.Clamp01(stateIntensity));
            ParticleSystem.EmissionModule rimEmission = rimParticles.emission;
            rimEmission.rateOverTime = Mathf.Lerp(2f, 10f, Mathf.Clamp01(stateIntensity));

            AnimatePortal(dt, stateIntensity);
        }

        void EnsureHierarchy()
        {
            if (generatedRoot)
                return;

            generatedRoot = new GameObject("Generated Portal Visuals").transform;
            generatedRoot.SetParent(transform, false);

            spriteMaterial = CreateMaterial("Extraction Portal Sprite Material");
            ringMaterial = CreateMaterial("Extraction Portal Ring Material");
            particleMaterial = CreateMaterial("Extraction Portal Particle Material");

            coreTexture = CreateSoftCircleTexture(64, false);
            coreSprite = Sprite.Create(
                coreTexture,
                new Rect(0f, 0f, coreTexture.width, coreTexture.height),
                new Vector2(0.5f, 0.5f),
                coreTexture.width);
            coreSprite.name = "Runtime Extraction Core";

            haloTexture = CreateSoftCircleTexture(96, true);
            haloSprite = Sprite.Create(
                haloTexture,
                new Rect(0f, 0f, haloTexture.width, haloTexture.height),
                new Vector2(0.5f, 0.5f),
                haloTexture.width);
            haloSprite.name = "Runtime Extraction Halo";

            haloRenderer = CreateSprite(
                "Accretion Halo",
                new Color(rimColor.r, rimColor.g, rimColor.b, 0.5f),
                1.7f,
                -2,
                haloSprite);
            coreRenderer = CreateSprite("Event Horizon", coreColor, coreRadius * 2f, 3, coreSprite);
            seedRenderer = CreateSprite(
                "Energy Seed",
                new Color(1f, 0.94f, 1f, 0.98f),
                coreRadius * 0.3f,
                7,
                coreSprite);

            innerRing = CreateStream(
                "Primary Energy Stream",
                portalDiameter * 0.59f,
                0.055f,
                innerRingColor,
                4,
                2.2f);
            middleRing = CreateStream(
                "Secondary Energy Stream",
                portalDiameter * 0.51f,
                0.035f,
                rimColor,
                3,
                1.6f);
            outerRing = CreateStream(
                "Faint Energy Stream",
                portalDiameter * 0.66f,
                0.025f,
                outerRingColor,
                2,
                1.2f);

            inwardParticles = CreateParticleBand(
                "Inward Particles",
                portalDiameter * 0.62f,
                42,
                rimColor,
                5);
            rimParticles = CreateParticleBand(
                "Rim Particles",
                portalDiameter * 0.19f,
                18,
                innerRingColor,
                6);

            TryCreatePortalSheet();
            inwardParticles.Play();
            rimParticles.Play();
        }

        void TryCreatePortalSheet()
        {
            if (string.IsNullOrWhiteSpace(portalSheetResource))
                return;

            Texture2D sheet = Resources.Load<Texture2D>(portalSheetResource);
            if (!sheet || sheet.width < PortalColumns || sheet.height < PortalRows)
                return;

            int frameWidth = sheet.width / PortalColumns;
            int frameHeight = sheet.height / PortalRows;
            portalFrames = new Sprite[PortalFrameCount];
            float pixelsPerUnit = frameWidth / Mathf.Max(0.1f, portalDiameter);

            for (int i = 0; i < portalFrames.Length; i++)
            {
                int column = i % PortalColumns;
                int row = i / PortalColumns;
                Rect frame = new Rect(
                    column * frameWidth,
                    sheet.height - (row + 1) * frameHeight,
                    frameWidth,
                    frameHeight);
                portalFrames[i] = Sprite.Create(
                    sheet,
                    frame,
                    new Vector2(0.5f, 0.5f),
                    pixelsPerUnit,
                    0,
                    SpriteMeshType.FullRect);
                portalFrames[i].name = $"Runtime Portal Frame {i:00}";
            }

            GameObject portalObject = new GameObject("Animated Plasma Portal");
            portalObject.transform.SetParent(generatedRoot, false);
            portalRenderer = portalObject.AddComponent<SpriteRenderer>();
            portalRenderer.sharedMaterial = spriteMaterial;
            portalRenderer.sprite = portalFrames[0];
            portalRenderer.sortingOrder = 3;

            // Keep only the inward suction helpers around the supplied focal
            // animation; otherwise the procedural core muddies the sheet.
            coreRenderer.gameObject.SetActive(false);
            haloRenderer.gameObject.SetActive(false);
            seedRenderer.gameObject.SetActive(false);
            innerRing.gameObject.SetActive(false);
            middleRing.gameObject.SetActive(false);
            outerRing.gameObject.SetActive(false);
            rimParticles.gameObject.SetActive(false);
        }

        void AnimatePortal(float dt, float stateIntensity)
        {
            if (!portalRenderer || portalFrames == null || portalFrames.Length == 0)
                return;

            portalTime += dt * Mathf.Lerp(0.9f, 1.35f, Mathf.Clamp01(stateIntensity));
            int frame = Mathf.FloorToInt(portalTime * portalFramesPerSecond) % portalFrames.Length;
            portalRenderer.sprite = portalFrames[frame];
            Color color = Color.white;
            color.a = Mathf.Clamp01(stateIntensity);
            portalRenderer.color = color;
            portalRenderer.transform.localScale = Vector3.one *
                (1f + Mathf.Sin(Time.time * pulseSpeed * 0.7f) * 0.025f);
        }

        SpriteRenderer CreateSprite(
            string objectName,
            Color color,
            float size,
            int sortingOrder,
            Sprite sprite)
        {
            GameObject spriteObject = new GameObject(objectName);
            spriteObject.transform.SetParent(generatedRoot, false);
            spriteObject.transform.localScale = Vector3.one * size;
            SpriteRenderer renderer = spriteObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sharedMaterial = spriteMaterial;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        LineRenderer CreateStream(
            string objectName,
            float radius,
            float width,
            Color color,
            int sortingOrder,
            float waveCount)
        {
            GameObject streamObject = new GameObject(objectName);
            streamObject.transform.SetParent(generatedRoot, false);
            LineRenderer stream = streamObject.AddComponent<LineRenderer>();
            stream.useWorldSpace = false;
            stream.loop = true;
            stream.positionCount = ringSegments;
            stream.startWidth = width;
            stream.endWidth = width;
            stream.numCapVertices = 2;
            stream.numCornerVertices = 2;
            stream.startColor = color;
            stream.endColor = color;
            stream.sharedMaterial = ringMaterial;
            stream.sortingOrder = sortingOrder;

            for (int i = 0; i < ringSegments; i++)
            {
                float t = i / (float)ringSegments;
                float angle = t * Mathf.PI * 2f;
                float wave = 1f + Mathf.Sin(t * Mathf.PI * 2f * waveCount) * 0.035f;
                stream.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * radius * wave,
                    Mathf.Sin(angle) * radius * wave,
                    0f));
            }

            return stream;
        }

        ParticleSystem CreateParticleBand(
            string objectName,
            float radius,
            int maxParticles,
            Color color,
            int sortingOrder)
        {
            GameObject particleObject = new GameObject(objectName);
            particleObject.transform.SetParent(generatedRoot, false);
            ParticleSystem particles = particleObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.055f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(color.r, color.g, color.b, 0.25f),
                new Color(1f, 0.94f, 1f, 0.82f));
            main.maxParticles = maxParticles;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 5f;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;

            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.radial = new ParticleSystem.MinMaxCurve(-1.1f, -0.45f);
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(color, 0.45f),
                    new GradientColorKey(color, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.7f, 0.18f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = gradient;

            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = sortingOrder;
            renderer.sharedMaterial = particleMaterial;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return particles;
        }

        Material CreateMaterial(string materialName)
        {
            Material material = new Material(Shader.Find("Sprites/Default"));
            material.name = materialName;
            return material;
        }

        static Texture2D CreateSoftCircleTexture(int size, bool halo)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = halo ? "Runtime Portal Halo Texture" : "Runtime Portal Core Texture";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            Color[] pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float radius = Mathf.Max(1f, center);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / radius;
                    float alpha;
                    if (halo)
                    {
                        float band = Mathf.Clamp01(1f - Mathf.Abs(distance - 0.62f) / 0.3f);
                        alpha = band * band * 0.75f;
                    }
                    else
                    {
                        alpha = 1f - Mathf.SmoothStep(0.72f, 1f, distance);
                    }

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static void SetSpriteState(SpriteRenderer renderer, Color baseColor, float intensity)
        {
            if (!renderer)
                return;

            Color color = baseColor;
            color.a = Mathf.Clamp01(baseColor.a * intensity);
            renderer.color = color;
        }

        static void SetRingState(LineRenderer ring, Color baseColor, float intensity)
        {
            if (!ring)
                return;

            Color color = baseColor;
            color.a = Mathf.Clamp01(baseColor.a * intensity);
            ring.startColor = color;
            ring.endColor = color;
        }
    }
}

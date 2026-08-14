using UnityEngine;

namespace ExtraterrestrialExhaust.Presentation
{
    /// <summary>
    /// Builds the repeating star layer used by EE5's realScene backdrop.
    /// Generation happens at runtime so the scene only stores the authored
    /// recipe; the deterministic seed keeps every playtest visually stable.
    /// </summary>
    public sealed class StarfieldGridGenerator : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] Sprite starTileSprite;

        [Header("Grid Size")]
        [SerializeField, Min(1)] int columns = 10;
        [SerializeField, Min(1)] int rows = 12;
        [SerializeField, Min(0f)] float seamOverlap = 0.02f;

        [Header("Repeat Obfuscation")]
        [SerializeField] int seed = 32090;
        [SerializeField] bool staggerRows = true;
        [SerializeField, Range(0f, 0.5f)] float rowStaggerAmount = 0.5f;
        [SerializeField] bool randomFlips = true;
        [SerializeField] bool randomQuarterRotations = true;
        [SerializeField, Range(0f, 0.25f)] float brightnessJitter = 0.06f;

        [Header("Extra Fake Stars")]
        [SerializeField] bool addExtraStars = true;
        [SerializeField, Min(0)] int extraStarCount = 350;
        [SerializeField] Vector2 extraStarSizeRange = new Vector2(0.015f, 0.055f);
        [SerializeField, Range(0f, 1f)] float yellowStarChance = 0.18f;
        [SerializeField, Range(0f, 1f)] float blueStarChance = 0.04f;

        [Header("Rendering")]
        [SerializeField] string generatedRootName = "_Generated Starfield";
        [SerializeField] string sortingLayerName = "Default";
        [SerializeField] int sortingOrder = -110;
        [SerializeField] bool generateOnStart = true;

        static Sprite generatedDotSprite;

        void Start()
        {
            if (generateOnStart)
                Generate();
        }

        /// <summary>
        /// Recreates the grid from the authored seed. This deliberately uses
        /// child SpriteRenderers instead of one giant texture so the copied EE5
        /// tile remains crisp at the game's point-filtered resolution.
        /// </summary>
        public void Generate()
        {
            if (!starTileSprite)
            {
                Debug.LogWarning("StarfieldGridGenerator needs a star tile sprite.", this);
                return;
            }

            ClearGenerated();

            Random.State previousState = Random.state;
            Random.InitState(seed);
            try
            {
                GameObject rootObject = new GameObject(generatedRootName);
                rootObject.transform.SetParent(transform, false);
                Transform root = rootObject.transform;

                float tileWidth = Mathf.Max(0.01f, starTileSprite.bounds.size.x - seamOverlap);
                float tileHeight = Mathf.Max(0.01f, starTileSprite.bounds.size.y - seamOverlap);
                Vector2 gridSize = new Vector2(columns * tileWidth, rows * tileHeight);

                for (int y = 0; y < rows; y++)
                {
                    float rowOffset = staggerRows && y % 2 == 1
                        ? tileWidth * rowStaggerAmount
                        : 0f;

                    for (int x = 0; x < columns; x++)
                    {
                        GameObject tile = new GameObject($"Star Tile {x},{y}");
                        tile.transform.SetParent(root, false);
                        tile.transform.localPosition = new Vector3(
                            x * tileWidth - gridSize.x * 0.5f + tileWidth * 0.5f + rowOffset,
                            y * tileHeight - gridSize.y * 0.5f + tileHeight * 0.5f,
                            0f);

                        if (randomQuarterRotations)
                            tile.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0, 4) * 90f);

                        SpriteRenderer renderer = tile.AddComponent<SpriteRenderer>();
                        renderer.sprite = starTileSprite;
                        renderer.sortingLayerName = sortingLayerName;
                        renderer.sortingOrder = sortingOrder;
                        renderer.color = BrightnessColor();

                        if (randomFlips)
                        {
                            renderer.flipX = Random.value > 0.5f;
                            renderer.flipY = Random.value > 0.5f;
                        }
                    }
                }

                if (addExtraStars)
                    AddExtraStars(root, gridSize, tileWidth);
            }
            finally
            {
                Random.state = previousState;
            }
        }

        /// <summary>
        /// Removes only the generated child root, leaving the authored recipe
        /// and backdrop transform intact for a clean rebuild.
        /// </summary>
        public void ClearGenerated()
        {
            Transform oldRoot = transform.Find(generatedRootName);
            if (!oldRoot)
                return;

            if (Application.isPlaying)
                Destroy(oldRoot.gameObject);
            else
                DestroyImmediate(oldRoot.gameObject);
        }

        Color BrightnessColor()
        {
            float brightness = Random.Range(1f - brightnessJitter, 1f + brightnessJitter);
            return new Color(brightness, brightness, brightness, 1f);
        }

        void AddExtraStars(Transform root, Vector2 gridSize, float tileWidth)
        {
            Sprite dot = GetGeneratedDotSprite();
            float width = gridSize.x + tileWidth;
            float height = gridSize.y;

            for (int i = 0; i < extraStarCount; i++)
            {
                GameObject star = new GameObject($"Extra Star {i}");
                star.transform.SetParent(root, false);
                star.transform.localPosition = new Vector3(
                    Random.Range(-width * 0.5f, width * 0.5f),
                    Random.Range(-height * 0.5f, height * 0.5f),
                    -0.01f);
                star.transform.localScale = Vector3.one * Random.Range(
                    extraStarSizeRange.x,
                    extraStarSizeRange.y);

                SpriteRenderer renderer = star.AddComponent<SpriteRenderer>();
                renderer.sprite = dot;
                renderer.sortingLayerName = sortingLayerName;
                renderer.sortingOrder = sortingOrder + 1;
                renderer.color = PickStarColor();
            }
        }

        Color PickStarColor()
        {
            float roll = Random.value;
            float alpha = Random.Range(0.45f, 1f);

            if (roll < yellowStarChance)
                return new Color(1f, 0.92f, 0.35f, alpha);

            if (roll < yellowStarChance + blueStarChance)
                return new Color(0.65f, 0.82f, 1f, alpha);

            return new Color(1f, 1f, 1f, alpha);
        }

        static Sprite GetGeneratedDotSprite()
        {
            if (generatedDotSprite)
                return generatedDotSprite;

            Texture2D texture = new Texture2D(3, 3, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            Color clear = new Color(1f, 1f, 1f, 0f);
            texture.SetPixels(new[]
            {
                clear, Color.white, clear,
                Color.white, Color.white, Color.white,
                clear, Color.white, clear
            });
            texture.Apply(false, true);

            generatedDotSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 3f, 3f),
                new Vector2(0.5f, 0.5f),
                3f);
            generatedDotSprite.name = "Generated Star Dot";
            generatedDotSprite.hideFlags = HideFlags.HideAndDontSave;
            return generatedDotSprite;
        }
    }
}

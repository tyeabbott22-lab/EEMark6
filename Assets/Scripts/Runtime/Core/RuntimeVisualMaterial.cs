using UnityEngine;

namespace ExtraterrestrialExhaust.Core
{
    /// <summary>
    /// Creates small runtime-only materials without assuming the legacy
    /// built-in sprite shader is present. Unity 6 projects may use URP, while
    /// older imported prefabs can still expect Sprites/Default.
    /// </summary>
    public static class RuntimeVisualMaterial
    {
        public static Material Create(string materialName)
        {
            Shader shader = FindSpriteShader();
            if (!shader)
                return null;

            try
            {
                Material material = new Material(shader);
                material.name = materialName;
                return material;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning(
                    $"Runtime visual material '{materialName}' could not be created: {exception.Message}");
                return null;
            }
        }

        public static Shader FindSpriteShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (!shader)
                shader = Shader.Find("Sprites/Default");
            if (!shader)
                shader = Shader.Find("Unlit/Color");
            return shader;
        }
    }
}

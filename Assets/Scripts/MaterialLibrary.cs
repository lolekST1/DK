using UnityEngine;
using UnityEngine.Rendering;

namespace DK
{
    /// <summary>
    /// Creates the handful of materials the prototype needs, at runtime, with no asset files
    /// and no Inspector wiring. Picks a shader that matches whichever render pipeline is
    /// actually active, so the project renders correctly under URP and under Built-in.
    /// </summary>
    public static class MaterialLibrary
    {
        public static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        public static readonly int ColorId = Shader.PropertyToID("_Color");

        static Shader _litShader;

        public static Shader LitShader
        {
            get
            {
                if (_litShader != null) return _litShader;

                // URP is active when a render pipeline asset is assigned.
                if (GraphicsSettings.currentRenderPipeline != null)
                    _litShader = Shader.Find("Universal Render Pipeline/Lit");

                if (_litShader == null) _litShader = Shader.Find("Standard");
                if (_litShader == null) _litShader = Shader.Find("Universal Render Pipeline/Lit");
                if (_litShader == null) _litShader = Shader.Find("Sprites/Default");

                return _litShader;
            }
        }

        public static Material CreateLit(string name, Color color, float smoothness = 0.1f, float metallic = 0f)
        {
            var mat = new Material(LitShader) { name = name };
            SetColor(mat, color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            return mat;
        }

        /// <summary>Sets both the URP and Built-in colour properties; missing ones are ignored.</summary>
        public static void SetColor(Material mat, Color color)
        {
            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, color);
            if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, color);
        }

        public static void SetColor(MaterialPropertyBlock block, Color color)
        {
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
        }
    }
}

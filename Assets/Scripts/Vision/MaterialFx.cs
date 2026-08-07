using UnityEngine;

namespace ScrapSiege.Vision
{
    /// <summary>
    /// Runtime material tweaks for the reveal system.
    ///
    /// Terrain and units are spawned with opaque URP Lit material instances, and an opaque
    /// material ignores alpha entirely - so fading an enemy needs the material switched to
    /// transparent first. Doing that here (rather than requiring a hand-authored transparent
    /// material in the Inspector) keeps the vision system self-contained and avoids adding
    /// another unwired-reference failure mode, which has already cost this project real debugging
    /// time twice.
    /// </summary>
    public static class MaterialFx
    {
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>
        /// Switches a URP Lit material instance to the transparent surface mode. Safe to call on
        /// a material that is already transparent. Must be given an instance, never a shared
        /// asset - this mutates it.
        /// </summary>
        public static void MakeTransparent(Material material)
        {
            if (material == null) return;

            material.SetFloat(SurfaceId, 1f); // 0 = Opaque, 1 = Transparent
            material.SetFloat(SrcBlendId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat(DstBlendId, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat(ZWriteId, 0f);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        /// <summary>
        /// Sets alpha on whichever colour property the shader actually uses. URP Lit exposes
        /// _BaseColor; the built-in fallback and some unlit shaders only have _Color. Writing
        /// only one of them silently does nothing on the other pipeline.
        /// </summary>
        public static void SetAlpha(Material material, float alpha)
        {
            if (material == null) return;

            if (material.HasProperty(BaseColorId))
            {
                Color c = material.GetColor(BaseColorId);
                c.a = alpha;
                material.SetColor(BaseColorId, c);
            }

            if (material.HasProperty(ColorId))
            {
                Color c = material.GetColor(ColorId);
                c.a = alpha;
                material.SetColor(ColorId, c);
            }
        }
    }
}

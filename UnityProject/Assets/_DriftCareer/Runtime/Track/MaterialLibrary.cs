using System.Collections.Generic;
using DriftCareer.Core.Garage;
using UnityEngine;
using UnityEngine.Rendering;

namespace DriftCareer.Runtime.Track
{
    /// <summary>
    /// Creates materials at runtime for URP (Universal Render Pipeline/Lit) with a Built-in (Standard) fallback,
    /// plus small procedural textures. No third-party textures are used (see docs/ASSET_LICENSES.md).
    /// </summary>
    public static class MaterialLibrary
    {
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static Shader _lit, _unlit, _particles;

        public static bool IsUrp => GraphicsSettings.currentRenderPipeline != null;

        public static Shader Lit
        {
            get
            {
                if (_lit == null)
                    _lit = (IsUrp ? Shader.Find("Universal Render Pipeline/Lit") : null) ?? Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
                return _lit;
            }
        }

        public static Shader Unlit
        {
            get
            {
                if (_unlit == null)
                    _unlit = (IsUrp ? Shader.Find("Universal Render Pipeline/Unlit") : null) ?? Shader.Find("Unlit/Color") ?? Lit;
                return _unlit;
            }
        }

        public static Shader Particles
        {
            get
            {
                if (_particles == null)
                    _particles = (IsUrp ? Shader.Find("Universal Render Pipeline/Particles/Unlit") : null)
                                 ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Unlit;
                return _particles;
            }
        }

        public static Material LitColor(string key, Color color, float smoothness = 0.3f, float metallic = 0f, Texture tex = null, Vector2? tiling = null)
        {
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Lit) { name = "DC_" + key };
            SetColor(m, color);
            SetSurface(m, smoothness, metallic);
            if (tex != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
                if (tiling.HasValue)
                {
                    if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", tiling.Value);
                    if (m.HasProperty("_MainTex")) m.SetTextureScale("_MainTex", tiling.Value);
                }
            }
            m.enableInstancing = true;
            Cache[key] = m;
            return m;
        }

        public static Material UnlitColor(string key, Color color)
        {
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Unlit) { name = "DC_" + key };
            SetColor(m, color);
            m.enableInstancing = true;
            Cache[key] = m;
            return m;
        }

        public static Material Emissive(string key, Color color, float intensity)
        {
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Lit) { name = "DC_" + key };
            SetColor(m, color);
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * intensity);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            Cache[key] = m;
            return m;
        }

        /// <summary>Transparent unlit material for decals/paint markings and ghost previews.</summary>
        public static Material Transparent(string key, Color color)
        {
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(IsUrp ? Unlit : (Shader.Find("Sprites/Default") ?? Unlit)) { name = "DC_" + key };
            SetColor(m, color);
            MakeTransparent(m);
            Cache[key] = m;
            return m;
        }

        public static Material ParticleMaterial(string key, Texture2D tex)
        {
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Particles) { name = "DC_" + key };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            SetColor(m, Color.white);
            MakeTransparent(m);
            Cache[key] = m;
            return m;
        }

        /// <summary>Car paint material from a garage finish (instance per car so try-on never leaks).</summary>
        public static Material CarPaint(string hex, PaintFinish finish)
        {
            var m = new Material(Lit) { name = "DC_Paint" };
            Color c = ParseHex(hex, new Color(0.78f, 0.06f, 0.18f));
            float smooth = 0.8f, metal = 0.1f;
            switch (finish)
            {
                case PaintFinish.Matte: smooth = 0.15f; metal = 0f; break;
                case PaintFinish.Satin: smooth = 0.45f; metal = 0.1f; break;
                case PaintFinish.Metallic: smooth = 0.75f; metal = 0.65f; break;
                case PaintFinish.Pearl: smooth = 0.88f; metal = 0.35f; break;
                case PaintFinish.Chrome: smooth = 0.97f; metal = 1f; c = Color.Lerp(c, Color.white, 0.4f); break;
            }
            SetColor(m, c);
            SetSurface(m, smooth, metal);
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static void SetSurface(Material m, float smoothness, float metallic)
        {
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        }

        public static void MakeTransparent(Material m)
        {
            if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            if (m.HasProperty("_SrcBlend"))
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
            }
            if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 3f);
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        public static Color ParseHex(string hex, Color fallback)
        {
            if (VisualPartCatalog.TryParseColor(hex, out float r, out float g, out float b, out float a)) return new Color(r, g, b, a);
            return fallback;
        }

        // ---------------- Procedural textures ----------------

        public static Texture2D AsphaltTexture()
        {
            const string key = "tex_asphalt";
            if (TexCache.TryGetValue(key, out var t) && t != null) return t;
            t = Noise(key, 256, new Color(0.17f, 0.17f, 0.18f), new Color(0.26f, 0.26f, 0.27f), 0.55f, 7);
            return t;
        }

        public static Texture2D ConcreteTexture()
        {
            const string key = "tex_concrete";
            if (TexCache.TryGetValue(key, out var t) && t != null) return t;
            return Noise(key, 128, new Color(0.5f, 0.5f, 0.49f), new Color(0.62f, 0.61f, 0.59f), 0.4f, 13);
        }

        private static readonly Dictionary<string, Texture2D> TexCache = new Dictionary<string, Texture2D>();

        private static Texture2D Noise(string key, int size, Color a, Color b, float contrast, int seed)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = key, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            var px = new Color32[size * size];
            var rng = new System.Random(seed);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.07f + seed, y * 0.07f) * 0.6f + (float)rng.NextDouble() * contrast * 0.6f;
                if (rng.NextDouble() < 0.015) n += 0.35f; // aggregate speckles
                px[y * size + x] = Color.Lerp(a, b, Mathf.Clamp01(n));
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            TexCache[key] = tex;
            return tex;
        }

        /// <summary>Soft round puff used by tire smoke particles.</summary>
        public static Texture2D SmokePuff()
        {
            const string key = "tex_smoke";
            if (TexCache.TryGetValue(key, out var t) && t != null) return t;
            const int s = 64;
            t = new Texture2D(s, s, TextureFormat.RGBA32, false) { name = key, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x - s * 0.5f) / (s * 0.5f), dy = (y - s * 0.5f) / (s * 0.5f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float noise = Mathf.PerlinNoise(x * 0.15f, y * 0.15f) * 0.35f + 0.65f;
                float alpha = Mathf.Clamp01(1f - d) * noise;
                alpha = alpha * alpha * (3f - 2f * alpha);
                px[y * s + x] = new Color(1f, 1f, 1f, alpha);
            }
            t.SetPixels32(px);
            t.Apply(false, true);
            TexCache[key] = t;
            return t;
        }
    }
}

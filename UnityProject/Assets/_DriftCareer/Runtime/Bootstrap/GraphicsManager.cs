using DriftCareer.Core.Persistence;
using DriftCareer.Core.Platform;
using UnityEngine;
using UnityEngine.Rendering;
#if DC_URP
using UnityEngine.Rendering.Universal;
#endif

namespace DriftCareer.Runtime
{
    /// <summary>
    /// Applies graphics settings at runtime: frame rate, v-sync, render scale, shadows, HDR, anti-aliasing and a
    /// global post-processing volume (ACES tonemapping, bloom, color grading, vignette, motion blur, film grain).
    /// SSAO is a URP renderer feature: add it once with "Drift Career > Setup URP Graphics" in the editor.
    /// </summary>
    public static class GraphicsManager
    {
#if DC_URP
        private static Volume _volume;
        private static Bloom _bloom;
        private static Tonemapping _tonemap;
        private static ColorAdjustments _color;
        private static Vignette _vignette;
        private static MotionBlur _motion;
        private static FilmGrain _grain;
        private static WhiteBalance _white;
#endif

        public static void Apply(SettingsData s, PlatformDefaults d, Camera cam)
        {
            int fps = s.targetFps > 0 ? s.targetFps : d.targetFps;
            Application.targetFrameRate = fps;
            QualitySettings.vSyncCount = s.vSync ? 1 : 0;
            QualitySettings.shadowDistance = s.shadowQuality == 0 ? 0f : s.shadowDistance;
            QualitySettings.shadows = s.shadowQuality == 0 ? ShadowQuality.Disable : (s.shadowQuality == 1 ? ShadowQuality.HardOnly : ShadowQuality.All);
            QualitySettings.shadowResolution = s.shadowQuality >= 3 ? ShadowResolution.VeryHigh : (s.shadowQuality == 2 ? ShadowResolution.High : ShadowResolution.Medium);
            QualitySettings.antiAliasing = s.antiAliasing == 3 ? 4 : 0;
            QualitySettings.anisotropicFiltering = d.quality >= QualityTier.Medium ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Enable;
            if (cam != null) cam.allowHDR = s.hdr;

#if DC_URP
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = Mathf.Clamp(s.renderScale, 0.5f, 1.5f);
                urp.shadowDistance = s.shadowQuality == 0 ? 0f : s.shadowDistance;
                urp.supportsHDR = s.hdr;
                urp.msaaSampleCount = s.antiAliasing == 3 ? 4 : 1;
            }
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = s.postProcessing;
                data.renderShadows = s.shadowQuality > 0;
                data.antialiasing = s.antiAliasing == 1 ? AntialiasingMode.FastApproximateAntialiasing
                    : s.antiAliasing == 2 ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
                data.antialiasingQuality = AntialiasingQuality.High;
            }
            EnsureVolume();
            _volume.enabled = s.postProcessing;
            _bloom.active = s.bloom;
            _vignette.active = s.vignette;
            _motion.active = s.motionBlur && !s.reduceMotion;
            _grain.active = s.filmGrain;
#endif
        }

#if DC_URP
        private static void EnsureVolume()
        {
            if (_volume != null) return;
            var go = new GameObject("DC_PostProcessing");
            Object.DontDestroyOnLoad(go);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 10f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _volume.sharedProfile = profile;
            _tonemap = profile.Add<Tonemapping>(true);
            _tonemap.mode.Override(TonemappingMode.ACES);
            _bloom = profile.Add<Bloom>(true);
            _bloom.threshold.Override(1.0f);
            _bloom.intensity.Override(0.65f);
            _bloom.scatter.Override(0.68f);
            _color = profile.Add<ColorAdjustments>(true);
            _color.postExposure.Override(0.15f);
            _color.contrast.Override(14f);
            _color.saturation.Override(6f);
            _white = profile.Add<WhiteBalance>(true);
            _white.temperature.Override(4f);
            _vignette = profile.Add<Vignette>(true);
            _vignette.intensity.Override(0.24f);
            _vignette.smoothness.Override(0.45f);
            _motion = profile.Add<MotionBlur>(true);
            _motion.intensity.Override(0.25f);
            _grain = profile.Add<FilmGrain>(true);
            _grain.intensity.Override(0.18f);
        }
#endif

        /// <summary>Realtime reflection probe rendered once after the world is built (skipped on low tier).</summary>
        public static ReflectionProbe CreateProbe(Transform parent, Vector3 position, Vector3 size, int resolution)
        {
            var go = new GameObject("ReflectionProbe");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.size = size;
            probe.resolution = resolution;
            probe.boxProjection = true;
            probe.hdr = true;
            probe.RenderProbe();
            return probe;
        }
    }
}

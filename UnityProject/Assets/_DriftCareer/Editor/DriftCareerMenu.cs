using System.Collections.Generic;
using System.IO;
using System.Text;
using DriftCareer.Core.Catalog;
using DriftCareer.Core.Localization;
using DriftCareer.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
#if DC_URP
using UnityEngine.Rendering.Universal;
#endif

namespace DriftCareer.EditorTools
{
    /// <summary>One-click project setup and validation (menu "Drift Career").</summary>
    public static class DriftCareerMenu
    {
        private const string Root = "Assets/_DriftCareer";
        private const string BootstrapScene = Root + "/Scenes/Bootstrap.unity";

        [MenuItem("Drift Career/1. Setup Project (scene, build settings, shaders)", priority = 1)]
        public static void SetupProject()
        {
            Directory.CreateDirectory(Root + "/Scenes");
            if (!File.Exists(BootstrapScene))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("DriftCareer").AddComponent<GameBootstrap>();
                EditorSceneManager.SaveScene(scene, BootstrapScene);
            }
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(BootstrapScene, true) };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != BootstrapScene) scenes.Add(new EditorBuildSettingsScene(s.path, false));
            EditorBuildSettings.scenes = scenes.ToArray();

            AddAlwaysIncludedShaders(new[]
            {
                "Skybox/Procedural", "Universal Render Pipeline/Lit", "Universal Render Pipeline/Complex Lit", "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Particles/Unlit", "Sprites/Default", "Standard", "Unlit/Color", "Particles/Standard Unlit"
            });
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            if (PlayerSettings.productName == "New Unity Project" || string.IsNullOrEmpty(PlayerSettings.productName)) PlayerSettings.productName = "Drift Career";
            Time.fixedDeltaTime = 0.01f;
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(BootstrapScene);
            Debug.Log("[DriftCareer] Project setup done. Open Assets/_DriftCareer/Scenes/Bootstrap.unity and press Play.");
        }

        private static void AddAlwaysIncludedShaders(IEnumerable<string> names)
        {
            var graphics = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            if (graphics == null) return;
            var so = new SerializedObject(graphics);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null) return;
            foreach (var name in names)
            {
                var shader = Shader.Find(name);
                if (shader == null) continue;
                bool exists = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) { exists = true; break; }
                if (exists) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            }
            so.ApplyModifiedProperties();
        }

        [MenuItem("Drift Career/2. Setup URP Graphics (HDR, shadows, SSAO)", priority = 2)]
        public static void SetupUrp()
        {
#if DC_URP
            var urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null)
            {
                Directory.CreateDirectory(Root + "/Settings");
                var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, Root + "/Settings/DC_Renderer.asset");
                urp = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(urp, Root + "/Settings/DC_URP.asset");
                GraphicsSettings.defaultRenderPipeline = urp;
                QualitySettings.renderPipeline = urp;
            }
            urp.supportsHDR = true;
            urp.shadowDistance = 120f;
            urp.shadowCascadeCount = 2;
            urp.renderScale = 1f;
            EditorUtility.SetDirty(urp);
            AddSsao(urp);
            AssetDatabase.SaveAssets();
            Debug.Log("[DriftCareer] URP configured: HDR, 2 shadow cascades, SSAO feature. Per-device quality is applied at runtime.");
#else
            EditorUtility.DisplayDialog("Drift Career", "URP package not found. Create the project from the 'Universal 3D' template (Unity Hub) or install com.unity.render-pipelines.universal.", "OK");
#endif
        }

#if DC_URP
        private static void AddSsao(UniversalRenderPipelineAsset urp)
        {
            var so = new SerializedObject(urp);
            var list = so.FindProperty("m_RendererDataList");
            if (list == null || list.arraySize == 0) return;
            var data = list.GetArrayElementAtIndex(0).objectReferenceValue as ScriptableRendererData;
            if (data == null) return;
            foreach (var f in data.rendererFeatures) if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion") return;
            var type = System.Type.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion, Unity.RenderPipelines.Universal.Runtime");
            if (type == null) { Debug.LogWarning("[DriftCareer] SSAO feature type not found in this URP version; add it manually in the renderer asset."); return; }
            var feature = ScriptableObject.CreateInstance(type) as ScriptableRendererFeature;
            if (feature == null) return;
            feature.name = "SSAO";
            AssetDatabase.AddObjectToAsset(feature, data);
            data.rendererFeatures.Add(feature);
            EditorUtility.SetDirty(data);
        }
#endif

        [MenuItem("Drift Career/3. Validate Project", priority = 3)]
        public static void Validate()
        {
            var report = new StringBuilder();
            int problems = 0;
            void Check(bool ok, string what)
            {
                report.AppendLine((ok ? "OK    " : "FIX   ") + what);
                if (!ok) problems++;
            }
            Check(Mathf.Abs(Time.fixedDeltaTime - 0.01f) < 1e-4f, "Fixed timestep 0.01 s (runtime also enforces it)");
            Check(PlayerSettings.colorSpace == ColorSpace.Linear, "Linear color space");
            Check(GraphicsSettings.defaultRenderPipeline != null, "URP asset assigned (Graphics settings)");
#if ENABLE_INPUT_SYSTEM
            Check(true, "Input System active");
#else
            Check(false, "Input System package active (Player Settings > Active Input Handling = Input System or Both)");
#endif
            bool sceneInBuild = false;
            foreach (var s in EditorBuildSettings.scenes) if (s.path == BootstrapScene && s.enabled) sceneInBuild = true;
            Check(sceneInBuild, "Bootstrap scene in Build Settings (run '1. Setup Project')");
            var catalog = Resources.Load<TextAsset>("DriftCareer/Catalog/catalog");
            var ru = Resources.Load<TextAsset>("DriftCareer/Localization/ru");
            var en = Resources.Load<TextAsset>("DriftCareer/Localization/en");
            Check(catalog != null && ru != null && en != null, "Catalog and RU/EN localization present");
            Check(Resources.Load<StyleSheet>("DriftCareer/UI/Main") != null, "UI stylesheet imported");
            Check(Resources.Load<ThemeStyleSheet>("DriftCareer/UI/DriftTheme") != null, "UI theme imported");
            if (catalog != null && ru != null && en != null)
            {
                var loc = new LocalizationService();
                loc.LoadTable("ru", ru.text);
                loc.LoadTable("en", en.text);
                var errors = CatalogJson.Parse(catalog.text).Validate(k => loc.HasIn("ru", k) && loc.HasIn("en", k), id => id == "procedural:academy");
                Check(errors.Count == 0, "Catalog consistency" + (errors.Count > 0 ? ": " + string.Join("; ", errors) : ""));
                Check(loc.MissingKeys("en", "ru").Count == 0, "RU/EN key parity");
            }
            Debug.Log("[DriftCareer] Validation " + (problems == 0 ? "passed" : $"found {problems} issue(s)") + "\n" + report);
            EditorUtility.DisplayDialog("Drift Career", problems == 0 ? "All checks passed." : $"{problems} issue(s). See Console for details.", "OK");
        }

        [MenuItem("Drift Career/Open Save Folder", priority = 20)]
        public static void OpenSaves() => EditorUtility.RevealInFinder(Path.Combine(Application.persistentDataPath, "DriftCareer"));

        [MenuItem("Drift Career/Delete Local Saves", priority = 21)]
        public static void DeleteSaves()
        {
            string dir = Path.Combine(Application.persistentDataPath, "DriftCareer");
            if (!Directory.Exists(dir)) return;
            if (EditorUtility.DisplayDialog("Drift Career", "Delete all local saves (settings, profile, layouts, ghosts)?\n" + dir, "Delete", "Cancel"))
                Directory.Delete(dir, true);
        }
    }
}

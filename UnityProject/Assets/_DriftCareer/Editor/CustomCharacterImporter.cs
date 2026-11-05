using System;
using System.IO;
using DriftCareer.Core;
using UnityEditor;
using UnityEngine;

namespace DriftCareer.EditorTools
{
    /// <summary>
    /// Imports a player's own driver model (FBX/GLB, 1 unit = 1 m, Y up, facing +Z) as a standing-preview avatar.
    /// Validates size, triangle and material budgets, requires a rights attestation, writes manifest + prefab to
    /// Resources/DriftCareer/UserCharacters. The seated in-car driver stays procedural (no rig/IK requirements).
    /// </summary>
    public sealed class CustomCharacterImporter : EditorWindow
    {
        private const int MaxTriangles = 40000;
        private const int MaxMaterials = 8;
        private GameObject _model;
        private string _name = "My driver";
        private bool _rights;
        private string _report = "";
        private bool _valid;

        [MenuItem("Drift Career/Custom Character Import (own 3D driver)", priority = 11)]
        public static void Open() => GetWindow<CustomCharacterImporter>("Custom Character");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Model: humanoid, 1.5–2.1 m tall, up to 40k triangles and 8 materials. Only upload models you own.", MessageType.Info);
            _model = (GameObject)EditorGUILayout.ObjectField("Model", _model, typeof(GameObject), false);
            _name = EditorGUILayout.TextField("Display name", _name);
            using (new EditorGUI.DisabledScope(_model == null))
                if (GUILayout.Button("Validate")) Validate();
            if (!string.IsNullOrEmpty(_report)) EditorGUILayout.HelpBox(_report, _valid ? MessageType.Info : MessageType.Warning);
            _rights = EditorGUILayout.ToggleLeft("I own the rights to this model and its textures (required)", _rights);
            using (new EditorGUI.DisabledScope(!_valid || !_rights))
                if (GUILayout.Button("Create")) Create();
        }

        private void Validate()
        {
            var renderers = _model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) { _valid = false; _report = "No renderers found."; return; }
            var b = renderers[0].bounds;
            int tris = 0, mats = 0;
            foreach (var r in renderers)
            {
                b.Encapsulate(r.bounds);
                mats += r.sharedMaterials.Length;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                if (r is SkinnedMeshRenderer s && s.sharedMesh != null) tris += s.sharedMesh.triangles.Length / 3;
            }
            float h = b.size.y;
            _valid = h >= 1.5f && h <= 2.1f && tris <= MaxTriangles && mats <= MaxMaterials;
            _report = $"Height {h:0.00} m (1.5–2.1), triangles {tris} (≤{MaxTriangles}), materials {mats} (≤{MaxMaterials}) → {(_valid ? "OK" : "fix the model")}";
        }

        private void Create()
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in _name.ToLowerInvariant())
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            string id = sb.Length == 0 ? "driver" : sb.ToString().Trim('_');
            string dir = "Assets/_DriftCareer/Resources/DriftCareer/UserCharacters";
            Directory.CreateDirectory(dir);
            var manifest = JsonValue.NewObject().Set("version", 1).Set("id", id).Set("name", _name.Trim()).Set("rights", _rights)
                .Set("source", AssetDatabase.GetAssetPath(_model)).Set("created", DateTime.UtcNow.ToString("o"));
            File.WriteAllText(Path.Combine(dir, id + ".json"), JsonWriter.Write(manifest, true));
            PrefabUtility.SaveAsPrefabAsset(_model, Path.Combine(dir, id + ".prefab"));
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Drift Career", $"Driver '{_name}' created. Choose it in Personal Account → Driver.", "OK");
        }
    }
}

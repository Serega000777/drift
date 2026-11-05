using System;
using System.Collections.Generic;
using System.IO;
using DriftCareer.Core;
using DriftCareer.Core.Garage;
using DriftCareer.Core.Vehicle;
using UnityEditor;
using UnityEngine;

namespace DriftCareer.EditorTools
{
    /// <summary>
    /// Turns a player's own FBX/GLB car model into a drivable car (PC / dev pipeline):
    /// 1) detects the four wheels (by name, then by geometry), 2) measures wheelbase/track/size,
    /// 3) suggests a spec (offline archetype matcher; AI identification is a future server service),
    /// 4) writes a manifest + prefab into Resources/DriftCareer/UserCars. Requires a rights attestation.
    /// Model scale must be 1 unit = 1 m, +Z forward, Y up.
    /// </summary>
    public sealed class CustomCarImporter : EditorWindow
    {
        private GameObject _model;
        private ModelAnalysis _analysis;
        private List<string> _issues = new List<string>();
        private List<SpecCandidate> _candidates = new List<SpecCandidate>();
        private int _candidate;
        private VehicleSpecSheet _spec;
        private string _name = "My car";
        private bool _rights;
        private Vector2 _scroll;

        private static readonly string[] FrontLeft = { "fl", "frontleft", "front_left", "wheel_fl", "wheelfl", "lf", "переднеелевое" };
        private static readonly string[] FrontRight = { "fr", "frontright", "front_right", "wheel_fr", "wheelfr", "rf" };
        private static readonly string[] RearLeft = { "rl", "rearleft", "rear_left", "wheel_rl", "wheelrl", "lr", "bl", "backleft" };
        private static readonly string[] RearRight = { "rr", "rearright", "rear_right", "wheel_rr", "wheelrr", "br", "backright" };

        [MenuItem("Drift Career/Custom Car Import (own 3D model)", priority = 10)]
        public static void Open() => GetWindow<CustomCarImporter>("Custom Car Import");

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.HelpBox("Import your own car model (FBX, or GLB with glTFast installed). Scale 1 unit = 1 m, +Z forward. " +
                                    "Handling is an approximation from specifications - not measured real-world dynamics.", MessageType.Info);
            _model = (GameObject)EditorGUILayout.ObjectField("Model", _model, typeof(GameObject), false);
            _name = EditorGUILayout.TextField("Display name", _name);
            using (new EditorGUI.DisabledScope(_model == null))
                if (GUILayout.Button("1. Analyze model")) Analyze();

            if (_analysis != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Analysis", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Size {_analysis.lengthM:0.00} x {_analysis.widthM:0.00} x {_analysis.heightM:0.00} m, tris {_analysis.triangleCount}, materials {_analysis.materialCount}");
                EditorGUILayout.LabelField($"Wheels detected: {_analysis.wheelsDetected}  wheelbase {_analysis.Wheelbase:0.000} m  track F {_analysis.TrackFront:0.000} R {_analysis.TrackRear:0.000}");
                for (int i = 0; i < 4; i++) EditorGUILayout.LabelField($"  {(WheelId)i}: {_analysis.wheelPaths[i]}  r={_analysis.wheelRadii[i]:0.000}");
                foreach (var issue in _issues) EditorGUILayout.HelpBox(issue, MessageType.Warning);

                if (_candidates.Count > 0)
                {
                    var labels = new string[_candidates.Count];
                    for (int i = 0; i < labels.Length; i++) labels[i] = $"{_candidates[i].label} ({_candidates[i].confidence:0.00})";
                    int c = EditorGUILayout.Popup("2. Base archetype", _candidate, labels);
                    if (c != _candidate || _spec == null) { _candidate = c; _spec = _candidates[c].spec.Clone(); }
                }
                if (_spec != null)
                {
                    EditorGUILayout.LabelField("3. Specification (edit to match your real car)", EditorStyles.boldLabel);
                    _spec.layout = (DriveLayout)EditorGUILayout.EnumPopup("Drivetrain", _spec.layout);
                    _spec.curbMassKg = EditorGUILayout.FloatField("Curb mass (kg)", _spec.curbMassKg);
                    _spec.frontWeightFraction = EditorGUILayout.Slider("Front weight", _spec.frontWeightFraction, 0.35f, 0.7f);
                    _spec.peakPowerKw = EditorGUILayout.FloatField("Peak power (kW)", _spec.peakPowerKw);
                    _spec.peakPowerRpm = EditorGUILayout.FloatField("Power rpm", _spec.peakPowerRpm);
                    _spec.peakTorqueNm = EditorGUILayout.FloatField("Peak torque (Nm)", _spec.peakTorqueNm);
                    _spec.peakTorqueRpm = EditorGUILayout.FloatField("Torque rpm", _spec.peakTorqueRpm);
                    _spec.redlineRpm = EditorGUILayout.FloatField("Redline", _spec.redlineRpm);
                    _spec.turbo = EditorGUILayout.Toggle("Turbo", _spec.turbo);
                    _spec.gearCount = EditorGUILayout.IntSlider("Gears", _spec.gearCount, 4, 8);
                    _spec.tireSizeFront = EditorGUILayout.TextField("Front tires", _spec.tireSizeFront);
                    _spec.tireSizeRear = EditorGUILayout.TextField("Rear tires", _spec.tireSizeRear);
                    _rights = EditorGUILayout.ToggleLeft("I own the rights to this model and its textures (required)", _rights);
                    using (new EditorGUI.DisabledScope(!_rights || !_analysis.wheelsDetected))
                        if (GUILayout.Button("4. Create drivable car")) Create();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void Analyze()
        {
            _analysis = new ModelAnalysis();
            _issues.Clear();
            var renderers = _model.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds(_model.transform.position, Vector3.zero);
            bool first = true;
            var mats = new HashSet<Material>();
            int tris = 0;
            foreach (var r in renderers)
            {
                if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
                foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m);
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) tris += smr.sharedMesh.triangles.Length / 3;
            }
            _analysis.lengthM = bounds.size.z; _analysis.widthM = bounds.size.x; _analysis.heightM = bounds.size.y;
            _analysis.triangleCount = tris; _analysis.materialCount = mats.Count;

            var wheels = new Transform[4];
            foreach (var t in _model.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant().Replace(" ", "").Replace(".", "_");
                if (!(n.Contains("wheel") || n.Contains("tire") || n.Contains("tyre") || n.Contains("колес") || n.Contains("rim"))) continue;
                if (wheels[0] == null && Matches(n, FrontLeft)) wheels[0] = t;
                else if (wheels[1] == null && Matches(n, FrontRight)) wheels[1] = t;
                else if (wheels[2] == null && Matches(n, RearLeft)) wheels[2] = t;
                else if (wheels[3] == null && Matches(n, RearRight)) wheels[3] = t;
            }
            if (Array.Exists(wheels, w => w == null)) GuessWheelsByGeometry(renderers, bounds, wheels);
            _analysis.wheelsDetected = !Array.Exists(wheels, w => w == null);
            for (int i = 0; i < 4; i++)
            {
                if (wheels[i] == null) continue;
                var wb = WorldBounds(wheels[i]);
                Vector3 c = _model.transform.InverseTransformPoint(wb.center);
                _analysis.wheelCenters[i * 3] = c.x; _analysis.wheelCenters[i * 3 + 1] = c.y; _analysis.wheelCenters[i * 3 + 2] = c.z;
                _analysis.wheelRadii[i] = Mathf.Max(wb.extents.y, wb.extents.z);
                _analysis.wheelPaths[i] = AnimationUtility.CalculateTransformPath(wheels[i], _model.transform);
            }
            _issues = GeometrySpecEstimator.Validate(_analysis);
            IdentificationResult result = null;
            new GeometryOnlyIdentificationService().Identify(new IdentificationRequest { geometry = _analysis, userHint = _name }, r => result = r);
            _candidates = result?.candidates ?? new List<SpecCandidate>();
            _candidate = 0;
            _spec = _candidates.Count > 0 ? _candidates[0].spec.Clone() : new VehicleSpecSheet();
            GeometrySpecEstimator.ApplyGeometry(_spec, _analysis);
        }

        private static bool Matches(string n, string[] keys)
        {
            foreach (var k in keys) if (n.EndsWith(k) || n.Contains("_" + k) || n.Contains(k + "_") || n.Contains(k)) return true;
            return false;
        }

        private static Bounds WorldBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(t.position, Vector3.one * 0.6f);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>Fallback: roughly round renderers near the four lower corners of the car.</summary>
        private void GuessWheelsByGeometry(Renderer[] renderers, Bounds car, Transform[] wheels)
        {
            var corners = new[]
            {
                new Vector3(car.min.x, car.min.y, car.max.z), new Vector3(car.max.x, car.min.y, car.max.z),
                new Vector3(car.min.x, car.min.y, car.min.z), new Vector3(car.max.x, car.min.y, car.min.z)
            };
            for (int i = 0; i < 4; i++)
            {
                if (wheels[i] != null) continue;
                float best = float.MaxValue;
                foreach (var r in renderers)
                {
                    var b = r.bounds;
                    float round = Mathf.Abs(b.size.y - b.size.z) / Mathf.Max(0.01f, b.size.y);
                    if (b.size.y < 0.35f || b.size.y > 1.2f || round > 0.25f || b.size.x > 0.5f) continue;
                    float d = Vector3.Distance(new Vector3(b.center.x, car.min.y, b.center.z), corners[i]);
                    if (d < best) { best = d; wheels[i] = r.transform; }
                }
            }
            _issues.Add("Wheels were guessed from geometry - check the list above.");
        }

        private void Create()
        {
            string id = SpecSlug(_name);
            string dir = "Assets/_DriftCareer/Resources/DriftCareer/UserCars";
            Directory.CreateDirectory(dir);
            var manifest = new CustomVehicleManifest
            {
                id = id, displayName = _name, rightsAttested = _rights, analysis = _analysis, spec = _spec,
                modelFileName = AssetDatabase.GetAssetPath(_model), status = CustomVehicleStatus.ReadyLocal, createdUtc = DateTime.UtcNow.ToString("o")
            };
            _spec.displayName = _name;
            File.WriteAllText(Path.Combine(dir, id + ".json"), JsonWriter.Write(CustomVehicleJson.Encode(manifest), true));
            PrefabUtility.SaveAsPrefabAsset(_model, Path.Combine(dir, id + ".prefab"));
            AssetDatabase.Refresh();
            var profile = SpecToProfileConverter.Convert(_spec);
            var errors = profile.Validate();
            EditorUtility.DisplayDialog("Drift Career", errors.Count == 0
                ? $"Car '{_name}' created. Select it in the in-game Garage > Cars."
                : "Car created, but the physics profile has issues:\n" + string.Join("\n", errors), "OK");
        }

        private static string SpecSlug(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in name.ToLowerInvariant())
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            return sb.Length == 0 ? "car" : sb.ToString().Trim('_');
        }
    }
}

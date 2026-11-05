using System;
using System.Collections.Generic;
using DriftCareer.Core.Vehicle;

namespace DriftCareer.Core.Garage
{
    public enum CustomVehicleScope { Private = 0, PublicRequested = 1 }

    public enum CustomVehicleStatus { Draft = 0, Identifying = 1, ReadyLocal = 2, PendingModeration = 3, Approved = 4, Rejected = 5 }

    /// <summary>Geometry facts extracted from a player's 3D model (by the editor/import tool).</summary>
    [Serializable]
    public sealed class ModelAnalysis
    {
        /// <summary>Wheel centers in model space (m): FL, FR, RL, RR as x,y,z triples.</summary>
        public float[] wheelCenters = new float[12];
        public float[] wheelRadii = new float[4];
        /// <summary>Hierarchy paths of the wheel transforms inside the model (FL, FR, RL, RR).</summary>
        public string[] wheelPaths = new string[4];
        public float lengthM, widthM, heightM;
        public int triangleCount;
        public int materialCount;
        public bool wheelsDetected;
        public List<string> warnings = new List<string>();

        public float Wheelbase => Math.Abs(0.5f * (wheelCenters[2] + wheelCenters[5]) - 0.5f * (wheelCenters[8] + wheelCenters[11]));
        public float TrackFront => Math.Abs(wheelCenters[3] - wheelCenters[0]);
        public float TrackRear => Math.Abs(wheelCenters[9] - wheelCenters[6]);
    }

    /// <summary>
    /// Player car manifest. Mobile/console builds never load player AssetBundles or scripts; a public car
    /// must go through the server pipeline (scan, validation, moderation, trusted platform build).
    /// </summary>
    [Serializable]
    public sealed class CustomVehicleManifest
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        public string id;
        public string displayName;
        public CustomVehicleScope scope = CustomVehicleScope.Private;
        public CustomVehicleStatus status = CustomVehicleStatus.Draft;
        /// <summary>Player confirms they own the rights to the model and media.</summary>
        public bool rightsAttested;
        public string modelFileName;
        public ModelAnalysis analysis = new ModelAnalysis();
        public VehicleSpecSheet spec = new VehicleSpecSheet();
        public string createdUtc;
    }

    /// <summary>Explicit JSON mapping for manifests written by the import tool and read by the game.</summary>
    public static class CustomVehicleJson
    {
        public static JsonValue Encode(CustomVehicleManifest m)
        {
            var a = m.analysis ?? new ModelAnalysis();
            var wc = JsonValue.NewArray();
            foreach (var f in a.wheelCenters) wc.Add(JsonValue.Number(f));
            var wr = JsonValue.NewArray();
            foreach (var f in a.wheelRadii) wr.Add(JsonValue.Number(f));
            var wp = JsonValue.NewArray();
            foreach (var path in a.wheelPaths ?? new string[4]) wp.Add(JsonValue.String(path ?? ""));
            var s = m.spec ?? new VehicleSpecSheet();
            var spec = JsonValue.NewObject()
                .Set("name", s.displayName).Set("layout", s.layout.ToString()).Set("mass", s.curbMassKg).Set("front", s.frontWeightFraction)
                .Set("powerKw", s.peakPowerKw).Set("powerRpm", s.peakPowerRpm).Set("torque", s.peakTorqueNm).Set("torqueRpm", s.peakTorqueRpm)
                .Set("redline", s.redlineRpm).Set("turbo", s.turbo).Set("gears", s.gearCount).Set("wheelbase", s.wheelbaseM)
                .Set("trackF", s.trackFrontM).Set("trackR", s.trackRearM).Set("length", s.lengthM).Set("width", s.widthM).Set("height", s.heightM)
                .Set("tireF", s.tireSizeFront).Set("tireR", s.tireSizeRear).Set("source", s.source.ToString()).Set("confidence", s.confidence);
            return JsonValue.NewObject()
                .Set("version", m.version).Set("id", m.id).Set("name", m.displayName).Set("scope", m.scope.ToString()).Set("status", m.status.ToString())
                .Set("rights", m.rightsAttested).Set("model", m.modelFileName ?? "").Set("created", m.createdUtc ?? "")
                .Set("analysis", JsonValue.NewObject().Set("wheelCenters", wc).Set("wheelRadii", wr).Set("wheelPaths", wp).Set("length", a.lengthM).Set("width", a.widthM)
                    .Set("height", a.heightM).Set("tris", a.triangleCount).Set("materials", a.materialCount).Set("wheels", a.wheelsDetected))
                .Set("spec", spec);
        }

        public static CustomVehicleManifest Decode(JsonValue j)
        {
            var m = new CustomVehicleManifest
            {
                id = j.GetString("id", "custom"), displayName = j.GetString("name", "Custom car"),
                scope = j.GetEnum("scope", CustomVehicleScope.Private), status = j.GetEnum("status", CustomVehicleStatus.Draft),
                rightsAttested = j.GetBool("rights"), modelFileName = j.GetString("model", ""), createdUtc = j.GetString("created", "")
            };
            var a = j.GetObject("analysis");
            if (a != null)
            {
                var wc = a.GetArray("wheelCenters");
                if (wc != null) for (int i = 0; i < Math.Min(12, wc.Count); i++) m.analysis.wheelCenters[i] = wc[i].Type == JsonType.Number ? wc[i].AsFloat : 0f;
                var wr = a.GetArray("wheelRadii");
                if (wr != null) for (int i = 0; i < Math.Min(4, wr.Count); i++) m.analysis.wheelRadii[i] = wr[i].Type == JsonType.Number ? wr[i].AsFloat : 0.3f;
                var wp = a.GetArray("wheelPaths");
                if (wp != null) for (int i = 0; i < Math.Min(4, wp.Count); i++) m.analysis.wheelPaths[i] = wp[i].Type == JsonType.String ? wp[i].AsString : "";
                m.analysis.lengthM = a.GetFloat("length"); m.analysis.widthM = a.GetFloat("width"); m.analysis.heightM = a.GetFloat("height");
                m.analysis.triangleCount = a.GetInt("tris"); m.analysis.materialCount = a.GetInt("materials"); m.analysis.wheelsDetected = a.GetBool("wheels");
            }
            var s = j.GetObject("spec");
            if (s != null)
            {
                var d = new VehicleSpecSheet();
                m.spec = new VehicleSpecSheet
                {
                    displayName = s.GetString("name", m.displayName), layout = s.GetEnum("layout", d.layout), curbMassKg = s.GetFloat("mass", d.curbMassKg),
                    frontWeightFraction = s.GetFloat("front", d.frontWeightFraction), peakPowerKw = s.GetFloat("powerKw", d.peakPowerKw),
                    peakPowerRpm = s.GetFloat("powerRpm", d.peakPowerRpm), peakTorqueNm = s.GetFloat("torque", d.peakTorqueNm),
                    peakTorqueRpm = s.GetFloat("torqueRpm", d.peakTorqueRpm), redlineRpm = s.GetFloat("redline", d.redlineRpm), turbo = s.GetBool("turbo"),
                    gearCount = s.GetInt("gears", d.gearCount), wheelbaseM = s.GetFloat("wheelbase", d.wheelbaseM), trackFrontM = s.GetFloat("trackF", d.trackFrontM),
                    trackRearM = s.GetFloat("trackR", d.trackRearM), lengthM = s.GetFloat("length", d.lengthM), widthM = s.GetFloat("width", d.widthM),
                    heightM = s.GetFloat("height", d.heightM), tireSizeFront = s.GetString("tireF", d.tireSizeFront), tireSizeRear = s.GetString("tireR", d.tireSizeRear),
                    source = s.GetEnum("source", SpecSource.Manual), confidence = s.GetFloat("confidence", 1f)
                };
            }
            return m;
        }
    }

    public sealed class IdentificationRequest
    {
        /// <summary>PNG renders/photos of the car (front, side, rear, 3/4).</summary>
        public List<byte[]> images = new List<byte[]>();
        public string userHint;
        public ModelAnalysis geometry;
    }

    public sealed class SpecCandidate
    {
        public string label;
        public float confidence;
        public VehicleSpecSheet spec;
        /// <summary>Human-readable references the service used (for transparency/review).</summary>
        public List<string> sources = new List<string>();
    }

    public sealed class IdentificationResult
    {
        public bool ok;
        public string error;
        public List<SpecCandidate> candidates = new List<SpecCandidate>();
    }

    /// <summary>
    /// Identifies a car (body type, likely drivetrain/gearbox, spec) from images + geometry.
    /// Production implementation is a server endpoint (vision LLM + licensed spec database);
    /// API keys never ship in the game client. Results are suggestions the player confirms.
    /// </summary>
    public interface IVehicleIdentificationService
    {
        bool IsAvailable { get; }
        string ProviderName { get; }
        void Identify(IdentificationRequest request, Action<IdentificationResult> onDone);
    }

    /// <summary>Offline fallback: matches model geometry against brand-free archetypes (low confidence).</summary>
    public sealed class GeometryOnlyIdentificationService : IVehicleIdentificationService
    {
        public bool IsAvailable => true;
        public string ProviderName => "offline-geometry";

        public void Identify(IdentificationRequest request, Action<IdentificationResult> onDone)
        {
            var result = new IdentificationResult { ok = true };
            var g = request?.geometry;
            foreach (var id in SpecTemplates.Ids)
            {
                var spec = SpecTemplates.Create(id);
                float score = 1f;
                if (g != null && g.wheelsDetected)
                {
                    score = 1f / (1f + Math.Abs(g.Wheelbase - spec.wheelbaseM) * 4f + Math.Abs(g.lengthM - spec.lengthM) * 0.8f + Math.Abs(g.heightM - spec.heightM) * 2f);
                    GeometrySpecEstimator.ApplyGeometry(spec, g);
                }
                spec.source = SpecSource.Template;
                spec.confidence = MathUtil.Clamp01(score * 0.5f);
                result.candidates.Add(new SpecCandidate { label = id, confidence = spec.confidence, spec = spec, sources = { "built-in archetype (no real-model data)" } });
            }
            result.candidates.Sort((a, b) => b.confidence.CompareTo(a.confidence));
            onDone?.Invoke(result);
        }
    }

    public static class GeometrySpecEstimator
    {
        /// <summary>Copies measured geometry (wheelbase, track, size, wheel radius) into the spec.</summary>
        public static void ApplyGeometry(VehicleSpecSheet spec, ModelAnalysis g)
        {
            if (g == null) return;
            if (g.lengthM > 2f) spec.lengthM = g.lengthM;
            if (g.widthM > 1f) spec.widthM = g.widthM;
            if (g.heightM > 0.8f) spec.heightM = g.heightM;
            if (!g.wheelsDetected) return;
            if (g.Wheelbase > 1.5f) spec.wheelbaseM = g.Wheelbase;
            if (g.TrackFront > 1f) spec.trackFrontM = g.TrackFront;
            if (g.TrackRear > 1f) spec.trackRearM = g.TrackRear;
        }

        /// <summary>Validation used by import tools before a car can be driven.</summary>
        public static List<string> Validate(ModelAnalysis g, int maxTriangles = 150000)
        {
            var e = new List<string>();
            if (g == null) { e.Add("no_analysis"); return e; }
            if (!g.wheelsDetected) e.Add("wheels_not_detected");
            if (g.Wheelbase < 1.6f || g.Wheelbase > 4f) e.Add("wheelbase_out_of_range");
            if (g.lengthM < 2.5f || g.lengthM > 6.5f) e.Add("length_out_of_range (check scale: 1 unit = 1 m)");
            if (g.triangleCount > maxTriangles) e.Add("too_many_triangles");
            for (int i = 0; i < 4; i++)
                if (g.wheelRadii[i] < 0.2f || g.wheelRadii[i] > 0.55f) { e.Add("wheel_radius_out_of_range"); break; }
            return e;
        }
    }
}

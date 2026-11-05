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

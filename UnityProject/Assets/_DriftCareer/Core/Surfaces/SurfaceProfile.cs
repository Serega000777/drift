using System;
using System.Collections.Generic;

namespace DriftCareer.Core.Surfaces
{
    public enum TireCompound
    {
        Street = 0,
        Sport = 1,
        DriftSemiSlick = 2,
        Winter = 3,
        Studded = 4,
    }

    /// <summary>
    /// Friction multipliers handed to the tire model for one contact. Value type, no allocations.
    /// </summary>
    public struct SurfaceGrip
    {
        public float longitudinal;
        public float lateral;
        /// <summary>Multiplier for the sliding/peak ratio (ice is "peakier"). 1 = tire default.</summary>
        public float slideRatioScale;
        public float rollingResistanceScale;
        /// <summary>Scales the peak slip (ratio and angle). Deflated tires get large values (sloppy response).</summary>
        public float peakSlipScale;

        public static SurfaceGrip Default => new SurfaceGrip { longitudinal = 1f, lateral = 1f, slideRatioScale = 1f, rollingResistanceScale = 1f, peakSlipScale = 1f };
    }

    /// <summary>
    /// Ground surface description. Coefficients are relative multipliers on the tire's own friction,
    /// calibrated for gameplay - they are NOT universal physical constants.
    /// </summary>
    [Serializable]
    public sealed class SurfaceProfile
    {
        public string id;
        public string titleKey;
        public float friction = 1f;
        public float lateralScale = 1f;
        public float slideRatioScale = 1f;
        public float rollingResistanceScale = 1f;
        /// <summary>Visual/audio tags, e.g. "smoke", "spray", "snow".</summary>
        public string vfxTag = "smoke";
        public string audioTag = "asphalt";
        /// <summary>Per tire compound friction multiplier. Missing compound -> 1.</summary>
        public Dictionary<TireCompound, float> compoundScale = new Dictionary<TireCompound, float>();

        public SurfaceGrip GripFor(TireCompound compound)
        {
            float c = compoundScale != null && compoundScale.TryGetValue(compound, out var s) ? s : 1f;
            return new SurfaceGrip
            {
                longitudinal = friction * c,
                lateral = friction * lateralScale * c,
                slideRatioScale = slideRatioScale,
                rollingResistanceScale = rollingResistanceScale,
                peakSlipScale = 1f
            };
        }
    }

    /// <summary>Built-in surface table. Future surfaces are added here or loaded from data.</summary>
    public sealed class SurfaceLibrary
    {
        public const string DryAsphalt = "dry_asphalt";
        public const string WetAsphalt = "wet_asphalt";
        public const string Concrete = "concrete";
        public const string PackedSnow = "packed_snow";
        public const string LooseSnow = "loose_snow";
        public const string Ice = "ice";
        public const string Grass = "grass";
        public const string Gravel = "gravel";

        private readonly Dictionary<string, SurfaceProfile> _byId = new Dictionary<string, SurfaceProfile>(StringComparer.Ordinal);

        public SurfaceProfile Fallback { get; private set; }

        public IEnumerable<SurfaceProfile> All => _byId.Values;

        public void Register(SurfaceProfile p)
        {
            if (p == null || string.IsNullOrEmpty(p.id)) throw new ArgumentException("Surface needs id");
            _byId[p.id] = p;
            if (Fallback == null || p.id == DryAsphalt) Fallback = p;
        }

        public SurfaceProfile Get(string id)
        {
            if (id != null && _byId.TryGetValue(id, out var p)) return p;
            return Fallback;
        }

        public static SurfaceLibrary CreateDefault()
        {
            var lib = new SurfaceLibrary();
            lib.Register(new SurfaceProfile { id = DryAsphalt, titleKey = "surface.dry_asphalt", friction = 1f, audioTag = "asphalt", vfxTag = "smoke" });
            lib.Register(new SurfaceProfile { id = Concrete, titleKey = "surface.concrete", friction = 0.95f, audioTag = "asphalt", vfxTag = "smoke" });
            lib.Register(new SurfaceProfile { id = WetAsphalt, titleKey = "surface.wet_asphalt", friction = 0.72f, slideRatioScale = 0.95f, audioTag = "wet", vfxTag = "spray" });
            lib.Register(new SurfaceProfile { id = Grass, titleKey = "surface.grass", friction = 0.55f, rollingResistanceScale = 4f, audioTag = "grass", vfxTag = "dirt" });
            lib.Register(new SurfaceProfile { id = Gravel, titleKey = "surface.gravel", friction = 0.65f, rollingResistanceScale = 3f, audioTag = "gravel", vfxTag = "dust" });
            lib.Register(new SurfaceProfile
            {
                id = PackedSnow, titleKey = "surface.packed_snow", friction = 0.38f, rollingResistanceScale = 1.6f, audioTag = "snow", vfxTag = "snow",
                compoundScale = new Dictionary<TireCompound, float> { { TireCompound.Winter, 1.45f }, { TireCompound.Studded, 1.6f }, { TireCompound.DriftSemiSlick, 0.8f } }
            });
            lib.Register(new SurfaceProfile
            {
                id = LooseSnow, titleKey = "surface.loose_snow", friction = 0.3f, rollingResistanceScale = 3.5f, audioTag = "snow", vfxTag = "snow",
                compoundScale = new Dictionary<TireCompound, float> { { TireCompound.Winter, 1.35f }, { TireCompound.Studded, 1.4f }, { TireCompound.DriftSemiSlick, 0.75f } }
            });
            lib.Register(new SurfaceProfile
            {
                id = Ice, titleKey = "surface.ice", friction = 0.12f, slideRatioScale = 0.85f, audioTag = "ice", vfxTag = "ice",
                compoundScale = new Dictionary<TireCompound, float> { { TireCompound.Winter, 1.4f }, { TireCompound.Studded, 3.2f }, { TireCompound.DriftSemiSlick, 0.85f } }
            });
            return lib;
        }
    }
}

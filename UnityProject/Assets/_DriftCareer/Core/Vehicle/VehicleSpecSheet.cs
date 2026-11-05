using System;
using System.Collections.Generic;

namespace DriftCareer.Core.Vehicle
{
    public enum SpecSource { Manual = 0, Template = 1, AiSuggested = 2, Measured = 3 }

    /// <summary>
    /// Real-world style specification of a car (what a brochure/registration lists).
    /// Converted to a physics profile by <see cref="SpecToProfileConverter"/>. Produces a plausible
    /// approximation - not measured dynamics. Missing values (0) fall back to typical defaults.
    /// </summary>
    [Serializable]
    public sealed class VehicleSpecSheet
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        public string displayName = "Custom car";
        public SpecSource source = SpecSource.Manual;
        /// <summary>0..1 confidence when values were suggested by an AI service.</summary>
        public float confidence = 1f;

        public DriveLayout layout = DriveLayout.Rwd;
        public float curbMassKg = 1300f;
        public float frontWeightFraction = 0.53f;
        public float peakPowerKw = 150f;
        public float peakPowerRpm = 6500f;
        public float peakTorqueNm = 260f;
        public float peakTorqueRpm = 4500f;
        public float redlineRpm = 7000f;
        public bool turbo;
        public int gearCount = 5;
        /// <summary>Optional exact ratios (first..top). Null/empty = synthesized.</summary>
        public float[] gearRatios;
        public float finalDrive;
        public bool automaticGearbox;

        public float wheelbaseM = 2.55f;
        public float trackFrontM = 1.47f;
        public float trackRearM = 1.47f;
        public float lengthM = 4.4f;
        public float widthM = 1.72f;
        public float heightM = 1.32f;
        /// <summary>Tire size like 225/45R17. Optional; used for wheel radius.</summary>
        public string tireSizeFront = "215/45R17";
        public string tireSizeRear = "225/45R17";

        public VehicleSpecSheet Clone()
        {
            var c = (VehicleSpecSheet)MemberwiseClone();
            c.gearRatios = gearRatios != null ? (float[])gearRatios.Clone() : null;
            return c;
        }
    }

    public static class SpecToProfileConverter
    {
        /// <summary>Parses "225/45R17" -> rolling radius (m). Returns fallback if unparsable.</summary>
        public static float TireRadius(string size, float fallback = 0.31f)
        {
            if (string.IsNullOrEmpty(size)) return fallback;
            try
            {
                string s = size.ToUpperInvariant().Replace(" ", "");
                int slash = s.IndexOf('/');
                int r = s.IndexOf('R');
                if (slash <= 0 || r <= slash) return fallback;
                float width = float.Parse(s.Substring(0, slash), System.Globalization.CultureInfo.InvariantCulture);
                float aspect = float.Parse(s.Substring(slash + 1, r - slash - 1), System.Globalization.CultureInfo.InvariantCulture);
                float rim = float.Parse(s.Substring(r + 1), System.Globalization.CultureInfo.InvariantCulture);
                float radius = (rim * 25.4f * 0.5f + width * aspect / 100f) / 1000f;
                return MathUtil.Clamp(radius, 0.2f, 0.5f);
            }
            catch (FormatException) { return fallback; }
            catch (OverflowException) { return fallback; }
        }

        /// <summary>
        /// Synthesizes a full-throttle torque curve that hits the given peak torque and peak power.
        /// </summary>
        public static Curve SynthesizeTorqueCurve(VehicleSpecSheet s)
        {
            float idle = 850f;
            float redline = MathUtil.Max(s.redlineRpm, s.peakPowerRpm + 300f);
            float tPeak = MathUtil.Max(50f, s.peakTorqueNm);
            float tAtPower = s.peakPowerKw * 1000f / MathUtil.Max(100f, s.peakPowerRpm * MathUtil.RpmToRadPerSec);
            tAtPower = MathUtil.Min(tAtPower, tPeak);
            float tqRpm = MathUtil.Clamp(s.peakTorqueRpm, idle + 500f, s.peakPowerRpm - 200f);
            var keys = new List<CurveKey>
            {
                new CurveKey(idle - 50f, tPeak * (s.turbo ? 0.45f : 0.62f)),
                new CurveKey(MathUtil.Lerp(idle, tqRpm, 0.5f), tPeak * (s.turbo ? 0.7f : 0.85f)),
                new CurveKey(tqRpm, tPeak),
                new CurveKey(MathUtil.Lerp(tqRpm, s.peakPowerRpm, 0.5f), MathUtil.Lerp(tPeak, tAtPower, 0.4f)),
                new CurveKey(s.peakPowerRpm, tAtPower),
                new CurveKey(redline + 100f, tAtPower * 0.85f),
            };
            var c = new Curve(keys.ToArray());
            // Remove non-increasing x (degenerate specs).
            for (int i = c.keys.Count - 1; i > 0; i--)
                if (c.keys[i].x <= c.keys[i - 1].x + 1f) c.keys.RemoveAt(i);
            return c;
        }

        /// <summary>Typical close-ratio sequence for N gears with a 0.8-1.0 top gear.</summary>
        public static float[] SynthesizeRatios(int count)
        {
            count = MathUtil.Clamp(count, 4, 8);
            float first = 3.6f, top = count >= 6 ? 0.82f : 0.95f;
            var r = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0f : (float)i / (count - 1);
                // Geometric progression with slight tightening at the top.
                r[i] = first * (float)Math.Pow(top / first, Math.Pow(t, 0.9));
            }
            return r;
        }

        public static VehiclePhysicsProfile Convert(VehicleSpecSheet s, VehiclePhysicsProfile template = null)
        {
            var p = (template ?? VehiclePhysicsProfile.CreatePlaceholderRwd()).Clone();
            p.id = "custom_" + Sanitize(s.displayName);
            p.drivetrain.layout = s.layout;
            p.drivetrain.awdFrontSplit = s.layout == DriveLayout.Awd ? 0.4f : p.drivetrain.awdFrontSplit;

            float mass = MathUtil.Clamp(s.curbMassKg + 75f, 600f, 3500f); // + driver
            p.chassis.massKg = mass;
            p.chassis.frontWeightFraction = MathUtil.Clamp(s.frontWeightFraction, 0.35f, 0.7f);
            p.chassis.wheelbase = MathUtil.Clamp(s.wheelbaseM, 1.8f, 3.8f);
            p.chassis.length = MathUtil.Clamp(s.lengthM, 3f, 6f);
            p.chassis.width = MathUtil.Clamp(s.widthM, 1.4f, 2.3f);
            p.chassis.height = MathUtil.Clamp(s.heightM, 1f, 2.2f);
            p.chassis.cgHeight = MathUtil.Clamp(p.chassis.height * 0.38f, 0.35f, 0.9f);
            p.front.trackWidth = MathUtil.Clamp(s.trackFrontM, 1.2f, 2f);
            p.rear.trackWidth = MathUtil.Clamp(s.trackRearM, 1.2f, 2f);
            p.front.wheelRadius = TireRadius(s.tireSizeFront, p.front.wheelRadius);
            p.rear.wheelRadius = TireRadius(s.tireSizeRear, p.rear.wheelRadius);

            // Springs scaled for ~1.7 Hz front / 1.6 Hz rear ride frequency.
            ScaleSprings(p.front, mass * p.chassis.frontWeightFraction * 0.5f, 1.75f);
            ScaleSprings(p.rear, mass * (1f - p.chassis.frontWeightFraction) * 0.5f, 1.6f);

            p.engine.torqueCurve = SynthesizeTorqueCurve(s);
            p.engine.redlineRpm = MathUtil.Max(s.redlineRpm, s.peakPowerRpm + 300f);
            p.engine.limiterRpm = p.engine.redlineRpm + 300f;
            p.transmission.autoUpshiftRpm = p.engine.redlineRpm - 200f;
            p.transmission.autoDownshiftRpm = MathUtil.Max(2200f, s.peakTorqueRpm * 0.6f);
            p.transmission.gearRatios = s.gearRatios != null && s.gearRatios.Length >= 3 ? (float[])s.gearRatios.Clone() : SynthesizeRatios(s.gearCount);
            p.transmission.finalDrive = s.finalDrive > 1f ? s.finalDrive : EstimateFinalDrive(p, s);
            p.transmission.clutchMaxTorque = MathUtil.Max(300f, s.peakTorqueNm * 1.7f);
            p.brakes.maxTorqueNm = mass * 4.2f;
            p.aero.dragArea = MathUtil.Clamp(0.32f * p.chassis.width * p.chassis.height * 0.85f, 0.45f, 1.2f);
            return p;
        }

        private static void ScaleSprings(AxleSpec a, float cornerMass, float freqHz)
        {
            float w = MathUtil.TwoPi * freqHz;
            a.suspension.springRate = MathUtil.Clamp(cornerMass * w * w, 12000f, 120000f);
            float critical = 2f * MathUtil.Sqrt(a.suspension.springRate * cornerMass);
            a.suspension.bumpDamping = critical * 0.3f;
            a.suspension.reboundDamping = critical * 0.42f;
            a.suspension.antiRollRate = a.suspension.springRate * 0.25f;
        }

        /// <summary>Picks a final drive so that top gear reaches peak power around the aero-limited top speed.</summary>
        private static float EstimateFinalDrive(VehiclePhysicsProfile p, VehicleSpecSheet s)
        {
            float power = s.peakPowerKw * 1000f;
            float vTop = (float)Math.Pow(power / (0.5f * 1.225f * p.aero.dragArea + 1e-3f), 1.0 / 3.0) * 0.92f;
            vTop = MathUtil.Clamp(vTop, 40f, 95f);
            float wheelOmega = vTop / p.rear.wheelRadius;
            float engineOmega = s.peakPowerRpm * 1.05f * MathUtil.RpmToRadPerSec;
            float top = p.transmission.gearRatios[p.transmission.gearRatios.Length - 1];
            return MathUtil.Clamp(engineOmega / (wheelOmega * top), 2.8f, 5.5f);
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "car";
            var sb = new System.Text.StringBuilder();
            foreach (char c in name.ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
                if (sb.Length >= 32) break;
            }
            return sb.Length == 0 ? "car" : sb.ToString().Trim('_');
        }
    }

    /// <summary>Brand-free archetype templates (no trademarks). Real-model data needs a licensed source.</summary>
    public static class SpecTemplates
    {
        public static readonly string[] Ids = { "jdm_coupe_turbo", "euro_i6_sedan", "rotary_coupe", "v8_muscle", "hot_hatch_fwd", "awd_rally_sedan", "kei_roadster" };

        public static VehicleSpecSheet Create(string id)
        {
            var s = new VehicleSpecSheet { source = SpecSource.Template, displayName = id };
            switch (id)
            {
                case "jdm_coupe_turbo":
                    s.curbMassKg = 1240f; s.frontWeightFraction = 0.55f; s.peakPowerKw = 184f; s.peakPowerRpm = 6400f; s.peakTorqueNm = 275f; s.peakTorqueRpm = 4000f;
                    s.redlineRpm = 7200f; s.turbo = true; s.gearCount = 6; s.wheelbaseM = 2.525f; s.tireSizeFront = "215/45R17"; s.tireSizeRear = "225/45R17";
                    break;
                case "euro_i6_sedan":
                    s.curbMassKg = 1430f; s.frontWeightFraction = 0.51f; s.peakPowerKw = 170f; s.peakPowerRpm = 6000f; s.peakTorqueNm = 300f; s.peakTorqueRpm = 3500f;
                    s.redlineRpm = 6800f; s.gearCount = 5; s.wheelbaseM = 2.725f; s.tireSizeFront = "225/45R17"; s.tireSizeRear = "245/40R17"; s.widthM = 1.75f;
                    break;
                case "rotary_coupe":
                    s.curbMassKg = 1280f; s.frontWeightFraction = 0.5f; s.peakPowerKw = 206f; s.peakPowerRpm = 6500f; s.peakTorqueNm = 294f; s.peakTorqueRpm = 5000f;
                    s.redlineRpm = 8000f; s.turbo = true; s.gearCount = 5; s.wheelbaseM = 2.425f; s.heightM = 1.23f;
                    break;
                case "v8_muscle":
                    s.curbMassKg = 1700f; s.frontWeightFraction = 0.54f; s.peakPowerKw = 330f; s.peakPowerRpm = 6500f; s.peakTorqueNm = 530f; s.peakTorqueRpm = 4250f;
                    s.redlineRpm = 7000f; s.gearCount = 6; s.wheelbaseM = 2.72f; s.lengthM = 4.8f; s.widthM = 1.9f; s.tireSizeFront = "255/40R19"; s.tireSizeRear = "275/40R19";
                    break;
                case "hot_hatch_fwd":
                    s.layout = DriveLayout.Fwd; s.curbMassKg = 1350f; s.frontWeightFraction = 0.62f; s.peakPowerKw = 180f; s.peakPowerRpm = 6000f; s.peakTorqueNm = 350f;
                    s.peakTorqueRpm = 2500f; s.redlineRpm = 6800f; s.turbo = true; s.gearCount = 6; s.wheelbaseM = 2.62f; s.lengthM = 4.25f;
                    break;
                case "awd_rally_sedan":
                    s.layout = DriveLayout.Awd; s.curbMassKg = 1450f; s.frontWeightFraction = 0.58f; s.peakPowerKw = 221f; s.peakPowerRpm = 6000f; s.peakTorqueNm = 407f;
                    s.peakTorqueRpm = 4000f; s.redlineRpm = 7000f; s.turbo = true; s.gearCount = 6; s.wheelbaseM = 2.62f;
                    break;
                case "kei_roadster":
                    s.curbMassKg = 950f; s.frontWeightFraction = 0.5f; s.peakPowerKw = 97f; s.peakPowerRpm = 7000f; s.peakTorqueNm = 152f; s.peakTorqueRpm = 5000f;
                    s.redlineRpm = 7500f; s.gearCount = 6; s.wheelbaseM = 2.31f; s.lengthM = 3.9f; s.widthM = 1.73f; s.heightM = 1.23f; s.tireSizeFront = "195/50R16"; s.tireSizeRear = "195/50R16";
                    break;
            }
            return s;
        }
    }
}

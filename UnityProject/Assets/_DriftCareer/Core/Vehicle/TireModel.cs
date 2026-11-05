using DriftCareer.Core.Surfaces;

namespace DriftCareer.Core.Vehicle
{
    public struct TireForce
    {
        /// <summary>Longitudinal force along wheel forward (N). Positive pushes the car forward.</summary>
        public float fx;
        /// <summary>Lateral force along wheel right (N).</summary>
        public float fy;
        public float slipRatio;
        public float slipAngleRad;
        /// <summary>Combined normalized slip (1 = at peak grip).</summary>
        public float combinedSlip;
        /// <summary>Used friction / available friction (0..1).</summary>
        public float gripUsage;
        public bool sliding;
    }

    /// <summary>
    /// Simplified empirical tire model ("normalized combined slip").
    /// 1. Slip ratio and tan(slip angle) are normalized by their peak values.
    /// 2. Combined slip s = |(sx, sy)| gives a friction circle/ellipse automatically.
    /// 3. Shape f(s): rises s*(2-s) to 1 at s=1 (zero slope at peak), then decays exponentially to slideRatio.
    /// This is a gameplay model (Prototype B-lite), not a calibrated Pacejka fit.
    /// </summary>
    public static class TireModel
    {
        /// <summary>Velocity floor for slip denominators (m/s). Avoids division blow-up near standstill.</summary>
        public const float LowSpeed = 2.0f;
        private const float MaxSlipRatio = 12f;
        private const float MaxTanSlip = 12f;

        public static float Shape(float s, float slideRatio, float falloff)
        {
            if (s <= 0f) return 0f;
            if (s < 1f) return s * (2f - s);
            return slideRatio + (1f - slideRatio) * MathUtil.Exp(-(s - 1f) * falloff);
        }

        public static float LoadFactor(TireSpec t, float fz)
        {
            float f = 1f - t.loadSensitivity * (fz - t.nominalLoadN) / t.nominalLoadN;
            return MathUtil.Clamp(f, 0.5f, 1.25f);
        }

        /// <summary>
        /// Evaluates tire forces.
        /// </summary>
        /// <param name="fz">Normal load (N).</param>
        /// <param name="vLong">Contact patch ground-relative velocity along wheel forward (m/s).</param>
        /// <param name="vLat">Contact patch velocity along wheel right (m/s).</param>
        /// <param name="wheelSurfaceSpeed">omega * radius (m/s).</param>
        public static TireForce Evaluate(TireSpec t, in SurfaceGrip grip, float fz, float vLong, float vLat, float wheelSurfaceSpeed)
        {
            var r = new TireForce();
            if (!(fz > 0f)) return r;

            float denom = MathUtil.Max(MathUtil.Abs(vLong), LowSpeed);
            float slipRatio = MathUtil.Clamp((wheelSurfaceSpeed - vLong) / denom, -MaxSlipRatio, MaxSlipRatio);
            float tanSlip = MathUtil.Clamp(vLat / denom, -MaxTanSlip, MaxTanSlip);

            float peakScale = grip.peakSlipScale > 0f ? grip.peakSlipScale : 1f;
            float sx = slipRatio / (t.peakSlipRatio * peakScale);
            float sy = tanSlip / MathUtil.Tan(MathUtil.Min(t.peakSlipAngleDeg * peakScale, 45f) * MathUtil.Deg2Rad);
            float s = MathUtil.Sqrt(sx * sx + sy * sy);

            r.slipRatio = slipRatio;
            r.slipAngleRad = MathUtil.Atan(tanSlip);
            r.combinedSlip = s;

            float rolling = t.rollingResistance * grip.rollingResistanceScale * fz * MathUtil.Tanh(vLong / 0.5f);
            if (s < 1e-5f)
            {
                r.fx = -rolling;
                return r;
            }

            float slideRatio = MathUtil.Clamp(t.slideFrictionRatio * grip.slideRatioScale, 0.05f, 1f);
            float f = Shape(s, slideRatio, t.falloffRate);
            float load = LoadFactor(t, fz);
            float muX = t.peakFrictionLong * grip.longitudinal * load;
            float muY = t.peakFrictionLat * grip.lateral * load;

            r.fx = muX * fz * f * (sx / s) - rolling;
            r.fy = -muY * fz * f * (sy / s);
            r.gripUsage = MathUtil.Clamp01(f);
            r.sliding = s > 1.15f;
            return r;
        }

        /// <summary>
        /// Clamps forces so they cannot reverse the contact slip velocity within one step
        /// (prevents low-speed jitter/explosions with explicit rigid body integration).
        /// </summary>
        public static void ClampNoOvershoot(ref TireForce f, float massShare, float dt, float vLongSlip, float vLat)
        {
            if (!(massShare > 0f) || !(dt > 0f)) return;
            float maxLong = massShare * MathUtil.Abs(vLongSlip) / dt + 50f;
            float maxLat = massShare * MathUtil.Abs(vLat) / dt + 50f;
            f.fx = MathUtil.Clamp(f.fx, -maxLong, maxLong);
            f.fy = MathUtil.Clamp(f.fy, -maxLat, maxLat);
        }
    }
}

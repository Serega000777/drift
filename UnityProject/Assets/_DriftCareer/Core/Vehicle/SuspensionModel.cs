namespace DriftCareer.Core.Vehicle
{
    /// <summary>
    /// Spring-damper suspension with bump stop and per-axle anti-roll bar.
    /// Geometry: a ray is cast from the suspension mount straight down (chassis -Y).
    /// compression = travel + radius - hitDistance, clamped to [0, travel + bumpStopRange].
    /// </summary>
    public static class SuspensionModel
    {
        public const float BumpStopRange = 0.05f;

        public static float Compression(SuspensionSpec s, float wheelRadius, float hitDistance)
        {
            return MathUtil.Clamp(s.travel + wheelRadius - hitDistance, 0f, s.travel + BumpStopRange);
        }

        public static float RayLength(SuspensionSpec s, float wheelRadius) => s.travel + wheelRadius;

        /// <summary>Normal force from spring, damper and bump stop (N). Never negative (no "glue").</summary>
        public static float Force(SuspensionSpec s, float compression, float compressionVelocity)
        {
            if (compression <= 0f) return 0f;
            float spring = s.springRate * compression;
            float damping = compressionVelocity >= 0f ? s.bumpDamping : s.reboundDamping;
            float damper = damping * compressionVelocity;
            float bump = compression > s.travel ? s.bumpStopRate * (compression - s.travel) : 0f;
            float f = spring + damper + bump;
            // Cap pathological values (e.g. teleport into ground) to protect the solver.
            return MathUtil.Clamp(f, 0f, 120000f);
        }

        /// <summary>Anti-roll transfer added to left and subtracted from right.</summary>
        public static float AntiRoll(SuspensionSpec s, float compressionLeft, float compressionRight)
        {
            return s.antiRollRate * (compressionLeft - compressionRight);
        }

        /// <summary>Static compression under a given load (m).</summary>
        public static float StaticCompression(SuspensionSpec s, float loadN) => MathUtil.Clamp(loadN / s.springRate, 0f, s.travel);

        /// <summary>
        /// Mount height above ground so that the wheel center sits at its radius under static load.
        /// Vehicle root (y=0) is at ground level.
        /// </summary>
        public static float MountHeight(SuspensionSpec s, float wheelRadius, float staticWheelLoadN)
        {
            return wheelRadius + s.travel - StaticCompression(s, staticWheelLoadN);
        }
    }

    /// <summary>Steering geometry with rack speed limit and Ackermann.</summary>
    public static class SteeringModel
    {
        /// <summary>
        /// Computes left/right road-wheel angles (rad) for a center steering angle.
        /// Positive angle = steer right. Inner wheel turns more with Ackermann.
        /// </summary>
        public static void WheelAngles(SteeringSpec spec, float centerAngleRad, float wheelbase, float trackWidth, out float left, out float right)
        {
            if (MathUtil.Abs(centerAngleRad) < 1e-4f || spec.ackermann <= 0f)
            {
                left = right = centerAngleRad;
                return;
            }
            float sign = MathUtil.Sign(centerAngleRad);
            float a = MathUtil.Abs(centerAngleRad);
            float turnRadius = wheelbase / MathUtil.Tan(a);
            float inner = MathUtil.Atan(wheelbase / MathUtil.Max(0.1f, turnRadius - trackWidth * 0.5f));
            float outer = MathUtil.Atan(wheelbase / (turnRadius + trackWidth * 0.5f));
            inner = MathUtil.Lerp(a, inner, spec.ackermann);
            outer = MathUtil.Lerp(a, outer, spec.ackermann);
            // Right turn: right wheel is inner.
            if (sign > 0f) { right = inner; left = outer; }
            else { left = -inner; right = -outer; }
        }
    }
}

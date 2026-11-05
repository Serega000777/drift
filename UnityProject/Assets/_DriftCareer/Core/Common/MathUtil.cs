using System;

namespace DriftCareer.Core
{
    /// <summary>
    /// Engine-free math helpers. All angles are radians unless the name says Deg.
    /// Coordinate convention (matches Unity): X = right, Y = up, Z = forward, left-handed.
    /// Positive yaw rotates +Z toward +X (clockwise when viewed from above).
    /// </summary>
    public static class MathUtil
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float Rad2Deg = (float)(180.0 / Math.PI);
        public const float Pi = (float)Math.PI;
        public const float TwoPi = (float)(Math.PI * 2.0);
        public const float Gravity = 9.81f;
        public const float RadPerSecToRpm = (float)(60.0 / (2.0 * Math.PI));
        public const float RpmToRadPerSec = (float)(2.0 * Math.PI / 60.0);

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float v) => Math.Abs(b - a) < 1e-9f ? 0f : Clamp01((v - a) / (b - a));
        public static float Remap(float v, float inMin, float inMax, float outMin, float outMax) => LerpUnclamped(outMin, outMax, InverseLerp(inMin, inMax, v));
        public static float Sign(float v) => v > 0f ? 1f : (v < 0f ? -1f : 0f);
        public static float Abs(float v) => v < 0f ? -v : v;
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Sqr(float v) => v * v;

        public static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>Returns <paramref name="fallback"/> when value is NaN/Infinity.</summary>
        public static float Finite(float v, float fallback = 0f) => IsFinite(v) ? v : fallback;

        public static float SafeDiv(float a, float b, float fallback = 0f) => Math.Abs(b) < 1e-9f ? fallback : a / b;

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta) return target;
            return current + Sign(target - current) * maxDelta;
        }

        /// <summary>Frame-rate independent exponential smoothing. sharpness ~ 1/timeConstant.</summary>
        public static float Damp(float current, float target, float sharpness, float dt)
        {
            return LerpUnclamped(current, target, 1f - (float)Math.Exp(-sharpness * dt));
        }

        public static float SmoothStep01(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Wraps angle to (-PI, PI].</summary>
        public static float WrapAngle(float rad)
        {
            while (rad > Pi) rad -= TwoPi;
            while (rad <= -Pi) rad += TwoPi;
            return rad;
        }

        public static float WrapAngleDeg(float deg)
        {
            while (deg > 180f) deg -= 360f;
            while (deg <= -180f) deg += 360f;
            return deg;
        }

        public static float Sin(float v) => (float)Math.Sin(v);
        public static float Cos(float v) => (float)Math.Cos(v);
        public static float Tan(float v) => (float)Math.Tan(v);
        public static float Atan(float v) => (float)Math.Atan(v);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Exp(float v) => (float)Math.Exp(v);
        public static float Tanh(float v) => (float)Math.Tanh(v);
    }
}

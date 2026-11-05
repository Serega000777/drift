using System;

namespace DriftCareer.Core
{
    /// <summary>
    /// 2D vector on the ground plane. <see cref="X"/> = world X (right), <see cref="Y"/> = world Z (forward).
    /// </summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X;
        public float Y;

        public Vec2(float x, float y) { X = x; Y = y; }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);
        public static readonly Vec2 Forward = new Vec2(0f, 1f);
        public static readonly Vec2 Right = new Vec2(1f, 0f);

        public float Length => MathUtil.Sqrt(X * X + Y * Y);
        public float SqrLength => X * X + Y * Y;

        public Vec2 Normalized
        {
            get
            {
                float l = Length;
                return l > 1e-6f ? new Vec2(X / l, Y / l) : Zero;
            }
        }

        public bool IsFinite => MathUtil.IsFinite(X) && MathUtil.IsFinite(Y);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);
        public static bool operator ==(Vec2 a, Vec2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

        /// <summary>2D cross product (a.X*b.Y - a.Y*b.X).</summary>
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;

        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => a + (b - a) * MathUtil.Clamp01(t);

        /// <summary>Forward direction for a yaw angle (Unity convention: yaw 0 = +Z, yaw +90deg = +X).</summary>
        public static Vec2 FromYaw(float yawRad) => new Vec2(MathUtil.Sin(yawRad), MathUtil.Cos(yawRad));

        /// <summary>Right direction for a yaw angle.</summary>
        public static Vec2 RightFromYaw(float yawRad) => new Vec2(MathUtil.Cos(yawRad), -MathUtil.Sin(yawRad));

        /// <summary>Yaw angle of a direction (inverse of <see cref="FromYaw"/>).</summary>
        public static float ToYaw(Vec2 dir) => MathUtil.Atan2(dir.X, dir.Y);

        /// <summary>Transforms a local vector (X=right, Y=forward) into world by yaw.</summary>
        public static Vec2 LocalToWorld(Vec2 local, float yawRad)
        {
            float c = MathUtil.Cos(yawRad), s = MathUtil.Sin(yawRad);
            return new Vec2(local.X * c + local.Y * s, -local.X * s + local.Y * c);
        }

        /// <summary>Transforms a world vector into local frame (X=right, Y=forward).</summary>
        public static Vec2 WorldToLocal(Vec2 world, float yawRad)
        {
            float c = MathUtil.Cos(yawRad), s = MathUtil.Sin(yawRad);
            return new Vec2(world.X * c - world.Y * s, world.X * s + world.Y * c);
        }

        public bool Equals(Vec2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => X.GetHashCode() * 397 ^ Y.GetHashCode();
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }
}

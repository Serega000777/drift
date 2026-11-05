using System;
using DriftCareer.Core;
using DriftCareer.Core.Surfaces;
using DriftCareer.Core.Vehicle;
using NUnit.Framework;

namespace DriftCareer.Tests.EditMode
{
    public class TireModelTests
    {
        private static readonly SurfaceGrip Grip = SurfaceGrip.Default;

        [Test]
        public void Shape_PeaksAtOne_AndDecaysToSlide()
        {
            Assert.AreEqual(0f, TireModel.Shape(0f, 0.8f, 1f), 1e-6);
            Assert.AreEqual(1f, TireModel.Shape(1f, 0.8f, 1f), 1e-6);
            Assert.Less(TireModel.Shape(0.5f, 0.8f, 1f), 1f);
            Assert.AreEqual(0.8f, TireModel.Shape(50f, 0.8f, 1f), 1e-3);
            Assert.Greater(TireModel.Shape(1.5f, 0.8f, 1f), 0.8f);
        }

        [Test]
        public void LateralForce_OpposesLateralVelocity()
        {
            var t = new TireSpec();
            var right = TireModel.Evaluate(t, Grip, 3500f, 20f, 1f, 20f);
            var left = TireModel.Evaluate(t, Grip, 3500f, 20f, -1f, 20f);
            Assert.Less(right.fy, 0f);
            Assert.Greater(left.fy, 0f);
            Assert.AreEqual(-right.fy, left.fy, 1e-3);
        }

        [Test]
        public void LongitudinalForce_FollowsSlipRatio()
        {
            var t = new TireSpec();
            var drive = TireModel.Evaluate(t, Grip, 3500f, 10f, 0f, 11f);
            var brake = TireModel.Evaluate(t, Grip, 3500f, 10f, 0f, 9f);
            Assert.Greater(drive.fx, 0f);
            Assert.Less(brake.fx, 0f);
        }

        [Test]
        public void CombinedSlip_RespectsFrictionCircle()
        {
            var t = new TireSpec();
            for (float sr = -1f; sr <= 1f; sr += 0.1f)
            for (float lat = -10f; lat <= 10f; lat += 1f)
            {
                var f = TireModel.Evaluate(t, Grip, 4000f, 15f, lat, 15f * (1f + sr));
                float muX = t.peakFrictionLong * TireModel.LoadFactor(t, 4000f);
                float muY = t.peakFrictionLat * TireModel.LoadFactor(t, 4000f);
                float ellipse = MathUtil.Sqr(f.fx / (muX * 4000f)) + MathUtil.Sqr(f.fy / (muY * 4000f));
                Assert.LessOrEqual(ellipse, 1.0f + 0.03f, $"sr={sr} lat={lat}");
            }
        }

        [Test]
        public void ZeroLoad_ZeroForce_AndNearZeroSpeed_IsFinite()
        {
            var t = new TireSpec();
            var z = TireModel.Evaluate(t, Grip, 0f, 10f, 3f, 5f);
            Assert.AreEqual(0f, z.fx);
            Assert.AreEqual(0f, z.fy);
            var slow = TireModel.Evaluate(t, Grip, 3500f, 1e-7f, -1e-7f, 0f);
            Assert.IsTrue(MathUtil.IsFinite(slow.fx) && MathUtil.IsFinite(slow.fy));
            var nan = TireModel.Evaluate(t, Grip, float.NaN, 1f, 1f, 1f);
            Assert.AreEqual(0f, nan.fx);
        }

        [Test]
        public void HigherLoad_HasLowerFrictionCoefficient()
        {
            var t = new TireSpec();
            var light = TireModel.Evaluate(t, Grip, 2000f, 20f, 3f, 20f);
            var heavy = TireModel.Evaluate(t, Grip, 6000f, 20f, 3f, 20f);
            Assert.Greater(Math.Abs(light.fy) / 2000f, Math.Abs(heavy.fy) / 6000f);
        }

        [Test]
        public void SuspensionForce_NeverNegative_AndStaticMountIsConsistent()
        {
            var s = new SuspensionSpec();
            Assert.AreEqual(0f, SuspensionModel.Force(s, 0f, -5f));
            Assert.GreaterOrEqual(SuspensionModel.Force(s, 0.01f, -10f), 0f);
            float load = 3200f, radius = 0.315f;
            float mount = SuspensionModel.MountHeight(s, radius, load);
            float compression = SuspensionModel.Compression(s, radius, mount);
            Assert.AreEqual(load, SuspensionModel.Force(s, compression, 0f), 1f);
        }

        [Test]
        public void Ackermann_InnerWheelTurnsMore()
        {
            var spec = new SteeringSpec { ackermann = 1f };
            SteeringModel.WheelAngles(spec, 20f * MathUtil.Deg2Rad, 2.55f, 1.47f, out float l, out float r);
            Assert.Greater(r, l, "Right turn: right wheel is inner");
            SteeringModel.WheelAngles(spec, -20f * MathUtil.Deg2Rad, 2.55f, 1.47f, out l, out r);
            Assert.Less(l, r, "Left turn: left wheel is inner (more negative)");
        }
    }
}

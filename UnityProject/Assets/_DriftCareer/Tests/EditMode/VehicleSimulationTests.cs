using System;
using DriftCareer.Core;
using DriftCareer.Core.Surfaces;
using DriftCareer.Core.Vehicle;
using NUnit.Framework;

namespace DriftCareer.Tests.EditMode
{
    /// <summary>
    /// Headless dynamics scenarios on the planar harness (see docs/TEST_PLAN.md, section "Physics").
    /// These validate stability and qualitative behaviour, not real-world calibration.
    /// </summary>
    public class VehicleSimulationTests
    {
        private const float Dt = 0.01f;

        private static PlanarVehicleHarness NewCar() => new PlanarVehicleHarness(VehiclePhysicsProfile.CreatePlaceholderRwd());

        private static VehicleControls Controls(float throttle = 0f, float brake = 0f, float steer = 0f, float handbrake = 0f)
        {
            var c = VehicleControls.Neutral;
            c.throttle = throttle; c.brake = brake; c.steer = steer; c.handbrake = handbrake;
            return c;
        }

        private static void Run(PlanarVehicleHarness car, float seconds, VehicleControls c)
        {
            int steps = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                car.Step(Dt, c);
                Assert.IsTrue(car.IsFinite, $"Non-finite state at t={car.Time:0.00}");
            }
        }

        [Test]
        public void DefaultProfile_IsValid()
        {
            var errors = VehiclePhysicsProfile.CreatePlaceholderRwd().Validate();
            Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
        }

        [Test]
        public void InvalidProfile_IsRejected()
        {
            var p = VehiclePhysicsProfile.CreatePlaceholderRwd();
            p.chassis.massKg = float.NaN;
            Assert.Throws<ArgumentException>(() => new VehicleSimulation(p));
        }

        [Test]
        public void ParkedCar_StaysStill()
        {
            var car = NewCar();
            Run(car, 5f, Controls());
            Assert.Less(car.Velocity.Length, 0.05f);
            Assert.Less(car.Position.Length, 0.05f);
            Assert.AreEqual(0, car.Sim.NonFiniteRecoveries);
        }

        [Test]
        public void FullThrottle_Accelerates_WithPlausible0To100()
        {
            var car = NewCar();
            float t100 = -1f;
            for (int i = 0; i < 2000; i++)
            {
                car.Step(Dt, Controls(throttle: 1f));
                Assert.IsTrue(car.IsFinite);
                if (t100 < 0f && car.Telemetry.SpeedKmh >= 100f) { t100 = car.Time; break; }
            }
            TestContext.WriteLine($"0-100 km/h: {t100:0.00}s, gear {car.Sim.Gear}");
            Assert.Greater(t100, 3.5f, "Unrealistically fast launch");
            Assert.Less(t100, 11f, "Car too slow or not shifting");
            Assert.Less(Math.Abs(car.Position.X), 1.0f, "Car should drive straight");
        }

        [Test]
        public void Braking_From100_StopsInReasonableDistance()
        {
            var car = NewCar();
            car.Sim.SetGearImmediate(3);
            car.SetMotion(Vec2.Zero, 0f, new Vec2(0f, 27.8f), 0f);
            float startZ = car.Position.Y;
            for (int i = 0; i < 1500 && car.Velocity.Length > 0.3f; i++) car.Step(Dt, Controls(brake: 1f));
            float dist = car.Position.Y - startZ;
            TestContext.WriteLine($"100-0 km/h distance: {dist:0.0} m");
            Assert.Less(car.Velocity.Length, 0.5f);
            Assert.Greater(dist, 25f);
            Assert.Less(dist, 60f);
        }

        [Test]
        public void Coasting_Decelerates_Slowly()
        {
            var car = NewCar();
            car.Sim.SetGearImmediate(3);
            car.SetMotion(Vec2.Zero, 0f, new Vec2(0f, 20f), 0f);
            Run(car, 3f, Controls());
            Assert.Less(car.Telemetry.forwardSpeedMs, 20f);
            Assert.Greater(car.Telemetry.forwardSpeedMs, 12f);
        }

        [TestCase(1f)]
        [TestCase(-1f)]
        public void Donut_SustainedRotation_BothDirections(float steerSign)
        {
            var car = NewCar();
            // Launch, then full lock + throttle with a handbrake kick.
            Run(car, 2.0f, Controls(throttle: 1f));
            Run(car, 0.4f, Controls(throttle: 1f, steer: steerSign, handbrake: 1f));
            float yawSum = 0f;
            float minDrift = float.MaxValue, maxDrift = 0f;
            float lastYaw = car.Yaw;
            Vec2 sum = Vec2.Zero; int n = 0;
            for (int i = 0; i < 800; i++)
            {
                car.Step(Dt, Controls(throttle: 0.85f, steer: steerSign));
                Assert.IsTrue(car.IsFinite);
                yawSum += MathUtil.WrapAngle(car.Yaw - lastYaw);
                lastYaw = car.Yaw;
                if (i > 200)
                {
                    float a = Math.Abs(car.Telemetry.driftAngleDeg);
                    minDrift = Math.Min(minDrift, a);
                    maxDrift = Math.Max(maxDrift, a);
                    sum += car.Position; n++;
                }
            }
            float turns = yawSum / MathUtil.TwoPi;
            TestContext.WriteLine($"steer {steerSign}: turns {turns:0.00}, drift {minDrift:0}-{maxDrift:0} deg, speed {car.Telemetry.speedMs:0.0} m/s, gear {car.Sim.Gear}");
            Assert.Greater(Math.Abs(turns), 1.5f, "Car should rotate repeatedly");
            Assert.AreEqual(Math.Sign(steerSign), Math.Sign(turns), "Rotation follows steering direction");
            Assert.Greater(maxDrift, 25f, "Rear should be sliding (drift)");
            Assert.AreEqual(0, car.Sim.NonFiniteRecoveries);
        }

        [Test]
        public void HandbrakeAtSpeed_InitiatesSlide()
        {
            var car = NewCar();
            car.Sim.SetGearImmediate(2);
            car.SetMotion(Vec2.Zero, 0f, new Vec2(0f, 16f), 0f);
            Run(car, 0.6f, Controls(steer: 0.6f, handbrake: 1f));
            float drift = Math.Abs(car.Telemetry.driftAngleDeg);
            TestContext.WriteLine($"Drift angle after handbrake: {drift:0.0}");
            Assert.Greater(drift, 8f);
        }

        [Test]
        public void FixedStep_IsIndependentOfRenderFrameRate()
        {
            // Same fixed dt, different render frame times through an accumulator -> identical results.
            float[] frameTimes = { 1f / 30f, 1f / 60f, 1f / 120f };
            Vec2[] results = new Vec2[frameTimes.Length];
            for (int k = 0; k < frameTimes.Length; k++)
            {
                var car = NewCar();
                float acc = 0f; int stepsDone = 0;
                while (stepsDone < 400)
                {
                    acc += frameTimes[k];
                    while (acc >= Dt && stepsDone < 400)
                    {
                        float t = stepsDone * Dt;
                        var c = Controls(throttle: 1f, steer: t > 2f ? 0.5f : 0f);
                        car.Step(Dt, c);
                        acc -= Dt;
                        stepsDone++;
                    }
                }
                results[k] = car.Position;
            }
            Assert.AreEqual(results[0], results[1]);
            Assert.AreEqual(results[0], results[2]);
        }

        [Test]
        public void FigureEight_TransitionsBetweenDirections()
        {
            var car = NewCar();
            Run(car, 2.0f, Controls(throttle: 1f));
            Run(car, 0.35f, Controls(throttle: 1f, steer: 1f, handbrake: 1f));
            int signChanges = 0;
            float lastSign = 0f;
            for (int loop = 0; loop < 4; loop++)
            {
                float dir = loop % 2 == 0 ? 1f : -1f;
                for (int i = 0; i < 300; i++)
                {
                    car.Step(Dt, Controls(throttle: 0.8f, steer: dir));
                    Assert.IsTrue(car.IsFinite);
                }
                float s = Math.Sign(car.YawRate);
                if (lastSign != 0f && s != lastSign) signChanges++;
                lastSign = s;
            }
            TestContext.WriteLine($"yaw-rate sign changes: {signChanges}");
            Assert.GreaterOrEqual(signChanges, 2);
        }

        [Test]
        public void LongSession_NoNaN_UnderRandomInputs()
        {
            var car = NewCar();
            var rng = new Random(1234);
            var c = Controls();
            for (int i = 0; i < 90000; i++) // 15 simulated minutes
            {
                if (i % 50 == 0)
                {
                    c.throttle = (float)rng.NextDouble();
                    c.brake = rng.NextDouble() < 0.2 ? (float)rng.NextDouble() : 0f;
                    c.steer = (float)(rng.NextDouble() * 2 - 1);
                    c.handbrake = rng.NextDouble() < 0.1 ? 1f : 0f;
                }
                car.Step(Dt, c);
                if (!car.IsFinite) Assert.Fail($"Non-finite at step {i}");
                if (car.Position.Length > 400f) car.SetMotion(Vec2.Zero, car.Yaw, car.Velocity, car.YawRate);
            }
            Assert.AreEqual(0, car.Sim.NonFiniteRecoveries);
            Assert.Less(car.Telemetry.engineRpm, car.Sim.Profile.engine.limiterRpm + 500f);
        }

        [Test]
        public void Reverse_IsSelectedByHoldingBrakeAtStandstill()
        {
            var car = NewCar();
            Run(car, 1.0f, Controls(brake: 1f));
            Assert.AreEqual(-1, car.Sim.Gear);
            Run(car, 2.0f, Controls(brake: 1f));
            Assert.Less(car.Telemetry.forwardSpeedMs, -1f, "Holding brake in reverse drives backwards");
        }

        [Test]
        public void Ice_HasMuchLessGrip_ThanAsphalt()
        {
            var lib = SurfaceLibrary.CreateDefault();
            var spec = new TireSpec();
            var dry = TireModel.Evaluate(spec, lib.Get(SurfaceLibrary.DryAsphalt).GripFor(spec.compound), 3500f, 10f, 3f, 10f);
            var ice = TireModel.Evaluate(spec, lib.Get(SurfaceLibrary.Ice).GripFor(spec.compound), 3500f, 10f, 3f, 10f);
            Assert.Greater(Math.Abs(dry.fy), Math.Abs(ice.fy) * 4f);
            var studded = TireModel.Evaluate(spec, lib.Get(SurfaceLibrary.Ice).GripFor(TireCompound.Studded), 3500f, 10f, 3f, 10f);
            Assert.Greater(Math.Abs(studded.fy), Math.Abs(ice.fy) * 2f);
        }
    }
}

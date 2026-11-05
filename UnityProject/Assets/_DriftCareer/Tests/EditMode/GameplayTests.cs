using System;
using System.Collections.Generic;
using DriftCareer.Core;
using DriftCareer.Core.Assists;
using DriftCareer.Core.Gameplay;
using DriftCareer.Core.Vehicle;
using NUnit.Framework;

namespace DriftCareer.Tests.EditMode
{
    public class GameplayTests
    {
        private static DriftSample S(float angle, float speed = 15f, float dt = 0.1f, bool onTrack = true, float impulse = 0f)
            => new DriftSample { dt = dt, driftAngleDeg = angle, speedMs = speed, onTrack = onTrack, collisionImpulse = impulse };

        [Test]
        public void Scoring_BanksChainAfterGrace()
        {
            var s = new DriftScoring();
            float banked = -1f;
            s.ChainEnded += (p, r) => { if (r == ChainEndReason.Completed) banked = p; };
            for (int i = 0; i < 30; i++) s.Update(S(40f));
            Assert.IsTrue(s.ChainActive);
            Assert.Greater(s.ChainPoints, 0f);
            for (int i = 0; i < 12; i++) s.Update(S(0f));
            Assert.IsFalse(s.ChainActive);
            Assert.Greater(banked, 0f);
            Assert.AreEqual(banked, s.TotalPoints, 1e-3);
        }

        [Test]
        public void Scoring_MoreAngleAndSpeed_MorePoints()
        {
            var a = new DriftScoring(); var b = new DriftScoring();
            for (int i = 0; i < 20; i++) { a.Update(S(25f, 12f)); b.Update(S(60f, 22f)); }
            Assert.Greater(b.ChainPoints, a.ChainPoints * 2f);
        }

        [Test]
        public void Scoring_CollisionAndSpinLoseChain()
        {
            var s = new DriftScoring();
            ChainEndReason reason = ChainEndReason.None;
            s.ChainEnded += (p, r) => reason = r;
            for (int i = 0; i < 20; i++) s.Update(S(40f));
            s.Update(S(40f, impulse: 10000f));
            Assert.AreEqual(ChainEndReason.Collision, reason);
            Assert.AreEqual(0f, s.TotalPoints);
            for (int i = 0; i < 20; i++) s.Update(S(40f));
            s.Update(S(150f));
            Assert.AreEqual(ChainEndReason.Spin, reason);
        }

        [Test]
        public void Scoring_Transitions_GiveBonusAndCombo()
        {
            var s = new DriftScoring();
            int transitions = 0;
            s.TransitionScored += n => transitions = n;
            for (int k = 0; k < 4; k++)
                for (int i = 0; i < 10; i++) s.Update(S(k % 2 == 0 ? 35f : -35f));
            Assert.AreEqual(3, transitions);
            Assert.Greater(s.Multiplier, 1.5f);
        }

        [Test]
        public void Scoring_TooSlowOrSmallAngle_NotDrifting()
        {
            var s = new DriftScoring();
            s.Update(S(40f, speed: 2f));
            Assert.IsFalse(s.IsDrifting);
            s.Update(S(5f));
            Assert.IsFalse(s.IsDrifting);
        }

        [Test]
        public void DonutDrill_CompletesAfterRotations()
        {
            var d = new DonutDrill(Vec2.Zero, 10f, 2f);
            for (int i = 0; i < 400 && d.State != DrillState.Completed; i++)
            {
                float a = i * 0.05f;
                d.Update(new DrillSample { dt = 0.02f, position = Vec2.FromYaw(a) * 5f, isDrifting = true, speedMs = 8f });
            }
            Assert.AreEqual(DrillState.Completed, d.State);
        }

        [Test]
        public void DonutDrill_OutsideRing_NoProgress()
        {
            var d = new DonutDrill(Vec2.Zero, 10f, 2f);
            for (int i = 0; i < 400; i++)
                d.Update(new DrillSample { dt = 0.02f, position = Vec2.FromYaw(i * 0.05f) * 30f, isDrifting = true });
            Assert.AreEqual(0f, d.Progress);
        }

        [Test]
        public void FigureEight_RequiresAlternatingCenters()
        {
            var a = new Vec2(-10f, 0f); var b = new Vec2(10f, 0f);
            var d = new FigureEightDrill(a, b, 9f, 2);
            for (int i = 0; i < 300; i++) d.Update(new DrillSample { dt = 0.02f, position = a + Vec2.FromYaw(i * 0.05f) * 5f, isDrifting = true });
            Assert.AreEqual(1, d.Loops, "Repeated loops around one center count once");
            for (int i = 0; i < 300; i++) d.Update(new DrillSample { dt = 0.02f, position = b + Vec2.FromYaw(-i * 0.05f) * 5f, isDrifting = true });
            Assert.AreEqual(DrillState.Completed, d.State);
        }

        [Test]
        public void Slalom_CountsGatesAndMisses()
        {
            var cones = new List<Vec2> { new Vec2(0f, 0f), new Vec2(10f, 0f), new Vec2(20f, 0f) };
            var d = new SlalomDrill(cones, new Vec2(1f, 0f));
            // Weave: pass cone0 on the left (+Z side), cone1 on the right, cone2 on the left.
            for (float x = -5f; x <= 25f; x += 0.25f)
            {
                float z = MathUtil.Sin((x + 0f) / 10f * MathUtil.Pi) * -2f + (x < 5f ? 2f : 0f);
                float side = (int)Math.Floor((x + 5f) / 10f) % 2 == 0 ? 2f : -2f;
                d.Update(new DrillSample { dt = 0.05f, position = new Vec2(x, side), isDrifting = true });
            }
            Assert.AreEqual(DrillState.Completed, d.State);
            Assert.AreEqual(0, d.Misses);
        }

        [Test]
        public void GameFlow_RejectsIllegalTransitions()
        {
            var f = new GameFlow();
            Assert.IsFalse(f.TryTransition(GameState.Driving));
            Assert.IsTrue(f.TryTransition(GameState.MainMenu));
            Assert.IsTrue(f.TryTransition(GameState.Loading));
            Assert.IsTrue(f.TryTransition(GameState.Intro));
            Assert.IsTrue(f.SimulationRunning);
            Assert.IsTrue(f.TryTransition(GameState.Driving));
            Assert.IsTrue(f.TryTransition(GameState.LayoutEditor));
            Assert.IsFalse(f.SimulationRunning);
            Assert.IsFalse(f.TryTransition(GameState.MainMenu));
            Assert.IsTrue(f.TryTransition(GameState.Driving));
        }

        [Test]
        public void Countersteer_SteersTowardVelocity_AndNeverExceedsLock()
        {
            var pipeline = new DriverAssistPipeline(AssistSettings.For(AssistPreset.Assisted));
            var tel = new VehicleTelemetry { speedMs = 12f, driftAngleDeg = 35f };
            VehicleControls c = default;
            for (int i = 0; i < 100; i++) c = pipeline.Process(new DriverInput(), tel, 50f, 0.01f);
            Assert.Less(c.steer, -0.2f, "Right-hand drift (positive angle) -> countersteer left");
            Assert.GreaterOrEqual(c.steer, -1f);
            var sim = new DriverAssistPipeline(AssistSettings.For(AssistPreset.Simulation));
            var raw = sim.Process(new DriverInput { steer = 0.3f }, tel, 50f, 0.01f);
            Assert.AreEqual(0.3f, raw.steer, 1e-5, "Simulation preset passes input through");
        }

        [Test]
        public void SpinProtection_CutsThrottleAtExtremeAngle()
        {
            var p = new DriverAssistPipeline(AssistSettings.For(AssistPreset.Assisted));
            var tel = new VehicleTelemetry { speedMs = 10f, driftAngleDeg = 95f };
            var c = p.Process(new DriverInput { throttle = 1f }, tel, 50f, 0.01f);
            Assert.Less(c.throttle, 0.6f);
            Assert.Greater(p.LastThrottleCut, 0f);
        }

        [Test]
        public void AssistedDonut_StaysInControlledDrift()
        {
            var car = new PlanarVehicleHarness(VehiclePhysicsProfile.CreatePlaceholderRwd());
            var assists = new DriverAssistPipeline(AssistSettings.For(AssistPreset.Balanced));
            var input = new DriverInput { throttle = 1f };
            for (int i = 0; i < 200; i++) car.Step(0.01f, assists.Process(input, car.Telemetry, 50f, 0.01f));
            input.steer = 1f; input.handbrake = 1f;
            for (int i = 0; i < 40; i++) car.Step(0.01f, assists.Process(input, car.Telemetry, 50f, 0.01f));
            input.handbrake = 0f; input.throttle = 0.9f;
            float maxAngle = 0f;
            for (int i = 0; i < 800; i++)
            {
                car.Step(0.01f, assists.Process(input, car.Telemetry, 50f, 0.01f));
                Assert.IsTrue(car.IsFinite);
                if (i > 200) maxAngle = Math.Max(maxAngle, Math.Abs(car.Telemetry.driftAngleDeg));
            }
            Assert.Greater(maxAngle, 20f);
        }
    }
}

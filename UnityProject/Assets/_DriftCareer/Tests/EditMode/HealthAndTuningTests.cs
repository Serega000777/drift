using System;
using DriftCareer.Core;
using DriftCareer.Core.Garage;
using DriftCareer.Core.Vehicle;
using NUnit.Framework;

namespace DriftCareer.Tests.EditMode
{
    public class HealthAndTuningTests
    {
        private const float Dt = 0.01f;

        private static VehicleControls Cruise(float throttle)
        {
            var c = VehicleControls.Neutral;
            c.throttle = throttle;
            return c;
        }

        /// <summary>Drives straight at ~90 km/h, optionally blowing a tire, returns lateral drift (m) and heading change (deg).</summary>
        private static void DriveStraight(int blowWheel, out float lateral, out float headingDeg)
        {
            var car = new PlanarVehicleHarness(VehiclePhysicsProfile.CreatePlaceholderRwd());
            var health = new VehicleHealth();
            car.Sim.SetGearImmediate(4);
            car.SetMotion(Vec2.Zero, 0f, new Vec2(0f, 25f), 0f);
            if (blowWheel >= 0) health.Blow(blowWheel);
            for (int i = 0; i < 300; i++)
            {
                car.Step(Dt, Cruise(0.35f));
                health.Update(Dt, car.Sim, car.Telemetry.speedMs);
                Assert.IsTrue(car.IsFinite);
            }
            lateral = car.Position.X;
            headingDeg = car.Yaw * MathUtil.Rad2Deg;
        }

        [Test]
        public void HealthyCar_DrivesStraight()
        {
            DriveStraight(-1, out float lat, out float heading);
            Assert.Less(Math.Abs(lat), 0.5f);
            Assert.Less(Math.Abs(heading), 1f);
        }

        [TestCase(0, -1f)] // front-left blown -> pulls left (negative X)
        [TestCase(1, 1f)]  // front-right blown -> pulls right
        [TestCase(2, -1f)]
        [TestCase(3, 1f)]
        public void Blowout_PullsTowardBlownSide_ThroughForcesOnly(int wheel, float expectedSign)
        {
            DriveStraight(wheel, out float lat, out float heading);
            TestContext.WriteLine($"wheel {wheel}: lateral {lat:0.00} m, heading {heading:0.0} deg");
            Assert.AreEqual(expectedSign, Math.Sign(lat), "Car must drift toward the blown tire");
            Assert.Greater(Math.Abs(lat), 0.3f);
        }

        [Test]
        public void HardDonut_HeatsAndWearsRearTires()
        {
            var car = new PlanarVehicleHarness(VehiclePhysicsProfile.CreatePlaceholderRwd());
            var health = new VehicleHealth(new HealthSettings { tireWearMultiplier = 1f, blowouts = false });
            var c = Cruise(1f);
            for (int i = 0; i < 200; i++) { car.Step(Dt, c); health.Update(Dt, car.Sim, car.Telemetry.speedMs); }
            c.steer = 1f; c.handbrake = 1f;
            for (int i = 0; i < 40; i++) { car.Step(Dt, c); health.Update(Dt, car.Sim, car.Telemetry.speedMs); }
            c.handbrake = 0f; c.throttle = 0.9f;
            for (int i = 0; i < 2000; i++) { car.Step(Dt, c); health.Update(Dt, car.Sim, car.Telemetry.speedMs); }
            var rl = health.tires[2];
            var fl = health.tires[0];
            TestContext.WriteLine($"RL temp {rl.temperatureC:0} C wear {rl.wear:0.000}; FL temp {fl.temperatureC:0} C");
            Assert.Greater(rl.temperatureC, 70f);
            Assert.Greater(rl.wear, 0.01f);
            Assert.Greater(rl.temperatureC, fl.temperatureC);
        }

        [Test]
        public void SevereCornerImpact_DamagesAndPuncturesNearestWheel()
        {
            var p = VehiclePhysicsProfile.CreatePlaceholderRwd();
            var health = new VehicleHealth();
            int punctured = -1;
            health.TireBlown += i => punctured = i;
            health.ReportImpact(-0.8f, p.CgToFront + 0.3f, 25000f, p);
            Assert.AreEqual(0, punctured, "Front-left impact should burst the front-left tire");
            Assert.Greater(health.cornerDamage[0], 0.2f);
            Assert.Less(health.BodyIntegrity, 1f);
            var sim = new VehicleSimulation(p);
            health.ApplyModifiers(sim);
            Assert.Less(sim.GetWheelModifiers(0).radiusOffset, 0f);
            Assert.AreNotEqual(0f, sim.GetWheelModifiers(0).toeOffsetRad);
            health.Repair();
            health.ApplyModifiers(sim);
            Assert.AreEqual(0f, sim.GetWheelModifiers(0).radiusOffset);
        }

        [Test]
        public void DamageOff_IgnoresImpacts()
        {
            var p = VehiclePhysicsProfile.CreatePlaceholderRwd();
            var health = new VehicleHealth(new HealthSettings { damage = DamageMode.Off });
            health.ReportImpact(0f, 2f, 50000f, p);
            Assert.AreEqual(1f, health.BodyIntegrity);
        }

        [Test]
        public void Tuning_ChangesProfileMeasurably_AndStaysValid()
        {
            var baseP = VehiclePhysicsProfile.CreatePlaceholderRwd();
            foreach (var id in TuningPresets.Ids)
            {
                var tuned = TuningApplier.Apply(baseP, TuningPresets.Create(id));
                var errors = tuned.Validate();
                Assert.AreEqual(0, errors.Count, id + ": " + string.Join("; ", errors));
            }
            var pro = TuningApplier.Apply(baseP, TuningPresets.Create("pro_drift"));
            Assert.Less(pro.chassis.massKg, baseP.chassis.massKg);
            Assert.Less(pro.chassis.cgHeight, baseP.chassis.cgHeight);
            Assert.Greater(pro.engine.torqueCurve.MaxY(), baseP.engine.torqueCurve.MaxY() * 1.4f);
            Assert.AreEqual(DifferentialType.Locked, pro.differential.type);
            Assert.AreEqual(64f, pro.steering.maxAngleDeg);
            Assert.AreEqual(1250f, baseP.chassis.massKg, "Base profile must not be mutated");
        }

        [Test]
        public void Tuning_SanitizeClampsGarbage()
        {
            var t = VehicleTuningState.Stock();
            t.powerScale = 99f;
            t.massOffsetKg = float.NaN;
            t.gearRatios = new[] { 3f, 4f, 1f };
            t.visual.paintColor = "red";
            t.visual.parts[PartSlot.Spoiler] = "hood.carbon";
            var s = TuningApplier.Sanitize(t);
            Assert.LessOrEqual(s.powerScale, 2f);
            Assert.IsTrue(MathUtil.IsFinite(s.massOffsetKg));
            for (int i = 1; i < s.gearRatios.Length; i++) Assert.Less(s.gearRatios[i], s.gearRatios[i - 1]);
            Assert.IsTrue(VisualPartCatalog.IsValidColor(s.visual.paintColor));
            Assert.IsFalse(s.visual.parts.ContainsKey(PartSlot.Spoiler));
        }

        [Test]
        public void PowerUpgrade_ImprovesSimulatedAcceleration()
        {
            var baseP = VehiclePhysicsProfile.CreatePlaceholderRwd();
            var stock = PerformanceEstimator.Estimate(baseP, 15f);
            var t = VehicleTuningState.Stock();
            t.powerScale = 1.5f;
            var tuned = PerformanceEstimator.Estimate(TuningApplier.Apply(baseP, t), 15f);
            TestContext.WriteLine($"stock {stock.peakPowerHp:0} hp 0-100 {stock.zeroTo100:0.00}s; tuned {tuned.peakPowerHp:0} hp 0-100 {tuned.zeroTo100:0.00}s; drift {stock.driftRating:0.0}");
            Assert.Greater(tuned.peakPowerHp, stock.peakPowerHp * 1.4f);
            Assert.Less(tuned.zeroTo100, stock.zeroTo100);
            Assert.Greater(stock.braking100To0M, 20f);
            Assert.Greater(stock.driftRating, 3f);
        }

        [Test]
        public void GtWing_AddsDownforce_AndMass()
        {
            var baseP = VehiclePhysicsProfile.CreatePlaceholderRwd();
            var t = VehicleTuningState.Stock();
            t.visual.parts[PartSlot.Spoiler] = "spoiler.gt_wing";
            var p = TuningApplier.Apply(baseP, t);
            Assert.Greater(p.aero.downforceArea, baseP.aero.downforceArea);
            Assert.Greater(p.chassis.massKg, baseP.chassis.massKg);
        }

        [TestCase("jdm_coupe_turbo")]
        [TestCase("hot_hatch_fwd")]
        [TestCase("awd_rally_sedan")]
        [TestCase("v8_muscle")]
        [TestCase("kei_roadster")]
        public void SpecTemplates_ConvertToValidDrivableProfiles(string id)
        {
            var spec = SpecTemplates.Create(id);
            var p = SpecToProfileConverter.Convert(spec);
            var errors = p.Validate();
            Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
            Assert.AreEqual(spec.layout, p.drivetrain.layout);
            var report = PerformanceEstimator.Estimate(p, 15f);
            TestContext.WriteLine($"{id}: {report.peakPowerHp:0} hp, {report.peakTorqueNm:0} Nm, 0-100 {report.zeroTo100:0.0}s, layout {p.drivetrain.layout}");
            Assert.AreEqual(spec.peakTorqueNm, report.peakTorqueNm, spec.peakTorqueNm * 0.05f);
            Assert.AreEqual(spec.peakPowerKw, report.peakPowerKw, spec.peakPowerKw * 0.08f);
            Assert.Greater(report.zeroTo100, 2.5f);
            Assert.Less(report.zeroTo100, 15f);
        }

        [Test]
        public void TireSize_ParsesToRadius()
        {
            Assert.AreEqual(0.3172f, SpecToProfileConverter.TireRadius("225/45R17"), 0.002f); // 17" rim / 2 + 45% of 225 mm
            Assert.AreEqual(0.31f, SpecToProfileConverter.TireRadius("garbage"), 1e-6f);
        }

        [Test]
        public void FwdAndAwd_DriveTheRightWheels()
        {
            var fwd = SpecToProfileConverter.Convert(SpecTemplates.Create("hot_hatch_fwd"));
            var car = new PlanarVehicleHarness(fwd);
            for (int i = 0; i < 300; i++) car.Step(Dt, Cruise(1f));
            Assert.Greater(car.Telemetry.speedMs, 10f);
            Assert.IsTrue(car.Sim.IsDriven(0) && !car.Sim.IsDriven(2));
            var awd = SpecToProfileConverter.Convert(SpecTemplates.Create("awd_rally_sedan"));
            var a = new PlanarVehicleHarness(awd);
            Assert.IsTrue(a.Sim.IsDriven(0) && a.Sim.IsDriven(3));
            for (int i = 0; i < 300; i++) { a.Step(Dt, Cruise(1f)); Assert.IsTrue(a.IsFinite); }
            Assert.Greater(a.Telemetry.speedMs, 10f);
        }

        [Test]
        public void GeometryIdentification_RanksCandidates()
        {
            var g = new ModelAnalysis { wheelsDetected = true, lengthM = 4.25f, widthM = 1.8f, heightM = 1.45f };
            g.wheelCenters = new[] { -0.77f, 0.3f, 1.31f, 0.77f, 0.3f, 1.31f, -0.77f, 0.3f, -1.31f, 0.77f, 0.3f, -1.31f };
            g.wheelRadii = new[] { 0.31f, 0.31f, 0.31f, 0.31f };
            IdentificationResult result = null;
            new GeometryOnlyIdentificationService().Identify(new IdentificationRequest { geometry = g }, r => result = r);
            Assert.IsTrue(result.ok);
            Assert.Greater(result.candidates.Count, 3);
            Assert.AreEqual(2.62f, result.candidates[0].spec.wheelbaseM, 0.01f);
            Assert.AreEqual(0, GeometrySpecEstimator.Validate(g).Count, string.Join(",", GeometrySpecEstimator.Validate(g)));
        }
    }
}

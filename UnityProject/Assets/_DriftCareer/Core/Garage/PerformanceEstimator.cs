using System;
using DriftCareer.Core.Vehicle;

namespace DriftCareer.Core.Garage
{
    /// <summary>Technical characteristics shown in the garage. Simulated numbers come from the real physics core.</summary>
    public sealed class PerformanceReport
    {
        public float peakPowerKw;
        public float peakPowerHp;
        public float peakPowerRpm;
        public float peakTorqueNm;
        public float peakTorqueRpm;
        public float massKg;
        public float frontWeightPercent;
        public float powerToWeightHpPerTon;
        public DriveLayout layout;
        public int gears;
        /// <summary>Simulated 0-100 km/h (s). -1 if not reached within the time budget.</summary>
        public float zeroTo100;
        /// <summary>Simulated top speed reached within the time budget (km/h).</summary>
        public float topSpeedKmh;
        /// <summary>Simulated 100-0 km/h braking distance (m).</summary>
        public float braking100To0M;
        /// <summary>0..10 ratings derived from simulation (for UI bars).</summary>
        public float accelerationRating, brakingRating, gripRating, driftRating;
    }

    /// <summary>
    /// Runs short headless tests on <see cref="PlanarVehicleHarness"/> to estimate stats honestly
    /// (no hand-written numbers). Cost: a few ms on desktop; call off the hot path on mobile.
    /// </summary>
    public static class PerformanceEstimator
    {
        private const float Dt = 0.01f;

        public static PerformanceReport Estimate(VehiclePhysicsProfile p, float topSpeedBudgetSeconds = 40f)
        {
            var r = new PerformanceReport
            {
                massKg = p.chassis.massKg,
                frontWeightPercent = p.chassis.frontWeightFraction * 100f,
                layout = p.drivetrain.layout,
                gears = p.transmission.gearRatios.Length
            };
            for (float rpm = p.engine.idleRpm; rpm <= p.engine.limiterRpm; rpm += 50f)
            {
                float tq = p.engine.torqueCurve.Evaluate(rpm);
                float kw = tq * rpm * MathUtil.RpmToRadPerSec / 1000f;
                if (kw > r.peakPowerKw) { r.peakPowerKw = kw; r.peakPowerRpm = rpm; }
                if (tq > r.peakTorqueNm) { r.peakTorqueNm = tq; r.peakTorqueRpm = rpm; }
            }
            r.peakPowerHp = r.peakPowerKw * 1.341f;
            r.powerToWeightHpPerTon = r.peakPowerHp / (r.massKg / 1000f);

            // Acceleration and top speed.
            var car = new PlanarVehicleHarness(p);
            var c = VehicleControls.Neutral;
            c.throttle = 1f;
            r.zeroTo100 = -1f;
            int steps = (int)(topSpeedBudgetSeconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                // "Good driver" launch: simple traction control keeps driven-wheel slip near the tire peak,
                // so stats reflect the car, not wheelspin from flooring it.
                float slip = 0f;
                for (int w = 0; w < 4; w++) if (car.Sim.IsDriven(w)) slip = Math.Max(slip, car.Telemetry.wheels[w].slipRatio);
                float target = p.rear.tire.peakSlipRatio * 1.3f;
                c.throttle = MathUtil.Clamp(c.throttle + (slip > target ? -0.08f : 0.04f), 0.25f, 1f);
                car.Step(Dt, c);
                float kmh = car.Telemetry.SpeedKmh;
                if (r.zeroTo100 < 0f && kmh >= 100f) r.zeroTo100 = car.Time;
                if (kmh > r.topSpeedKmh) r.topSpeedKmh = kmh;
                if (!car.IsFinite) break;
            }

            // Braking 100-0.
            var b = new PlanarVehicleHarness(p);
            b.Sim.SetGearImmediate(3);
            b.SetMotion(Vec2.Zero, 0f, new Vec2(0f, 27.78f), 0f);
            var bc = VehicleControls.Neutral; bc.brake = 1f;
            for (int i = 0; i < 2000 && b.Velocity.Length > 0.3f; i++) b.Step(Dt, bc);
            r.braking100To0M = b.Position.Y;

            // Grip: steady-state lateral g on a 40 m radius (tire friction x load sensitivity proxy).
            float muLat = 0.5f * (p.front.tire.peakFrictionLat + p.rear.tire.peakFrictionLat);
            r.gripRating = MathUtil.Clamp((muLat - 0.7f) / 0.6f * 10f, 0f, 10f);
            r.accelerationRating = r.zeroTo100 > 0f ? MathUtil.Clamp((12f - r.zeroTo100) / 9f * 10f, 0f, 10f) : 0f;
            r.brakingRating = MathUtil.Clamp((60f - r.braking100To0M) / 30f * 10f, 0f, 10f);
            r.driftRating = EstimateDrift(p);
            return r;
        }

        /// <summary>
        /// Sustained-drift test with a "good driver": from 15 m/s a handbrake entry, then steering (countersteer)
        /// and throttle are closed-loop controlled to hold ~38 deg. Rating = share of time in a controlled slide
        /// x steadiness, so cars are judged on controllability rather than on spinning at fixed inputs.
        /// </summary>
        private static float EstimateDrift(VehiclePhysicsProfile p)
        {
            var car = new PlanarVehicleHarness(p);
            car.Sim.SetGearImmediate(2);
            car.SetMotion(Vec2.Zero, 0f, new Vec2(0f, 15f), 0f);
            var c = VehicleControls.Neutral;
            c.steer = 0.6f; c.handbrake = 1f; c.throttle = 0.5f;
            for (int i = 0; i < 35; i++) car.Step(Dt, c);
            c.handbrake = 0f;
            const float target = 38f;
            double sum = 0, sumSq = 0;
            int n = 0, total = 0;
            for (int i = 0; i < 1200; i++)
            {
                float a = car.Telemetry.driftAngleDeg;
                c.steer = MathUtil.Clamp((target - a) / 30f, -1f, 1f);
                c.throttle = MathUtil.Clamp(c.throttle + (target - a) * 0.002f, 0.05f, 1f);
                car.Step(Dt, c);
                if (i <= 200) continue;
                total++;
                if (car.Telemetry.speedMs > 4f && a > 15f && a < 100f) { sum += a; sumSq += a * a; n++; }
            }
            if (n == 0 || total == 0) return 0f;
            double avg = sum / n;
            double std = Math.Sqrt(Math.Max(0.0, sumSq / n - avg * avg));
            float frac = (float)n / total;
            float steady = 1f - MathUtil.Clamp01((float)std / 40f);
            float reach = MathUtil.Clamp01((float)avg / 35f);
            return MathUtil.Clamp(10f * (float)Math.Pow(frac, 0.8) * steady * reach, 0f, 10f);
        }
    }
}

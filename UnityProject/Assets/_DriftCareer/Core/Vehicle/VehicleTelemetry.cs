namespace DriftCareer.Core.Vehicle
{
    /// <summary>
    /// Snapshot of vehicle state for HUD, scoring, assists, audio and debug logging.
    /// A single instance is reused by the host (no per-frame allocations).
    /// </summary>
    public sealed class VehicleTelemetry
    {
        public float time;
        public float speedMs;
        public float forwardSpeedMs;
        public float lateralSpeedMs;
        public float yawRateDegPerSec;
        /// <summary>Signed angle between heading and velocity (deg). Positive = car rotated right of its velocity.</summary>
        public float driftAngleDeg;
        public float engineRpm;
        public int gear;
        public bool shifting;
        public bool engineRunning;
        public float clutch;
        public float driveTorque;
        public float steerInput;
        public float steerAngleDeg;
        public float throttle;
        public float brake;
        public float handbrake;
        public float redlineRpm;
        public float limiterRpm;
        public bool anyWheelGrounded;
        public int nonFiniteRecoveries;
        public readonly WheelOutput[] wheels = new WheelOutput[VehicleSimulation.WheelCount];

        public float SpeedKmh => speedMs * 3.6f;
        public float SpeedMph => speedMs * 2.23694f;

        public float RearSlipAverage => 0.5f * (MathUtil.Abs(wheels[2].slipRatio) + MathUtil.Abs(wheels[3].slipRatio));

        public bool IsFinite =>
            MathUtil.IsFinite(speedMs) && MathUtil.IsFinite(engineRpm) && MathUtil.IsFinite(driftAngleDeg) && MathUtil.IsFinite(yawRateDegPerSec);

        /// <summary>Copies simulation-owned values (rpm, gear, wheels...).</summary>
        public void CaptureFrom(VehicleSimulation sim)
        {
            engineRpm = sim.EngineRpm;
            gear = sim.Gear;
            shifting = sim.IsShifting;
            engineRunning = sim.EngineRunning;
            clutch = sim.ClutchEngagement;
            driveTorque = sim.DriveTorque;
            steerAngleDeg = sim.CenterSteerAngleRad * MathUtil.Rad2Deg;
            var c = sim.AppliedControls;
            steerInput = c.steer;
            throttle = c.throttle;
            brake = c.brake;
            handbrake = c.handbrake;
            redlineRpm = sim.Profile.engine.redlineRpm;
            limiterRpm = sim.Profile.engine.limiterRpm;
            nonFiniteRecoveries = sim.NonFiniteRecoveries;
            anyWheelGrounded = false;
            for (int i = 0; i < wheels.Length; i++)
            {
                wheels[i] = sim.Wheel(i);
                anyWheelGrounded |= wheels[i].grounded;
            }
        }

        /// <summary>
        /// Sets planar motion values from CG local-frame velocity (x = right, y = forward).
        /// Drift angle is measured at the rear axle center (distance <paramref name="cgToRear"/> behind the CG),
        /// so kinematic sideslip of the CG in tight low-speed turns is not reported as drifting.
        /// </summary>
        public void SetMotion(Vec2 localVelocity, float yawRateRad, float cgToRear)
        {
            forwardSpeedMs = localVelocity.Y;
            lateralSpeedMs = localVelocity.X;
            speedMs = localVelocity.Length;
            yawRateDegPerSec = yawRateRad * MathUtil.Rad2Deg;
            var rearAxle = new Vec2(localVelocity.X - yawRateRad * cgToRear, localVelocity.Y);
            driftAngleDeg = speedMs < DriftMath.MinSpeedForAngle ? 0f : DriftMath.DriftAngleDeg(rearAxle);
        }
    }

    public static class DriftMath
    {
        /// <summary>Below this speed the drift angle is reported as 0 (direction is noise).</summary>
        public const float MinSpeedForAngle = 1.5f;

        /// <summary>
        /// Signed drift angle from local velocity (x right, y forward).
        /// Positive when the velocity points to the car's left, i.e. the car is rotated clockwise
        /// (to the right) relative to its direction of travel.
        /// </summary>
        public static float DriftAngleDeg(Vec2 localVelocity)
        {
            if (localVelocity.Length < 0.5f) return 0f;
            return MathUtil.Atan2(-localVelocity.X, localVelocity.Y) * MathUtil.Rad2Deg;
        }
    }
}

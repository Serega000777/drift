using DriftCareer.Core.Surfaces;

namespace DriftCareer.Core.Vehicle
{
    /// <summary>
    /// Headless planar (x, z, yaw) rigid body that drives <see cref="VehicleSimulation"/> without Unity.
    /// Loads use quasi-static load transfer instead of suspension. Purpose: deterministic unit tests of
    /// tires/drivetrain/assists (donuts, figure-eights, FPS invariance) on CI. Not the gameplay body.
    /// </summary>
    public sealed class PlanarVehicleHarness
    {
        private readonly WheelContact[] _contacts = new WheelContact[VehicleSimulation.WheelCount];
        private readonly Vec2[] _wheelLocal = new Vec2[VehicleSimulation.WheelCount];
        private readonly float _mass;
        private readonly float _iz;
        private readonly float _frontRollShare;
        private readonly float[] _fz = new float[VehicleSimulation.WheelCount];
        private float _aLong;
        private float _aLat;

        public VehicleSimulation Sim { get; }
        public VehicleTelemetry Telemetry { get; } = new VehicleTelemetry();
        public Vec2 Position;
        /// <summary>Heading in radians (0 = +Z, positive = clockwise from above).</summary>
        public float Yaw;
        public Vec2 Velocity;
        /// <summary>Yaw rate (rad/s), positive = clockwise from above (turning right).</summary>
        public float YawRate;
        public SurfaceGrip Grip = SurfaceGrip.Default;
        public float Time { get; private set; }

        public PlanarVehicleHarness(VehiclePhysicsProfile profile)
        {
            Sim = new VehicleSimulation(profile);
            var ch = profile.chassis;
            _mass = ch.massKg;
            _iz = _mass * (ch.length * ch.length + ch.width * ch.width) / 12f * ch.inertiaScale;
            float a = profile.CgToFront, b = profile.CgToRear;
            _wheelLocal[0] = new Vec2(-profile.front.trackWidth * 0.5f, a);
            _wheelLocal[1] = new Vec2(profile.front.trackWidth * 0.5f, a);
            _wheelLocal[2] = new Vec2(-profile.rear.trackWidth * 0.5f, -b);
            _wheelLocal[3] = new Vec2(profile.rear.trackWidth * 0.5f, -b);
            float rf = profile.front.suspension.springRate + 2f * profile.front.suspension.antiRollRate;
            float rr = profile.rear.suspension.springRate + 2f * profile.rear.suspension.antiRollRate;
            _frontRollShare = rf / (rf + rr);
        }

        public Vec2 Forward => Vec2.FromYaw(Yaw);
        public Vec2 LocalVelocity => Vec2.WorldToLocal(Velocity, Yaw);

        public void SetMotion(Vec2 position, float yaw, Vec2 velocity, float yawRate)
        {
            Position = position; Yaw = yaw; Velocity = velocity; YawRate = yawRate;
            Sim.MatchWheelSpeeds(Vec2.WorldToLocal(velocity, yaw).Y);
        }

        public void Step(float dt, VehicleControls controls)
        {
            var p = Sim.Profile;
            Sim.Prepare(dt, controls);

            float speed = Velocity.Length;
            float q = 0.5f * p.aero.airDensity * speed * speed;
            float downforce = q * p.aero.downforceArea;
            float h = p.chassis.cgHeight;
            float L = p.chassis.wheelbase;
            float totalW = _mass * MathUtil.Gravity + downforce;
            float frontStatic = totalW * p.chassis.frontWeightFraction * 0.5f;
            float rearStatic = totalW * (1f - p.chassis.frontWeightFraction) * 0.5f;
            float longTransfer = _mass * _aLong * h / L * 0.5f;
            float latMoment = _mass * _aLat * h;
            float dFront = latMoment * _frontRollShare / p.front.trackWidth;
            float dRear = latMoment * (1f - _frontRollShare) / p.rear.trackWidth;

            var fz = _fz;
            {
                fz[0] = frontStatic - longTransfer + dFront;
                fz[1] = frontStatic - longTransfer - dFront;
                fz[2] = rearStatic + longTransfer + dRear;
                fz[3] = rearStatic + longTransfer - dRear;
            }
            float fzSum = 0f;
            for (int i = 0; i < 4; i++) { fz[i] = MathUtil.Max(0f, fz[i]); fzSum += fz[i]; }

            for (int i = 0; i < 4; i++)
            {
                Vec2 r = Vec2.LocalToWorld(_wheelLocal[i], Yaw);
                Vec2 vPoint = Velocity + new Vec2(YawRate * r.Y, -YawRate * r.X);
                float heading = Yaw + Sim.WheelSteerAngle(i);
                _contacts[i] = new WheelContact
                {
                    grounded = fz[i] > 1f,
                    normalLoad = fz[i],
                    longitudinalVelocity = Vec2.Dot(vPoint, Vec2.FromYaw(heading)),
                    lateralVelocity = Vec2.Dot(vPoint, Vec2.RightFromYaw(heading)),
                    grip = Grip,
                    massShare = fzSum > 0f ? _mass * fz[i] / fzSum : _mass * 0.25f
                };
            }

            Sim.Step(dt, _contacts);

            Vec2 force = Vec2.Zero;
            float torque = 0f;
            for (int i = 0; i < 4; i++)
            {
                var w = Sim.Wheel(i);
                float heading = Yaw + w.steerAngleRad;
                Vec2 f = Vec2.FromYaw(heading) * w.fx + Vec2.RightFromYaw(heading) * w.fy;
                Vec2 r = Vec2.LocalToWorld(_wheelLocal[i], Yaw);
                force += f;
                torque += -Vec2.Cross(r, f);
            }
            if (speed > 0.01f) force -= Velocity * (speed * 0.5f * p.aero.airDensity * p.aero.dragArea);

            Vec2 accel = force / _mass;
            Velocity += accel * dt;
            YawRate += torque / _iz * dt;
            Position += Velocity * dt;
            Yaw = MathUtil.WrapAngle(Yaw + YawRate * dt);
            Time += dt;

            Vec2 aLocal = Vec2.WorldToLocal(accel, Yaw);
            _aLong = MathUtil.Damp(_aLong, MathUtil.Clamp(aLocal.Y, -15f, 15f), 12f, dt);
            _aLat = MathUtil.Damp(_aLat, MathUtil.Clamp(aLocal.X, -15f, 15f), 12f, dt);

            Telemetry.time = Time;
            Telemetry.CaptureFrom(Sim);
            Telemetry.SetMotion(LocalVelocity, YawRate, p.CgToRear);
        }

        public bool IsFinite => Position.IsFinite && Velocity.IsFinite && MathUtil.IsFinite(Yaw) && MathUtil.IsFinite(YawRate) && Telemetry.IsFinite;
    }
}

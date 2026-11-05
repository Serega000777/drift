using System;
using System.Collections.Generic;
using DriftCareer.Core.Surfaces;

namespace DriftCareer.Core.Vehicle
{
    public enum DifferentialType { Open = 0, Lsd = 1, Locked = 2 }

    public enum TransmissionMode { Automatic = 0, Manual = 1 }

    public enum DriveLayout { Rwd = 0, Fwd = 1, Awd = 2 }

    [Serializable]
    public sealed class DrivetrainSpec
    {
        public DriveLayout layout = DriveLayout.Rwd;
        /// <summary>AWD: fraction of drive torque sent to the front axle.</summary>
        public float awdFrontSplit = 0.35f;
        /// <summary>AWD: center coupling capacity (Nm). 0 = fully open center.</summary>
        public float centerLockNm = 300f;
        /// <summary>FWD/AWD: whether the front axle diff uses the LSD settings (otherwise open).</summary>
        public bool frontDiffLsd;

        public DrivetrainSpec Clone() => (DrivetrainSpec)MemberwiseClone();
    }

    /// <summary>Tire friction curve parameters. See docs/PHYSICS_SPEC.md "Tire model".</summary>
    [Serializable]
    public sealed class TireSpec
    {
        public TireCompound compound = TireCompound.DriftSemiSlick;
        /// <summary>Peak longitudinal friction coefficient on reference surface.</summary>
        public float peakFrictionLong = 1.15f;
        /// <summary>Peak lateral friction coefficient on reference surface.</summary>
        public float peakFrictionLat = 1.05f;
        /// <summary>Slip ratio where longitudinal force peaks.</summary>
        public float peakSlipRatio = 0.11f;
        /// <summary>Slip angle (deg) where lateral force peaks.</summary>
        public float peakSlipAngleDeg = 8.5f;
        /// <summary>Friction at full slide as a fraction of peak (kinetic/static ratio).</summary>
        public float slideFrictionRatio = 0.8f;
        /// <summary>How quickly friction falls from peak toward slide value past the peak.</summary>
        public float falloffRate = 0.9f;
        /// <summary>Fractional friction loss per nominal load of extra load (load sensitivity).</summary>
        public float loadSensitivity = 0.12f;
        public float nominalLoadN = 3500f;
        public float rollingResistance = 0.012f;

        public TireSpec Clone() => (TireSpec)MemberwiseClone();
    }

    [Serializable]
    public sealed class SuspensionSpec
    {
        /// <summary>Total wheel travel from full droop to bump stop (m).</summary>
        public float travel = 0.24f;
        public float springRate = 38000f;
        public float bumpDamping = 2600f;
        public float reboundDamping = 3600f;
        public float bumpStopRate = 160000f;
        public float antiRollRate = 9000f;
        /// <summary>Height above ground where tire forces enter the chassis (roll center approximation).</summary>
        public float rollCenterHeight = 0.1f;

        public SuspensionSpec Clone() => (SuspensionSpec)MemberwiseClone();
    }

    [Serializable]
    public sealed class AxleSpec
    {
        public float trackWidth = 1.47f;
        public float wheelRadius = 0.315f;
        public float wheelInertia = 1.1f;
        /// <summary>Static toe per wheel (deg). Positive = toe-in.</summary>
        public float staticToeDeg;
        public SuspensionSpec suspension = new SuspensionSpec();
        public TireSpec tire = new TireSpec();

        public AxleSpec Clone()
        {
            var a = (AxleSpec)MemberwiseClone();
            a.suspension = suspension.Clone();
            a.tire = tire.Clone();
            return a;
        }
    }

    [Serializable]
    public sealed class ChassisSpec
    {
        public float massKg = 1250f;
        public float wheelbase = 2.55f;
        public float cgHeight = 0.5f;
        /// <summary>Fraction of static weight on the front axle (0..1).</summary>
        public float frontWeightFraction = 0.52f;
        /// <summary>Body dimensions used for inertia approximation (m).</summary>
        public float length = 4.4f;
        public float width = 1.72f;
        public float height = 1.3f;
        /// <summary>Scale on the box-approximated inertia tensor. Real cars are ~0.8-1.0 of a uniform box.</summary>
        public float inertiaScale = 0.9f;

        public ChassisSpec Clone() => (ChassisSpec)MemberwiseClone();
    }

    [Serializable]
    public sealed class SteeringSpec
    {
        /// <summary>Maximum road-wheel angle (deg). Drift cars use large lock.</summary>
        public float maxAngleDeg = 50f;
        /// <summary>0 = parallel steer, 1 = full Ackermann.</summary>
        public float ackermann = 0.3f;
        /// <summary>Rack speed limit in road-wheel deg/s.</summary>
        public float rackSpeedDegPerSec = 600f;

        public SteeringSpec Clone() => (SteeringSpec)MemberwiseClone();
    }

    [Serializable]
    public sealed class EngineSpec
    {
        public float idleRpm = 900f;
        public float redlineRpm = 7200f;
        public float limiterRpm = 7500f;
        public float inertia = 0.2f;
        public float frictionTorque = 12f;
        /// <summary>Engine braking torque per 1000 rpm at closed throttle (Nm).</summary>
        public float engineBrakePer1000Rpm = 9f;
        /// <summary>Full throttle torque (Nm) by rpm.</summary>
        public Curve torqueCurve = Curve.FromPairs(
            800f, 170f, 2000f, 235f, 3000f, 285f, 4000f, 315f, 5000f, 335f, 6000f, 322f, 7000f, 285f, 7600f, 250f);

        public EngineSpec Clone()
        {
            var e = (EngineSpec)MemberwiseClone();
            e.torqueCurve = torqueCurve.Clone();
            return e;
        }
    }

    [Serializable]
    public sealed class TransmissionSpec
    {
        public float[] gearRatios = { 3.55f, 2.2f, 1.55f, 1.2f, 1.0f, 0.84f };
        public float reverseRatio = 3.4f;
        public float finalDrive = 4.1f;
        public float efficiency = 0.9f;
        public float shiftTimeSec = 0.16f;
        public float clutchMaxTorque = 650f;
        /// <summary>Rpm at which the auto clutch is fully engaged from standstill.</summary>
        public float launchRpm = 2600f;
        public float autoUpshiftRpm = 6900f;
        public float autoDownshiftRpm = 3000f;

        public TransmissionSpec Clone()
        {
            var t = (TransmissionSpec)MemberwiseClone();
            t.gearRatios = (float[])gearRatios.Clone();
            return t;
        }
    }

    [Serializable]
    public sealed class DifferentialSpec
    {
        public DifferentialType type = DifferentialType.Lsd;
        public float preloadNm = 120f;
        /// <summary>Locking torque as a fraction of input torque under power.</summary>
        public float powerLockRatio = 0.65f;
        /// <summary>Locking torque as a fraction of input torque on overrun.</summary>
        public float coastLockRatio = 0.4f;

        public DifferentialSpec Clone() => (DifferentialSpec)MemberwiseClone();
    }

    [Serializable]
    public sealed class BrakeSpec
    {
        /// <summary>Total brake torque at full pedal across the whole car (Nm).</summary>
        public float maxTorqueNm = 5200f;
        /// <summary>Fraction of brake torque at the front axle.</summary>
        public float frontBias = 0.66f;
        /// <summary>Handbrake torque per rear wheel (Nm).</summary>
        public float handbrakeTorqueNm = 2400f;

        public BrakeSpec Clone() => (BrakeSpec)MemberwiseClone();
    }

    [Serializable]
    public sealed class AeroSpec
    {
        /// <summary>Drag coefficient times frontal area (m^2).</summary>
        public float dragArea = 0.68f;
        /// <summary>Downforce coefficient times area (m^2). Positive pushes down.</summary>
        public float downforceArea = 0.12f;
        public float airDensity = 1.225f;

        public AeroSpec Clone() => (AeroSpec)MemberwiseClone();
    }

    /// <summary>
    /// Physical description of a vehicle, independent of any visual model (FBX).
    /// SI units: kg, m, s, N, Nm, rad/s (deg only where named *Deg).
    /// </summary>
    [Serializable]
    public sealed class VehiclePhysicsProfile
    {
        public const int CurrentVersion = 1;

        public string id = "placeholder_rwd";
        public int version = CurrentVersion;
        public ChassisSpec chassis = new ChassisSpec();
        public AxleSpec front = new AxleSpec();
        public AxleSpec rear = new AxleSpec();
        public SteeringSpec steering = new SteeringSpec();
        public EngineSpec engine = new EngineSpec();
        public TransmissionSpec transmission = new TransmissionSpec();
        public DrivetrainSpec drivetrain = new DrivetrainSpec();
        public DifferentialSpec differential = new DifferentialSpec();
        public BrakeSpec brakes = new BrakeSpec();
        public AeroSpec aero = new AeroSpec();
        /// <summary>Drivetrain/wheel sub-steps per physics step.</summary>
        public int drivetrainSubsteps = 2;

        public float StaticAxleLoadN(bool frontAxle)
        {
            float frac = frontAxle ? chassis.frontWeightFraction : 1f - chassis.frontWeightFraction;
            return chassis.massKg * MathUtil.Gravity * frac;
        }

        /// <summary>Distance from CG to the front axle (m).</summary>
        public float CgToFront => chassis.wheelbase * (1f - chassis.frontWeightFraction);

        /// <summary>Distance from CG to the rear axle (m).</summary>
        public float CgToRear => chassis.wheelbase * chassis.frontWeightFraction;

        public VehiclePhysicsProfile Clone()
        {
            var p = (VehiclePhysicsProfile)MemberwiseClone();
            p.chassis = chassis.Clone();
            p.front = front.Clone();
            p.rear = rear.Clone();
            p.steering = steering.Clone();
            p.engine = engine.Clone();
            p.transmission = transmission.Clone();
            p.drivetrain = (drivetrain ?? new DrivetrainSpec()).Clone();
            p.differential = differential.Clone();
            p.brakes = brakes.Clone();
            p.aero = aero.Clone();
            return p;
        }

        /// <summary>Returns human-readable problems. Empty list = valid.</summary>
        public List<string> Validate()
        {
            var e = new List<string>();
            void Range(string name, float v, float min, float max)
            {
                if (!MathUtil.IsFinite(v) || v < min || v > max) e.Add($"{name}={v} outside [{min}, {max}]");
            }

            Range("chassis.massKg", chassis.massKg, 300f, 5000f);
            Range("chassis.wheelbase", chassis.wheelbase, 1.5f, 4.5f);
            Range("chassis.cgHeight", chassis.cgHeight, 0.15f, 1.5f);
            Range("chassis.frontWeightFraction", chassis.frontWeightFraction, 0.2f, 0.8f);
            Range("chassis.inertiaScale", chassis.inertiaScale, 0.3f, 2f);
            foreach (var (name, axle) in new[] { ("front", front), ("rear", rear) })
            {
                Range(name + ".trackWidth", axle.trackWidth, 0.8f, 2.5f);
                Range(name + ".wheelRadius", axle.wheelRadius, 0.15f, 0.6f);
                Range(name + ".wheelInertia", axle.wheelInertia, 0.1f, 10f);
                Range(name + ".suspension.travel", axle.suspension.travel, 0.03f, 0.6f);
                Range(name + ".suspension.springRate", axle.suspension.springRate, 2000f, 400000f);
                Range(name + ".suspension.bumpDamping", axle.suspension.bumpDamping, 0f, 50000f);
                Range(name + ".suspension.reboundDamping", axle.suspension.reboundDamping, 0f, 50000f);
                Range(name + ".tire.peakFrictionLong", axle.tire.peakFrictionLong, 0.05f, 3f);
                Range(name + ".tire.peakFrictionLat", axle.tire.peakFrictionLat, 0.05f, 3f);
                Range(name + ".tire.peakSlipRatio", axle.tire.peakSlipRatio, 0.02f, 0.5f);
                Range(name + ".tire.peakSlipAngleDeg", axle.tire.peakSlipAngleDeg, 2f, 25f);
                Range(name + ".tire.slideFrictionRatio", axle.tire.slideFrictionRatio, 0.2f, 1f);
                Range(name + ".tire.nominalLoadN", axle.tire.nominalLoadN, 500f, 20000f);
                float staticLoad = StaticAxleLoadN(name == "front") * 0.5f;
                float sag = staticLoad / axle.suspension.springRate;
                if (sag >= axle.suspension.travel * 0.85f) e.Add($"{name} spring too soft: static sag {sag:0.000} m vs travel {axle.suspension.travel}");
            }
            Range("steering.maxAngleDeg", steering.maxAngleDeg, 10f, 70f);
            Range("engine.idleRpm", engine.idleRpm, 400f, 3000f);
            if (engine.redlineRpm <= engine.idleRpm + 1000f) e.Add("engine.redlineRpm must exceed idle by 1000");
            if (engine.limiterRpm < engine.redlineRpm) e.Add("engine.limiterRpm must be >= redline");
            if (!engine.torqueCurve.IsValid(out var curveErr)) e.Add("engine.torqueCurve: " + curveErr);
            if (transmission.gearRatios == null || transmission.gearRatios.Length == 0) e.Add("transmission.gearRatios empty");
            else
                for (int i = 0; i < transmission.gearRatios.Length; i++)
                {
                    Range($"transmission.gearRatios[{i}]", transmission.gearRatios[i], 0.3f, 6f);
                    if (i > 0 && transmission.gearRatios[i] >= transmission.gearRatios[i - 1]) e.Add("gear ratios must decrease");
                }
            Range("transmission.finalDrive", transmission.finalDrive, 1.5f, 8f);
            Range("transmission.clutchMaxTorque", transmission.clutchMaxTorque, 50f, 5000f);
            Range("brakes.frontBias", brakes.frontBias, 0f, 1f);
            if (drivetrain == null) e.Add("drivetrain missing");
            else Range("drivetrain.awdFrontSplit", drivetrain.awdFrontSplit, 0.05f, 0.95f);
            Range("drivetrainSubsteps", drivetrainSubsteps, 1, 16);
            return e;
        }

        /// <summary>Default placeholder RWD drift car (~1250 kg, ~270 hp). Tuned for gameplay, not calibrated to a real car.</summary>
        public static VehiclePhysicsProfile CreatePlaceholderRwd()
        {
            var p = new VehiclePhysicsProfile();
            p.front.suspension.springRate = 42000f;
            p.front.suspension.rollCenterHeight = 0.08f;
            p.front.suspension.antiRollRate = 11000f;
            p.rear.suspension.springRate = 36000f;
            p.rear.suspension.rollCenterHeight = 0.14f;
            p.rear.suspension.antiRollRate = 7000f;
            p.front.tire.peakFrictionLat = 1.1f;
            p.rear.tire.peakFrictionLat = 1.0f;
            p.rear.tire.slideFrictionRatio = 0.8f;
            return p;
        }
    }
}

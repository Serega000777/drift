using System;
using System.Collections.Generic;
using DriftCareer.Core.Surfaces;
using DriftCareer.Core.Vehicle;

namespace DriftCareer.Core.Garage
{
    public enum TuningGroup { Suspension, Alignment, Tires, Engine, Gearbox, Differential, Brakes, Weight, Aero, Visual }

    /// <summary>
    /// Player tuning for one car. Plain data, versioned. Applied to a base <see cref="VehiclePhysicsProfile"/>
    /// by <see cref="TuningApplier"/>; the base profile is never mutated.
    /// </summary>
    [Serializable]
    public sealed class VehicleTuningState
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        public string vehicleId = "placeholder_rwd";
        public string presetName = "stock";

        // Suspension (ходовая)
        public float rideHeightOffset;          // m, negative = lower
        public float springFront = 1f, springRear = 1f;
        public float damperFront = 1f, damperRear = 1f;
        public float antiRollFront = 1f, antiRollRear = 1f;

        // Alignment (развал/схождение/руль)
        public float camberFrontDeg = -2f, camberRearDeg = -0.5f;
        public float toeFrontDeg, toeRearDeg = 0.1f;   // positive = toe-in
        public float steeringLockDeg = 50f;
        public float ackermann = 0.3f;

        // Tires (width/profile change grip, response and the real rolling radius together with rim size)
        public TireCompound compoundFront = TireCompound.DriftSemiSlick;
        public TireCompound compoundRear = TireCompound.Sport;
        public float pressureFrontBar = 2.2f, pressureRearBar = 2.2f;
        public float tireWidthFront = 225f, tireWidthRear = 225f;     // mm
        public float tireAspectFront = 45f, tireAspectRear = 45f;     // %
        /// <summary>Starting wear of fitted tires (used "budget" rubber common in korch builds).</summary>
        public float usedTireWear;

        // Body widening: spacers / wide rims. Allowed range depends on the fender kit.
        public float trackOffsetFront, trackOffsetRear;               // m per axle (total)

        // Engine swap ("" = stock engine of the car)
        public string engineSwapId = "";

        // Korch / build items
        public bool strippedInterior;
        public bool rollCage;
        public bool hydraulicHandbrake;
        public bool batteryInTrunk;

        // Engine tune
        public float powerScale = 1f;           // ECU/turbo stage
        public float limiterOffsetRpm;
        public float flywheelScale = 1f;        // engine inertia

        // Gearbox
        public float[] gearRatios;              // null = stock
        public float finalDrive;                // 0 = stock
        public float shiftTimeSec;              // 0 = stock

        // Differential
        public DifferentialType diffType = DifferentialType.Lsd;
        public float diffPreloadNm = 120f;
        public float diffPowerLock = 0.65f;
        public float diffCoastLock = 0.4f;

        // Brakes
        public float brakeForceScale = 1f;
        public float brakeFrontBias = 0.66f;
        public float handbrakeScale = 1f;

        // Weight (вес)
        public float massOffsetKg;              // negative = weight reduction, positive = ballast
        public float frontWeightFraction = 0.52f;

        // Aero
        public float downforceScale = 1f;

        // Visual (paint, rims, body kit). Parts carry small documented mass/aero deltas.
        public VisualSetup visual = new VisualSetup();

        public VehicleTuningState Clone()
        {
            var c = (VehicleTuningState)MemberwiseClone();
            c.gearRatios = gearRatios != null ? (float[])gearRatios.Clone() : null;
            c.visual = (visual ?? new VisualSetup()).Clone();
            return c;
        }

        public static VehicleTuningState Stock(string vehicleId = "placeholder_rwd") => new VehicleTuningState { vehicleId = vehicleId };
    }

    /// <summary>
    /// Applies tuning to a base profile. Effects are simplified, documented approximations
    /// (see docs/PHYSICS_SPEC.md "Tuning"): e.g. camber is modelled as a lateral/longitudinal grip trade-off,
    /// not as contact patch geometry.
    /// </summary>
    public static class TuningApplier
    {
        public static float CompoundFriction(TireCompound c)
        {
            switch (c)
            {
                case TireCompound.Street: return 0.9f;
                case TireCompound.Sport: return 1f;
                case TireCompound.DriftSemiSlick: return 1.08f;
                case TireCompound.Winter: return 0.86f;
                case TireCompound.Studded: return 0.8f;
                default: return 1f;
            }
        }

        /// <summary>Lateral grip factor for camber: optimum near -3.5 deg front / -1.5 deg rear.</summary>
        public static float CamberLateralFactor(float camberDeg, bool front)
        {
            float optimum = front ? -3.5f : -1.5f;
            float d = (camberDeg - optimum) / 4f;
            return 1.04f - 0.08f * d * d;
        }

        public static float CamberLongitudinalFactor(float camberDeg) => 1f - 0.012f * MathUtil.Abs(camberDeg);

        public static VehiclePhysicsProfile Apply(VehiclePhysicsProfile baseProfile, VehicleTuningState t)
        {
            var p = baseProfile.Clone();
            if (t == null) return p;
            t = Sanitize(t);

            // Weight
            p.chassis.massKg = MathUtil.Clamp(p.chassis.massKg + t.massOffsetKg, 600f, 3000f);
            p.chassis.frontWeightFraction = t.frontWeightFraction;

            // Suspension: lowering lowers the CG and costs travel.
            p.chassis.cgHeight = MathUtil.Max(0.25f, p.chassis.cgHeight + t.rideHeightOffset);
            ApplyAxle(p.front, t.springFront, t.damperFront, t.antiRollFront, t.rideHeightOffset);
            ApplyAxle(p.rear, t.springRear, t.damperRear, t.antiRollRear, t.rideHeightOffset);

            // Tires: compound, pressure, camber.
            ApplyTire(p.front.tire, t.compoundFront, t.pressureFrontBar, t.camberFrontDeg, true);
            ApplyTire(p.rear.tire, t.compoundRear, t.pressureRearBar, t.camberRearDeg, false);
            ApplyTireSize(p.front, t.tireWidthFront, t.tireAspectFront, t.visual.rimSizeInch);
            ApplyTireSize(p.rear, t.tireWidthRear, t.tireAspectRear, t.visual.rimSizeInch);
            p.front.staticToeDeg = t.toeFrontDeg;
            p.rear.staticToeDeg = t.toeRearDeg;

            // Steering
            p.steering.maxAngleDeg = t.steeringLockDeg;
            p.steering.ackermann = t.ackermann;

            // Engine swap: new curve/redline, mass and weight distribution change with the swap mass.
            var swap = EngineCatalog.Find(t.engineSwapId);
            if (swap != null)
            {
                p.engine.torqueCurve = swap.TorqueCurve();
                p.engine.redlineRpm = swap.redlineRpm;
                p.engine.limiterRpm = swap.redlineRpm + 300f;
                p.transmission.autoUpshiftRpm = swap.redlineRpm - 200f;
                p.transmission.autoDownshiftRpm = MathUtil.Max(2200f, swap.torqueRpm * 0.6f);
                p.engine.inertia = swap.layout == "I6" ? 0.26f : (swap.layout == "R2" ? 0.14f : 0.18f);
                float delta = swap.swapMassKg - baseProfile.engine.swapMassKg;
                float oldMass = p.chassis.massKg;
                float front = p.chassis.frontWeightFraction * oldMass + delta * 0.95f;
                p.chassis.massKg = oldMass + delta;
                p.chassis.frontWeightFraction = MathUtil.Clamp(front / p.chassis.massKg, 0.35f, 0.7f);
            }

            // Build items.
            if (t.strippedInterior) p.chassis.massKg -= 70f;
            if (t.rollCage) { p.chassis.massKg += 40f; p.chassis.cgHeight += 0.01f; }
            if (t.batteryInTrunk)
            {
                float m = p.chassis.massKg;
                p.chassis.frontWeightFraction = MathUtil.Clamp((p.chassis.frontWeightFraction * m - 18f) / m, 0.35f, 0.7f);
            }
            if (t.hydraulicHandbrake) p.brakes.handbrakeTorqueNm *= 1.8f;

            // Body widening (validated against the fender kit in Sanitize).
            p.front.trackWidth += t.trackOffsetFront;
            p.rear.trackWidth += t.trackOffsetRear;

            // Engine tune
            var curve = p.engine.torqueCurve.Clone();
            for (int i = 0; i < curve.keys.Count; i++)
                curve.keys[i] = new CurveKey(curve.keys[i].x, curve.keys[i].y * t.powerScale);
            if (t.limiterOffsetRpm > 0f)
            {
                var last = curve.keys[curve.keys.Count - 1];
                curve.keys.Add(new CurveKey(last.x + t.limiterOffsetRpm + 200f, last.y * 0.9f));
            }
            p.engine.torqueCurve = curve;
            p.engine.limiterRpm += t.limiterOffsetRpm;
            p.engine.redlineRpm += t.limiterOffsetRpm;
            p.transmission.autoUpshiftRpm += t.limiterOffsetRpm;
            p.engine.inertia *= t.flywheelScale;
            // More power needs a stronger clutch (stage clutch comes with the tune).
            p.transmission.clutchMaxTorque = MathUtil.Max(p.transmission.clutchMaxTorque, p.engine.torqueCurve.MaxY() * 1.6f);

            // Gearbox
            if (t.gearRatios != null && t.gearRatios.Length > 0) p.transmission.gearRatios = (float[])t.gearRatios.Clone();
            if (t.finalDrive > 0f) p.transmission.finalDrive = t.finalDrive;
            if (t.shiftTimeSec > 0f) p.transmission.shiftTimeSec = t.shiftTimeSec;

            // Differential
            p.differential.type = t.diffType;
            p.differential.preloadNm = t.diffPreloadNm;
            p.differential.powerLockRatio = t.diffPowerLock;
            p.differential.coastLockRatio = t.diffCoastLock;

            // Brakes
            p.brakes.maxTorqueNm *= t.brakeForceScale;
            p.brakes.frontBias = t.brakeFrontBias;
            p.brakes.handbrakeTorqueNm *= t.handbrakeScale;

            // Aero
            p.aero.downforceArea *= t.downforceScale;

            // Body kit / wheels deltas.
            foreach (PartSlot slot in Enum.GetValues(typeof(PartSlot)))
            {
                var part = VisualPartCatalog.Find(t.visual.PartFor(slot));
                if (part == null) continue;
                p.chassis.massKg += part.massDeltaKg;
                p.aero.downforceArea += part.downforceDelta;
                p.aero.dragArea += part.dragDelta;
            }
            p.chassis.massKg = MathUtil.Clamp(p.chassis.massKg, 600f, 3000f);
            p.aero.downforceArea = MathUtil.Max(0f, p.aero.downforceArea);
            return p;
        }

        private static void ApplyAxle(AxleSpec a, float spring, float damper, float arb, float rideOffset)
        {
            a.suspension.springRate *= spring;
            a.suspension.bumpDamping *= damper;
            a.suspension.reboundDamping *= damper;
            a.suspension.antiRollRate *= arb;
            a.suspension.travel = MathUtil.Max(0.08f, a.suspension.travel + rideOffset);
        }

        /// <summary>Rolling radius from rim + sidewall; wider tires gain grip with diminishing returns and less load sensitivity.</summary>
        public static void ApplyTireSize(AxleSpec a, float widthMm, float aspect, float rimInch)
        {
            float radius = (rimInch * 25.4f * 0.5f + widthMm * aspect / 100f) / 1000f;
            a.wheelRadius = MathUtil.Clamp(radius, 0.22f, 0.45f);
            float wScale = widthMm / 225f;
            float grip = (float)Math.Pow(wScale, 0.22);
            a.tire.peakFrictionLat *= grip;
            a.tire.peakFrictionLong *= grip;
            a.tire.loadSensitivity = MathUtil.Clamp(a.tire.loadSensitivity / MathUtil.Max(0.6f, wScale), 0.03f, 0.3f);
            a.tire.rollingResistance *= 0.85f + 0.15f * wScale;
            // Low profile = stiffer sidewall = earlier, crisper peak.
            a.tire.peakSlipAngleDeg = MathUtil.Clamp(a.tire.peakSlipAngleDeg * (0.8f + aspect / 225f), 3f, 20f);
            a.wheelInertia = MathUtil.Clamp(0.9f + (wScale - 1f) * 0.4f + (rimInch - 17f) * 0.08f, 0.5f, 3f);
        }

        /// <summary>Maximum extra track width allowed by the fitted fender kit (m).</summary>
        public static float MaxTrackOffset(VisualSetup v)
        {
            var fender = VisualPartCatalog.Find(v?.PartFor(PartSlot.Fenders));
            int style = fender?.style ?? 0;
            return style == 2 ? 0.09f : (style == 1 ? 0.05f : 0.02f);
        }

        private static void ApplyTire(TireSpec tire, TireCompound compound, float pressureBar, float camberDeg, bool front)
        {
            tire.compound = compound;
            float c = CompoundFriction(compound);
            // Lower pressure: a little more grip and a softer (later) peak; higher: crisper, less grip.
            float dp = pressureBar - 2.2f;
            float pressureGrip = 1f - 0.05f * dp;
            tire.peakFrictionLat *= c * pressureGrip * CamberLateralFactor(camberDeg, front);
            tire.peakFrictionLong *= c * pressureGrip * CamberLongitudinalFactor(camberDeg);
            tire.peakSlipAngleDeg = MathUtil.Clamp(tire.peakSlipAngleDeg * (1f - 0.12f * dp), 3f, 20f);
            tire.rollingResistance *= 1f - 0.25f * dp;
        }

        /// <summary>Clamps every field into its allowed range (protects against corrupt saves / bad UI input).</summary>
        public static VehicleTuningState Sanitize(VehicleTuningState t)
        {
            var c = t.Clone();
            foreach (var d in TuningCatalog.Parameters) d.Set(c, d.Get(c));
            if (c.gearRatios != null)
            {
                if (c.gearRatios.Length < 3 || c.gearRatios.Length > 8) c.gearRatios = null;
                else
                    for (int i = 0; i < c.gearRatios.Length; i++)
                    {
                        float max = i == 0 ? 5f : c.gearRatios[i - 1] - 0.05f;
                        c.gearRatios[i] = MathUtil.Clamp(MathUtil.Finite(c.gearRatios[i], 1f), 0.5f, max);
                    }
            }
            c.visual = VisualPartCatalog.Sanitize(c.visual);
            float maxTrack = MaxTrackOffset(c.visual);
            c.trackOffsetFront = MathUtil.Clamp(MathUtil.Finite(c.trackOffsetFront), 0f, maxTrack);
            c.trackOffsetRear = MathUtil.Clamp(MathUtil.Finite(c.trackOffsetRear), 0f, maxTrack);
            if (!string.IsNullOrEmpty(c.engineSwapId) && EngineCatalog.Find(c.engineSwapId) == null) c.engineSwapId = "";
            if (!Enum.IsDefined(typeof(DifferentialType), c.diffType)) c.diffType = DifferentialType.Lsd;
            if (!Enum.IsDefined(typeof(TireCompound), c.compoundFront)) c.compoundFront = TireCompound.Sport;
            if (!Enum.IsDefined(typeof(TireCompound), c.compoundRear)) c.compoundRear = TireCompound.Sport;
            return c;
        }
    }

    /// <summary>Describes one numeric tuning slider for data-driven UI generation.</summary>
    public sealed class TuningParameter
    {
        public string id;
        public TuningGroup group;
        public string titleKey;
        public string unit;
        public float min, max, step;
        public Func<VehicleTuningState, float> getter;
        public Action<VehicleTuningState, float> setter;

        public float Get(VehicleTuningState s) => getter(s);

        public void Set(VehicleTuningState s, float v)
        {
            v = MathUtil.Finite(v, (min + max) * 0.5f);
            if (step > 0f) v = (float)Math.Round(v / step) * step;
            setter(s, MathUtil.Clamp(v, min, max));
        }
    }

    public static class TuningCatalog
    {
        public static readonly IReadOnlyList<TuningParameter> Parameters = Build();

        private static TuningParameter P(string id, TuningGroup g, string unit, float min, float max, float step,
            Func<VehicleTuningState, float> get, Action<VehicleTuningState, float> set)
            => new TuningParameter { id = id, group = g, titleKey = "tuning." + id, unit = unit, min = min, max = max, step = step, getter = get, setter = set };

        private static List<TuningParameter> Build()
        {
            return new List<TuningParameter>
            {
                P("ride_height", TuningGroup.Suspension, "mm", -0.06f, 0.03f, 0.005f, s => s.rideHeightOffset, (s, v) => s.rideHeightOffset = v),
                P("spring_front", TuningGroup.Suspension, "x", 0.6f, 1.8f, 0.05f, s => s.springFront, (s, v) => s.springFront = v),
                P("spring_rear", TuningGroup.Suspension, "x", 0.6f, 1.8f, 0.05f, s => s.springRear, (s, v) => s.springRear = v),
                P("damper_front", TuningGroup.Suspension, "x", 0.5f, 1.8f, 0.05f, s => s.damperFront, (s, v) => s.damperFront = v),
                P("damper_rear", TuningGroup.Suspension, "x", 0.5f, 1.8f, 0.05f, s => s.damperRear, (s, v) => s.damperRear = v),
                P("arb_front", TuningGroup.Suspension, "x", 0f, 2.5f, 0.05f, s => s.antiRollFront, (s, v) => s.antiRollFront = v),
                P("arb_rear", TuningGroup.Suspension, "x", 0f, 2.5f, 0.05f, s => s.antiRollRear, (s, v) => s.antiRollRear = v),

                P("camber_front", TuningGroup.Alignment, "deg", -7f, 1f, 0.1f, s => s.camberFrontDeg, (s, v) => s.camberFrontDeg = v),
                P("camber_rear", TuningGroup.Alignment, "deg", -4f, 1f, 0.1f, s => s.camberRearDeg, (s, v) => s.camberRearDeg = v),
                P("toe_front", TuningGroup.Alignment, "deg", -1f, 1f, 0.05f, s => s.toeFrontDeg, (s, v) => s.toeFrontDeg = v),
                P("toe_rear", TuningGroup.Alignment, "deg", -1f, 1f, 0.05f, s => s.toeRearDeg, (s, v) => s.toeRearDeg = v),
                P("steering_lock", TuningGroup.Alignment, "deg", 30f, 68f, 1f, s => s.steeringLockDeg, (s, v) => s.steeringLockDeg = v),
                P("ackermann", TuningGroup.Alignment, "%", 0f, 1f, 0.05f, s => s.ackermann, (s, v) => s.ackermann = v),

                P("pressure_front", TuningGroup.Tires, "bar", 1.4f, 3f, 0.05f, s => s.pressureFrontBar, (s, v) => s.pressureFrontBar = v),
                P("pressure_rear", TuningGroup.Tires, "bar", 1.4f, 3f, 0.05f, s => s.pressureRearBar, (s, v) => s.pressureRearBar = v),
                P("tire_width_front", TuningGroup.Tires, "mm", 155f, 295f, 10f, s => s.tireWidthFront, (s, v) => s.tireWidthFront = v),
                P("tire_width_rear", TuningGroup.Tires, "mm", 155f, 315f, 10f, s => s.tireWidthRear, (s, v) => s.tireWidthRear = v),
                P("tire_aspect_front", TuningGroup.Tires, "%", 30f, 80f, 5f, s => s.tireAspectFront, (s, v) => s.tireAspectFront = v),
                P("tire_aspect_rear", TuningGroup.Tires, "%", 30f, 80f, 5f, s => s.tireAspectRear, (s, v) => s.tireAspectRear = v),
                P("used_tire_wear", TuningGroup.Tires, "%", 0f, 0.8f, 0.05f, s => s.usedTireWear, (s, v) => s.usedTireWear = v),
                P("track_front", TuningGroup.Suspension, "mm", 0f, 0.09f, 0.005f, s => s.trackOffsetFront, (s, v) => s.trackOffsetFront = v),
                P("track_rear", TuningGroup.Suspension, "mm", 0f, 0.09f, 0.005f, s => s.trackOffsetRear, (s, v) => s.trackOffsetRear = v),

                P("power", TuningGroup.Engine, "x", 0.7f, 2f, 0.05f, s => s.powerScale, (s, v) => s.powerScale = v),
                P("limiter", TuningGroup.Engine, "rpm", 0f, 1200f, 50f, s => s.limiterOffsetRpm, (s, v) => s.limiterOffsetRpm = v),
                P("flywheel", TuningGroup.Engine, "x", 0.5f, 1.5f, 0.05f, s => s.flywheelScale, (s, v) => s.flywheelScale = v),

                P("final_drive", TuningGroup.Gearbox, "", 0f, 6f, 0.05f, s => s.finalDrive, (s, v) => s.finalDrive = v <= 0.01f ? 0f : MathUtil.Max(2.5f, v)),
                P("shift_time", TuningGroup.Gearbox, "s", 0f, 0.5f, 0.01f, s => s.shiftTimeSec, (s, v) => s.shiftTimeSec = v <= 0.001f ? 0f : MathUtil.Max(0.05f, v)),

                P("diff_preload", TuningGroup.Differential, "Nm", 0f, 500f, 10f, s => s.diffPreloadNm, (s, v) => s.diffPreloadNm = v),
                P("diff_power", TuningGroup.Differential, "%", 0f, 1f, 0.05f, s => s.diffPowerLock, (s, v) => s.diffPowerLock = v),
                P("diff_coast", TuningGroup.Differential, "%", 0f, 1f, 0.05f, s => s.diffCoastLock, (s, v) => s.diffCoastLock = v),

                P("brake_force", TuningGroup.Brakes, "x", 0.5f, 1.5f, 0.05f, s => s.brakeForceScale, (s, v) => s.brakeForceScale = v),
                P("brake_bias", TuningGroup.Brakes, "%", 0.45f, 0.85f, 0.01f, s => s.brakeFrontBias, (s, v) => s.brakeFrontBias = v),
                P("handbrake", TuningGroup.Brakes, "x", 0.5f, 2.5f, 0.05f, s => s.handbrakeScale, (s, v) => s.handbrakeScale = v),

                P("mass_offset", TuningGroup.Weight, "kg", -250f, 200f, 5f, s => s.massOffsetKg, (s, v) => s.massOffsetKg = v),
                P("front_weight", TuningGroup.Weight, "%", 0.42f, 0.6f, 0.01f, s => s.frontWeightFraction, (s, v) => s.frontWeightFraction = v),

                P("downforce", TuningGroup.Aero, "x", 0f, 4f, 0.1f, s => s.downforceScale, (s, v) => s.downforceScale = v),
            };
        }

        public static IEnumerable<TuningParameter> InGroup(TuningGroup g)
        {
            foreach (var p in Parameters) if (p.group == g) yield return p;
        }

        public static TuningParameter Find(string id)
        {
            foreach (var p in Parameters) if (p.id == id) return p;
            return null;
        }
    }

    /// <summary>Ready-made setups so players can start from something sensible.</summary>
    public static class TuningPresets
    {
        public static readonly string[] Ids = { "stock", "street_drift", "pro_drift", "grip", "winter", "korch" };

        public static VehicleTuningState Create(string id, string vehicleId = "placeholder_rwd")
        {
            var t = VehicleTuningState.Stock(vehicleId);
            t.presetName = id;
            switch (id)
            {
                case "street_drift":
                    t.rideHeightOffset = -0.02f; t.springFront = 1.2f; t.springRear = 1.1f; t.steeringLockDeg = 55f;
                    t.diffType = DifferentialType.Lsd; t.diffPreloadNm = 180f; t.diffPowerLock = 0.8f; t.diffCoastLock = 0.5f;
                    t.powerScale = 1.15f; t.camberFrontDeg = -3.5f; t.pressureRearBar = 2.5f;
                    break;
                case "pro_drift":
                    t.rideHeightOffset = -0.035f; t.springFront = 1.45f; t.springRear = 1.3f; t.damperFront = 1.3f; t.damperRear = 1.2f;
                    t.antiRollFront = 1.4f; t.antiRollRear = 0.8f; t.steeringLockDeg = 64f; t.ackermann = 0.1f;
                    t.diffType = DifferentialType.Locked; t.powerScale = 1.55f; t.limiterOffsetRpm = 600f; t.flywheelScale = 0.7f;
                    t.camberFrontDeg = -5f; t.camberRearDeg = -0.5f; t.toeRearDeg = 0.2f; t.massOffsetKg = -120f;
                    t.compoundRear = TireCompound.Sport; t.compoundFront = TireCompound.DriftSemiSlick; t.pressureRearBar = 2.6f;
                    t.handbrakeScale = 1.8f; t.finalDrive = 4.4f;
                    break;
                case "grip":
                    t.rideHeightOffset = -0.025f; t.springFront = 1.4f; t.springRear = 1.4f; t.antiRollFront = 1.3f; t.antiRollRear = 1.1f;
                    t.steeringLockDeg = 40f; t.ackermann = 0.7f; t.diffPowerLock = 0.4f; t.diffCoastLock = 0.2f;
                    t.compoundFront = TireCompound.DriftSemiSlick; t.compoundRear = TireCompound.DriftSemiSlick; t.downforceScale = 2.5f;
                    break;
                case "korch":
                    return KorchBuilder.Apply(t);
                case "winter":
                    t.rideHeightOffset = 0.02f; t.springFront = 0.8f; t.springRear = 0.8f; t.compoundFront = TireCompound.Studded;
                    t.compoundRear = TireCompound.Studded; t.diffType = DifferentialType.Lsd; t.diffPowerLock = 0.5f; t.powerScale = 0.9f;
                    break;
            }
            return t;
        }
    }
}

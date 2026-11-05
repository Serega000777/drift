using System;

namespace DriftCareer.Core.Vehicle
{
    public enum DamageMode { Off = 0, VisualOnly = 1, Full = 2 }

    [Serializable]
    public sealed class HealthSettings
    {
        public DamageMode damage = DamageMode.Full;
        /// <summary>0 = no wear, 1 = normal, 3 = fast (arcade sessions).</summary>
        public float tireWearMultiplier = 1f;
        public bool blowouts = true;
        public float ambientTempC = 20f;

        public HealthSettings Clone() => (HealthSettings)MemberwiseClone();
    }

    public enum DamageZone { Front = 0, Rear = 1, Left = 2, Right = 3 }

    /// <summary>Thermal/wear/pressure state of one tire.</summary>
    public sealed class TireCondition
    {
        public float temperatureC = 20f;
        /// <summary>0 = new, 1 = worn to the cords (bursts).</summary>
        public float wear;
        /// <summary>1 = nominal pressure, 0 = fully deflated.</summary>
        public float pressure = 1f;
        public float leakRatePerSec;
        public bool blown;
        public float overheatTimer;

        public float Deflation => 1f - MathUtil.Clamp01(pressure);

        public void Reset(float ambient)
        {
            temperatureC = ambient;
            wear = 0f;
            pressure = 1f;
            leakRatePerSec = 0f;
            blown = false;
            overheatTimer = 0f;
        }
    }

    /// <summary>
    /// Tire temperature, wear, punctures/blowouts and body damage. Produces <see cref="WheelModifiers"/>
    /// and engine torque scale for <see cref="VehicleSimulation"/>; the resulting pull/instability after a
    /// blowout or bent suspension emerges from tire forces - nothing steers or rotates the car directly.
    /// Deterministic (no randomness) so replays/tests are reproducible.
    /// </summary>
    public sealed class VehicleHealth
    {
        // Thermal model: dT/dt = P*HeatGain - (T - ambient) * (CoolBase + CoolPerMs * speed)
        public const float HeatGain = 1.7e-4f;
        public const float CoolBase = 0.08f;
        public const float CoolPerMs = 0.004f;
        /// <summary>Wear per joule of slip energy at multiplier 1 (~4 min of a hard donut to wear out).</summary>
        public const float WearPerJoule = 7e-8f;
        public const float OverheatTempC = 185f;
        public const float OverheatBurstSeconds = 5f;
        /// <summary>Sidewall height that collapses when deflated (m).</summary>
        public const float SidewallDrop = 0.075f;
        public const float ImpactThreshold = 1800f;
        public const float ImpactToDamage = 1f / 28000f;
        public const float PunctureImpulse = 9000f;
        public const float BlowoutImpulse = 15000f;

        private static readonly Curve TempGrip = Curve.FromPairs(0f, 0.85f, 30f, 0.9f, 60f, 0.96f, 85f, 1f, 115f, 1f, 145f, 0.94f, 175f, 0.85f, 210f, 0.75f);

        public readonly TireCondition[] tires = { new TireCondition(), new TireCondition(), new TireCondition(), new TireCondition() };
        public readonly float[] zoneDamage = new float[4];
        /// <summary>Damage at each wheel corner (suspension/alignment).</summary>
        public readonly float[] cornerDamage = new float[4];
        private readonly float[] _toeSign = { 1f, -1f, 1f, -1f };

        public HealthSettings Settings { get; set; }

        public event Action<int> TireBlown;
        public event Action<int> TirePunctured;
        public event Action<DamageZone, float> Damaged;

        public VehicleHealth(HealthSettings settings = null)
        {
            Settings = settings ?? new HealthSettings();
            Repair();
        }

        /// <summary>0 = wrecked, 1 = pristine (HUD).</summary>
        public float BodyIntegrity
        {
            get
            {
                float sum = 0f;
                foreach (var d in zoneDamage) sum += d;
                return 1f - MathUtil.Clamp01(sum / 4f);
            }
        }

        public void Repair()
        {
            Array.Clear(zoneDamage, 0, 4);
            Array.Clear(cornerDamage, 0, 4);
            RepairTires();
        }

        public void RepairTires()
        {
            foreach (var t in tires) t.Reset(Settings.ambientTempC);
        }

        public static float TemperatureGripFactor(float tempC) => TempGrip.Evaluate(tempC);

        public void Update(float dt, VehicleSimulation sim, float speedMs)
        {
            if (!(dt > 0f) || sim == null) return;
            float cool = CoolBase + CoolPerMs * MathUtil.Max(0f, speedMs);
            for (int i = 0; i < 4; i++)
            {
                var t = tires[i];
                var w = sim.Wheel(i);
                float power = MathUtil.Finite(w.slipPower);
                t.temperatureC += (power * HeatGain - (t.temperatureC - Settings.ambientTempC) * cool) * dt;
                t.temperatureC = MathUtil.Clamp(t.temperatureC, -40f, 260f);
                if (!t.blown && Settings.tireWearMultiplier > 0f)
                    t.wear = MathUtil.Clamp01(t.wear + power * WearPerJoule * Settings.tireWearMultiplier * dt);

                if (t.leakRatePerSec > 0f && t.pressure > 0f)
                    t.pressure = MathUtil.Max(0f, t.pressure - t.leakRatePerSec * dt);

                if (!Settings.blowouts || t.blown) continue;
                if (t.temperatureC > OverheatTempC) t.overheatTimer += dt;
                else t.overheatTimer = MathUtil.Max(0f, t.overheatTimer - dt * 0.5f);
                if (t.wear >= 1f || t.overheatTimer >= OverheatBurstSeconds) Blow(i);
            }
            ApplyModifiers(sim);
        }

        public void Blow(int wheel)
        {
            var t = tires[wheel];
            if (t.blown) return;
            t.blown = true;
            t.pressure = 0f;
            t.leakRatePerSec = 0f;
            TireBlown?.Invoke(wheel);
        }

        public void Puncture(int wheel, float leakRatePerSec)
        {
            var t = tires[wheel];
            if (t.blown || t.leakRatePerSec > 0f) return;
            t.leakRatePerSec = MathUtil.Max(0.01f, leakRatePerSec);
            TirePunctured?.Invoke(wheel);
        }

        /// <summary>
        /// Registers a collision. Local point uses chassis axes (x right, z forward) relative to the CG.
        /// </summary>
        public void ReportImpact(float localX, float localZ, float impulse, VehiclePhysicsProfile p)
        {
            if (Settings.damage == DamageMode.Off || !(impulse > ImpactThreshold)) return;
            float amount = MathUtil.Clamp((impulse - ImpactThreshold) * ImpactToDamage, 0f, 0.6f);
            DamageZone zone;
            float halfL = p.chassis.length * 0.5f, halfW = p.chassis.width * 0.5f;
            if (MathUtil.Abs(localZ) / halfL > MathUtil.Abs(localX) / halfW) zone = localZ > 0f ? DamageZone.Front : DamageZone.Rear;
            else zone = localX > 0f ? DamageZone.Right : DamageZone.Left;
            zoneDamage[(int)zone] = MathUtil.Clamp01(zoneDamage[(int)zone] + amount);
            Damaged?.Invoke(zone, amount);

            // Corner damage for wheels close to the impact.
            for (int i = 0; i < 4; i++)
            {
                float wx = (VehicleSimulation.IsLeft(i) ? -0.5f : 0.5f) * (VehicleSimulation.IsFront(i) ? p.front.trackWidth : p.rear.trackWidth);
                float wz = VehicleSimulation.IsFront(i) ? p.CgToFront : -p.CgToRear;
                float dist = MathUtil.Sqrt(MathUtil.Sqr(localX - wx) + MathUtil.Sqr(localZ - wz));
                float proximity = MathUtil.Clamp01(1f - dist / 1.1f);
                if (proximity <= 0f) continue;
                cornerDamage[i] = MathUtil.Clamp01(cornerDamage[i] + amount * proximity * 1.5f);
                if (!Settings.blowouts) continue;
                float cornerImpulse = impulse * proximity;
                if (cornerImpulse > BlowoutImpulse) Blow(i);
                else if (cornerImpulse > PunctureImpulse) Puncture(i, 0.2f);
            }
        }

        public void ApplyModifiers(VehicleSimulation sim)
        {
            bool mechanical = Settings.damage == DamageMode.Full;
            for (int i = 0; i < 4; i++)
            {
                var t = tires[i];
                var m = WheelModifiers.None;
                float d = t.Deflation;
                float radius = sim.Axle(i).wheelRadius;
                m.radiusOffset = -d * MathUtil.Min(SidewallDrop, radius * 0.3f);
                m.gripScale = MathUtil.Lerp(1f, 0.5f, d) * TemperatureGripFactor(t.temperatureC);
                m.gripScale *= 1f - 0.3f * MathUtil.Clamp01((t.wear - 0.7f) / 0.3f);
                m.rollingResistanceScale = MathUtil.Lerp(1f, 12f, d);
                m.peakSlipScale = MathUtil.Lerp(1f, 2.5f, d);
                if (mechanical)
                {
                    float c = cornerDamage[i];
                    m.toeOffsetRad = c * 3.5f * MathUtil.Deg2Rad * _toeSign[i];
                    m.gripScale *= 1f - 0.15f * c;
                    m.springScale = 1f - 0.35f * c;
                }
                sim.SetWheelModifiers(i, m);
            }
            sim.EngineTorqueScale = mechanical ? 1f - 0.45f * zoneDamage[(int)DamageZone.Front] : 1f;
        }
    }
}

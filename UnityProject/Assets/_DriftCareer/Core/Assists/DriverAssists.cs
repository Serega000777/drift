using System;
using DriftCareer.Core.Vehicle;

namespace DriftCareer.Core.Assists
{
    public enum AssistPreset { Simulation = 0, Balanced = 1, Assisted = 2 }

    /// <summary>
    /// Raw, device-independent driver commands (after device mapping, before assists).
    /// Produced by keyboard, gamepad, touch or wheel input sources.
    /// </summary>
    [Serializable]
    public struct DriverInput
    {
        public float steer;
        public float throttle;
        public float brake;
        public float handbrake;
        public float clutch;
        public bool shiftUp;
        public bool shiftDown;
        /// <summary>True for digital steering (keys/buttons) - enables keyboard ramping.</summary>
        public bool digitalSteer;

        public static DriverInput None => default;
    }

    /// <summary>
    /// Tunable assist parameters. Assists transform input only - they never move or rotate the car.
    /// </summary>
    [Serializable]
    public sealed class AssistSettings
    {
        public AssistPreset preset;
        /// <summary>Steering input rate limit (full lock fractions per second). 0 = unlimited.</summary>
        public float steerRate = 6f;
        /// <summary>Extra return-to-center rate for digital input.</summary>
        public float steerReturnRate = 8f;
        /// <summary>Fraction of steering lock removed at high speed (grip driving stability).</summary>
        public float speedSteerReduction = 0.35f;
        public float speedSteerReductionAtMs = 40f;
        /// <summary>0..1 strength of automatic countersteer toward the direction of travel.</summary>
        public float countersteer;
        /// <summary>Max fraction of lock the countersteer assist may add.</summary>
        public float countersteerMax = 0.8f;
        /// <summary>Throttle reduction when the rear spins far beyond the target slip (spin protection).</summary>
        public float spinProtection;
        public float spinProtectionAngleDeg = 65f;
        /// <summary>Pulse brake when front wheels lock (ABS-like).</summary>
        public bool antiLock;
        public bool autoClutch = true;
        public TransmissionMode transmission = TransmissionMode.Automatic;

        public AssistSettings Clone() => (AssistSettings)MemberwiseClone();

        public static AssistSettings For(AssistPreset preset)
        {
            switch (preset)
            {
                case AssistPreset.Simulation:
                    return new AssistSettings
                    {
                        preset = preset, steerRate = 0f, steerReturnRate = 0f, speedSteerReduction = 0f, countersteer = 0f,
                        spinProtection = 0f, antiLock = false, autoClutch = true, transmission = TransmissionMode.Manual
                    };
                case AssistPreset.Balanced:
                    return new AssistSettings
                    {
                        preset = preset, steerRate = 5f, steerReturnRate = 7f, speedSteerReduction = 0.25f, countersteer = 0.35f,
                        countersteerMax = 0.6f, spinProtection = 0.3f, spinProtectionAngleDeg = 75f, antiLock = true,
                        autoClutch = true, transmission = TransmissionMode.Automatic
                    };
                default:
                    return new AssistSettings
                    {
                        preset = AssistPreset.Assisted, steerRate = 3.5f, steerReturnRate = 5f, speedSteerReduction = 0.4f,
                        countersteer = 0.7f, countersteerMax = 0.85f, spinProtection = 0.7f, spinProtectionAngleDeg = 60f,
                        antiLock = true, autoClutch = true, transmission = TransmissionMode.Automatic
                    };
            }
        }
    }

    /// <summary>
    /// raw DriverInput -> smoothing -> assists (countersteer, spin protection, ABS) -> VehicleControls.
    /// Stateless except for steering/brake smoothing. All effects are visible to the player (HUD badge).
    /// </summary>
    public sealed class DriverAssistPipeline
    {
        private float _steer;
        private float _absPhase;

        public AssistSettings Settings { get; set; }

        /// <summary>Last countersteer contribution in [-1,1] for HUD/debug.</summary>
        public float LastCountersteer { get; private set; }

        /// <summary>Last throttle reduction applied by spin protection (0..1).</summary>
        public float LastThrottleCut { get; private set; }

        public DriverAssistPipeline(AssistSettings settings)
        {
            Settings = settings ?? AssistSettings.For(AssistPreset.Balanced);
        }

        public void Reset()
        {
            _steer = 0f;
            _absPhase = 0f;
            LastCountersteer = 0f;
            LastThrottleCut = 0f;
        }

        public VehicleControls Process(in DriverInput input, VehicleTelemetry telemetry, float maxSteerAngleDeg, float dt)
        {
            var s = Settings;
            float speed = telemetry != null ? telemetry.speedMs : 0f;
            float drift = telemetry != null ? telemetry.driftAngleDeg : 0f;

            // 1. Steering: speed sensitive range + rate limiting.
            float rawSteer = MathUtil.Clamp(MathUtil.Finite(input.steer), -1f, 1f);
            float range = 1f - s.speedSteerReduction * MathUtil.Clamp01(speed / MathUtil.Max(1f, s.speedSteerReductionAtMs));
            // Do not reduce lock while sliding - the driver needs it to catch the car.
            range = MathUtil.Lerp(range, 1f, MathUtil.Clamp01((MathUtil.Abs(drift) - 8f) / 15f));
            float target = rawSteer * range;

            // 2. Countersteer: blend toward the wheel angle that points along the velocity.
            LastCountersteer = 0f;
            if (s.countersteer > 0f && speed > 3f && MathUtil.Abs(drift) > 4f && MathUtil.Abs(drift) < 120f)
            {
                float alignSteer = MathUtil.Clamp(-drift / MathUtil.Max(1f, maxSteerAngleDeg), -s.countersteerMax, s.countersteerMax);
                // Assist fades out when the player actively steers against it, so it never fights intent.
                float userAuthority = MathUtil.Abs(rawSteer);
                float sameDirection = MathUtil.Sign(rawSteer) == MathUtil.Sign(alignSteer) ? 1f : 0f;
                float w = s.countersteer * MathUtil.Lerp(1f, sameDirection, userAuthority) * MathUtil.Clamp01((MathUtil.Abs(drift) - 4f) / 10f);
                float blended = MathUtil.LerpUnclamped(target, alignSteer, w);
                LastCountersteer = blended - target;
                target = blended;
            }

            if (s.steerRate > 0f)
            {
                bool returning = MathUtil.Abs(target) < MathUtil.Abs(_steer) && MathUtil.Sign(target) == MathUtil.Sign(_steer);
                float rate = input.digitalSteer && returning ? MathUtil.Max(s.steerRate, s.steerReturnRate) : s.steerRate;
                if (!input.digitalSteer) rate *= 2.5f; // analog sticks/wheels already provide smooth input
                _steer = MathUtil.MoveTowards(_steer, target, rate * dt);
            }
            else _steer = target;

            // 3. Throttle: spin protection when the car is about to swap ends.
            float throttle = MathUtil.Clamp01(MathUtil.Finite(input.throttle));
            LastThrottleCut = 0f;
            if (s.spinProtection > 0f && speed > 2f)
            {
                float over = (MathUtil.Abs(drift) - s.spinProtectionAngleDeg) / 25f;
                if (over > 0f)
                {
                    float cut = MathUtil.Clamp01(over) * s.spinProtection;
                    LastThrottleCut = cut;
                    throttle *= 1f - cut;
                }
            }

            // 4. Brake: simple ABS-like pulsing when the fronts lock at speed.
            float brake = MathUtil.Clamp01(MathUtil.Finite(input.brake));
            if (s.antiLock && telemetry != null && brake > 0.3f && speed > 4f)
            {
                float frontSlip = 0.5f * (telemetry.wheels[0].slipRatio + telemetry.wheels[1].slipRatio);
                if (frontSlip < -0.2f)
                {
                    _absPhase += dt * 14f;
                    brake *= _absPhase % 1f < 0.5f ? 0.45f : 0.9f;
                }
            }

            return new VehicleControls
            {
                steer = MathUtil.Clamp(_steer, -1f, 1f),
                throttle = throttle,
                brake = brake,
                handbrake = MathUtil.Clamp01(MathUtil.Finite(input.handbrake)),
                clutchPedal = MathUtil.Clamp01(MathUtil.Finite(input.clutch)),
                shiftUp = input.shiftUp,
                shiftDown = input.shiftDown,
                autoClutch = s.autoClutch,
                transmission = s.transmission
            };
        }
    }
}

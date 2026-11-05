using System;
using DriftCareer.Core.Surfaces;

namespace DriftCareer.Core.Vehicle
{
    /// <summary>Final commands consumed by the simulation (after input mapping and assists).</summary>
    [Serializable]
    public struct VehicleControls
    {
        /// <summary>-1 (full left) .. +1 (full right).</summary>
        public float steer;
        public float throttle;
        public float brake;
        public float handbrake;
        /// <summary>Clutch pedal 0 (released, engaged) .. 1 (pressed). Used only with manual clutch.</summary>
        public float clutchPedal;
        public bool shiftUp;
        public bool shiftDown;
        /// <summary>When true the simulation applies the auto-clutch logic.</summary>
        public bool autoClutch;
        public TransmissionMode transmission;

        public static VehicleControls Neutral => new VehicleControls { autoClutch = true, transmission = TransmissionMode.Automatic };
    }

    /// <summary>Per-wheel contact data provided by the host (Unity raycasts or the headless harness).</summary>
    public struct WheelContact
    {
        public bool grounded;
        /// <summary>Normal load (N).</summary>
        public float normalLoad;
        /// <summary>Ground-relative contact velocity along the steered wheel forward axis (m/s).</summary>
        public float longitudinalVelocity;
        /// <summary>Ground-relative contact velocity along the steered wheel right axis (m/s).</summary>
        public float lateralVelocity;
        public SurfaceGrip grip;
        /// <summary>Share of chassis mass carried by this wheel (kg), for the no-overshoot clamp.</summary>
        public float massShare;
    }

    public struct WheelOutput
    {
        public float fx;
        public float fy;
        public float angularVelocity;
        public float steerAngleRad;
        public float slipRatio;
        public float slipAngleRad;
        public float gripUsage;
        public bool sliding;
        public float normalLoad;
        public bool grounded;
        /// <summary>Frictional power dissipated in the contact patch (W). Drives smoke, heat and wear.</summary>
        public float slipPower;
    }

    public enum WheelId { FrontLeft = 0, FrontRight = 1, RearLeft = 2, RearRight = 3 }

    /// <summary>
    /// Per-wheel physical modifiers set by damage / tire condition / tuning (e.g. a blown tire).
    /// They change forces only; any trajectory change is an emergent result of those forces.
    /// </summary>
    public struct WheelModifiers
    {
        public float gripScale;
        /// <summary>Added to the wheel radius (negative for a deflated tire running on the rim).</summary>
        public float radiusOffset;
        public float rollingResistanceScale;
        public float peakSlipScale;
        /// <summary>Static toe angle offset (rad). Positive = wheel points right.</summary>
        public float toeOffsetRad;
        /// <summary>Spring rate scale read by the host suspension (damaged suspension).</summary>
        public float springScale;

        public static WheelModifiers None => new WheelModifiers
        {
            gripScale = 1f, radiusOffset = 0f, rollingResistanceScale = 1f, peakSlipScale = 1f, toeOffsetRad = 0f, springScale = 1f
        };
    }

    /// <summary>
    /// Engine-free vehicle dynamics core (RWD / FWD / AWD). Host responsibilities: rigid body integration,
    /// wheel raycasts and suspension loads. This class handles steering, engine, clutch, gearbox,
    /// differential, brakes and tire forces with a small sequential-impulse drivetrain solver that
    /// is unconditionally stable for stiff couplings (locked clutch/diff, locked brakes).
    /// It never rotates the car directly: it only produces contact forces.
    /// </summary>
    public sealed class VehicleSimulation
    {
        public const int WheelCount = 4;
        private const int SolverIterations = 8;

        private VehiclePhysicsProfile _p;
        private readonly float[] _omega = new float[WheelCount];
        private readonly float[] _steer = new float[WheelCount];
        private readonly WheelOutput[] _out = new WheelOutput[WheelCount];
        private readonly float[] _iEff = new float[WheelCount];
        private readonly float[] _fx0 = new float[WheelCount];
        private readonly float[] _brakeLimit = new float[WheelCount];
        private readonly float[] _brakeAccum = new float[WheelCount];
        private readonly float[] _fxSum = new float[WheelCount];
        private readonly float[] _fySum = new float[WheelCount];
        private readonly float[] _driveWeight = new float[WheelCount];
        private readonly WheelModifiers[] _mods = { WheelModifiers.None, WheelModifiers.None, WheelModifiers.None, WheelModifiers.None };

        private float _engineOmega;
        private float _centerSteer;
        private float _shiftTimer;
        private float _shiftCooldown;
        private float _reverseHoldTimer;
        private float _clutchEngagement;
        private float _lastAxleTorque;
        private float _forwardSpeed;
        private VehicleControls _controls;

        public VehicleSimulation(VehiclePhysicsProfile profile)
        {
            SetProfile(profile);
            Reset();
        }

        public VehiclePhysicsProfile Profile => _p;
        public int Gear { get; private set; }
        public bool EngineRunning { get; set; } = true;
        public float EngineRpm => _engineOmega * MathUtil.RadPerSecToRpm;
        public float ClutchEngagement => _clutchEngagement;
        public float CenterSteerAngleRad => _centerSteer;
        public bool IsShifting => _shiftTimer > 0f;
        public float DriveTorque => _lastAxleTorque;
        public VehicleControls AppliedControls => _controls;
        public int GearCount => _p.transmission.gearRatios.Length;

        public static bool IsFront(int i) => i < 2;
        public static bool IsLeft(int i) => (i & 1) == 0;
        /// <summary>Share of the carrier speed/torque that goes to wheel i (sums to 1).</summary>
        public float DriveWeight(int i) => _driveWeight[i];

        public bool IsDriven(int i) => _driveWeight[i] > 0f;

        private void UpdateDriveWeights()
        {
            var d = _p.drivetrain;
            float front;
            switch (d.layout)
            {
                case DriveLayout.Fwd: front = 1f; break;
                case DriveLayout.Awd: front = MathUtil.Clamp(d.awdFrontSplit, 0.05f, 0.95f); break;
                default: front = 0f; break;
            }
            _driveWeight[0] = _driveWeight[1] = front * 0.5f;
            _driveWeight[2] = _driveWeight[3] = (1f - front) * 0.5f;
        }

        private float CarrierOmega()
        {
            float c = 0f;
            for (int i = 0; i < WheelCount; i++) c += _driveWeight[i] * _omega[i];
            return c;
        }

        private float DrivenRadius()
        {
            float r = 0f;
            for (int i = 0; i < WheelCount; i++) r += _driveWeight[i] * EffectiveRadius(i);
            return MathUtil.Max(0.1f, r);
        }

        public AxleSpec Axle(int i) => IsFront(i) ? _p.front : _p.rear;

        public float WheelSteerAngle(int i) => _steer[i];

        /// <summary>Engine output multiplier (damage). 1 = healthy.</summary>
        public float EngineTorqueScale { get; set; } = 1f;

        public WheelModifiers GetWheelModifiers(int i) => _mods[i];

        public void SetWheelModifiers(int i, WheelModifiers m)
        {
            m.gripScale = MathUtil.Clamp(MathUtil.Finite(m.gripScale, 1f), 0.05f, 2f);
            m.rollingResistanceScale = MathUtil.Clamp(MathUtil.Finite(m.rollingResistanceScale, 1f), 0f, 100f);
            m.peakSlipScale = MathUtil.Clamp(MathUtil.Finite(m.peakSlipScale, 1f), 0.3f, 5f);
            m.toeOffsetRad = MathUtil.Clamp(MathUtil.Finite(m.toeOffsetRad), -0.2f, 0.2f);
            m.springScale = MathUtil.Clamp(MathUtil.Finite(m.springScale, 1f), 0.1f, 3f);
            m.radiusOffset = MathUtil.Clamp(MathUtil.Finite(m.radiusOffset), -0.2f, 0.1f);
            _mods[i] = m;
        }

        public void ClearWheelModifiers()
        {
            for (int i = 0; i < WheelCount; i++) _mods[i] = WheelModifiers.None;
            EngineTorqueScale = 1f;
        }

        /// <summary>Effective rolling radius including modifiers (deflated tires are smaller).</summary>
        public float EffectiveRadius(int i) => MathUtil.Max(0.1f, Axle(i).wheelRadius + _mods[i].radiusOffset);

        private SurfaceGrip ModifiedGrip(int i, in SurfaceGrip g)
        {
            var m = _mods[i];
            var r = g;
            r.longitudinal *= m.gripScale;
            r.lateral *= m.gripScale;
            r.rollingResistanceScale *= m.rollingResistanceScale;
            r.peakSlipScale = (g.peakSlipScale > 0f ? g.peakSlipScale : 1f) * m.peakSlipScale;
            return r;
        }

        public WheelOutput Wheel(int i) => _out[i];

        public void SetProfile(VehiclePhysicsProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            var errors = profile.Validate();
            if (errors.Count > 0) throw new ArgumentException("Invalid vehicle profile: " + string.Join("; ", errors));
            _p = profile;
            UpdateDriveWeights();
            Gear = MathUtil.Clamp(Gear, -1, profile.transmission.gearRatios.Length);
        }

        public void Reset()
        {
            Array.Clear(_omega, 0, WheelCount);
            Array.Clear(_steer, 0, WheelCount);
            Array.Clear(_out, 0, WheelCount);
            _engineOmega = EngineRunning ? _p.engine.idleRpm * MathUtil.RpmToRadPerSec : 0f;
            _centerSteer = 0f;
            _shiftTimer = 0f;
            _shiftCooldown = 0f;
            _reverseHoldTimer = 0f;
            _clutchEngagement = 0f;
            _lastAxleTorque = 0f;
            _forwardSpeed = 0f;
            Gear = 0;
            _controls = VehicleControls.Neutral;
        }

        /// <summary>Total ratio engine->wheel for the current gear (negative in reverse, 0 in neutral).</summary>
        public float TotalRatio
        {
            get
            {
                var t = _p.transmission;
                if (Gear == 0) return 0f;
                if (Gear < 0) return -t.reverseRatio * t.finalDrive;
                return t.gearRatios[Gear - 1] * t.finalDrive;
            }
        }

        /// <summary>
        /// Phase 1: steering and gearbox logic. Call before computing contact velocities,
        /// because they depend on the steered wheel frames (<see cref="WheelSteerAngle"/>).
        /// </summary>
        public void Prepare(float dt, VehicleControls c)
        {
            if (!(dt > 0f)) return;
            c.steer = MathUtil.Clamp(MathUtil.Finite(c.steer), -1f, 1f);
            c.throttle = MathUtil.Clamp01(MathUtil.Finite(c.throttle));
            c.brake = MathUtil.Clamp01(MathUtil.Finite(c.brake));
            c.handbrake = MathUtil.Clamp01(MathUtil.Finite(c.handbrake));
            c.clutchPedal = MathUtil.Clamp01(MathUtil.Finite(c.clutchPedal));

            // Steering with rack speed limit.
            float maxAngle = _p.steering.maxAngleDeg * MathUtil.Deg2Rad;
            float target = c.steer * maxAngle;
            _centerSteer = MathUtil.MoveTowards(_centerSteer, target, _p.steering.rackSpeedDegPerSec * MathUtil.Deg2Rad * dt);
            SteeringModel.WheelAngles(_p.steering, _centerSteer, _p.chassis.wheelbase, _p.front.trackWidth, out _steer[0], out _steer[1]);
            _steer[2] = _steer[3] = 0f;
            for (int i = 0; i < WheelCount; i++)
            {
                // Toe-in: left wheel points right (+), right wheel points left (-).
                float toe = Axle(i).staticToeDeg * MathUtil.Deg2Rad * (IsLeft(i) ? 1f : -1f);
                _steer[i] += toe + _mods[i].toeOffsetRad;
            }

            UpdateGearbox(dt, ref c);
            _controls = c;
            UpdateClutch(dt, c);
        }

        private void UpdateGearbox(float dt, ref VehicleControls c)
        {
            var t = _p.transmission;
            int maxGear = t.gearRatios.Length;
            if (_shiftTimer > 0f) _shiftTimer = MathUtil.Max(0f, _shiftTimer - dt);
            if (_shiftCooldown > 0f) _shiftCooldown = MathUtil.Max(0f, _shiftCooldown - dt);

            if (c.transmission == TransmissionMode.Manual)
            {
                if (c.shiftUp && Gear < maxGear) ShiftTo(Gear + 1);
                else if (c.shiftDown && Gear > -1) ShiftTo(Gear - 1);
                return;
            }

            // Automatic: neutral -> first on throttle, reverse by holding brake at standstill.
            bool nearlyStopped = MathUtil.Abs(_forwardSpeed) < 0.8f;
            if (Gear == 0 && c.throttle > 0.05f && c.brake < 0.3f) ShiftTo(1);

            // Raw pedals (before the reverse swap below): holding brake at standstill selects reverse,
            // holding throttle at standstill while in reverse selects first gear.
            bool wantReverse = Gear >= 0 && nearlyStopped && c.brake > 0.3f && c.throttle < 0.05f;
            bool wantForward = Gear == -1 && nearlyStopped && c.throttle > 0.3f && c.brake < 0.05f;
            if (wantReverse || wantForward)
            {
                _reverseHoldTimer += dt;
                if (_reverseHoldTimer > 0.35f)
                {
                    ShiftTo(wantReverse ? -1 : 1);
                    _reverseHoldTimer = 0f;
                }
            }
            else _reverseHoldTimer = 0f;

            if (Gear == -1)
            {
                // In reverse the pedals swap roles: brake pedal drives backwards, throttle brakes.
                float th = c.throttle;
                c.throttle = c.brake;
                c.brake = th;
            }

            if (_shiftTimer > 0f || _shiftCooldown > 0f || Gear < 1) return;
            // Decisions use the rpm implied by ground speed (not wheel spin or the free-revving engine):
            // the box holds its gear while the rear tires spin in a drift and cannot cascade upshifts.
            float groundRpm = GroundSpeedRpm(Gear);
            bool atLimiter = EngineRpm > _p.engine.limiterRpm - 80f && groundRpm > t.autoUpshiftRpm * 0.9f;
            if (Gear < maxGear && c.throttle > 0.1f && (groundRpm > t.autoUpshiftRpm || atLimiter))
            {
                ShiftTo(Gear + 1);
            }
            else if (Gear > 1)
            {
                // Coasting only downshifts to avoid lugging; on throttle downshift earlier (kickdown).
                float threshold = c.throttle > 0.1f ? t.autoDownshiftRpm : MathUtil.Max(_p.engine.idleRpm * 1.6f, 1500f);
                float newRpm = GroundSpeedRpm(Gear - 1);
                if (groundRpm < threshold && newRpm < t.autoUpshiftRpm * 0.9f) ShiftTo(Gear - 1);
            }
        }

        private float GearRatio(int gear)
        {
            var t = _p.transmission;
            if (gear == 0) return 0f;
            if (gear < 0) return -t.reverseRatio * t.finalDrive;
            return t.gearRatios[MathUtil.Clamp(gear, 1, t.gearRatios.Length) - 1] * t.finalDrive;
        }

        /// <summary>Engine rpm implied by the vehicle ground speed in the given gear.</summary>
        public float GroundSpeedRpm(int gear) => MathUtil.Abs(_forwardSpeed / DrivenRadius() * GearRatio(gear)) * MathUtil.RadPerSecToRpm;

        /// <summary>Engine rpm that the driven wheels would impose in the given gear.</summary>
        public float DrivenWheelRpm(int gear) => MathUtil.Abs(CarrierOmega() * GearRatio(gear)) * MathUtil.RadPerSecToRpm;

        private void ShiftTo(int gear)
        {
            gear = MathUtil.Clamp(gear, -1, _p.transmission.gearRatios.Length);
            if (gear == Gear) return;
            Gear = gear;
            _shiftTimer = _p.transmission.shiftTimeSec;
            _shiftCooldown = _p.transmission.shiftTimeSec + 0.5f;
        }

        /// <summary>Forces a gear (used by tests/reset/manual UI). Skips the shift delay.</summary>
        public void SetGearImmediate(int gear)
        {
            Gear = MathUtil.Clamp(gear, -1, _p.transmission.gearRatios.Length);
            _shiftTimer = 0f;
        }

        private void UpdateClutch(float dt, VehicleControls c)
        {
            var t = _p.transmission;
            float idle = _p.engine.idleRpm;
            float target;
            if (!EngineRunning || Gear == 0 || _shiftTimer > 0f) target = 0f;
            else if (!c.autoClutch) target = 1f - c.clutchPedal;
            else if (c.handbrake > 0.3f) target = 0f;
            else
            {
                float carrier = CarrierOmega() * TotalRatio * MathUtil.RadPerSecToRpm;
                if (carrier > idle * 1.15f) target = 1f;
                else target = MathUtil.SmoothStep01((EngineRpm - idle * 1.05f) / MathUtil.Max(100f, t.launchRpm - idle * 1.05f));
            }
            float rate = target > _clutchEngagement ? 6f : 25f;
            _clutchEngagement = MathUtil.MoveTowards(_clutchEngagement, target, rate * dt);
        }

        private float EngineTorque(float throttle)
        {
            var e = _p.engine;
            if (!EngineRunning) return -e.frictionTorque * MathUtil.Clamp(_engineOmega, -1f, 1f);
            float rpm = EngineRpm;
            if (rpm >= e.limiterRpm) throttle = 0f;
            float drive = throttle * e.torqueCurve.Evaluate(rpm) * _p.transmission.efficiency * MathUtil.Clamp(EngineTorqueScale, 0f, 2f);
            float engineBrake = (1f - throttle) * e.engineBrakePer1000Rpm * MathUtil.Max(0f, rpm) * 0.001f;
            float idleAssist = rpm < e.idleRpm ? MathUtil.Clamp((e.idleRpm - rpm) * 0.6f, 0f, 90f) : 0f;
            return drive - engineBrake - e.frictionTorque + idleAssist;
        }

        /// <summary>
        /// Phase 2: drivetrain + tire solve. Fills outputs; read them with <see cref="Wheel"/>.
        /// </summary>
        public void Step(float dt, WheelContact[] contacts)
        {
            if (contacts == null || contacts.Length < WheelCount) throw new ArgumentException("Need 4 contacts");
            if (!(dt > 0f)) return;

            // Estimate forward speed for gearbox logic (average of grounded wheels).
            float speedSum = 0f; int groundedCount = 0;
            for (int i = 0; i < WheelCount; i++)
                if (contacts[i].grounded) { speedSum += contacts[i].longitudinalVelocity; groundedCount++; }
            if (groundedCount > 0) _forwardSpeed = speedSum / groundedCount;

            int sub = MathUtil.Clamp(_p.drivetrainSubsteps, 1, 16);
            float h = dt / sub;
            Array.Clear(_fxSum, 0, WheelCount);
            Array.Clear(_fySum, 0, WheelCount);
            for (int s = 0; s < sub; s++) SubStep(h, contacts);

            for (int i = 0; i < WheelCount; i++)
            {
                _out[i].fx = _fxSum[i] / sub;
                _out[i].fy = _fySum[i] / sub;
                _out[i].angularVelocity = _omega[i];
                _out[i].steerAngleRad = _steer[i];
                _out[i].normalLoad = contacts[i].grounded ? contacts[i].normalLoad : 0f;
                _out[i].grounded = contacts[i].grounded;
            }
            SanitizeState();
        }

        private void SubStep(float h, WheelContact[] contacts)
        {
            var c = _controls;
            var brakes = _p.brakes;

            // 1. Linearize tires around current wheel speeds -> effective inertia and base force.
            for (int i = 0; i < WheelCount; i++)
            {
                var axle = Axle(i);
                float r = EffectiveRadius(i);
                ref var ct = ref contacts[i];
                if (ct.grounded && ct.normalLoad > 0f)
                {
                    var grip = ModifiedGrip(i, ct.grip);
                    var f0 = TireModel.Evaluate(axle.tire, grip, ct.normalLoad, ct.longitudinalVelocity, ct.lateralVelocity, _omega[i] * r);
                    const float delta = 0.05f;
                    var f1 = TireModel.Evaluate(axle.tire, grip, ct.normalLoad, ct.longitudinalVelocity, ct.lateralVelocity, (_omega[i] + delta) * r);
                    float k = MathUtil.Clamp((f1.fx - f0.fx) / delta, 0f, 1e6f);
                    _fx0[i] = f0.fx;
                    _iEff[i] = axle.wheelInertia + h * r * k;
                }
                else
                {
                    _fx0[i] = 0f;
                    _iEff[i] = axle.wheelInertia;
                }
                // Unconstrained wheel update (implicit in tire stiffness).
                _omega[i] += h * (-r * _fx0[i]) / _iEff[i];

                float pedal = IsFront(i) ? brakes.frontBias : 1f - brakes.frontBias;
                float bt = c.brake * brakes.maxTorqueNm * pedal * 0.5f;
                if (!IsFront(i)) bt += c.handbrake * brakes.handbrakeTorqueNm;
                _brakeLimit[i] = bt * h;
                _brakeAccum[i] = 0f;
            }

            // 2. Engine unconstrained update.
            float ie = _p.engine.inertia;
            float engineThrottle = c.throttle;
            if (_shiftTimer > 0f && Gear != 0)
            {
                // Rev matching while the clutch is open: blip toward the rpm of the new gear, cut above it.
                float target = MathUtil.Max(DrivenWheelRpm(Gear), _p.engine.idleRpm);
                engineThrottle = MathUtil.Clamp01((target - EngineRpm) / 600f);
            }
            _engineOmega += h * EngineTorque(engineThrottle) / ie;

            // 3. Sequential impulses: clutch (engine <-> driven carrier), axle diffs, center diff (AWD), brakes.
            float g = TotalRatio;
            float clutchLimit = _p.transmission.clutchMaxTorque * _clutchEngagement * h;
            bool clutchActive = g != 0f && clutchLimit > 0f;
            float clutchAccum = 0f, frontDiffAccum = 0f, rearDiffAccum = 0f, centerAccum = 0f;
            var w = _driveWeight;
            bool awd = _p.drivetrain.layout == DriveLayout.Awd;

            for (int it = 0; it < SolverIterations; it++)
            {
                if (clutchActive)
                {
                    float carrier = 0f, invMass = 1f / ie;
                    for (int i = 0; i < WheelCount; i++)
                    {
                        if (w[i] <= 0f) continue;
                        carrier += w[i] * _omega[i];
                        invMass += g * g * w[i] * w[i] / _iEff[i];
                    }
                    float lambda = (_engineOmega - g * carrier) / invMass;
                    float old = clutchAccum;
                    clutchAccum = MathUtil.Clamp(old + lambda, -clutchLimit, clutchLimit);
                    lambda = clutchAccum - old;
                    _engineOmega -= lambda / ie;
                    for (int i = 0; i < WheelCount; i++)
                        if (w[i] > 0f) _omega[i] += g * w[i] * lambda / _iEff[i];
                }

                float driveTorque = clutchAccum * g / h;
                if (w[2] > 0f)
                {
                    float axleTorque = MathUtil.Abs(driveTorque) * (w[2] + w[3]);
                    SolveAxleDiff(2, 3, DiffLockLimit(axleTorque, driveTorque, _omega[2] + _omega[3]) * h, ref rearDiffAccum);
                }
                if (w[0] > 0f)
                {
                    // Front axle of FWD/AWD cars uses an open diff unless the profile says otherwise.
                    float axleTorque = MathUtil.Abs(driveTorque) * (w[0] + w[1]);
                    float limit = _p.drivetrain.frontDiffLsd ? DiffLockLimit(axleTorque, driveTorque, _omega[0] + _omega[1]) : 0f;
                    SolveAxleDiff(0, 1, limit * h, ref frontDiffAccum);
                }
                if (awd && _p.drivetrain.centerLockNm > 0f)
                {
                    // Center coupling (viscous/clutch): drives front and rear axle mean speeds together.
                    float err = 0.5f * (_omega[0] + _omega[1]) - 0.5f * (_omega[2] + _omega[3]);
                    float invMass = 0.25f * (1f / _iEff[0] + 1f / _iEff[1] + 1f / _iEff[2] + 1f / _iEff[3]);
                    float lambda = err / invMass;
                    float old = centerAccum;
                    float lim = _p.drivetrain.centerLockNm * h;
                    centerAccum = MathUtil.Clamp(old + lambda, -lim, lim);
                    lambda = centerAccum - old;
                    _omega[0] -= 0.5f * lambda / _iEff[0];
                    _omega[1] -= 0.5f * lambda / _iEff[1];
                    _omega[2] += 0.5f * lambda / _iEff[2];
                    _omega[3] += 0.5f * lambda / _iEff[3];
                }

                for (int i = 0; i < WheelCount; i++)
                {
                    if (_brakeLimit[i] <= 0f) continue;
                    float lambda = _omega[i] * _iEff[i];
                    float old = _brakeAccum[i];
                    _brakeAccum[i] = MathUtil.Clamp(old + lambda, -_brakeLimit[i], _brakeLimit[i]);
                    lambda = _brakeAccum[i] - old;
                    _omega[i] -= lambda / _iEff[i];
                }
            }

            _lastAxleTorque = clutchAccum * g / h;
            if (_engineOmega < 0f) _engineOmega = 0f;
            float maxEngine = (_p.engine.limiterRpm + 400f) * MathUtil.RpmToRadPerSec;
            if (_engineOmega > maxEngine) _engineOmega = maxEngine;

            // 4. Final tire forces at the solved wheel speeds.
            for (int i = 0; i < WheelCount; i++)
            {
                ref var ct = ref contacts[i];
                if (!ct.grounded || ct.normalLoad <= 0f)
                {
                    _out[i].slipRatio = 0f; _out[i].slipAngleRad = 0f; _out[i].gripUsage = 0f; _out[i].sliding = false; _out[i].slipPower = 0f;
                    continue;
                }
                var axle = Axle(i);
                float wheelSpeed = _omega[i] * EffectiveRadius(i);
                var f = TireModel.Evaluate(axle.tire, ModifiedGrip(i, ct.grip), ct.normalLoad, ct.longitudinalVelocity, ct.lateralVelocity, wheelSpeed);
                TireModel.ClampNoOvershoot(ref f, ct.massShare, h * _p.drivetrainSubsteps, ct.longitudinalVelocity - wheelSpeed, ct.lateralVelocity);
                _fxSum[i] += f.fx;
                _fySum[i] += f.fy;
                _out[i].slipRatio = f.slipRatio;
                _out[i].slipAngleRad = f.slipAngleRad;
                _out[i].gripUsage = f.gripUsage;
                _out[i].sliding = f.sliding;
                _out[i].slipPower = MathUtil.Abs(f.fx * (ct.longitudinalVelocity - wheelSpeed)) + MathUtil.Abs(f.fy * ct.lateralVelocity);
            }
        }

        private void SolveAxleDiff(int left, int right, float limit, ref float accum)
        {
            if (limit <= 0f) return;
            float err = _omega[left] - _omega[right];
            float lambda = err / (1f / _iEff[left] + 1f / _iEff[right]);
            float old = accum;
            accum = MathUtil.Clamp(old + lambda, -limit, limit);
            lambda = accum - old;
            _omega[left] -= lambda / _iEff[left];
            _omega[right] += lambda / _iEff[right];
        }

        private float DiffLockLimit(float axleTorqueAbs, float signedDriveTorque, float carrierOmegaSum)
        {
            var d = _p.differential;
            switch (d.type)
            {
                case DifferentialType.Locked: return 1e6f;
                case DifferentialType.Lsd:
                    bool power = signedDriveTorque * carrierOmegaSum >= 0f;
                    return d.preloadNm + (power ? d.powerLockRatio : d.coastLockRatio) * axleTorqueAbs;
                default: return 0f;
            }
        }

        private void SanitizeState()
        {
            bool bad = !MathUtil.IsFinite(_engineOmega) || !MathUtil.IsFinite(_centerSteer);
            for (int i = 0; i < WheelCount && !bad; i++)
                bad |= !MathUtil.IsFinite(_omega[i]) || !MathUtil.IsFinite(_out[i].fx) || !MathUtil.IsFinite(_out[i].fy);
            if (!bad) return;
            NonFiniteRecoveries++;
            int gear = Gear;
            Reset();
            Gear = gear;
        }

        /// <summary>Counts how many times NaN/Infinity was detected and the state was reset (should stay 0).</summary>
        public int NonFiniteRecoveries { get; private set; }

        /// <summary>Sets all wheel speeds to match a ground speed (used when spawning a moving car in tests).</summary>
        public void MatchWheelSpeeds(float forwardSpeed)
        {
            for (int i = 0; i < WheelCount; i++) _omega[i] = forwardSpeed / EffectiveRadius(i);
            _forwardSpeed = forwardSpeed;
            float g = TotalRatio;
            if (g != 0f)
            {
                _engineOmega = MathUtil.Max(_p.engine.idleRpm * MathUtil.RpmToRadPerSec, forwardSpeed / DrivenRadius() * g);
                _clutchEngagement = 1f;
            }
        }
    }
}

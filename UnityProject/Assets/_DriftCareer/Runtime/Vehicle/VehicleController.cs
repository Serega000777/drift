using System;
using System.Collections.Generic;
using DriftCareer.Core;
using DriftCareer.Core.Assists;
using DriftCareer.Core.Surfaces;
using DriftCareer.Core.Vehicle;
using DriftCareer.Runtime.Track;
using DriftCareer.Runtime.Security;
using UnityEngine;

namespace DriftCareer.Runtime.Vehicle
{
    /// <summary>
    /// Unity adapter for <see cref="VehicleSimulation"/>: raycast wheels + suspension on a Rigidbody.
    /// Vehicle root origin = ground level, midway between the axles; physics is independent of the visual mesh.
    /// All motion comes from forces at contact points (no yaw/position tricks). Runs in FixedUpdate.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(-50)]
    public sealed class VehicleController : MonoBehaviour
    {
        private const int Wheels = VehicleSimulation.WheelCount;

        private Rigidbody _rb;
        private readonly WheelContact[] _contacts = new WheelContact[Wheels];
        private readonly Vector3[] _mountLocal = new Vector3[Wheels];
        private readonly float[] _compression = new float[Wheels];
        private readonly float[] _prevCompression = new float[Wheels];
        private readonly float[] _suspensionForce = new float[Wheels];
        private readonly Vector3[] _contactPoint = new Vector3[Wheels];
        private readonly Vector3[] _contactNormal = new Vector3[Wheels];
        private readonly bool[] _grounded = new bool[Wheels];
        private readonly string[] _surfaceId = new string[Wheels];
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private readonly Dictionary<int, string> _surfaceCache = new Dictionary<int, string>();
        private float _pendingImpulse;
        private Vector3 _lastSafePosition;
        private Quaternion _lastSafeRotation;
        private bool _parkingBrake = true;
        private bool _frozen;

        public VehiclePhysicsProfile Profile { get; private set; }
        public VehicleSimulation Sim { get; private set; }
        public VehicleHealth Health { get; private set; }
        public DriverAssistPipeline Assists { get; private set; }
        public VehicleTelemetry Telemetry { get; } = new VehicleTelemetry();
        public SurfaceLibrary Surfaces { get; set; }
        public Rigidbody Body => _rb;

        /// <summary>Driver input for the next physics step (set by the input router each frame).</summary>
        public DriverInput Input { get; set; }

        /// <summary>Set false to disable player input (intro cinematic) - the car holds its parking brake.</summary>
        public bool InputEnabled { get; set; } = true;

        public bool Frozen => _frozen;
        public bool ParkingBrakeOn => _parkingBrake;

        /// <summary>Collision: impulse magnitude (N*s), world contact point, other collider.</summary>
        public event Action<float, Vector3, Collider> Impact;
        public event Action EngineStarted;

        public Vector3 WheelMountWorld(int i) => transform.TransformPoint(_mountLocal[i]);
        public bool WheelGrounded(int i) => _grounded[i];
        public Vector3 WheelContactPoint(int i) => _contactPoint[i];
        public Vector3 WheelContactNormal(int i) => _contactNormal[i];
        public float WheelCompression(int i) => _compression[i];
        public string WheelSurface(int i) => _surfaceId[i];

        /// <summary>Wheel center in local space for visuals (follows suspension compression).</summary>
        public Vector3 WheelCenterLocal(int i)
        {
            // Distance mount -> wheel center = travel - compression (compression already uses the effective radius).
            var axle = Sim.Axle(i);
            float droop = axle.suspension.travel - _compression[i];
            return _mountLocal[i] - new Vector3(0f, MathUtil.Clamp(droop, -0.05f, axle.suspension.travel), 0f);
        }

        public void Initialize(VehiclePhysicsProfile profile, HealthSettings health, AssistSettings assists, SurfaceLibrary surfaces)
        {
            _rb = GetComponent<Rigidbody>();
            Surfaces = surfaces ?? SurfaceLibrary.CreateDefault();
            Health = new VehicleHealth(health);
            Assists = new DriverAssistPipeline(assists);
            Sim = new VehicleSimulation(profile);
            ApplyProfile(profile);
            Sim.Reset();
            _lastSafePosition = transform.position;
            _lastSafeRotation = transform.rotation;
        }

        /// <summary>Applies a (tuned) profile without resetting motion. Used by the garage "test drive".</summary>
        public void ApplyProfile(VehiclePhysicsProfile profile)
        {
            Profile = profile;
            Sim.SetProfile(profile);
            var ch = profile.chassis;
            _rb.mass = ch.massKg;
            float l = ch.length, w = ch.width, h = ch.height;
            float m = ch.massKg * ch.inertiaScale / 12f;
            _rb.centerOfMass = new Vector3(0f, ch.cgHeight, ch.wheelbase * (ch.frontWeightFraction - 0.5f));
            _rb.inertiaTensor = new Vector3(m * (h * h + l * l), m * (l * l + w * w), m * (w * w + h * h));
            _rb.inertiaTensorRotation = Quaternion.identity;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.maxAngularVelocity = 25f;
            _rb.solverIterations = 12;
            _rb.solverVelocityIterations = 4;
#if UNITY_6000_0_OR_NEWER
            _rb.linearDamping = 0.01f;
            _rb.angularDamping = 0.05f;
#else
            _rb.drag = 0.01f;
            _rb.angularDrag = 0.05f;
#endif
            for (int i = 0; i < Wheels; i++)
            {
                bool front = VehicleSimulation.IsFront(i);
                var axle = front ? profile.front : profile.rear;
                float staticLoad = profile.StaticAxleLoadN(front) * 0.5f;
                float x = (VehicleSimulation.IsLeft(i) ? -0.5f : 0.5f) * axle.trackWidth;
                float z = (front ? 0.5f : -0.5f) * ch.wheelbase;
                _mountLocal[i] = new Vector3(x, SuspensionModel.MountHeight(axle.suspension, axle.wheelRadius, staticLoad), z);
            }
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        public void SetFrozen(bool frozen)
        {
            if (_frozen == frozen) return;
            _frozen = frozen;
            if (frozen)
            {
                SetVelocity(Vector3.zero);
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
            }
            else
            {
                _rb.isKinematic = false;
                SetVelocity(Vector3.zero);
                _rb.angularVelocity = Vector3.zero;
                Sim.Reset();
                Assists.Reset();
                _parkingBrake = true;
                _rb.WakeUp();
            }
        }

        /// <summary>Teleports the car to a pose (reset only - never used for driving).</summary>
        public void ResetTo(Vector3 position, Quaternion rotation, bool repair)
        {
            bool wasKinematic = _rb.isKinematic;
            _rb.isKinematic = true;
            transform.SetPositionAndRotation(position, rotation);
            _rb.position = position;
            _rb.rotation = rotation;
            _rb.isKinematic = wasKinematic;
            if (!_rb.isKinematic)
            {
                SetVelocity(Vector3.zero);
                _rb.angularVelocity = Vector3.zero;
            }
            bool running = Sim.EngineRunning;
            Sim.Reset();
            Sim.EngineRunning = running;
            Assists.Reset();
            Array.Clear(_compression, 0, Wheels);
            Array.Clear(_prevCompression, 0, Wheels);
            _parkingBrake = true;
            if (repair) Health.Repair();
            Health.ApplyModifiers(Sim);
            _lastSafePosition = position;
            _lastSafeRotation = rotation;
        }

        public float ConsumeImpactImpulse()
        {
            float v = _pendingImpulse;
            _pendingImpulse = 0f;
            return v;
        }

        public Vector3 Velocity
        {
            get
            {
#if UNITY_6000_0_OR_NEWER
                return _rb.linearVelocity;
#else
                return _rb.velocity;
#endif
            }
        }

        private void SetVelocity(Vector3 v)
        {
#if UNITY_6000_0_OR_NEWER
            _rb.linearVelocity = v;
#else
            _rb.velocity = v;
#endif
        }

        private void FixedUpdate()
        {
            if (Sim == null || _frozen) return;
            float dt = Time.fixedDeltaTime;
            var p = Profile;

            // 1. Input -> assists -> controls.
            var input = InputEnabled ? Input : default;
            var controls = Assists.Process(input, Telemetry, p.steering.maxAngleDeg, dt);
            if (!Sim.EngineRunning && (controls.throttle > 0.05f || input.shiftUp))
            {
                Sim.EngineRunning = true;
                EngineStarted?.Invoke();
            }
            if (_parkingBrake)
            {
                if (InputEnabled && (controls.throttle > 0.1f || controls.brake > 0.1f)) _parkingBrake = false;
                else controls.handbrake = 1f;
            }
            Sim.Prepare(dt, controls);

            // 2. Wheel raycasts and suspension.
            Vector3 up = transform.up;
            float fzSum = 0f;
            for (int i = 0; i < Wheels; i++)
            {
                var axle = Sim.Axle(i);
                float radius = Sim.EffectiveRadius(i);
                Vector3 origin = transform.TransformPoint(_mountLocal[i]);
                float rayLength = SuspensionModel.RayLength(axle.suspension, radius);
                _grounded[i] = CastWheel(origin, -up, rayLength + 0.05f, out var hit);
                _prevCompression[i] = _compression[i];
                if (_grounded[i] && hit.distance <= rayLength)
                {
                    _compression[i] = SuspensionModel.Compression(axle.suspension, radius, hit.distance);
                    _contactPoint[i] = hit.point;
                    _contactNormal[i] = hit.normal;
                    _surfaceId[i] = ResolveSurface(hit.collider);
                }
                else
                {
                    _grounded[i] = false;
                    _compression[i] = 0f;
                    _contactPoint[i] = origin - up * rayLength;
                    _contactNormal[i] = up;
                    _surfaceId[i] = null;
                }
            }
            for (int axleIndex = 0; axleIndex < 2; axleIndex++)
            {
                int l = axleIndex * 2, r = l + 1;
                var susp = axleIndex == 0 ? p.front.suspension : p.rear.suspension;
                float arb = SuspensionModel.AntiRoll(susp, _compression[l], _compression[r]);
                for (int k = 0; k < 2; k++)
                {
                    int i = l + k;
                    if (!_grounded[i]) { _suspensionForce[i] = 0f; continue; }
                    float vel = (_compression[i] - _prevCompression[i]) / dt;
                    float scale = Sim.GetWheelModifiers(i).springScale;
                    var s = susp;
                    float f = SuspensionModel.Force(s, _compression[i], vel) * scale + (k == 0 ? arb : -arb);
                    _suspensionForce[i] = Mathf.Max(0f, f);
                    fzSum += _suspensionForce[i];
                }
            }

            // 3. Contact velocities in steered wheel frames.
            for (int i = 0; i < Wheels; i++)
            {
                if (!_grounded[i]) { _contacts[i] = default; continue; }
                Vector3 n = _contactNormal[i];
                Quaternion steer = Quaternion.AngleAxis(Sim.WheelSteerAngle(i) * Mathf.Rad2Deg, up);
                Vector3 fwd = Vector3.ProjectOnPlane(steer * transform.forward, n).normalized;
                Vector3 right = Vector3.Cross(n, fwd);
                Vector3 v = _rb.GetPointVelocity(_contactPoint[i]);
                var surface = Surfaces.Get(_surfaceId[i]);
                _contacts[i] = new WheelContact
                {
                    grounded = true,
                    normalLoad = _suspensionForce[i],
                    longitudinalVelocity = Vector3.Dot(v, fwd),
                    lateralVelocity = Vector3.Dot(v, right),
                    grip = surface.GripFor(Sim.Axle(i).tire.compound),
                    massShare = fzSum > 1f ? _rb.mass * _suspensionForce[i] / fzSum : _rb.mass * 0.25f
                };
            }

            // 4. Drivetrain + tires.
            Sim.Step(dt, _contacts);

            // 5. Apply forces.
            for (int i = 0; i < Wheels; i++)
            {
                if (!_grounded[i]) continue;
                var w = Sim.Wheel(i);
                Vector3 n = _contactNormal[i];
                Quaternion steer = Quaternion.AngleAxis(w.steerAngleRad * Mathf.Rad2Deg, up);
                Vector3 fwd = Vector3.ProjectOnPlane(steer * transform.forward, n).normalized;
                Vector3 right = Vector3.Cross(n, fwd);
                _rb.AddForceAtPosition(up * _suspensionForce[i], _contactPoint[i], ForceMode.Force);
                float rc = (VehicleSimulation.IsFront(i) ? p.front : p.rear).suspension.rollCenterHeight;
                Vector3 tirePoint = _contactPoint[i] + up * rc;
                Vector3 tireForce = fwd * MathUtil.Finite(w.fx) + right * MathUtil.Finite(w.fy);
                _rb.AddForceAtPosition(tireForce, tirePoint, ForceMode.Force);
            }

            Vector3 vel3 = Velocity;
            float speed = vel3.magnitude;
            if (speed > 0.1f)
            {
                float q = 0.5f * p.aero.airDensity * speed * speed;
                _rb.AddForce(-vel3 / speed * q * p.aero.dragArea, ForceMode.Force);
                float down = q * p.aero.downforceArea;
                Vector3 frontAxle = transform.TransformPoint(new Vector3(0f, 0.3f, p.chassis.wheelbase * 0.5f));
                Vector3 rearAxle = transform.TransformPoint(new Vector3(0f, 0.3f, -p.chassis.wheelbase * 0.5f));
                _rb.AddForceAtPosition(-up * down * p.chassis.frontWeightFraction, frontAxle, ForceMode.Force);
                _rb.AddForceAtPosition(-up * down * (1f - p.chassis.frontWeightFraction), rearAxle, ForceMode.Force);
            }

            // 6. Health (tire heat/wear/blowouts) and telemetry.
            Health.Update(dt, Sim, speed);
            Vector3 localV = transform.InverseTransformDirection(vel3);
            float yawRate = transform.InverseTransformDirection(_rb.angularVelocity).y;
            Telemetry.time += dt;
            Telemetry.CaptureFrom(Sim);
            Telemetry.SetMotion(new Vec2(localV.x, localV.z), yawRate, p.CgToRear);

            GuardAgainstExplosion(speed);
        }

        private bool CastWheel(Vector3 origin, Vector3 dir, float length, out RaycastHit best)
        {
            best = default;
            int count = Physics.RaycastNonAlloc(origin, dir, _hits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            bool found = false;
            for (int k = 0; k < count; k++)
            {
                var h = _hits[k];
                if (h.collider == null || h.collider.attachedRigidbody == _rb) continue;
                // Wheels ride on static world geometry only; light dynamic props (cones, loose tires) are pushed by the body.
                var arb = h.collider.attachedRigidbody;
                if (arb != null && !arb.isKinematic) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
            }
            return found;
        }

        private string ResolveSurface(Collider c)
        {
            int id = c.GetInstanceID();
            if (_surfaceCache.TryGetValue(id, out var s)) return s;
            var tag = c.GetComponentInParent<SurfaceTag>();
            s = tag != null ? tag.surfaceId : SurfaceLibrary.DryAsphalt;
            _surfaceCache[id] = s;
            return s;
        }

        private void GuardAgainstExplosion(float speed)
        {
            bool bad = !IsFinite(transform.position) || !IsFinite(Velocity) || !IsFinite(_rb.angularVelocity) || speed > 150f;
            if (bad)
            {
                SafeLog.Warn("Vehicle state invalid - restoring last safe pose.");
                ResetTo(_lastSafePosition + Vector3.up * 0.3f, _lastSafeRotation, false);
                return;
            }
            if (Telemetry.anyWheelGrounded && Vector3.Dot(transform.up, Vector3.up) > 0.7f && speed < 60f)
            {
                _lastSafePosition = transform.position;
                _lastSafeRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            }
        }

        private static bool IsFinite(Vector3 v) => MathUtil.IsFinite(v.x) && MathUtil.IsFinite(v.y) && MathUtil.IsFinite(v.z);

        private void OnCollisionEnter(Collision collision) => HandleCollision(collision, true);

        private void OnCollisionStay(Collision collision) => HandleCollision(collision, false);

        private void HandleCollision(Collision collision, bool enter)
        {
            if (Health == null || collision.contactCount == 0) return;
            float impulse = collision.impulse.magnitude;
            if (!enter && impulse < 1500f) return; // sustained scraping only counts when hard
            var contact = collision.GetContact(0);
            _pendingImpulse = Mathf.Max(_pendingImpulse, impulse);
            Vector3 local = transform.InverseTransformPoint(contact.point) - _rb.centerOfMass;
            Health.ReportImpact(local.x, local.z, impulse, Profile);
            Impact?.Invoke(impulse, contact.point, collision.collider);
        }
    }
}

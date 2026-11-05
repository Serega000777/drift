using DriftCareer.Core.Persistence;
using DriftCareer.Runtime.Vehicle;
using UnityEngine;

namespace DriftCareer.Runtime.Cameras
{
    public enum RigMode { Chase, ChaseFar, Hood, Cockpit, Bumper, TopDown, Showroom, Cinematic }

    /// <summary>
    /// Single camera rig for all views. Drift-aware chase camera (looks partly along the velocity),
    /// collision avoidance (no clipping through hangar walls), speed FOV, impact shake, editor top-down,
    /// garage showroom orbit and an externally driven cinematic mode with smooth blends between modes.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class CameraRig : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        public RigMode Mode { get; private set; } = RigMode.Chase;
        public float baseFov = 60f;
        public float blendTime = 0.7f;

        // Top-down editor state (driven by the layout editor).
        public Vector3 topDownFocus;
        public float topDownSize = 45f;
        public float topDownYaw;

        // Showroom state (garage).
        public float showroomYaw = 35f;
        public float showroomPitch = 12f;
        public float showroomDistance = 7f;
        public bool showroomAutoRotate = true;

        private VehicleController _car;
        private CarRig _rig;
        private Vector3 _pos;
        private Quaternion _rot = Quaternion.identity;
        private float _yawSmoothed;
        private float _trauma;
        private float _blend = 1f;
        private Vector3 _blendFromPos;
        private Quaternion _blendFromRot;
        private float _blendFromFov;
        private float _fov;
        private readonly RaycastHit[] _hits = new RaycastHit[8];

        public static CameraRig Create(Transform parent)
        {
            var go = new GameObject("CameraRig");
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<CameraRig>();
            var camGo = new GameObject("MainCamera") { tag = "MainCamera" };
            camGo.transform.SetParent(go.transform, false);
            rig.Cam = camGo.AddComponent<Camera>();
            rig.Cam.nearClipPlane = 0.08f;
            rig.Cam.farClipPlane = 1200f;
            rig.Cam.fieldOfView = rig.baseFov;
            rig.Cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<AudioListener>();
            rig._fov = rig.baseFov;
            return rig;
        }

        public void SetTarget(VehicleController car, CarRig rig)
        {
            _car = car;
            _rig = rig;
            if (car != null) _yawSmoothed = car.transform.eulerAngles.y;
        }

        public static RigMode FromSetting(CameraMode m)
        {
            switch (m)
            {
                case CameraMode.ChaseFar: return RigMode.ChaseFar;
                case CameraMode.Hood: return RigMode.Hood;
                case CameraMode.Cockpit: return RigMode.Cockpit;
                case CameraMode.Bumper: return RigMode.Bumper;
                default: return RigMode.Chase;
            }
        }

        public void SetMode(RigMode mode, bool instant = false)
        {
            if (mode == Mode && !instant) return;
            _blendFromPos = Cam.transform.position;
            _blendFromRot = Cam.transform.rotation;
            _blendFromFov = Cam.fieldOfView;
            _blend = instant ? 1f : 0f;
            Mode = mode;
            Cam.orthographic = mode == RigMode.TopDown;
            if (_car != null) _yawSmoothed = _car.transform.eulerAngles.y;
        }

        /// <summary>Cinematic mode: caller supplies the pose every frame.</summary>
        public void SetCinematicPose(Vector3 position, Quaternion rotation, float fov)
        {
            if (Mode != RigMode.Cinematic) SetMode(RigMode.Cinematic, true);
            _pos = position;
            _rot = rotation;
            _fov = fov;
        }

        public void AddShake(float amount) => _trauma = Mathf.Clamp01(_trauma + amount);

        /// <summary>Pose the given mode would produce right now (used by the intro to blend into gameplay).</summary>
        public void EvaluateMode(RigMode mode, out Vector3 pos, out Quaternion rot, out float fov)
        {
            pos = Cam.transform.position; rot = Cam.transform.rotation; fov = baseFov;
            if (_car == null) return;
            var t = _car.transform;
            Vector3 vel = _car.Velocity;
            float speed = vel.magnitude;
            switch (mode)
            {
                case RigMode.Chase:
                case RigMode.ChaseFar:
                {
                    bool far = mode == RigMode.ChaseFar;
                    float dist = far ? 7.8f : 5.4f, height = far ? 2.4f : 1.75f;
                    float carYaw = t.eulerAngles.y;
                    float velYaw = speed > 4f ? Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg : carYaw;
                    // Drift cam: blend toward the travel direction so the slide stays in frame.
                    float targetYaw = Mathf.LerpAngle(carYaw, velYaw, Mathf.Clamp01((speed - 4f) / 10f) * 0.45f);
                    float yaw = Mathf.LerpAngle(_yawSmoothed, targetYaw, 1f - Mathf.Exp(-5f * Time.deltaTime));
                    Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
                    Vector3 pivot = t.position + Vector3.up * 1.0f;
                    Vector3 desired = pivot + yawRot * new Vector3(0f, height - 1.0f, -dist - Mathf.Clamp(speed * 0.04f, 0f, 1.5f));
                    pos = ResolveCollision(pivot, desired);
                    rot = Quaternion.LookRotation(pivot + yawRot * Vector3.forward * 2.5f - pos, Vector3.up);
                    fov = baseFov + Mathf.Clamp(speed * 0.35f, 0f, 14f);
                    break;
                }
                case RigMode.Hood:
                {
                    var a = _rig != null && _rig.hoodAnchor != null ? _rig.hoodAnchor : t;
                    pos = a.position;
                    rot = Quaternion.LookRotation(t.forward, t.up);
                    fov = baseFov + 5f + Mathf.Clamp(speed * 0.2f, 0f, 10f);
                    break;
                }
                case RigMode.Cockpit:
                {
                    var a = _rig != null && _rig.cockpitAnchor != null ? _rig.cockpitAnchor : t;
                    pos = a.position;
                    // Slight look-into-the-slide for cockpit drifting.
                    float look = Mathf.Clamp(-_car.Telemetry.driftAngleDeg * 0.35f, -25f, 25f);
                    rot = Quaternion.LookRotation(Quaternion.AngleAxis(look, t.up) * t.forward, t.up) * Quaternion.Euler(4f, 0f, 0f);
                    fov = baseFov + 8f;
                    break;
                }
                case RigMode.Bumper:
                {
                    var a = _rig != null && _rig.bumperAnchor != null ? _rig.bumperAnchor : t;
                    pos = a.position;
                    rot = Quaternion.LookRotation(t.forward, t.up);
                    fov = baseFov + 10f + Mathf.Clamp(speed * 0.25f, 0f, 12f);
                    break;
                }
                case RigMode.TopDown:
                    pos = topDownFocus + Vector3.up * 120f;
                    rot = Quaternion.Euler(90f, topDownYaw, 0f);
                    fov = baseFov;
                    break;
                case RigMode.Showroom:
                {
                    if (showroomAutoRotate) showroomYaw += 8f * Time.unscaledDeltaTime;
                    Quaternion r = Quaternion.Euler(showroomPitch, showroomYaw, 0f);
                    Vector3 pivot = t.position + Vector3.up * 0.7f;
                    pos = pivot + r * new Vector3(0f, 0f, -showroomDistance);
                    rot = Quaternion.LookRotation(pivot - pos, Vector3.up);
                    fov = 40f;
                    break;
                }
                case RigMode.Cinematic:
                    pos = _pos; rot = _rot; fov = _fov;
                    break;
            }
        }

        private Vector3 ResolveCollision(Vector3 pivot, Vector3 desired)
        {
            Vector3 dir = desired - pivot;
            float dist = dir.magnitude;
            if (dist < 0.01f) return desired;
            dir /= dist;
            int n = Physics.SphereCastNonAlloc(pivot, 0.3f, dir, _hits, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = dist;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                var rb = h.collider.attachedRigidbody;
                if (rb != null && !rb.isKinematic) continue; // ignore cones and loose props
                if (h.distance > 0f && h.distance < best) best = h.distance;
            }
            return pivot + dir * Mathf.Max(0.6f, best - 0.15f);
        }

        private void LateUpdate()
        {
            if (Cam == null) return;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            EvaluateMode(Mode, out var pos, out var rot, out float fov);
            if (Mode == RigMode.Chase || Mode == RigMode.ChaseFar)
            {
                _yawSmoothed = rot.eulerAngles.y;
                // Position spring for weight, rotation stays tight to avoid nausea.
                pos = Vector3.Lerp(Cam.transform.position, pos, _blend >= 1f ? 1f - Mathf.Exp(-14f * dt) : 1f);
            }
            if (_blend < 1f)
            {
                _blend = Mathf.Min(1f, _blend + dt / blendTime);
                float k = Mathf.SmoothStep(0f, 1f, _blend);
                pos = Vector3.Lerp(_blendFromPos, pos, k);
                rot = Quaternion.Slerp(_blendFromRot, rot, k);
                fov = Mathf.Lerp(_blendFromFov, fov, k);
            }
            if (_trauma > 0f)
            {
                float s = _trauma * _trauma;
                float t = Time.time * 25f;
                pos += new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, 0f) * (0.25f * s);
                rot *= Quaternion.Euler((Mathf.PerlinNoise(t, 3f) - 0.5f) * 4f * s, (Mathf.PerlinNoise(5f, t) - 0.5f) * 4f * s, 0f);
                _trauma = Mathf.Max(0f, _trauma - dt * 1.6f);
            }
            Cam.transform.SetPositionAndRotation(pos, rot);
            if (Cam.orthographic) Cam.orthographicSize = topDownSize;
            else Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, fov, 1f - Mathf.Exp(-6f * dt));
        }
    }
}

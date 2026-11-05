using System;
using System.Collections;
using DriftCareer.Core.Assists;
using DriftCareer.Runtime.Vehicle;
using UnityEngine;

namespace DriftCareer.Runtime.Cameras
{
    /// <summary>
    /// Intro cinematic: first-person walk through the hangar to the car (head bob), door opens, sit down,
    /// door closes, engine starts, then blend to the chosen gameplay camera (cockpit stays inside, chase pulls out)
    /// and an optional input-driven roll-out through the hangar door (physics, no teleport). Skippable at any time.
    /// </summary>
    public sealed class IntroDirector : MonoBehaviour
    {
        public float walkDuration = 4.8f;
        public float eyeHeight = 1.68f;
        public bool rollOut = true;

        private CameraRig _cam;
        private VehicleController _car;
        private CarRig _rig;
        private RigMode _finalMode;
        private Vector3 _exitPoint;
        private bool _skip;
        private bool _running;
        private Action _onDone;

        public bool Running => _running;
        public bool InRollOut { get; private set; }

        public void Play(CameraRig cam, VehicleController car, CarRig rig, RigMode finalMode, Vector3 walkStart, Vector3 exitPoint, Action onDone)
        {
            _cam = cam; _car = car; _rig = rig; _finalMode = finalMode; _exitPoint = exitPoint; _onDone = onDone;
            _skip = false;
            _running = true;
            car.InputEnabled = false;
            car.Sim.EngineRunning = false;
            StartCoroutine(Run(walkStart));
        }

        public void Skip() => _skip = true;

        private IEnumerator Run(Vector3 walkStart)
        {
            var t = _car.transform;
            Vector3 doorSide = t.position - t.right * 1.45f + t.forward * 0.05f;
            Vector3 p0 = walkStart, p1 = Vector3.Lerp(walkStart, doorSide, 0.45f) + t.right * -1.2f, p2 = doorSide - t.forward * 1.2f, p3 = doorSide;
            p0.y = p1.y = p2.y = p3.y = t.position.y + eyeHeight;

            // 1. Walk.
            float time = 0f;
            float pathLength = Vector3.Distance(p0, p1) + Vector3.Distance(p1, p2) + Vector3.Distance(p2, p3);
            while (time < walkDuration && !_skip)
            {
                time += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, time / walkDuration);
                Vector3 pos = CatmullRom(p0, p0, p1, p2, p3, k);
                float stride = k * pathLength / 0.75f;
                pos.y += Mathf.Abs(Mathf.Sin(stride * Mathf.PI)) * 0.035f - 0.02f;
                pos += t.right * (Mathf.Sin(stride * Mathf.PI * 0.5f) * 0.015f);
                Vector3 look = Vector3.Lerp(t.position + Vector3.up * 0.9f, _rig.doorPivot.position + Vector3.up * 0.6f, Mathf.Clamp01(k * 1.5f - 0.5f));
                _cam.SetCinematicPose(pos, Quaternion.LookRotation(look - pos, Vector3.up), 62f);
                if (k > 0.7f) _rig.SetDoorOpen((k - 0.7f) / 0.3f);
                yield return null;
            }

            // 2. Sit down into the cockpit.
            if (!_skip)
            {
                Vector3 from = _cam.Cam.transform.position;
                Quaternion fromRot = _cam.Cam.transform.rotation;
                time = 0f;
                const float sit = 1.6f;
                while (time < sit && !_skip)
                {
                    time += Time.deltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, time / sit);
                    Vector3 target = _rig.cockpitAnchor.position;
                    Vector3 mid = Vector3.Lerp(from, target, 0.5f) + Vector3.down * 0.25f;
                    Vector3 pos = Vector3.Lerp(Vector3.Lerp(from, mid, k), Vector3.Lerp(mid, target, k), k);
                    Quaternion rot = Quaternion.Slerp(fromRot, Quaternion.LookRotation(t.forward, t.up), k);
                    _cam.SetCinematicPose(pos, rot, Mathf.Lerp(62f, 68f, k));
                    _rig.SetDoorOpen(1f - Mathf.Clamp01((k - 0.55f) / 0.45f));
                    yield return null;
                }
            }
            _rig.SetDoorOpen(0f);

            // 3. Engine start.
            _car.Sim.EngineRunning = true;
            _cam.AddShake(0.25f);
            if (!_skip) yield return new WaitForSeconds(0.6f);

            // 4. Blend to the gameplay camera.
            if (_finalMode != RigMode.Cockpit)
            {
                Vector3 from = _cam.Cam.transform.position;
                Quaternion fromRot = _cam.Cam.transform.rotation;
                time = 0f;
                const float pull = 1.6f;
                while (time < pull && !_skip)
                {
                    time += Time.deltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, time / pull);
                    _cam.EvaluateMode(_finalMode, out var pos, out var rot, out float fov);
                    // Arc up and over the roof for a satisfying pull-out.
                    Vector3 arc = Vector3.up * Mathf.Sin(k * Mathf.PI) * 1.2f;
                    _cam.SetCinematicPose(Vector3.Lerp(from, pos, k) + arc, Quaternion.Slerp(fromRot, rot, k), Mathf.Lerp(68f, fov, k));
                    yield return null;
                }
            }
            _cam.SetMode(_finalMode, _skip);

            // 5. Optional roll-out: an input-level autopilot (light throttle, steering toward the exit).
            if (rollOut && !_skip)
            {
                InRollOut = true;
                float timeout = 12f;
                while (timeout > 0f && !_skip)
                {
                    timeout -= Time.deltaTime;
                    Vector3 toExit = _exitPoint - t.position;
                    toExit.y = 0f;
                    if (toExit.magnitude < 4f || Vector3.Dot(toExit, t.forward) < 0f) break;
                    float angle = Vector3.SignedAngle(t.forward, toExit, Vector3.up);
                    float speed = _car.Velocity.magnitude;
                    _car.InputEnabled = true;
                    _car.Input = new DriverInput
                    {
                        steer = Mathf.Clamp(angle / 25f, -1f, 1f),
                        throttle = speed < 4.5f ? 0.32f : 0f,
                        brake = speed > 6.5f ? 0.3f : 0f
                    };
                    yield return null;
                }
                InRollOut = false;
            }

            _car.InputEnabled = true;
            _running = false;
            _onDone?.Invoke();
        }

        private static Vector3 CatmullRom(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 e, float t)
        {
            // Piecewise Catmull-Rom over four control segments b->c->d->e with phantom endpoints.
            Vector3[] pts = { a, b, c, d, e, e };
            float seg = t * 3f;
            int i = Mathf.Clamp((int)seg, 0, 2);
            float u = seg - i;
            Vector3 p0 = pts[i], p1 = pts[i + 1], p2 = pts[i + 2], p3 = pts[i + 3];
            return 0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u);
        }
    }
}

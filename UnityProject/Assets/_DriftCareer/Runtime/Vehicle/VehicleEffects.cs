using System.Collections.Generic;
using DriftCareer.Core.Vehicle;
using DriftCareer.Runtime.Track;
using UnityEngine;

namespace DriftCareer.Runtime.Vehicle
{
    /// <summary>
    /// Tire smoke (density from frictional slip power), skidmarks, rim sparks on deflated tires,
    /// blowout burst, body-impact sparks and engine damage smoke. Particle budgets scale with quality tier.
    /// </summary>
    public sealed class VehicleEffects : MonoBehaviour
    {
        public float smokeScale = 1f;
        public int maxSmokeParticles = 900;

        private VehicleController _car;
        private CarRig _rig;
        private readonly ParticleSystem[] _smoke = new ParticleSystem[4];
        private readonly int[] _skidIndex = { -1, -1, -1, -1 };
        private ParticleSystem _sparks;
        private ParticleSystem _burst;
        private ParticleSystem _engineSmoke;
        private float[] _emitAccumulator = new float[4];

        public void Initialize(VehicleController car, CarRig rig, float qualityScale)
        {
            _car = car;
            _rig = rig;
            smokeScale = qualityScale;
            maxSmokeParticles = Mathf.RoundToInt(Mathf.Lerp(250, 1500, Mathf.Clamp01(qualityScale)));
            var puff = MaterialLibrary.ParticleMaterial("smoke", MaterialLibrary.SmokePuff());
            for (int i = 0; i < 4; i++) _smoke[i] = CreateSmoke("Smoke_" + i, puff);
            _sparks = CreateSparks();
            _burst = CreateBurst(puff);
            _engineSmoke = CreateEngineSmoke(puff);
            car.Health.TireBlown += OnTireBlown;
            car.Impact += OnImpact;
        }

        private void OnDestroy()
        {
            if (_car != null && _car.Health != null)
            {
                _car.Health.TireBlown -= OnTireBlown;
                _car.Impact -= OnImpact;
            }
        }

        private ParticleSystem CreateSmoke(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.035f;
            main.maxParticles = maxSmokeParticles / 4;
            main.startColor = new Color(0.92f, 0.92f, 0.92f, 0.55f);
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 3.8f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.8f, 0.8f, 0.8f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.08f), new GradientAlphaKey(0.25f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var vel = ps.limitVelocityOverLifetime;
            vel.enabled = true;
            vel.dampen = 0.35f;
            vel.limit = 0.6f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.sortMode = ParticleSystemSortMode.Distance;
            ps.Play();
            return ps;
        }

        private ParticleSystem CreateSparks()
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.gravityModifier = 1.2f;
            main.maxParticles = 300;
            main.startColor = new Color(1f, 0.75f, 0.3f);
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.05f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.04f;
            r.lengthScale = 1f;
            r.sharedMaterial = MaterialLibrary.Emissive("spark", new Color(1f, 0.7f, 0.25f), 8f);
            ps.Play();
            return ps;
        }

        private ParticleSystem CreateBurst(Material puff)
        {
            var go = new GameObject("Burst");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 1.4f);
            main.startColor = new Color(0.25f, 0.25f, 0.25f, 0.8f);
            main.gravityModifier = 0.3f;
            main.maxParticles = 120;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = puff;
            return ps;
        }

        private ParticleSystem CreateEngineSmoke(Material puff)
        {
            var go = new GameObject("EngineSmoke");
            go.transform.SetParent(_rig != null && _rig.bodyRoot != null ? _rig.bodyRoot : transform, false);
            go.transform.localPosition = new Vector3(0f, 0.9f, 1.4f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 2.5f;
            main.startSpeed = 0.8f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startColor = new Color(0.2f, 0.2f, 0.22f, 0.5f);
            main.gravityModifier = -0.08f;
            main.maxParticles = 80;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = puff;
            ps.Play();
            return ps;
        }

        private void OnTireBlown(int wheel)
        {
            if (_burst == null) return;
            _burst.transform.position = _car.WheelContactPoint(wheel) + Vector3.up * 0.3f;
            _burst.Emit(60);
            _sparks.transform.position = _car.WheelContactPoint(wheel);
            _sparks.Emit(40);
        }

        private void OnImpact(float impulse, Vector3 point, Collider other)
        {
            if (impulse < 2500f || _sparks == null) return;
            _sparks.transform.position = point;
            _sparks.Emit(Mathf.Clamp(Mathf.RoundToInt(impulse / 400f), 5, 60));
        }

        private void Update()
        {
            if (_car == null || _car.Sim == null) return;
            float dt = Time.deltaTime;
            var sim = _car.Sim;
            var skid = Skidmarks.Instance;
            Vector3 carVel = _car.Velocity;
            for (int i = 0; i < 4; i++)
            {
                var w = sim.Wheel(i);
                bool grounded = _car.WheelGrounded(i);
                float power = grounded ? w.slipPower : 0f;
                var surface = _car.Surfaces.Get(_car.WheelSurface(i));
                string vfx = surface != null ? surface.vfxTag : "smoke";

                // Smoke density grows with frictional power (W). 25 kW ~ light haze, 80 kW+ ~ thick cloud.
                float intensity = Mathf.Clamp01((power - 12000f) / 70000f);
                var ps = _smoke[i];
                if (intensity > 0.01f)
                {
                    ps.transform.position = _car.WheelContactPoint(i) + Vector3.up * 0.25f;
                    var main = ps.main;
                    main.startColor = SmokeColor(vfx, intensity);
                    _emitAccumulator[i] += intensity * 70f * smokeScale * dt;
                    int n = (int)_emitAccumulator[i];
                    if (n > 0)
                    {
                        _emitAccumulator[i] -= n;
                        var ep = new ParticleSystem.EmitParams { velocity = carVel * 0.25f + Vector3.up * 0.5f, applyShapeToPosition = true };
                        ps.Emit(ep, n);
                    }
                }

                // Skidmarks while the tire slides.
                if (skid != null)
                {
                    float mark = grounded ? Mathf.Clamp01((power - 8000f) / 50000f) : 0f;
                    if (_car.Health.tires[i].Deflation > 0.5f && grounded && carVel.magnitude > 2f) mark = Mathf.Max(mark, 0.6f);
                    if (mark > 0.02f)
                        _skidIndex[i] = skid.Add(_car.WheelContactPoint(i) + _car.WheelContactNormal(i) * 0.02f, _car.WheelContactNormal(i), mark, _skidIndex[i], 0.22f);
                    else _skidIndex[i] = -1;
                }

                // Rim sparks for a deflated tire rolling on the rim.
                if (_car.Health.tires[i].Deflation > 0.8f && grounded && carVel.magnitude > 4f && Random.value < 0.6f)
                {
                    _sparks.transform.position = _car.WheelContactPoint(i);
                    _sparks.transform.rotation = Quaternion.LookRotation(-carVel.normalized + Vector3.up * 0.3f);
                    _sparks.Emit(2);
                }
            }

            float frontDamage = _car.Health.zoneDamage[(int)DamageZone.Front];
            var es = _engineSmoke.emission;
            es.rateOverTime = frontDamage > 0.45f ? Mathf.Lerp(2f, 25f, (frontDamage - 0.45f) / 0.55f) : 0f;
        }

        private static Color SmokeColor(string vfx, float intensity)
        {
            float a = Mathf.Lerp(0.15f, 0.6f, intensity);
            switch (vfx)
            {
                case "dust": case "dirt": return new Color(0.55f, 0.45f, 0.33f, a);
                case "snow": case "ice": return new Color(0.95f, 0.97f, 1f, a * 0.8f);
                case "spray": return new Color(0.8f, 0.85f, 0.9f, a * 0.6f);
                default: return new Color(0.93f, 0.93f, 0.93f, a);
            }
        }
    }

    /// <summary>World skidmark renderer: one dynamic mesh ring buffer for all cars.</summary>
    public sealed class Skidmarks : MonoBehaviour
    {
        public static Skidmarks Instance { get; private set; }

        private const int MaxMarks = 2048;
        private struct Mark { public Vector3 pos, normal, left; public float intensity; public int prev; }
        private readonly Mark[] _marks = new Mark[MaxMarks];
        private int _next;
        private int _count;
        private bool _dirty;
        private Mesh _mesh;
        private Vector3[] _v;
        private Color32[] _c;
        private Vector2[] _uv;
        private int[] _t;

        public static Skidmarks Create(Transform parent)
        {
            var go = new GameObject("Skidmarks");
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<Skidmarks>();
            // Particle shaders honour vertex colors (alpha fades each segment); plain Unlit does not.
            var mat = MaterialLibrary.ParticleMaterial("skidmark", Texture2D.whiteTexture);
            MaterialLibrary.SetColor(mat, new Color(0.03f, 0.03f, 0.03f, 0.85f));
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s._mesh = new Mesh { name = "DC_Skidmarks" };
            s._mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = s._mesh;
            s._v = new Vector3[MaxMarks * 4];
            s._c = new Color32[MaxMarks * 4];
            s._uv = new Vector2[MaxMarks * 4];
            s._t = new int[MaxMarks * 6];
            Instance = s;
            return s;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Adds a mark point connected to <paramref name="previous"/> (or starts a new strip with -1).</summary>
        public int Add(Vector3 pos, Vector3 normal, float intensity, int previous, float width)
        {
            if (previous >= 0 && (pos - _marks[previous].pos).sqrMagnitude < 0.05f) return previous;
            int idx = _next;
            _next = (_next + 1) % MaxMarks;
            if (_count < MaxMarks) _count++;
            var m = new Mark { pos = pos, normal = normal, intensity = Mathf.Clamp01(intensity), prev = previous };
            if (previous >= 0)
            {
                Vector3 dir = pos - _marks[previous].pos;
                m.left = Vector3.Cross(dir, normal).normalized * (width * 0.5f);
            }
            _marks[idx] = m;
            if (previous >= 0 && _marks[previous].left == Vector3.zero) { var pm = _marks[previous]; pm.left = m.left; _marks[previous] = pm; }
            _dirty = true;
            return idx;
        }

        public void Clear()
        {
            _count = 0;
            _next = 0;
            _dirty = true;
        }

        private void LateUpdate()
        {
            if (!_dirty) return;
            _dirty = false;
            for (int i = 0; i < MaxMarks; i++)
            {
                int vi = i * 4, ti = i * 6;
                var m = _marks[i];
                bool valid = i < _count && m.prev >= 0 && m.prev < _count;
                if (!valid)
                {
                    _v[vi] = _v[vi + 1] = _v[vi + 2] = _v[vi + 3] = Vector3.zero;
                    for (int k = 0; k < 6; k++) _t[ti + k] = vi;
                    continue;
                }
                var p = _marks[m.prev];
                _v[vi] = p.pos + p.left; _v[vi + 1] = p.pos - p.left;
                _v[vi + 2] = m.pos + m.left; _v[vi + 3] = m.pos - m.left;
                byte a0 = (byte)(p.intensity * 200), a1 = (byte)(m.intensity * 200);
                _c[vi] = _c[vi + 1] = new Color32(255, 255, 255, a0);
                _c[vi + 2] = _c[vi + 3] = new Color32(255, 255, 255, a1);
                _uv[vi] = new Vector2(0, 0); _uv[vi + 1] = new Vector2(1, 0); _uv[vi + 2] = new Vector2(0, 1); _uv[vi + 3] = new Vector2(1, 1);
                _t[ti] = vi; _t[ti + 1] = vi + 2; _t[ti + 2] = vi + 1;
                _t[ti + 3] = vi + 2; _t[ti + 4] = vi + 3; _t[ti + 5] = vi + 1;
            }
            _mesh.vertices = _v;
            _mesh.colors32 = _c;
            _mesh.uv = _uv;
            _mesh.triangles = _t;
            _mesh.RecalculateBounds();
        }
    }

    /// <summary>Deforms body meshes on impacts (visual damage). Original vertices are kept for repair.</summary>
    public sealed class DamageDeformer : MonoBehaviour
    {
        public float maxDeform = 0.18f;
        public float radius = 0.7f;
        private readonly List<MeshFilter> _filters = new List<MeshFilter>();
        private readonly List<Vector3[]> _original = new List<Vector3[]>();
        private VehicleController _car;

        public void Initialize(VehicleController car, Transform bodyRoot)
        {
            _car = car;
            _filters.Clear();
            _original.Clear();
            foreach (var mf in bodyRoot.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                mf.sharedMesh = Instantiate(mf.sharedMesh);
                mf.sharedMesh.MarkDynamic();
                _filters.Add(mf);
                _original.Add(mf.sharedMesh.vertices);
            }
            car.Impact += OnImpact;
        }

        private void OnDestroy()
        {
            if (_car != null) _car.Impact -= OnImpact;
        }

        private void OnImpact(float impulse, Vector3 point, Collider other)
        {
            if (_car.Health.Settings.damage == DamageMode.Off || impulse < 2500f) return;
            float strength = Mathf.Clamp01((impulse - 2500f) / 25000f) * maxDeform;
            Vector3 inward = (_car.Body.worldCenterOfMass - point).normalized;
            foreach (var mf in _filters)
            {
                var mesh = mf.sharedMesh;
                var verts = mesh.vertices;
                var tr = mf.transform;
                Vector3 localPoint = tr.InverseTransformPoint(point);
                Vector3 localDir = tr.InverseTransformDirection(inward);
                bool changed = false;
                for (int i = 0; i < verts.Length; i++)
                {
                    float d = Vector3.Distance(verts[i], localPoint);
                    if (d > radius) continue;
                    float falloff = 1f - d / radius;
                    verts[i] += localDir * (strength * falloff * falloff);
                    changed = true;
                }
                if (!changed) continue;
                mesh.vertices = verts;
                mesh.RecalculateNormals();
            }
        }

        public void Repair()
        {
            for (int i = 0; i < _filters.Count; i++)
            {
                if (_filters[i] == null) continue;
                _filters[i].sharedMesh.vertices = _original[i];
                _filters[i].sharedMesh.RecalculateNormals();
            }
        }
    }
}

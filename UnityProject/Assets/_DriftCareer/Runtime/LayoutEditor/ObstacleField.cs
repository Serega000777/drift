using System.Collections.Generic;
using DriftCareer.Core;
using DriftCareer.Core.Layout;
using DriftCareer.Runtime.Track;
using UnityEngine;

namespace DriftCareer.Runtime.LayoutEditor
{
    /// <summary>Link from a scene object to its layout placement.</summary>
    public sealed class ObstacleInstance : MonoBehaviour
    {
        public int uid;
        public string defId;
        public Vector3 homePosition;
        public Quaternion homeRotation;
        public Rigidbody body;

        public void ReturnHome()
        {
            if (body != null)
            {
                bool wasKinematic = body.isKinematic;
                body.isKinematic = true;
                transform.SetPositionAndRotation(homePosition, homeRotation);
                body.position = homePosition;
                body.rotation = homeRotation;
                body.isKinematic = wasKinematic;
                if (!wasKinematic) body.Sleep();
            }
            else transform.SetPositionAndRotation(homePosition, homeRotation);
        }
    }

    /// <summary>Builds obstacle GameObjects from definitions (procedural meshes, colliders, rigidbodies).</summary>
    public static class ObstacleFactory
    {
        private static PhysicMaterialHandle _coneMaterial;

        private struct PhysicMaterialHandle
        {
#if UNITY_6000_0_OR_NEWER
            public PhysicsMaterial value;
#else
            public PhysicMaterial value;
#endif
        }

        private static void ApplyLowFriction(Collider c)
        {
            if (_coneMaterial.value == null)
            {
#if UNITY_6000_0_OR_NEWER
                _coneMaterial.value = new PhysicsMaterial("DC_Prop") { dynamicFriction = 0.45f, staticFriction = 0.5f, bounciness = 0.15f };
#else
                _coneMaterial.value = new PhysicMaterial("DC_Prop") { dynamicFriction = 0.45f, staticFriction = 0.5f, bounciness = 0.15f };
#endif
            }
            c.sharedMaterial = _coneMaterial.value;
        }

        public static GameObject Create(ObstacleDefinition def, Transform parent, bool ghost)
        {
            var go = new GameObject((ghost ? "Ghost_" : "Obstacle_") + def.id);
            go.transform.SetParent(parent, false);
            var orange = MaterialLibrary.LitColor("cone_orange", new Color(1f, 0.38f, 0.05f), 0.45f);
            var white = MaterialLibrary.LitColor("prop_white", new Color(0.92f, 0.92f, 0.9f), 0.5f);
            var rubber = MaterialLibrary.LitColor("tire_rubber", new Color(0.05f, 0.05f, 0.055f), 0.15f);
            var red = MaterialLibrary.LitColor("prop_red", new Color(0.85f, 0.08f, 0.06f), 0.5f);
            var concrete = MaterialLibrary.LitColor("concrete", Color.white, 0.25f, 0f, MaterialLibrary.ConcreteTexture(), new Vector2(0.6f, 0.6f));
            var yellow = MaterialLibrary.LitColor("prop_yellow", new Color(0.95f, 0.78f, 0.05f), 0.5f);
            var paint = MaterialLibrary.UnlitColor("paint_white", new Color(0.92f, 0.92f, 0.9f));
            var clip = MaterialLibrary.Emissive("clip_glow", new Color(0.1f, 0.85f, 1f), 2.5f);
            var t = go.transform;
            switch (def.id)
            {
                case "cone":
                    ProceduralMeshes.Spawn("Mesh", t, ProceduralMeshes.Cone(0.16f, 0.7f), orange, Vector3.zero, Quaternion.identity);
                    ProceduralMeshes.Spawn("Band", t, ProceduralMeshes.CylinderY(0.095f, 0.08f, 12), white, new Vector3(0f, 0.38f, 0f), Quaternion.identity, false);
                    if (!ghost)
                    {
                        var cap = go.AddComponent<CapsuleCollider>(); cap.radius = 0.11f; cap.height = 0.7f; cap.center = new Vector3(0f, 0.35f, 0f); ApplyLowFriction(cap);
                        var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(0.42f, 0.04f, 0.42f); bc.center = new Vector3(0f, 0.02f, 0f); ApplyLowFriction(bc);
                    }
                    break;
                case "tire":
                    ProceduralMeshes.Spawn("Mesh", t, ProceduralMeshes.Tire(0.33f, 0.19f, 0.24f, 16), rubber, new Vector3(0f, 0.12f, 0f), Quaternion.Euler(0f, 0f, 90f));
                    if (!ghost)
                    {
                        var mc = go.AddComponent<MeshCollider>();
                        mc.sharedMesh = ProceduralMeshes.CylinderY(0.33f, 0.24f, 10);
                        mc.convex = true;
                        ApplyLowFriction(mc);
                    }
                    break;
                case "tire_stack":
                    for (int k = 0; k < 3; k++)
                        ProceduralMeshes.Spawn("Tire" + k, t, ProceduralMeshes.Tire(0.33f, 0.19f, 0.24f, 16), k == 1 ? white : rubber, new Vector3(0f, 0.12f + k * 0.245f, 0f), Quaternion.Euler(0f, k * 20f, 90f));
                    if (!ghost) { var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(0.66f, 0.74f, 0.66f); bc.center = new Vector3(0f, 0.37f, 0f); }
                    break;
                case "tire_wall":
                    for (int x = 0; x < 5; x++)
                    for (int k = 0; k < 3; k++)
                        ProceduralMeshes.Spawn("Tire", t, ProceduralMeshes.Tire(0.32f, 0.18f, 0.24f, 12), (x + k) % 4 == 0 ? red : rubber,
                            new Vector3(-1.2f + x * 0.6f, 0.12f + k * 0.245f, 0f), Quaternion.Euler(0f, 0f, 90f));
                    if (!ghost) { var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(3f, 0.74f, 0.66f); bc.center = new Vector3(0f, 0.37f, 0f); }
                    break;
                case "plastic_barrier":
                    ProceduralMeshes.Spawn("Body", t, ProceduralMeshes.Box(new Vector3(1.8f, 0.8f, 0.5f)), red, Vector3.zero, Quaternion.identity);
                    ProceduralMeshes.Spawn("Stripe", t, ProceduralMeshes.Box(new Vector3(0.6f, 0.805f, 0.505f)), white, Vector3.zero, Quaternion.identity);
                    if (!ghost) { var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(1.8f, 0.8f, 0.5f); bc.center = new Vector3(0f, 0.4f, 0f); ApplyLowFriction(bc); }
                    break;
                case "concrete_barrier":
                    ProceduralMeshes.Spawn("Base", t, ProceduralMeshes.Box(new Vector3(2f, 0.3f, 0.6f)), concrete, Vector3.zero, Quaternion.identity);
                    ProceduralMeshes.Spawn("Top", t, ProceduralMeshes.Box(new Vector3(2f, 0.5f, 0.24f)), concrete, new Vector3(0f, 0.3f, 0f), Quaternion.identity);
                    if (!ghost) { var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(2f, 0.8f, 0.6f); bc.center = new Vector3(0f, 0.4f, 0f); }
                    break;
                case "bollard":
                    ProceduralMeshes.Spawn("Post", t, ProceduralMeshes.CylinderY(0.1f, 1f, 12), yellow, Vector3.zero, Quaternion.identity);
                    ProceduralMeshes.Spawn("Band", t, ProceduralMeshes.CylinderY(0.105f, 0.1f, 12), white, new Vector3(0f, 0.8f, 0f), Quaternion.identity, false);
                    if (!ghost) { var cap = go.AddComponent<CapsuleCollider>(); cap.radius = 0.12f; cap.height = 1f; cap.center = new Vector3(0f, 0.5f, 0f); }
                    break;
                case "clipping_point":
                    ProceduralMeshes.Spawn("Pole", t, ProceduralMeshes.CylinderY(0.04f, 1.4f, 8), white, Vector3.zero, Quaternion.identity);
                    ProceduralMeshes.Spawn("Head", t, ProceduralMeshes.Box(new Vector3(0.3f, 0.3f, 0.05f)), clip, new Vector3(0f, 1.25f, 0f), Quaternion.identity, false);
                    ProceduralMeshes.Spawn("Zone", t, ProceduralMeshes.Ring(2.5f, 0.15f, 48), MaterialLibrary.UnlitColor("clip_ring", new Color(0.1f, 0.85f, 1f)), new Vector3(0f, 0.013f, 0f), Quaternion.identity, false);
                    break;
                case "clipping_gate":
                    for (int s = -1; s <= 1; s += 2) ProceduralMeshes.Spawn("Pole", t, ProceduralMeshes.CylinderY(0.05f, 1.6f, 8), white, new Vector3(s * 1.5f, 0f, 0f), Quaternion.identity);
                    ProceduralMeshes.Spawn("Banner", t, ProceduralMeshes.Box(new Vector3(3f, 0.25f, 0.04f)), clip, new Vector3(0f, 1.45f, 0f), Quaternion.identity, false);
                    ProceduralMeshes.Spawn("Line", t, ProceduralMeshes.FlatQuad(3f, 0.25f), MaterialLibrary.UnlitColor("clip_ring", new Color(0.1f, 0.85f, 1f)), new Vector3(0f, 0.013f, 0f), Quaternion.identity, false);
                    break;
                case "marker":
                    ProceduralMeshes.Spawn("Paint", t, ProceduralMeshes.FlatQuad(2f, 0.2f), paint, new Vector3(0f, 0.012f, 0f), Quaternion.identity, false);
                    break;
                case "marker_box":
                    ProceduralMeshes.Spawn("N", t, ProceduralMeshes.FlatQuad(2.5f, 0.15f), paint, new Vector3(0f, 0.012f, 2.5f), Quaternion.identity, false);
                    ProceduralMeshes.Spawn("S", t, ProceduralMeshes.FlatQuad(2.5f, 0.15f), paint, new Vector3(0f, 0.012f, -2.5f), Quaternion.identity, false);
                    ProceduralMeshes.Spawn("E", t, ProceduralMeshes.FlatQuad(0.15f, 5f), paint, new Vector3(1.25f, 0.012f, 0f), Quaternion.identity, false);
                    ProceduralMeshes.Spawn("W", t, ProceduralMeshes.FlatQuad(0.15f, 5f), paint, new Vector3(-1.25f, 0.012f, 0f), Quaternion.identity, false);
                    break;
                default:
                    ProceduralMeshes.Spawn("Mesh", t, ProceduralMeshes.Box(new Vector3(0.5f, 0.5f, 0.5f)), white, Vector3.zero, Quaternion.identity);
                    if (!ghost) go.AddComponent<BoxCollider>().center = new Vector3(0f, 0.25f, 0f);
                    break;
            }
            if (!ghost && def.behavior == ObstacleBehavior.Dynamic)
            {
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = Mathf.Max(0.5f, def.massKg);
#if UNITY_6000_0_OR_NEWER
                rb.linearDamping = 0.15f; rb.angularDamping = 0.4f;
#else
                rb.drag = 0.15f; rb.angularDrag = 0.4f;
#endif
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.sleepThreshold = 0.05f;
                rb.maxDepenetrationVelocity = 3f;
            }
            else if (!ghost) go.isStatic = def.behavior == ObstacleBehavior.Static;
            return go;
        }

        public static void MakeGhost(GameObject go, Material mat)
        {
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
    }

    /// <summary>
    /// Presenter: keeps scene obstacles in sync with an <see cref="ObstacleLayout"/> (diff-based, pooled),
    /// resets knocked props, and exposes clipping targets for scoring.
    /// </summary>
    public sealed class ObstacleField : MonoBehaviour
    {
        private readonly Dictionary<int, ObstacleInstance> _live = new Dictionary<int, ObstacleInstance>();
        private readonly Dictionary<string, Stack<ObstacleInstance>> _pool = new Dictionary<string, Stack<ObstacleInstance>>();
        private readonly List<Vector3> _clipTargets = new List<Vector3>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _remove = new List<int>();

        public ObstacleLibrary Library { get; private set; }
        public IReadOnlyList<Vector3> ClippingTargets => _clipTargets;
        public int LiveCount => _live.Count;

        public static ObstacleField Create(Transform parent, ObstacleLibrary library)
        {
            var go = new GameObject("Obstacles");
            go.transform.SetParent(parent, false);
            var f = go.AddComponent<ObstacleField>();
            f.Library = library;
            return f;
        }

        public void Sync(ObstacleLayout layout)
        {
            _seen.Clear();
            _clipTargets.Clear();
            foreach (var p in layout.placements)
            {
                var def = Library.Get(p.defId);
                if (def == null) continue;
                _seen.Add(p.uid);
                Vector3 pos = new Vector3(p.position.X, 0.002f, p.position.Y);
                Quaternion rot = Quaternion.Euler(0f, p.yawDeg, 0f);
                if (!_live.TryGetValue(p.uid, out var inst) || inst == null || inst.defId != p.defId)
                {
                    if (inst != null) Release(inst);
                    inst = Acquire(def);
                    inst.uid = p.uid;
                    _live[p.uid] = inst;
                    inst.homePosition = pos;
                    inst.homeRotation = rot;
                    inst.ReturnHome();
                }
                else if (inst.homePosition != pos || inst.homeRotation != rot)
                {
                    inst.homePosition = pos;
                    inst.homeRotation = rot;
                    inst.ReturnHome();
                }
                if (def.isClippingTarget) _clipTargets.Add(pos);
            }
            _remove.Clear();
            foreach (var kv in _live) if (!_seen.Contains(kv.Key)) _remove.Add(kv.Key);
            foreach (var uid in _remove)
            {
                Release(_live[uid]);
                _live.Remove(uid);
            }
        }

        /// <summary>Puts every knocked cone/tire back to its placed pose.</summary>
        public void ResetDynamic()
        {
            foreach (var inst in _live.Values) if (inst != null && inst.body != null) inst.ReturnHome();
        }

        /// <summary>Editor mode: freeze dynamic props so the layout stays exact while editing.</summary>
        public void SetEditing(bool editing)
        {
            foreach (var inst in _live.Values)
            {
                if (inst == null || inst.body == null) continue;
                if (editing) inst.ReturnHome();
                inst.body.isKinematic = editing;
            }
        }

        public ObstacleInstance Find(int uid) => _live.TryGetValue(uid, out var i) ? i : null;

        private ObstacleInstance Acquire(ObstacleDefinition def)
        {
            if (_pool.TryGetValue(def.id, out var stack) && stack.Count > 0)
            {
                var pooled = stack.Pop();
                pooled.gameObject.SetActive(true);
                return pooled;
            }
            var go = ObstacleFactory.Create(def, transform, false);
            var inst = go.AddComponent<ObstacleInstance>();
            inst.defId = def.id;
            inst.body = go.GetComponent<Rigidbody>();
            return inst;
        }

        private void Release(ObstacleInstance inst)
        {
            if (inst == null) return;
            inst.gameObject.SetActive(false);
            if (!_pool.TryGetValue(inst.defId, out var stack)) _pool[inst.defId] = stack = new Stack<ObstacleInstance>();
            stack.Push(inst);
        }

        /// <summary>Distance-based clipping proximity (0..1) for scoring: 1 at the pole, 0 at the zone edge.</summary>
        public float ClippingProximity(Vector3 carPosition, float radius = 2.5f)
        {
            float best = 0f;
            foreach (var c in _clipTargets)
            {
                float d = Vector2.Distance(new Vector2(c.x, c.z), new Vector2(carPosition.x, carPosition.z));
                best = Mathf.Max(best, 1f - d / radius);
            }
            return Mathf.Clamp01(best);
        }
    }
}

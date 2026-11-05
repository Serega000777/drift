using System;
using System.Collections.Generic;

namespace DriftCareer.Core.Layout
{
    public enum ObstacleBehavior { Dynamic = 0, Static = 1, Trigger = 2, Decal = 3 }

    public enum FootprintKind { Circle = 0, Box = 1 }

    [Serializable]
    public struct Footprint
    {
        public FootprintKind kind;
        public float radius;
        public float halfX;
        public float halfZ;

        public static Footprint Circle(float r) => new Footprint { kind = FootprintKind.Circle, radius = r };
        public static Footprint Box(float hx, float hz) => new Footprint { kind = FootprintKind.Box, halfX = hx, halfZ = hz };

        public float BoundingRadius => kind == FootprintKind.Circle ? radius : MathUtil.Sqrt(halfX * halfX + halfZ * halfZ);
    }

    [Serializable]
    public sealed class ObstacleDefinition
    {
        public string id;
        public string titleKey;
        public string iconId;
        public Footprint footprint;
        public float height;
        public ObstacleBehavior behavior;
        public float massKg;
        /// <summary>Performance budget units (rigidbodies cost more than static/decal).</summary>
        public float cost = 1f;
        public int maxCount = 100;
        public float snapRotationDeg = 15f;
        /// <summary>Decals may overlap anything; triggers may overlap decals.</summary>
        public bool allowOverlap;
        /// <summary>Marks a clipping point/zone used by scoring.</summary>
        public bool isClippingTarget;
    }

    [Serializable]
    public struct ObstaclePlacement
    {
        public int uid;
        public string defId;
        public Vec2 position;
        public float yawDeg;
    }

    [Serializable]
    public sealed class ObstacleLayout
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        public string trackId = "academy";
        public string name = "custom";
        public string createdUtc = DateTime.UtcNow.ToString("o");
        public string modifiedUtc = DateTime.UtcNow.ToString("o");
        public List<ObstaclePlacement> placements = new List<ObstaclePlacement>();

        public int NextUid()
        {
            int max = 0;
            foreach (var p in placements) if (p.uid > max) max = p.uid;
            return max + 1;
        }

        public int IndexOf(int uid)
        {
            for (int i = 0; i < placements.Count; i++) if (placements[i].uid == uid) return i;
            return -1;
        }

        public ObstacleLayout Clone()
        {
            var c = (ObstacleLayout)MemberwiseClone();
            c.placements = new List<ObstaclePlacement>(placements);
            return c;
        }
    }

    /// <summary>Oriented rectangle on the ground plane (yaw in degrees, Unity convention).</summary>
    [Serializable]
    public struct OrientedRect
    {
        public string name;
        public Vec2 center;
        public Vec2 halfExtents;
        public float yawDeg;

        public OrientedRect(string name, Vec2 center, Vec2 halfExtents, float yawDeg = 0f)
        {
            this.name = name; this.center = center; this.halfExtents = halfExtents; this.yawDeg = yawDeg;
        }

        public bool Contains(Vec2 p)
        {
            var l = Vec2.WorldToLocal(p - center, yawDeg * MathUtil.Deg2Rad);
            return MathUtil.Abs(l.X) <= halfExtents.X && MathUtil.Abs(l.Y) <= halfExtents.Y;
        }
    }

    public static class Geometry2D
    {
        public static void Corners(Vec2 c, Vec2 half, float yawDeg, Vec2[] into)
        {
            float yaw = yawDeg * MathUtil.Deg2Rad;
            Vec2 ax = Vec2.LocalToWorld(new Vec2(half.X, 0f), yaw);
            Vec2 az = Vec2.LocalToWorld(new Vec2(0f, half.Y), yaw);
            into[0] = c + ax + az; into[1] = c + ax - az; into[2] = c - ax - az; into[3] = c - ax + az;
        }

        /// <summary>Separating axis test for two oriented rectangles.</summary>
        public static bool RectsOverlap(Vec2 c1, Vec2 h1, float yaw1, Vec2 c2, Vec2 h2, float yaw2, float margin = 0f)
        {
            h1 = new Vec2(h1.X + margin, h1.Y + margin);
            Vec2[] a = new Vec2[4], b = new Vec2[4];
            Corners(c1, h1, yaw1, a);
            Corners(c2, h2, yaw2, b);
            Vec2[] axes =
            {
                Vec2.LocalToWorld(Vec2.Right, yaw1 * MathUtil.Deg2Rad), Vec2.LocalToWorld(Vec2.Forward, yaw1 * MathUtil.Deg2Rad),
                Vec2.LocalToWorld(Vec2.Right, yaw2 * MathUtil.Deg2Rad), Vec2.LocalToWorld(Vec2.Forward, yaw2 * MathUtil.Deg2Rad)
            };
            foreach (var axis in axes)
            {
                Project(a, axis, out float minA, out float maxA);
                Project(b, axis, out float minB, out float maxB);
                if (maxA < minB || maxB < minA) return false;
            }
            return true;
        }

        private static void Project(Vec2[] pts, Vec2 axis, out float min, out float max)
        {
            min = max = Vec2.Dot(pts[0], axis);
            for (int i = 1; i < pts.Length; i++)
            {
                float d = Vec2.Dot(pts[i], axis);
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }

        public static bool CircleRectOverlap(Vec2 cc, float r, Vec2 rc, Vec2 half, float yawDeg)
        {
            var l = Vec2.WorldToLocal(cc - rc, yawDeg * MathUtil.Deg2Rad);
            float dx = MathUtil.Max(MathUtil.Abs(l.X) - half.X, 0f);
            float dz = MathUtil.Max(MathUtil.Abs(l.Y) - half.Y, 0f);
            return dx * dx + dz * dz <= r * r;
        }

        public static bool FootprintsOverlap(Footprint fa, Vec2 pa, float yawA, Footprint fb, Vec2 pb, float yawB, float margin)
        {
            if (fa.kind == FootprintKind.Circle && fb.kind == FootprintKind.Circle)
                return Vec2.Distance(pa, pb) < fa.radius + fb.radius + margin;
            if (fa.kind == FootprintKind.Circle)
                return CircleRectOverlap(pa, fa.radius + margin, pb, new Vec2(fb.halfX, fb.halfZ), yawB);
            if (fb.kind == FootprintKind.Circle)
                return CircleRectOverlap(pb, fb.radius + margin, pa, new Vec2(fa.halfX, fa.halfZ), yawA);
            return RectsOverlap(pa, new Vec2(fa.halfX, fa.halfZ), yawA, pb, new Vec2(fb.halfX, fb.halfZ), yawB, margin);
        }

        public static bool FootprintOverlapsRect(Footprint f, Vec2 p, float yaw, OrientedRect r)
        {
            if (f.kind == FootprintKind.Circle) return CircleRectOverlap(p, f.radius, r.center, r.halfExtents, r.yawDeg);
            return RectsOverlap(p, new Vec2(f.halfX, f.halfZ), yaw, r.center, r.halfExtents, r.yawDeg);
        }

        /// <summary>True if the whole footprint lies inside the rect.</summary>
        public static bool FootprintInsideRect(Footprint f, Vec2 p, float yaw, OrientedRect r)
        {
            if (f.kind == FootprintKind.Circle)
            {
                var l = Vec2.WorldToLocal(p - r.center, r.yawDeg * MathUtil.Deg2Rad);
                return MathUtil.Abs(l.X) + f.radius <= r.halfExtents.X && MathUtil.Abs(l.Y) + f.radius <= r.halfExtents.Y;
            }
            var corners = new Vec2[4];
            Corners(p, new Vec2(f.halfX, f.halfZ), yaw, corners);
            foreach (var c in corners) if (!r.Contains(c)) return false;
            return true;
        }
    }

    public enum PlacementError { None, UnknownType, NonFinite, OutOfBounds, ForbiddenZone, Overlap, TypeLimit, BudgetExceeded, NotFound }

    public struct PlacementResult
    {
        public PlacementError error;
        /// <summary>Name of the forbidden zone or conflicting object id (for UI explanation).</summary>
        public string detail;
        public bool Ok => error == PlacementError.None;

        public static PlacementResult Success => new PlacementResult { error = PlacementError.None };
        public static PlacementResult Fail(PlacementError e, string detail = null) => new PlacementResult { error = e, detail = detail };
    }

    [Serializable]
    public sealed class PlacementRules
    {
        public OrientedRect bounds;
        public List<OrientedRect> forbiddenZones = new List<OrientedRect>();
        public float budget = 250f;
        public int maxObjects = 300;
        public float overlapMargin = 0.05f;
    }

    public sealed class ObstacleLibrary
    {
        private readonly Dictionary<string, ObstacleDefinition> _defs = new Dictionary<string, ObstacleDefinition>(StringComparer.Ordinal);
        private readonly List<ObstacleDefinition> _ordered = new List<ObstacleDefinition>();

        public IReadOnlyList<ObstacleDefinition> All => _ordered;

        public void Register(ObstacleDefinition d)
        {
            if (!_defs.ContainsKey(d.id)) _ordered.Add(d);
            _defs[d.id] = d;
        }

        public ObstacleDefinition Get(string id) => id != null && _defs.TryGetValue(id, out var d) ? d : null;

        public static ObstacleLibrary CreateDefault()
        {
            var lib = new ObstacleLibrary();
            lib.Register(new ObstacleDefinition { id = "cone", titleKey = "obstacle.cone", iconId = "cone", footprint = Footprint.Circle(0.18f), height = 0.7f, behavior = ObstacleBehavior.Dynamic, massKg = 2.5f, cost = 1f, maxCount = 150 });
            lib.Register(new ObstacleDefinition { id = "tire", titleKey = "obstacle.tire", iconId = "tire", footprint = Footprint.Circle(0.33f), height = 0.24f, behavior = ObstacleBehavior.Dynamic, massKg = 9f, cost = 2f, maxCount = 80 });
            lib.Register(new ObstacleDefinition { id = "tire_stack", titleKey = "obstacle.tire_stack", iconId = "tire_stack", footprint = Footprint.Circle(0.34f), height = 0.75f, behavior = ObstacleBehavior.Static, cost = 1.5f, maxCount = 80 });
            lib.Register(new ObstacleDefinition { id = "tire_wall", titleKey = "obstacle.tire_wall", iconId = "tire_wall", footprint = Footprint.Box(1.5f, 0.36f), height = 0.75f, behavior = ObstacleBehavior.Static, cost = 2f, maxCount = 40, snapRotationDeg = 15f });
            lib.Register(new ObstacleDefinition { id = "plastic_barrier", titleKey = "obstacle.plastic_barrier", iconId = "barrier", footprint = Footprint.Box(0.9f, 0.25f), height = 0.8f, behavior = ObstacleBehavior.Dynamic, massKg = 70f, cost = 3f, maxCount = 40 });
            lib.Register(new ObstacleDefinition { id = "concrete_barrier", titleKey = "obstacle.concrete_barrier", iconId = "concrete", footprint = Footprint.Box(1f, 0.3f), height = 0.8f, behavior = ObstacleBehavior.Static, cost = 1.5f, maxCount = 60 });
            lib.Register(new ObstacleDefinition { id = "bollard", titleKey = "obstacle.bollard", iconId = "bollard", footprint = Footprint.Circle(0.12f), height = 1f, behavior = ObstacleBehavior.Static, cost = 0.8f, maxCount = 80 });
            lib.Register(new ObstacleDefinition { id = "clipping_point", titleKey = "obstacle.clipping_point", iconId = "clip", footprint = Footprint.Circle(0.3f), height = 1.4f, behavior = ObstacleBehavior.Trigger, cost = 0.5f, maxCount = 20, isClippingTarget = true });
            lib.Register(new ObstacleDefinition { id = "clipping_gate", titleKey = "obstacle.clipping_gate", iconId = "gate", footprint = Footprint.Box(1.6f, 0.15f), height = 1.6f, behavior = ObstacleBehavior.Trigger, cost = 0.8f, maxCount = 20, isClippingTarget = true });
            lib.Register(new ObstacleDefinition { id = "marker", titleKey = "obstacle.marker", iconId = "marker", footprint = Footprint.Box(1f, 0.1f), height = 0.01f, behavior = ObstacleBehavior.Decal, cost = 0.2f, maxCount = 200, allowOverlap = true });
            lib.Register(new ObstacleDefinition { id = "marker_box", titleKey = "obstacle.marker_box", iconId = "marker_box", footprint = Footprint.Box(1.25f, 2.5f), height = 0.01f, behavior = ObstacleBehavior.Decal, cost = 0.3f, maxCount = 60, allowOverlap = true });
            return lib;
        }
    }

    public sealed class PlacementValidator
    {
        private readonly ObstacleLibrary _lib;

        public PlacementRules Rules { get; set; }
        /// <summary>Extra dynamic forbidden areas (e.g. where the parked car currently is).</summary>
        public readonly List<OrientedRect> DynamicZones = new List<OrientedRect>();

        public PlacementValidator(ObstacleLibrary lib, PlacementRules rules)
        {
            _lib = lib;
            Rules = rules;
        }

        public float UsedBudget(ObstacleLayout layout, int ignoreUid = -1)
        {
            float sum = 0f;
            foreach (var p in layout.placements)
            {
                if (p.uid == ignoreUid) continue;
                var d = _lib.Get(p.defId);
                if (d != null) sum += d.cost;
            }
            return sum;
        }

        public int CountOf(ObstacleLayout layout, string defId, int ignoreUid = -1)
        {
            int n = 0;
            foreach (var p in layout.placements) if (p.defId == defId && p.uid != ignoreUid) n++;
            return n;
        }

        /// <summary>Checks a candidate placement against bounds, zones, limits and other obstacles.</summary>
        public PlacementResult Validate(ObstacleLayout layout, string defId, Vec2 pos, float yawDeg, int ignoreUid = -1)
        {
            var def = _lib.Get(defId);
            if (def == null) return PlacementResult.Fail(PlacementError.UnknownType, defId);
            if (!pos.IsFinite || !MathUtil.IsFinite(yawDeg)) return PlacementResult.Fail(PlacementError.NonFinite);
            var r = Rules;
            if (!Geometry2D.FootprintInsideRect(def.footprint, pos, yawDeg, r.bounds)) return PlacementResult.Fail(PlacementError.OutOfBounds);
            if (def.behavior != ObstacleBehavior.Decal)
            {
                foreach (var z in r.forbiddenZones)
                    if (Geometry2D.FootprintOverlapsRect(def.footprint, pos, yawDeg, z)) return PlacementResult.Fail(PlacementError.ForbiddenZone, z.name);
                foreach (var z in DynamicZones)
                    if (Geometry2D.FootprintOverlapsRect(def.footprint, pos, yawDeg, z)) return PlacementResult.Fail(PlacementError.ForbiddenZone, z.name);
            }
            if (ignoreUid < 0 || layout.IndexOf(ignoreUid) < 0)
            {
                if (layout.placements.Count >= r.maxObjects) return PlacementResult.Fail(PlacementError.BudgetExceeded, "max_objects");
                if (CountOf(layout, defId) >= def.maxCount) return PlacementResult.Fail(PlacementError.TypeLimit, defId);
                if (UsedBudget(layout) + def.cost > r.budget + 1e-3f) return PlacementResult.Fail(PlacementError.BudgetExceeded, "budget");
            }
            if (!def.allowOverlap)
            {
                foreach (var p in layout.placements)
                {
                    if (p.uid == ignoreUid) continue;
                    var other = _lib.Get(p.defId);
                    if (other == null || other.allowOverlap) continue;
                    if (Vec2.Distance(p.position, pos) > def.footprint.BoundingRadius + other.footprint.BoundingRadius + r.overlapMargin) continue;
                    if (Geometry2D.FootprintsOverlap(def.footprint, pos, yawDeg, other.footprint, p.position, p.yawDeg, r.overlapMargin))
                        return PlacementResult.Fail(PlacementError.Overlap, p.uid.ToString());
                }
            }
            return PlacementResult.Success;
        }
    }
}

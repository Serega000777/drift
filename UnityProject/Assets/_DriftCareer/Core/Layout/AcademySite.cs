using System;
using System.Collections.Generic;

namespace DriftCareer.Core.Layout
{
    /// <summary>
    /// Single source of truth for the Drift Academy test ground geometry. The Unity world builder,
    /// placement rules, drills and presets all read from this, so they can never disagree.
    /// World origin = lot center; +Z = away from the hangar.
    /// </summary>
    [Serializable]
    public sealed class AcademySite
    {
        public float lotSize = 250f;
        public float perimeterMargin = 4f;
        public Vec2 hangarSize = new Vec2(32f, 26f);
        public float hangarHeight = 9f;
        public float doorWidth = 13f;
        public float doorHeight = 6.5f;
        public float exitLaneLength = 30f;
        public float exitLaneWidth = 16f;

        /// <summary>Hangar center (its door faces +Z).</summary>
        public Vec2 HangarCenter => new Vec2(0f, -lotSize * 0.5f + perimeterMargin + hangarSize.Y * 0.5f + 2f);
        public Vec2 DoorCenter => HangarCenter + new Vec2(0f, hangarSize.Y * 0.5f);
        /// <summary>Car spawn inside the hangar, facing the door.</summary>
        public Vec2 SpawnPosition => HangarCenter + new Vec2(0f, -hangarSize.Y * 0.12f);
        public float SpawnYawDeg => 0f;

        public Vec2 DonutCenter => new Vec2(-62f, -18f);
        public float DonutRadius => 11f;
        public Vec2 FigureEightA => new Vec2(48f, -40f);
        public Vec2 FigureEightB => new Vec2(48f, -8f);
        public float FigureEightRadius => 12f;
        public Vec2 SlalomStart => new Vec2(-80f, 30f);
        public Vec2 SlalomDirection => new Vec2(1f, 0f);
        public float SlalomSpacing => 15f;
        public int SlalomCones => 9;
        /// <summary>Open sandbox area for player layouts.</summary>
        public Vec2 SandboxCenter => new Vec2(10f, 72f);
        public float SandboxRadius => 45f;

        public OrientedRect LotBounds => new OrientedRect("lot", Vec2.Zero, new Vec2(lotSize * 0.5f - perimeterMargin, lotSize * 0.5f - perimeterMargin));

        public OrientedRect HangarZone => new OrientedRect("hangar", HangarCenter, hangarSize * 0.5f + new Vec2(1f, 1f));

        public OrientedRect ExitLane => new OrientedRect("exit_lane", DoorCenter + new Vec2(0f, exitLaneLength * 0.5f), new Vec2(exitLaneWidth * 0.5f, exitLaneLength * 0.5f));

        public OrientedRect SpawnZone => new OrientedRect("spawn", SpawnPosition, new Vec2(2.5f, 4f));

        public PlacementRules CreateRules(float budget)
        {
            var r = new PlacementRules { bounds = LotBounds, budget = budget, maxObjects = (int)Math.Max(50, budget * 1.5f) };
            r.forbiddenZones.Add(HangarZone);
            r.forbiddenZones.Add(ExitLane);
            r.forbiddenZones.Add(SpawnZone);
            return r;
        }
    }

    public static class LayoutPresets
    {
        public static readonly string[] Ids = { "empty", "donut", "figure_eight", "slalom", "clipping", "gymkhana" };

        public static List<ObstaclePlacement> Create(string id, AcademySite site)
        {
            var list = new List<ObstaclePlacement>();
            switch (id)
            {
                case "donut": Ring(list, site.DonutCenter, site.DonutRadius, 16, "cone"); Add(list, "tire_stack", site.DonutCenter, 0f); break;
                case "figure_eight":
                    Ring(list, site.FigureEightA, site.FigureEightRadius, 14, "cone");
                    Ring(list, site.FigureEightB, site.FigureEightRadius, 14, "cone");
                    Add(list, "tire_stack", site.FigureEightA, 0f);
                    Add(list, "tire_stack", site.FigureEightB, 0f);
                    break;
                case "slalom":
                    for (int i = 0; i < site.SlalomCones; i++)
                        Add(list, "cone", site.SlalomStart + site.SlalomDirection * (site.SlalomSpacing * i), 0f);
                    break;
                case "clipping":
                {
                    var c = site.SandboxCenter;
                    for (int i = 0; i < 5; i++)
                    {
                        float a = MathUtil.Lerp(-70f, 70f, i / 4f) * MathUtil.Deg2Rad;
                        var p = c + Vec2.FromYaw(a) * 24f;
                        Add(list, "clipping_point", p, a * MathUtil.Rad2Deg);
                        Add(list, "tire_wall", c + Vec2.FromYaw(a) * 27f, a * MathUtil.Rad2Deg + 90f);
                    }
                    break;
                }
                case "gymkhana":
                {
                    var c = site.SandboxCenter;
                    Ring(list, c + new Vec2(-18f, 0f), 7f, 10, "cone");
                    Ring(list, c + new Vec2(18f, 0f), 7f, 10, "cone");
                    for (int i = 0; i < 6; i++) Add(list, "bollard", c + new Vec2(-25f + i * 10f, 20f), 0f);
                    Add(list, "clipping_gate", c + new Vec2(0f, -15f), 0f);
                    for (int i = -2; i <= 2; i++) Add(list, "plastic_barrier", c + new Vec2(i * 2f, -30f), 0f);
                    break;
                }
            }
            return list;
        }

        private static void Ring(List<ObstaclePlacement> list, Vec2 center, float radius, int count, string def)
        {
            for (int i = 0; i < count; i++)
            {
                float a = MathUtil.TwoPi * i / count;
                Add(list, def, center + Vec2.FromYaw(a) * radius, a * MathUtil.Rad2Deg);
            }
        }

        private static void Add(List<ObstaclePlacement> list, string def, Vec2 p, float yaw)
            => list.Add(new ObstaclePlacement { uid = list.Count + 1, defId = def, position = p, yawDeg = MathUtil.WrapAngleDeg(yaw) });
    }

    public sealed class LayoutCodec : Persistence.IDocumentCodec<ObstacleLayout>
    {
        public const int MaxPlacements = 600;
        private readonly ObstacleLibrary _lib;

        public LayoutCodec(ObstacleLibrary lib) { _lib = lib; }

        public string SchemaId => "drift.layout";
        public int CurrentVersion => ObstacleLayout.CurrentVersion;
        public ObstacleLayout CreateDefault() => new ObstacleLayout();

        public JsonValue Encode(ObstacleLayout l)
        {
            var arr = JsonValue.NewArray();
            foreach (var p in l.placements)
                arr.Add(JsonValue.NewObject().Set("uid", p.uid).Set("type", p.defId).Set("x", p.position.X).Set("z", p.position.Y).Set("yaw", p.yawDeg));
            return JsonValue.NewObject().Set("track", l.trackId).Set("name", l.name).Set("created", l.createdUtc).Set("modified", l.modifiedUtc).Set("objects", arr);
        }

        /// <summary>Sanitizes: unknown types, non-finite values and duplicates are dropped; count is capped.</summary>
        public ObstacleLayout Decode(JsonValue j, int version)
        {
            var l = new ObstacleLayout
            {
                trackId = j.GetString("track", "academy"),
                name = LayoutNames.Sanitize(j.GetString("name", "custom")),
                createdUtc = j.GetString("created", DateTime.UtcNow.ToString("o")),
                modifiedUtc = j.GetString("modified", DateTime.UtcNow.ToString("o")),
            };
            var arr = j.GetArray("objects");
            if (arr == null) return l;
            var seen = new HashSet<int>();
            foreach (var o in arr.Items)
            {
                if (l.placements.Count >= MaxPlacements) break;
                if (!o.IsObject) continue;
                string type = o.GetString("type");
                if (_lib.Get(type) == null) continue;
                float x = o.GetFloat("x", float.NaN), z = o.GetFloat("z", float.NaN), yaw = o.GetFloat("yaw", 0f);
                if (!MathUtil.IsFinite(x) || !MathUtil.IsFinite(z) || Math.Abs(x) > 5000f || Math.Abs(z) > 5000f) continue;
                int uid = o.GetInt("uid", 0);
                if (uid <= 0 || !seen.Add(uid)) uid = 0;
                l.placements.Add(new ObstaclePlacement { uid = uid, defId = type, position = new Vec2(x, z), yawDeg = MathUtil.WrapAngleDeg(yaw) });
            }
            // Re-assign invalid/duplicate uids.
            for (int i = 0; i < l.placements.Count; i++)
                if (l.placements[i].uid == 0)
                {
                    var p = l.placements[i];
                    p.uid = l.NextUid();
                    l.placements[i] = p;
                }
            return l;
        }
    }

    public static class LayoutNames
    {
        public static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "custom";
            var sb = new System.Text.StringBuilder();
            foreach (char c in name.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_') sb.Append(c);
                if (sb.Length >= 40) break;
            }
            string s = sb.ToString().Trim();
            return s.Length == 0 ? "custom" : s;
        }

        /// <summary>File-system safe slug (keeps Cyrillic letters).</summary>
        public static string FileSlug(string name)
        {
            var s = Sanitize(name).ToLowerInvariant().Replace(' ', '_');
            return s;
        }
    }
}

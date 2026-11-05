using System;
using System.Collections.Generic;

namespace DriftCareer.Core.Catalog
{
    /// <summary>Implementation status lives in data, never in hard-coded button conditions.</summary>
    public enum FeatureAvailability { Playable = 0, ComingSoon = 1, Hidden = 2 }

    public enum LicenseStatus { Original = 0, RequiresLicense = 1, Licensed = 2, PlaceholderOnly = 3 }

    public enum MenuTargetKind { Track = 0, Mode = 1, Screen = 2 }

    [Serializable]
    public abstract class CatalogEntry
    {
        public string id;
        public string titleKey;
        public string descriptionKey;
        public FeatureAvailability availability = FeatureAvailability.ComingSoon;
        public int sortOrder;
        public string iconId;
        public string accentColor = "#E8322E";
        public List<string> tags = new List<string>();
        public LicenseStatus license = LicenseStatus.Original;
        public string contentVersion = "0";

        public bool IsPlayable => availability == FeatureAvailability.Playable;
        public bool IsVisible => availability != FeatureAvailability.Hidden;
    }

    [Serializable]
    public sealed class TrackDefinition : CatalogEntry
    {
        public string familyId;
        /// <summary>Loader key: "procedural:academy", later "scene:..." or "addressable:...".</summary>
        public string loaderId;
        public List<string> surfaceIds = new List<string>();
        public List<string> allowedModes = new List<string>();
        public float sizeMeters;
        public bool supportsLayoutEditor;
        public string environment = "day";
    }

    [Serializable]
    public sealed class GameModeDefinition : CatalogEntry
    {
        public string rulesId;
        public bool requiresOnline;
        public string unlockCondition;
    }

    [Serializable]
    public sealed class VehicleDefinition : CatalogEntry
    {
        public string physicsProfileId;
        public string visualId;
        public List<string> tuningSlots = new List<string>();
    }

    /// <summary>Main menu card. Points at a track, mode or screen.</summary>
    [Serializable]
    public sealed class MenuSection : CatalogEntry
    {
        public MenuTargetKind targetKind;
        public string targetId;
        public string group = "main";
    }

    public enum LaunchBlockReason { None, UnknownTrack, UnknownMode, TrackNotPlayable, ModeNotPlayable, ModeNotAllowedOnTrack, MissingLoader, RequiresLicense }

    /// <summary>
    /// Scene-independent content catalog. Future content is data with ComingSoon status,
    /// not empty scenes. <see cref="CanLaunch"/> is the single gate for starting gameplay.
    /// </summary>
    public sealed class ContentCatalog
    {
        private readonly Dictionary<string, TrackDefinition> _tracks = new Dictionary<string, TrackDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, GameModeDefinition> _modes = new Dictionary<string, GameModeDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, VehicleDefinition> _vehicles = new Dictionary<string, VehicleDefinition>(StringComparer.Ordinal);
        private readonly List<MenuSection> _menu = new List<MenuSection>();

        public string Version = "1";
        public IEnumerable<TrackDefinition> Tracks => _tracks.Values;
        public IEnumerable<GameModeDefinition> Modes => _modes.Values;
        public IEnumerable<VehicleDefinition> Vehicles => _vehicles.Values;

        public IReadOnlyList<MenuSection> MenuSections
        {
            get
            {
                _menu.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));
                return _menu;
            }
        }

        public void Add(TrackDefinition t) => _tracks[t.id] = t;
        public void Add(GameModeDefinition m) => _modes[m.id] = m;
        public void Add(VehicleDefinition v) => _vehicles[v.id] = v;
        public void Add(MenuSection s) => _menu.Add(s);

        public TrackDefinition Track(string id) => id != null && _tracks.TryGetValue(id, out var t) ? t : null;
        public GameModeDefinition Mode(string id) => id != null && _modes.TryGetValue(id, out var m) ? m : null;
        public VehicleDefinition Vehicle(string id) => id != null && _vehicles.TryGetValue(id, out var v) ? v : null;

        public LaunchBlockReason CanLaunch(string trackId, string modeId)
        {
            var t = Track(trackId);
            if (t == null) return LaunchBlockReason.UnknownTrack;
            var m = Mode(modeId);
            if (m == null) return LaunchBlockReason.UnknownMode;
            if (!t.IsPlayable) return LaunchBlockReason.TrackNotPlayable;
            if (!m.IsPlayable) return LaunchBlockReason.ModeNotPlayable;
            if (t.license == LicenseStatus.RequiresLicense) return LaunchBlockReason.RequiresLicense;
            if (string.IsNullOrEmpty(t.loaderId)) return LaunchBlockReason.MissingLoader;
            if (t.allowedModes.Count > 0 && !t.allowedModes.Contains(modeId)) return LaunchBlockReason.ModeNotAllowedOnTrack;
            return LaunchBlockReason.None;
        }

        /// <summary>Consistency rules; violations are returned as messages (tests fail on any).</summary>
        public List<string> Validate(Func<string, bool> localizationHasKey = null, Func<string, bool> loaderExists = null)
        {
            var e = new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            void Check(CatalogEntry c, string kind)
            {
                if (string.IsNullOrEmpty(c.id)) { e.Add(kind + " without id"); return; }
                if (!ids.Add(kind + ":" + c.id)) e.Add($"duplicate {kind} id {c.id}");
                if (string.IsNullOrEmpty(c.titleKey)) e.Add($"{kind} {c.id} has no titleKey");
                else if (localizationHasKey != null && !localizationHasKey(c.titleKey)) e.Add($"{kind} {c.id}: missing localization '{c.titleKey}'");
                if (c.IsPlayable && c.license == LicenseStatus.RequiresLicense) e.Add($"{kind} {c.id} is Playable but requires a license");
            }
            foreach (var t in _tracks.Values)
            {
                Check(t, "track");
                if (t.IsPlayable && string.IsNullOrEmpty(t.loaderId)) e.Add($"track {t.id} Playable without loader");
                if (t.IsPlayable && loaderExists != null && !loaderExists(t.loaderId)) e.Add($"track {t.id} loader '{t.loaderId}' not registered");
                foreach (var m in t.allowedModes) if (!_modes.ContainsKey(m)) e.Add($"track {t.id} references unknown mode {m}");
            }
            foreach (var m in _modes.Values) Check(m, "mode");
            foreach (var v in _vehicles.Values) Check(v, "vehicle");
            foreach (var s in _menu)
            {
                Check(s, "menu");
                bool targetExists = s.targetKind == MenuTargetKind.Track ? _tracks.ContainsKey(s.targetId ?? "")
                    : s.targetKind == MenuTargetKind.Mode ? _modes.ContainsKey(s.targetId ?? "") : !string.IsNullOrEmpty(s.targetId);
                if (!targetExists) e.Add($"menu {s.id} target '{s.targetId}' missing");
                if (s.IsPlayable && s.targetKind == MenuTargetKind.Track && Track(s.targetId) != null && !Track(s.targetId).IsPlayable)
                    e.Add($"menu {s.id} Playable but its track is not");
            }
            return e;
        }
    }

    /// <summary>Explicit JSON mapping for catalog files (Resources/DriftCareer/Catalog/catalog.json).</summary>
    public static class CatalogJson
    {
        public static ContentCatalog Parse(string json)
        {
            var root = JsonParser.Parse(json);
            var c = new ContentCatalog { Version = root.GetString("version", "1") };
            var tracks = root.GetArray("tracks");
            if (tracks != null)
                foreach (var j in tracks.Items)
                {
                    var t = new TrackDefinition();
                    ReadEntry(j, t);
                    t.familyId = j.GetString("family");
                    t.loaderId = j.GetString("loader");
                    t.sizeMeters = j.GetFloat("sizeMeters");
                    t.supportsLayoutEditor = j.GetBool("layoutEditor");
                    t.environment = j.GetString("environment", "day");
                    ReadList(j.GetArray("surfaces"), t.surfaceIds);
                    ReadList(j.GetArray("modes"), t.allowedModes);
                    c.Add(t);
                }
            var modes = root.GetArray("modes");
            if (modes != null)
                foreach (var j in modes.Items)
                {
                    var m = new GameModeDefinition();
                    ReadEntry(j, m);
                    m.rulesId = j.GetString("rules");
                    m.requiresOnline = j.GetBool("online");
                    m.unlockCondition = j.GetString("unlock");
                    c.Add(m);
                }
            var vehicles = root.GetArray("vehicles");
            if (vehicles != null)
                foreach (var j in vehicles.Items)
                {
                    var v = new VehicleDefinition();
                    ReadEntry(j, v);
                    v.physicsProfileId = j.GetString("physics");
                    v.visualId = j.GetString("visual");
                    ReadList(j.GetArray("tuningSlots"), v.tuningSlots);
                    c.Add(v);
                }
            var menu = root.GetArray("menu");
            if (menu != null)
                foreach (var j in menu.Items)
                {
                    var s = new MenuSection();
                    ReadEntry(j, s);
                    s.targetKind = j.GetEnum("targetKind", MenuTargetKind.Screen);
                    s.targetId = j.GetString("target");
                    s.group = j.GetString("group", "main");
                    c.Add(s);
                }
            return c;
        }

        private static void ReadEntry(JsonValue j, CatalogEntry e)
        {
            e.id = j.GetString("id");
            e.titleKey = j.GetString("title");
            e.descriptionKey = j.GetString("description");
            e.availability = j.GetEnum("status", FeatureAvailability.ComingSoon);
            e.license = j.GetEnum("license", LicenseStatus.Original);
            e.sortOrder = j.GetInt("order");
            e.iconId = j.GetString("icon");
            e.accentColor = j.GetString("accent", e.accentColor);
            e.contentVersion = j.GetString("contentVersion", "0");
            ReadList(j.GetArray("tags"), e.tags);
        }

        private static void ReadList(JsonValue arr, List<string> into)
        {
            if (arr == null) return;
            foreach (var v in arr.Items) if (v.Type == JsonType.String) into.Add(v.AsString);
        }
    }
}

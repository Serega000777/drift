using System;
using System.Collections.Generic;
using System.IO;
using DriftCareer.Core.Assists;
using DriftCareer.Core.Catalog;
using DriftCareer.Core.Garage;
using DriftCareer.Core.Layout;
using DriftCareer.Core.Localization;
using DriftCareer.Core.Persistence;
using DriftCareer.Core.Platform;
using DriftCareer.Core.Surfaces;
using DriftCareer.Core.Vehicle;
using DriftCareer.Runtime.Input;
using UnityEngine;

namespace DriftCareer.Runtime
{
    /// <summary>
    /// Composition root (plain object, created once by <see cref="GameBootstrap"/>). Owns data services; no scene logic.
    /// Platform specifics (save location, store services) are swapped here, behind interfaces.
    /// </summary>
    public sealed class GameServices
    {
        public static GameServices Instance { get; private set; }

        public const string SettingsFile = "settings.json";
        public const string ProfileFile = "profile.json";
        public const string LayoutsDir = "layouts";

        public IFileStore Files { get; private set; }
        public VersionedDocumentStore<SettingsData> SettingsStore { get; private set; }
        public VersionedDocumentStore<PlayerProfile> ProfileStore { get; private set; }
        public VersionedDocumentStore<ObstacleLayout> LayoutStore { get; private set; }
        public SettingsData Settings { get; private set; }
        public PlayerProfile Profile { get; private set; }
        public LocalizationService Loc { get; private set; }
        public ContentCatalog Catalog { get; private set; }
        public SurfaceLibrary Surfaces { get; private set; }
        public ObstacleLibrary Obstacles { get; private set; }
        public AcademySite Site { get; private set; }
        public VehiclePhysicsProfile BaseProfile { get; private set; }
        public IPlatformServices Platform { get; private set; }
        public DeviceInfo Device { get; private set; }
        public PlatformDefaults Defaults { get; private set; }
        public InputRouter Input { get; private set; }
        public IVehicleIdentificationService Identification { get; private set; }
        public readonly List<string> StartupWarnings = new List<string>();

        public static GameServices Create()
        {
            var s = new GameServices();
            s.Build();
            Instance = s;
            return s;
        }

        private void Build()
        {
            Files = new LocalFileStore(Path.Combine(Application.persistentDataPath, "DriftCareer"));
            SettingsStore = new VersionedDocumentStore<SettingsData>(Files, new SettingsCodec());
            ProfileStore = new VersionedDocumentStore<PlayerProfile>(Files, new ProfileCodec());
            Obstacles = ObstacleLibrary.CreateDefault();
            LayoutStore = new VersionedDocumentStore<ObstacleLayout>(Files, new LayoutCodec(Obstacles));

            Settings = SettingsStore.Load(SettingsFile, out var sr);
            Report("settings", sr);
            Profile = ProfileStore.Load(ProfileFile, out var pr);
            Report("profile", pr);

            Loc = new LocalizationService();
            LoadLocalization("ru");
            LoadLocalization("en");
            if (Settings.language == null) Settings.language = Application.systemLanguage == SystemLanguage.Russian ? "ru" : "en";
            Loc.SetLanguage(Settings.language);

            var catalogAsset = Resources.Load<TextAsset>("DriftCareer/Catalog/catalog");
            Catalog = catalogAsset != null ? CatalogJson.Parse(catalogAsset.text) : new ContentCatalog();
            foreach (var e in Catalog.Validate(Loc.Has, id => id == "procedural:academy")) StartupWarnings.Add("catalog: " + e);

            Surfaces = SurfaceLibrary.CreateDefault();
            Site = new AcademySite();
            BaseProfile = VehiclePhysicsProfile.CreatePlaceholderRwd();
            Identification = new GeometryOnlyIdentificationService();

            Device = new DeviceInfo
            {
                kind = Application.isMobilePlatform ? PlatformKind.Mobile : (Application.isConsolePlatform ? PlatformKind.Console : PlatformKind.Desktop),
                systemMemoryMb = SystemInfo.systemMemorySize,
                graphicsMemoryMb = SystemInfo.graphicsMemorySize,
                processorCount = SystemInfo.processorCount,
                touchSupported = UnityEngine.Input.touchSupported,
                gyroSupported = SystemInfo.supportsGyroscope,
            };
            Defaults = DeviceTierClassifier.DefaultsFor(Device, Settings.quality);
            Platform = new LocalPlatformServices(Device.kind);
            Input = new InputRouter { Settings = Settings };
            foreach (var w in StartupWarnings) Debug.LogWarning("[DriftCareer] " + w);
        }

        private void Report(string what, LoadReport r)
        {
            if (r.status == LoadStatus.CorruptReplacedWithDefault || r.status == LoadStatus.RecoveredFromBackup || r.status == LoadStatus.RecoveredFromTemp)
                StartupWarnings.Add($"{what}: {r.status} ({r.error}) quarantined={r.quarantinedPath}");
        }

        private void LoadLocalization(string lang)
        {
            var asset = Resources.Load<TextAsset>("DriftCareer/Localization/" + lang);
            if (asset == null) { StartupWarnings.Add("missing localization " + lang); return; }
            try { Loc.LoadTable(lang, asset.text); }
            catch (Core.JsonException e) { StartupWarnings.Add($"localization {lang}: {e.Message}"); }
        }

        public string T(string key) => Loc.Get(key);

        public AssistSettings AssistSettings()
        {
            var a = Core.Assists.AssistSettings.For(Settings.assistPreset);
            a.transmission = Settings.manualTransmission ? TransmissionMode.Manual : TransmissionMode.Automatic;
            return a;
        }

        public void SaveSettings()
        {
            Settings.Sanitize();
            Input.Settings = Settings;
            Try(() => SettingsStore.Save(SettingsFile, Settings));
        }

        public void SaveProfile() => Try(() => ProfileStore.Save(ProfileFile, Profile));

        public void SaveLayout(ObstacleLayout layout)
        {
            layout.name = LayoutNames.Sanitize(layout.name);
            Try(() => LayoutStore.Save(LayoutsDir + "/" + LayoutNames.FileSlug(layout.name) + ".json", layout));
            Profile.lastLayoutName = layout.name;
            SaveProfile();
        }

        public List<string> ListLayoutFiles()
        {
            var list = new List<string>();
            try { list.AddRange(Files.List(LayoutsDir, ".json")); }
            catch (IOException e) { Debug.LogWarning("[DriftCareer] list layouts: " + e.Message); }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        public ObstacleLayout LoadLayoutFile(string path)
        {
            var l = LayoutStore.Load(path, out var r);
            if (r.status == LoadStatus.CorruptReplacedWithDefault) Debug.LogWarning("[DriftCareer] Corrupt layout replaced: " + path);
            return l;
        }

        public ObstacleLayout LoadLastLayoutOrDefault()
        {
            if (!string.IsNullOrEmpty(Profile.lastLayoutName))
            {
                string path = LayoutsDir + "/" + LayoutNames.FileSlug(Profile.lastLayoutName) + ".json";
                if (Files.Exists(path)) return LoadLayoutFile(path);
            }
            var l = new ObstacleLayout { name = "Academy" };
            foreach (var p in LayoutPresets.Create("donut", Site)) l.placements.Add(p);
            return l;
        }

        private static void Try(Action a)
        {
            try { a(); }
            catch (IOException e) { Debug.LogError("[DriftCareer] Save failed: " + e.Message); }
            catch (UnauthorizedAccessException e) { Debug.LogError("[DriftCareer] Save failed: " + e.Message); }
        }
    }
}

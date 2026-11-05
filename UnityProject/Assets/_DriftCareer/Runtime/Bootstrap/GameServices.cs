using System;
using System.Collections.Generic;
using System.IO;
using DriftCareer.Core.Account;
using DriftCareer.Core.Assists;
using DriftCareer.Core.Gameplay;
using DriftCareer.Core.Catalog;
using DriftCareer.Core.Garage;
using DriftCareer.Core.Layout;
using DriftCareer.Core.Localization;
using DriftCareer.Core.Persistence;
using DriftCareer.Core.Platform;
using DriftCareer.Core.Surfaces;
using DriftCareer.Core.Vehicle;
using DriftCareer.Core;
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
        public IAccountService Account { get; private set; }
        public ChallengeTracker Challenges { get; private set; }
        public const string GhostsDir = "ghosts";
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
            if (sr.status == LoadStatus.CreatedDefault)
                Settings.language = Application.systemLanguage == SystemLanguage.Russian || Application.systemLanguage == SystemLanguage.Ukrainian
                    || Application.systemLanguage == SystemLanguage.Belarusian ? "ru" : "en";
            Profile = ProfileStore.Load(ProfileFile, out var pr);
            Report("profile", pr);
            if (!Profile.ownedVehicles.Contains(Profile.selectedVehicleId)) Profile.selectedVehicleId = "placeholder_rwd";

            Loc = new LocalizationService();
            LoadLocalization("ru");
            LoadLocalization("en");
            Loc.SetLanguage(Settings.language);

            var catalogAsset = Resources.Load<TextAsset>("DriftCareer/Catalog/catalog");
            Catalog = catalogAsset != null ? CatalogJson.Parse(catalogAsset.text) : new ContentCatalog();
            foreach (var e in Catalog.Validate(Loc.Has, id => id == "procedural:academy")) StartupWarnings.Add("catalog: " + e);

            LoadUserCars();
            Surfaces = SurfaceLibrary.CreateDefault();
            Site = new AcademySite();
            BaseProfile = BaseProfileFor(Profile.selectedVehicleId);
            Identification = new GeometryOnlyIdentificationService();

            Device = new DeviceInfo
            {
                kind = Application.isMobilePlatform ? PlatformKind.Mobile : (Application.isConsolePlatform ? PlatformKind.Console : PlatformKind.Desktop),
                systemMemoryMb = SystemInfo.systemMemorySize,
                graphicsMemoryMb = SystemInfo.graphicsMemorySize,
                processorCount = SystemInfo.processorCount,
                touchSupported = TouchSupported(),
                gyroSupported = SystemInfo.supportsGyroscope,
            };
            Defaults = DeviceTierClassifier.DefaultsFor(Device, Settings.quality);
            Platform = new LocalPlatformServices(Device.kind);
            Account = new LocalAccountService(PlatformPolicy.DefaultStore(Device.kind));
            Challenges = new ChallengeTracker(Profile.challenges, DateTime.UtcNow);
            Input = new InputRouter { Settings = Settings };
            foreach (var w in StartupWarnings) Debug.LogWarning("[DriftCareer] " + w);
        }

        private static bool TouchSupported()
        {
#if ENABLE_INPUT_SYSTEM && DC_INPUT_SYSTEM
            return UnityEngine.InputSystem.Touchscreen.current != null || Application.isMobilePlatform;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return UnityEngine.Input.touchSupported;
#else
            return Application.isMobilePlatform;
#endif
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

        /// <summary>Base physics for a catalog vehicle ("spec:&lt;template&gt;" or the placeholder).</summary>
        public readonly Dictionary<string, CustomVehicleManifest> UserCars = new Dictionary<string, CustomVehicleManifest>(StringComparer.Ordinal);
        public const string UserCarsResources = "DriftCareer/UserCars";

        /// <summary>
        /// Player cars created with "Drift Career > Custom Car Import" (editor) live in Resources as manifest + prefab.
        /// They are offered only where the platform policy allows user models (PC); consoles/mobile get them via the
        /// future moderated server pipeline, never as raw files or asset bundles.
        /// </summary>
        private void LoadUserCars()
        {
            var policy = PlatformPolicy.For(PlatformPolicy.DefaultStore(Application.isMobilePlatform ? PlatformKind.Mobile : Application.isConsolePlatform ? PlatformKind.Console : PlatformKind.Desktop));
            if (!policy.allowUserModelImport) return;
            foreach (var asset in Resources.LoadAll<TextAsset>(UserCarsResources))
            {
                try
                {
                    var m = CustomVehicleJson.Decode(JsonParser.Parse(asset.text));
                    if (string.IsNullOrEmpty(m.id) || !m.rightsAttested) continue;
                    UserCars["user:" + m.id] = m;
                    Catalog.Add(new Core.Catalog.VehicleDefinition
                    {
                        id = "user:" + m.id, titleKey = m.displayName, descriptionKey = "menu.custom_cars.desc", availability = Core.Catalog.FeatureAvailability.Playable,
                        physicsProfileId = "user:" + m.id, visualId = "model:" + m.id, sortOrder = 100
                    });
                    if (!Profile.ownedVehicles.Contains("user:" + m.id)) Profile.ownedVehicles.Add("user:" + m.id);
                }
                catch (Core.JsonException e) { StartupWarnings.Add("user car " + asset.name + ": " + e.Message); }
            }
        }

        public VehiclePhysicsProfile BaseProfileFor(string vehicleId)
        {
            if (vehicleId != null && UserCars.TryGetValue(vehicleId, out var userCar)) return SpecToProfileConverter.Convert(userCar.spec);
            var def = Catalog.Vehicle(vehicleId);
            if (def != null && def.physicsProfileId != null && def.physicsProfileId.StartsWith("spec:", StringComparison.Ordinal))
                return SpecToProfileConverter.Convert(SpecTemplates.Create(def.physicsProfileId.Substring(5)));
            return VehiclePhysicsProfile.CreatePlaceholderRwd();
        }

        public void SelectVehicle(string vehicleId)
        {
            if (Catalog.Vehicle(vehicleId) == null && vehicleId != "placeholder_rwd") return;
            Profile.selectedVehicleId = vehicleId;
            BaseProfile = BaseProfileFor(vehicleId);
            SaveProfile();
        }

        public string BodyStyleFor(string vehicleId)
        {
            if (vehicleId != null && UserCars.ContainsKey(vehicleId)) return "model:" + vehicleId.Substring(5);
            var def = Catalog.Vehicle(vehicleId);
            return def != null && def.visualId != null && def.visualId.EndsWith("sedan", StringComparison.Ordinal) ? "sedan" : "coupe";
        }

        public static int VehiclePrice(string vehicleId) =>
            vehicleId == "placeholder_rwd" || vehicleId == "classic_sedan_12" ? 0 : vehicleId == "jdm_coupe_turbo" ? 25000 : 6000;

        public void SaveGhost(GhostRecording g, string name)
        {
            Try(() => Files.WriteTextAtomic(GhostsDir + "/" + name + ".json", JsonWriter.Write(g.ToJson(), false)));
        }

        public GhostRecording LoadGhost(string name)
        {
            string path = GhostsDir + "/" + name + ".json";
            try
            {
                if (!Files.Exists(path)) return null;
                return GhostRecording.FromJson(JsonParser.Parse(Files.ReadText(path)));
            }
            catch (Core.JsonException e) { Debug.LogWarning("[DriftCareer] ghost: " + e.Message); return null; }
            catch (IOException e) { Debug.LogWarning("[DriftCareer] ghost: " + e.Message); return null; }
        }

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

using System;
using System.IO;
using DriftCareer.Core;
using DriftCareer.Core.Catalog;
using DriftCareer.Core.Garage;
using DriftCareer.Core.Layout;
using DriftCareer.Core.Localization;
using DriftCareer.Core.Persistence;
using NUnit.Framework;

namespace DriftCareer.Tests.EditMode
{
    public class DataAndLayoutTests
    {
        // ---------------- JSON ----------------
        [Test]
        public void Json_RoundTrip_PreservesValues()
        {
            var o = JsonValue.NewObject().Set("a", 0.3f).Set("s", "привет \"q\"\n").Set("b", true).Set("i", 42);
            o.Set("arr", JsonValue.NewArray().Add(JsonValue.Number(1)).Add(JsonValue.Null()));
            var text = JsonWriter.Write(o);
            var back = JsonParser.Parse(text);
            Assert.AreEqual(0.3f, back.GetFloat("a"));
            Assert.AreEqual("привет \"q\"\n", back.GetString("s"));
            Assert.IsTrue(back.GetBool("b"));
            Assert.AreEqual(42, back.GetInt("i"));
            Assert.AreEqual(2, back.GetArray("arr").Count);
            StringAssert.Contains("0.3", text);
        }

        [TestCase("{")]
        [TestCase("{\"a\":}")]
        [TestCase("[1,2")]
        [TestCase("{\"a\":1}x")]
        [TestCase("{\"a\":NaN}")]
        public void Json_Malformed_Throws(string bad)
        {
            Assert.Throws<JsonException>(() => JsonParser.Parse(bad));
        }

        [Test]
        public void Json_DeepNesting_IsRejected()
        {
            Assert.Throws<JsonException>(() => JsonParser.Parse(new string('[', 200) + new string(']', 200)));
        }

        // ---------------- Persistence ----------------
        private static VersionedDocumentStore<SettingsData> SettingsStore(MemoryFileStore fs) => new VersionedDocumentStore<SettingsData>(fs, new SettingsCodec());

        [Test]
        public void Settings_SaveLoad_RoundTrip()
        {
            var fs = new MemoryFileStore();
            var store = SettingsStore(fs);
            var s = new SettingsData { language = "en", cameraMode = CameraMode.Cockpit, steeringSensitivity = 1.4f };
            s.health.blowouts = false;
            store.Save("settings.json", s);
            var loaded = store.Load("settings.json", out var report);
            Assert.AreEqual(LoadStatus.Loaded, report.status);
            Assert.AreEqual("en", loaded.language);
            Assert.AreEqual(CameraMode.Cockpit, loaded.cameraMode);
            Assert.AreEqual(1.4f, loaded.steeringSensitivity, 1e-5);
            Assert.IsFalse(loaded.health.blowouts);
        }

        [Test]
        public void CorruptSave_RecoversFromBackup_AndQuarantines()
        {
            var fs = new MemoryFileStore();
            var store = SettingsStore(fs);
            store.Save("settings.json", new SettingsData { language = "en" });
            store.Save("settings.json", new SettingsData { language = "ru" });
            fs.Files["settings.json"] = fs.Files["settings.json"].Substring(0, 30); // truncated write
            var loaded = store.Load("settings.json", out var report);
            Assert.AreEqual(LoadStatus.RecoveredFromBackup, report.status);
            Assert.AreEqual("en", loaded.language);
            Assert.IsNotNull(report.quarantinedPath);
            Assert.IsTrue(fs.Exists(report.quarantinedPath));
        }

        [Test]
        public void TamperedChecksum_IsDetected()
        {
            var fs = new MemoryFileStore();
            var store = SettingsStore(fs);
            store.Save("s.json", new SettingsData());
            fs.Files["s.json"] = fs.Files["s.json"].Replace("\"ru\"", "\"en\"");
            fs.Files.Remove("s.json.bak");
            store.Load("s.json", out var report);
            Assert.AreEqual(LoadStatus.CorruptReplacedWithDefault, report.status);
            StringAssert.Contains("checksum", report.error);
        }

        [Test]
        public void MissingSave_CreatesDefault()
        {
            var store = SettingsStore(new MemoryFileStore());
            var s = store.Load("none.json", out var report);
            Assert.AreEqual(LoadStatus.CreatedDefault, report.status);
            Assert.IsNotNull(s);
        }

        [Test]
        public void LocalFileStore_AtomicWrite_AndRejectsTraversal()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dc_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                var fs = new LocalFileStore(dir);
                fs.WriteTextAtomic("a/b.json", "1");
                fs.WriteTextAtomic("a/b.json", "2");
                Assert.AreEqual("2", fs.ReadText("a/b.json"));
                Assert.IsTrue(fs.Exists("a/b.json.bak"));
                Assert.IsFalse(fs.Exists("a/b.json.tmp"));
                Assert.Throws<UnauthorizedAccessException>(() => fs.ReadText("../../etc/passwd"));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public void Profile_WithTuning_RoundTrips()
        {
            var fs = new MemoryFileStore();
            var store = new VersionedDocumentStore<PlayerProfile>(fs, new ProfileCodec());
            var p = new PlayerProfile();
            var t = TuningPresets.Create("pro_drift");
            t.visual.paintColor = "#00B5D8";
            t.visual.parts[PartSlot.Spoiler] = "spoiler.gt_wing";
            t.gearRatios = new[] { 3.2f, 2.1f, 1.5f, 1.15f, 0.95f };
            p.tunings["placeholder_rwd"] = t;
            p.RecordDrill("drill.donut", 12.5f, 3400f);
            store.Save("profile.json", p);
            var back = store.Load("profile.json", out var report).TuningFor("placeholder_rwd");
            Assert.AreEqual(LoadStatus.Loaded, report.status);
            Assert.AreEqual(t.powerScale, back.powerScale, 1e-4);
            Assert.AreEqual(t.diffType, back.diffType);
            Assert.AreEqual("#00B5D8", back.visual.paintColor);
            Assert.AreEqual("spoiler.gt_wing", back.visual.PartFor(PartSlot.Spoiler));
            Assert.AreEqual(5, back.gearRatios.Length);
        }

        // ---------------- Catalog / localization ----------------
        private static string FindRepoFile(string relative)
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
                string inProject = Path.Combine(dir.FullName, "UnityProject", relative);
                if (File.Exists(inProject)) return inProject;
                dir = dir.Parent;
            }
            return null;
        }

        private const string CatalogPath = "Assets/_DriftCareer/Resources/DriftCareer/Catalog/catalog.json";
        private const string RuPath = "Assets/_DriftCareer/Resources/DriftCareer/Localization/ru.json";
        private const string EnPath = "Assets/_DriftCareer/Resources/DriftCareer/Localization/en.json";

        private static LocalizationService LoadLocalization()
        {
            var loc = new LocalizationService();
            loc.LoadTable("ru", File.ReadAllText(FindRepoFile(RuPath)));
            loc.LoadTable("en", File.ReadAllText(FindRepoFile(EnPath)));
            return loc;
        }

        [Test]
        public void ShippedCatalog_IsConsistent_AndOnlyAcademyIsPlayable()
        {
            string path = FindRepoFile(CatalogPath);
            Assert.IsNotNull(path, "catalog.json not found");
            var catalog = CatalogJson.Parse(File.ReadAllText(path));
            var loc = LoadLocalization();
            var errors = catalog.Validate(k => loc.HasIn("ru", k) && loc.HasIn("en", k), id => id == "procedural:academy");
            Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
            int playableTracks = 0;
            foreach (var t in catalog.Tracks)
            {
                if (t.IsPlayable) { playableTracks++; Assert.AreEqual("academy", t.id); }
                else Assert.AreNotEqual(LaunchBlockReason.None, catalog.CanLaunch(t.id, "free_drive"), t.id + " must not launch");
            }
            Assert.AreEqual(1, playableTracks);
            Assert.AreEqual(LaunchBlockReason.None, catalog.CanLaunch("academy", "free_drive"));
            Assert.AreEqual(LaunchBlockReason.ModeNotPlayable, catalog.CanLaunch("academy", "online_tandem"));
        }

        [Test]
        public void Localization_RuAndEn_HaveSameKeys()
        {
            var loc = LoadLocalization();
            Assert.AreEqual(0, loc.MissingKeys("en", "ru").Count, "EN missing: " + string.Join(", ", loc.MissingKeys("en", "ru")));
            Assert.AreEqual(0, loc.MissingKeys("ru", "en").Count, "RU missing: " + string.Join(", ", loc.MissingKeys("ru", "en")));
            loc.SetLanguage("ru");
            Assert.AreEqual("Скоро", loc.Get("common.coming_soon"));
            loc.SetLanguage("en");
            Assert.AreEqual("Coming Soon", loc.Get("common.coming_soon"));
            Assert.AreEqual("no.such.key", loc.Get("no.such.key"));
        }

        [Test]
        public void TuningAndObstacleKeys_AreLocalized()
        {
            var loc = LoadLocalization();
            foreach (var p in TuningCatalog.Parameters) Assert.IsTrue(loc.HasIn("ru", p.titleKey), p.titleKey);
            foreach (var d in ObstacleLibrary.CreateDefault().All) Assert.IsTrue(loc.HasIn("ru", d.titleKey), d.titleKey);
            foreach (var part in VisualPartCatalog.Parts) Assert.IsTrue(loc.HasIn("ru", part.titleKey), part.titleKey);
        }

        // ---------------- Layout editor ----------------
        private static LayoutEditorModel NewEditor(out AcademySite site, float budget = 250f)
        {
            site = new AcademySite();
            return new LayoutEditorModel(ObstacleLibrary.CreateDefault(), site.CreateRules(budget));
        }

        [Test]
        public void Layout_PlaceTwentyObjects_UndoRedo()
        {
            var ed = NewEditor(out var site);
            for (int i = 0; i < 20; i++)
                Assert.IsTrue(ed.Place("cone", site.SandboxCenter + new Vec2(i * 1.5f - 15f, 0f), 0f, out _).Ok, "cone " + i);
            Assert.AreEqual(20, ed.Count);
            Assert.IsTrue(ed.Undo());
            Assert.AreEqual(19, ed.Count);
            Assert.IsTrue(ed.Redo());
            Assert.AreEqual(20, ed.Count);
        }

        [Test]
        public void Layout_BlocksHangarExitSpawnAndOutOfBounds()
        {
            var ed = NewEditor(out var site);
            var r = ed.CanPlace("cone", site.DoorCenter + new Vec2(0f, 5f), 0f);
            Assert.AreEqual(PlacementError.ForbiddenZone, r.error);
            Assert.AreEqual("exit_lane", r.detail);
            Assert.AreEqual(PlacementError.ForbiddenZone, ed.CanPlace("tire", site.SpawnPosition, 0f).error);
            Assert.AreEqual(PlacementError.OutOfBounds, ed.CanPlace("cone", new Vec2(500f, 0f), 0f).error);
            Assert.AreEqual(PlacementError.NonFinite, ed.CanPlace("cone", new Vec2(float.NaN, 0f), 0f).error);
            Assert.AreEqual(PlacementError.UnknownType, ed.CanPlace("tank", Vec2.Zero, 0f).error);
            // Decals (paint markers) are allowed on the exit lane.
            Assert.IsTrue(ed.CanPlace("marker", site.DoorCenter + new Vec2(0f, 8f), 0f).Ok);
        }

        [Test]
        public void Layout_OverlapAndBudgetLimits()
        {
            var ed = NewEditor(out var site, budget: 10f);
            Assert.IsTrue(ed.Place("tire_wall", site.SandboxCenter, 0f, out _).Ok);
            Assert.AreEqual(PlacementError.Overlap, ed.CanPlace("cone", site.SandboxCenter + new Vec2(1f, 0f), 0f).error);
            for (int i = 0; i < 8; i++) ed.Place("cone", site.SandboxCenter + new Vec2(0f, 5f + i * 2f), 0f, out _);
            Assert.AreEqual(PlacementError.BudgetExceeded, ed.CanPlace("plastic_barrier", site.SandboxCenter + new Vec2(10f, 10f), 0f).error);
        }

        [Test]
        public void Layout_MoveGesture_IsOneUndoStep_AndRejectsInvalid()
        {
            var ed = NewEditor(out var site);
            ed.Place("cone", site.SandboxCenter, 0f, out int uid);
            ed.BeginGesture();
            for (int i = 1; i <= 10; i++) ed.PreviewTransform(uid, site.SandboxCenter + new Vec2(i, 0f), 0f);
            var bad = ed.PreviewTransform(uid, site.DoorCenter + new Vec2(0f, 3f), 0f);
            Assert.IsFalse(bad.Ok);
            ed.CommitGesture();
            Assert.AreEqual(site.SandboxCenter.X + 10f, ed.Layout.placements[0].position.X, 1e-4);
            ed.Undo();
            Assert.AreEqual(site.SandboxCenter.X, ed.Layout.placements[0].position.X, 1e-4);
        }

        [Test]
        public void Layout_Presets_AreValidAndDoNotBlockExit()
        {
            var ed = NewEditor(out var site, budget: 400f);
            foreach (var id in LayoutPresets.Ids)
            {
                int skipped = ed.ApplyPreset(LayoutPresets.Create(id, site));
                Assert.AreEqual(0, skipped, id + " has invalid placements");
            }
        }

        [Test]
        public void Layout_SaveLoad_SanitizesCorruptEntries()
        {
            var lib = ObstacleLibrary.CreateDefault();
            var fs = new MemoryFileStore();
            var store = new VersionedDocumentStore<ObstacleLayout>(fs, new LayoutCodec(lib));
            var ed = NewEditor(out var site);
            ed.ApplyPreset(LayoutPresets.Create("slalom", site));
            ed.Layout.name = "Мой слалом";
            store.Save("layouts/slalom.json", ed.Layout);
            var back = store.Load("layouts/slalom.json", out var report);
            Assert.AreEqual(LoadStatus.Loaded, report.status);
            Assert.AreEqual(ed.Count, back.placements.Count);
            Assert.AreEqual("Мой слалом", back.name);

            string json = "{\"track\":\"academy\",\"name\":\"x\",\"objects\":[{\"uid\":1,\"type\":\"cone\",\"x\":1,\"z\":2},{\"uid\":1,\"type\":\"cone\",\"x\":3,\"z\":4},{\"type\":\"nuke\",\"x\":0,\"z\":0},{\"type\":\"cone\",\"x\":\"bad\"}]}";
            var decoded = new LayoutCodec(lib).Decode(JsonParser.Parse(json), 1);
            Assert.AreEqual(2, decoded.placements.Count);
            Assert.AreNotEqual(decoded.placements[0].uid, decoded.placements[1].uid);
        }

        [Test]
        public void LayoutNames_AreSanitized()
        {
            Assert.AreEqual("custom", LayoutNames.Sanitize("../../"));
            Assert.AreEqual("мой_слалом", LayoutNames.FileSlug("Мой слалом"));
        }
    }
}

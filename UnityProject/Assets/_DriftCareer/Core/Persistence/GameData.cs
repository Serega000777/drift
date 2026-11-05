using System;
using System.Collections.Generic;
using DriftCareer.Core.Assists;
using DriftCareer.Core.Gameplay;
using DriftCareer.Core.Garage;
using DriftCareer.Core.Vehicle;

namespace DriftCareer.Core.Persistence
{
    public enum ControlScheme { Auto = 0, TouchWheel = 1, TouchSlider = 2, TouchButtons = 3, Tilt = 4 }
    public enum CameraMode { Chase = 0, ChaseFar = 1, Hood = 2, Cockpit = 3, Bumper = 4 }
    public enum QualityTier { Auto = 0, Low = 1, Medium = 2, High = 3, Ultra = 4 }
    public enum SpeedUnits { Kmh = 0, Mph = 1 }

    [Serializable]
    public sealed class SettingsData
    {
        public string language = "ru";
        public AssistPreset assistPreset = AssistPreset.Balanced;
        public bool manualTransmission;
        public ControlScheme controlScheme = ControlScheme.Auto;
        public float steeringSensitivity = 1f;
        public float steeringDeadzone = 0.05f;
        public bool leftHanded;
        public float tiltSensitivity = 1f;
        public SpeedUnits units = SpeedUnits.Kmh;
        public CameraMode cameraMode = CameraMode.Chase;
        public float fieldOfView = 60f;
        public QualityTier quality = QualityTier.Auto;
        public int targetFps = 60;
        public float masterVolume = 0.9f;
        public float engineVolume = 1f;
        public float effectsVolume = 1f;
        public float musicVolume = 0.6f;
        public bool vibration = true;
        public bool showTelemetry;
        public float hudScale = 1f;
        public bool playIntro = true;
        public bool autoRollOut = true;
        public bool ghostEnabled = true;
        public HealthSettings health = new HealthSettings();

        // Detailed graphics
        public float renderScale = 1f;
        public int shadowQuality = 2;          // 0 off, 1 low, 2 medium, 3 high
        public float shadowDistance = 120f;
        public int antiAliasing = 2;           // 0 off, 1 FXAA, 2 SMAA, 3 MSAA 4x
        public bool postProcessing = true;
        public bool bloom = true;
        public bool ambientOcclusion = true;
        public bool motionBlur;
        public bool vignette = true;
        public bool filmGrain;
        public bool reflections = true;
        public bool hdr = true;
        public bool vSync;
        public float particleDensity = 1f;

        // Camera
        public float chaseDistance = 1f;
        public float chaseHeight = 1f;
        public float cameraShake = 1f;
        public float driftLook = 1f;

        // Controls extras
        public float steeringLinearity = 1f;   // exponent applied to stick/touch steering
        public bool handbrakeToggle;

        // HUD / accessibility
        public float hudOpacity = 1f;
        public bool showScore = true;
        public bool showTireDiagram = true;
        public bool showAngleMeter = true;
        public bool showHudButtons = true;
        public bool reduceMotion;
        public bool colorblindHud;

        // Audio extras
        public float tiresVolume = 1f;
        public float uiVolume = 0.8f;

        public SettingsData Clone()
        {
            var c = (SettingsData)MemberwiseClone();
            c.health = (health ?? new HealthSettings()).Clone();
            return c;
        }

        public void Sanitize()
        {
            steeringSensitivity = MathUtil.Clamp(MathUtil.Finite(steeringSensitivity, 1f), 0.3f, 2f);
            steeringDeadzone = MathUtil.Clamp(MathUtil.Finite(steeringDeadzone, 0.05f), 0f, 0.3f);
            tiltSensitivity = MathUtil.Clamp(MathUtil.Finite(tiltSensitivity, 1f), 0.3f, 3f);
            fieldOfView = MathUtil.Clamp(MathUtil.Finite(fieldOfView, 60f), 45f, 90f);
            targetFps = targetFps <= 30 ? 30 : (targetFps <= 60 ? 60 : (targetFps <= 90 ? 90 : 120));
            masterVolume = MathUtil.Clamp01(MathUtil.Finite(masterVolume, 0.9f));
            engineVolume = MathUtil.Clamp01(MathUtil.Finite(engineVolume, 1f));
            effectsVolume = MathUtil.Clamp01(MathUtil.Finite(effectsVolume, 1f));
            musicVolume = MathUtil.Clamp01(MathUtil.Finite(musicVolume, 0.6f));
            hudScale = MathUtil.Clamp(MathUtil.Finite(hudScale, 1f), 0.7f, 1.4f);
            renderScale = MathUtil.Clamp(MathUtil.Finite(renderScale, 1f), 0.5f, 1.5f);
            shadowQuality = MathUtil.Clamp(shadowQuality, 0, 3);
            shadowDistance = MathUtil.Clamp(MathUtil.Finite(shadowDistance, 120f), 30f, 300f);
            antiAliasing = MathUtil.Clamp(antiAliasing, 0, 3);
            particleDensity = MathUtil.Clamp(MathUtil.Finite(particleDensity, 1f), 0.2f, 1.5f);
            chaseDistance = MathUtil.Clamp(MathUtil.Finite(chaseDistance, 1f), 0.7f, 1.6f);
            chaseHeight = MathUtil.Clamp(MathUtil.Finite(chaseHeight, 1f), 0.6f, 1.8f);
            cameraShake = MathUtil.Clamp01(MathUtil.Finite(cameraShake, 1f));
            driftLook = MathUtil.Clamp(MathUtil.Finite(driftLook, 1f), 0f, 1.5f);
            steeringLinearity = MathUtil.Clamp(MathUtil.Finite(steeringLinearity, 1f), 0.5f, 2.5f);
            hudOpacity = MathUtil.Clamp(MathUtil.Finite(hudOpacity, 1f), 0.3f, 1f);
            tiresVolume = MathUtil.Clamp01(MathUtil.Finite(tiresVolume, 1f));
            uiVolume = MathUtil.Clamp01(MathUtil.Finite(uiVolume, 0.8f));
            if (health == null) health = new HealthSettings();
            health.tireWearMultiplier = MathUtil.Clamp(MathUtil.Finite(health.tireWearMultiplier, 1f), 0f, 5f);
            if (language != "ru" && language != "en") language = "ru";
        }
    }

    [Serializable]
    public sealed class DrillRecord
    {
        public string drillId;
        public float bestTimeSec;
        public float bestScore;
        public int completions;
    }

    [Serializable]
    public sealed class PlayerProfile
    {
        public string profileId = Guid.NewGuid().ToString("N");
        public string createdUtc = DateTime.UtcNow.ToString("o");
        public string selectedVehicleId = "placeholder_rwd";
        public Dictionary<string, VehicleTuningState> tunings = new Dictionary<string, VehicleTuningState>(StringComparer.Ordinal);
        /// <summary>Named setups saved by the player ("my donut setup").</summary>
        public List<VehicleTuningState> savedSetups = new List<VehicleTuningState>();
        public Dictionary<string, DrillRecord> drills = new Dictionary<string, DrillRecord>(StringComparer.Ordinal);
        public float totalDriftPoints;
        public float bestChain;
        public float distanceDrivenKm;
        public float playTimeSec;
        public string lastLayoutName = "";
        public List<string> customVehicleIds = new List<string>();
        public Wallet wallet = new Wallet { credits = 5000 };
        /// <summary>Owned tuning items/engines/parts (stock items are implicit).</summary>
        public HashSet<string> ownedItems = new HashSet<string>(StringComparer.Ordinal) { "tire.Sport", "tire.DriftSemiSlick", "tire.Street" };
        public List<string> ownedVehicles = new List<string> { "placeholder_rwd", "classic_sedan_12" };
        public Dictionary<string, ChallengeProgress> challenges = new Dictionary<string, ChallengeProgress>(StringComparer.Ordinal);
        public Dictionary<string, float> bestScores = new Dictionary<string, float>(StringComparer.Ordinal);
        public Dictionary<string, int> bestMedals = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Set once a save failed signature verification (edited outside the game). Sticky: online records are not trusted.</summary>
        public bool integrityCompromised;
        /// <summary>Public nickname (validated by PlayerIdentity). Empty = default "Drifter-XXXX".</summary>
        public string nickname = "";
        public Character.CharacterAppearance character = new Character.CharacterAppearance();

        public bool RecordScore(string key, float score, Medal medal)
        {
            bool improved = false;
            if (!bestScores.TryGetValue(key, out var b) || score > b) { bestScores[key] = score; improved = true; }
            if (!bestMedals.TryGetValue(key, out var m) || (int)medal > m) bestMedals[key] = (int)medal;
            return improved;
        }

        public VehicleTuningState TuningFor(string vehicleId)
        {
            if (!tunings.TryGetValue(vehicleId, out var t))
            {
                t = VehicleTuningState.Stock(vehicleId);
                tunings[vehicleId] = t;
            }
            return t;
        }

        public void RecordDrill(string id, float timeSec, float score)
        {
            if (!drills.TryGetValue(id, out var r)) drills[id] = r = new DrillRecord { drillId = id, bestTimeSec = float.MaxValue };
            r.completions++;
            if (timeSec > 0f && timeSec < r.bestTimeSec) r.bestTimeSec = timeSec;
            if (score > r.bestScore) r.bestScore = score;
        }
    }

    public static class TuningJson
    {
        public static JsonValue Encode(VehicleTuningState t)
        {
            var o = JsonValue.NewObject()
                .Set("version", t.version)
                .Set("vehicle", t.vehicleId ?? "")
                .Set("preset", t.presetName ?? "")
                .Set("compoundFront", t.compoundFront.ToString())
                .Set("compoundRear", t.compoundRear.ToString())
                .Set("diffType", t.diffType.ToString())
                .Set("engine", t.engineSwapId ?? "")
                .Set("stripped", t.strippedInterior).Set("cage", t.rollCage).Set("hydro", t.hydraulicHandbrake).Set("battery", t.batteryInTrunk);
            var values = JsonValue.NewObject();
            foreach (var p in TuningCatalog.Parameters) values.Set(p.id, p.Get(t));
            o.Set("values", values);
            if (t.gearRatios != null)
            {
                var arr = JsonValue.NewArray();
                foreach (var r in t.gearRatios) arr.Add(JsonValue.Number(r));
                o.Set("gearRatios", arr);
            }
            var v = t.visual ?? new VisualSetup();
            var vis = JsonValue.NewObject()
                .Set("paint", v.paintColor).Set("finish", v.paintFinish.ToString()).Set("secondary", v.secondaryColor)
                .Set("rim", v.rimColor).Set("caliper", v.caliperColor).Set("rimSize", v.rimSizeInch).Set("tint", v.windowTint)
                .Set("underglow", v.underglowColor ?? "");
            var parts = JsonValue.NewObject();
            foreach (var kv in v.parts) parts.Set(kv.Key.ToString(), kv.Value);
            vis.Set("parts", parts);
            o.Set("visual", vis);
            return o;
        }

        public static VehicleTuningState Decode(JsonValue o)
        {
            var t = VehicleTuningState.Stock(o.GetString("vehicle", "placeholder_rwd"));
            t.presetName = o.GetString("preset", "custom");
            t.compoundFront = o.GetEnum("compoundFront", t.compoundFront);
            t.compoundRear = o.GetEnum("compoundRear", t.compoundRear);
            t.diffType = o.GetEnum("diffType", t.diffType);
            t.engineSwapId = o.GetString("engine", "");
            t.strippedInterior = o.GetBool("stripped");
            t.rollCage = o.GetBool("cage");
            t.hydraulicHandbrake = o.GetBool("hydro");
            t.batteryInTrunk = o.GetBool("battery");
            var values = o.GetObject("values");
            if (values != null)
                foreach (var p in TuningCatalog.Parameters)
                    if (values.Has(p.id)) p.Set(t, values.GetFloat(p.id, p.Get(t)));
            var gears = o.GetArray("gearRatios");
            if (gears != null && gears.Count > 0)
            {
                t.gearRatios = new float[gears.Count];
                for (int i = 0; i < gears.Count; i++) t.gearRatios[i] = gears[i].Type == JsonType.Number ? gears[i].AsFloat : 1f;
            }
            var vis = o.GetObject("visual");
            if (vis != null)
            {
                var v = t.visual;
                v.paintColor = vis.GetString("paint", v.paintColor);
                v.paintFinish = vis.GetEnum("finish", v.paintFinish);
                v.secondaryColor = vis.GetString("secondary", v.secondaryColor);
                v.rimColor = vis.GetString("rim", v.rimColor);
                v.caliperColor = vis.GetString("caliper", v.caliperColor);
                v.rimSizeInch = vis.GetFloat("rimSize", v.rimSizeInch);
                v.windowTint = vis.GetFloat("tint", v.windowTint);
                v.underglowColor = vis.GetString("underglow", "");
                var parts = vis.GetObject("parts");
                if (parts != null)
                    foreach (var key in parts.Keys)
                        if (Enum.TryParse(key, out PartSlot slot)) v.parts[slot] = parts.GetString(key);
            }
            return TuningApplier.Sanitize(t);
        }
    }

    public sealed class SettingsCodec : IDocumentCodec<SettingsData>
    {
        public string SchemaId => "drift.settings";
        public int CurrentVersion => 1;
        public SettingsData CreateDefault() => new SettingsData();

        public JsonValue Encode(SettingsData s)
        {
            return JsonValue.NewObject()
                .Set("language", s.language).Set("assist", s.assistPreset.ToString()).Set("manual", s.manualTransmission)
                .Set("controls", s.controlScheme.ToString()).Set("steerSens", s.steeringSensitivity).Set("deadzone", s.steeringDeadzone)
                .Set("leftHanded", s.leftHanded).Set("tiltSens", s.tiltSensitivity).Set("units", s.units.ToString())
                .Set("camera", s.cameraMode.ToString()).Set("fov", s.fieldOfView).Set("quality", s.quality.ToString())
                .Set("fps", s.targetFps).Set("master", s.masterVolume).Set("engine", s.engineVolume).Set("sfx", s.effectsVolume)
                .Set("music", s.musicVolume).Set("vibration", s.vibration).Set("telemetry", s.showTelemetry).Set("hudScale", s.hudScale)
                .Set("intro", s.playIntro).Set("damage", s.health.damage.ToString()).Set("tireWear", s.health.tireWearMultiplier)
                .Set("blowouts", s.health.blowouts).Set("rollout", s.autoRollOut).Set("ghost", s.ghostEnabled)
                .Set("renderScale", s.renderScale).Set("shadows", s.shadowQuality).Set("shadowDist", s.shadowDistance).Set("aa", s.antiAliasing)
                .Set("post", s.postProcessing).Set("bloom", s.bloom).Set("ssao", s.ambientOcclusion).Set("motionBlur", s.motionBlur)
                .Set("vignette", s.vignette).Set("grain", s.filmGrain).Set("reflections", s.reflections).Set("hdr", s.hdr).Set("vsync", s.vSync)
                .Set("particles", s.particleDensity).Set("camDist", s.chaseDistance).Set("camHeight", s.chaseHeight).Set("shake", s.cameraShake)
                .Set("driftLook", s.driftLook).Set("linearity", s.steeringLinearity).Set("hbToggle", s.handbrakeToggle).Set("hudOpacity", s.hudOpacity)
                .Set("hudScore", s.showScore).Set("hudTires", s.showTireDiagram).Set("hudAngle", s.showAngleMeter).Set("hudButtons", s.showHudButtons)
                .Set("reduceMotion", s.reduceMotion).Set("colorblind", s.colorblindHud).Set("tiresVol", s.tiresVolume).Set("uiVol", s.uiVolume);
        }

        public SettingsData Decode(JsonValue j, int version)
        {
            var d = new SettingsData();
            var s = new SettingsData
            {
                language = j.GetString("language", d.language),
                assistPreset = j.GetEnum("assist", d.assistPreset),
                manualTransmission = j.GetBool("manual", d.manualTransmission),
                controlScheme = j.GetEnum("controls", d.controlScheme),
                steeringSensitivity = j.GetFloat("steerSens", d.steeringSensitivity),
                steeringDeadzone = j.GetFloat("deadzone", d.steeringDeadzone),
                leftHanded = j.GetBool("leftHanded", d.leftHanded),
                tiltSensitivity = j.GetFloat("tiltSens", d.tiltSensitivity),
                units = j.GetEnum("units", d.units),
                cameraMode = j.GetEnum("camera", d.cameraMode),
                fieldOfView = j.GetFloat("fov", d.fieldOfView),
                quality = j.GetEnum("quality", d.quality),
                targetFps = j.GetInt("fps", d.targetFps),
                masterVolume = j.GetFloat("master", d.masterVolume),
                engineVolume = j.GetFloat("engine", d.engineVolume),
                effectsVolume = j.GetFloat("sfx", d.effectsVolume),
                musicVolume = j.GetFloat("music", d.musicVolume),
                vibration = j.GetBool("vibration", d.vibration),
                showTelemetry = j.GetBool("telemetry", d.showTelemetry),
                hudScale = j.GetFloat("hudScale", d.hudScale),
                playIntro = j.GetBool("intro", d.playIntro),
            };
            s.health.damage = j.GetEnum("damage", s.health.damage);
            s.health.tireWearMultiplier = j.GetFloat("tireWear", s.health.tireWearMultiplier);
            s.health.blowouts = j.GetBool("blowouts", s.health.blowouts);
            s.autoRollOut = j.GetBool("rollout", d.autoRollOut);
            s.ghostEnabled = j.GetBool("ghost", d.ghostEnabled);
            s.renderScale = j.GetFloat("renderScale", d.renderScale);
            s.shadowQuality = j.GetInt("shadows", d.shadowQuality);
            s.shadowDistance = j.GetFloat("shadowDist", d.shadowDistance);
            s.antiAliasing = j.GetInt("aa", d.antiAliasing);
            s.postProcessing = j.GetBool("post", d.postProcessing);
            s.bloom = j.GetBool("bloom", d.bloom);
            s.ambientOcclusion = j.GetBool("ssao", d.ambientOcclusion);
            s.motionBlur = j.GetBool("motionBlur", d.motionBlur);
            s.vignette = j.GetBool("vignette", d.vignette);
            s.filmGrain = j.GetBool("grain", d.filmGrain);
            s.reflections = j.GetBool("reflections", d.reflections);
            s.hdr = j.GetBool("hdr", d.hdr);
            s.vSync = j.GetBool("vsync", d.vSync);
            s.particleDensity = j.GetFloat("particles", d.particleDensity);
            s.chaseDistance = j.GetFloat("camDist", d.chaseDistance);
            s.chaseHeight = j.GetFloat("camHeight", d.chaseHeight);
            s.cameraShake = j.GetFloat("shake", d.cameraShake);
            s.driftLook = j.GetFloat("driftLook", d.driftLook);
            s.steeringLinearity = j.GetFloat("linearity", d.steeringLinearity);
            s.handbrakeToggle = j.GetBool("hbToggle", d.handbrakeToggle);
            s.hudOpacity = j.GetFloat("hudOpacity", d.hudOpacity);
            s.showScore = j.GetBool("hudScore", d.showScore);
            s.showTireDiagram = j.GetBool("hudTires", d.showTireDiagram);
            s.showAngleMeter = j.GetBool("hudAngle", d.showAngleMeter);
            s.showHudButtons = j.GetBool("hudButtons", d.showHudButtons);
            s.reduceMotion = j.GetBool("reduceMotion", d.reduceMotion);
            s.colorblindHud = j.GetBool("colorblind", d.colorblindHud);
            s.tiresVolume = j.GetFloat("tiresVol", d.tiresVolume);
            s.uiVolume = j.GetFloat("uiVol", d.uiVolume);
            s.Sanitize();
            return s;
        }
    }

    public sealed class ProfileCodec : IDocumentCodec<PlayerProfile>
    {
        public string SchemaId => "drift.profile";
        public int CurrentVersion => 1;
        public PlayerProfile CreateDefault() => new PlayerProfile();

        public JsonValue Encode(PlayerProfile p)
        {
            var tunings = JsonValue.NewObject();
            foreach (var kv in p.tunings) tunings.Set(kv.Key, TuningJson.Encode(kv.Value));
            var setups = JsonValue.NewArray();
            foreach (var s in p.savedSetups) setups.Add(TuningJson.Encode(s));
            var drills = JsonValue.NewArray();
            foreach (var d in p.drills.Values)
                drills.Add(JsonValue.NewObject().Set("id", d.drillId).Set("bestTime", d.bestTimeSec < float.MaxValue ? d.bestTimeSec : 0f)
                    .Set("bestScore", d.bestScore).Set("count", d.completions));
            var customs = JsonValue.NewArray();
            foreach (var c in p.customVehicleIds) customs.Add(JsonValue.String(c));
            return JsonValue.NewObject()
                .Set("id", p.profileId).Set("created", p.createdUtc).Set("vehicle", p.selectedVehicleId)
                .Set("tunings", tunings).Set("setups", setups).Set("drills", drills)
                .Set("totalPoints", p.totalDriftPoints).Set("bestChain", p.bestChain).Set("distanceKm", p.distanceDrivenKm)
                .Set("playTime", p.playTimeSec).Set("lastLayout", p.lastLayoutName ?? "").Set("customVehicles", customs)
                .Set("credits", JsonValue.Number((double)p.wallet.credits)).Set("earned", JsonValue.Number((double)p.wallet.lifetimeEarned))
                .Set("owned", StringArray(p.ownedItems)).Set("vehicles", StringArray(p.ownedVehicles))
                .Set("challenges", EncodeChallenges(p)).Set("best", EncodeFloats(p.bestScores)).Set("medals", EncodeInts(p.bestMedals))
                .Set("integrity", !p.integrityCompromised)
                .Set("nickname", p.nickname ?? "").Set("spent", JsonValue.Number((double)p.wallet.lifetimeSpent))
                .Set("walletHistory", EncodeHistory(p.wallet))
                .Set("character", Character.CharacterJson.Encode(p.character ?? new Character.CharacterAppearance()));
        }

        private static JsonValue StringArray(IEnumerable<string> items)
        {
            var a = JsonValue.NewArray();
            foreach (var i in items) a.Add(JsonValue.String(i));
            return a;
        }

        private static JsonValue EncodeFloats(Dictionary<string, float> d)
        {
            var o = JsonValue.NewObject();
            foreach (var kv in d) o.Set(kv.Key, kv.Value);
            return o;
        }

        private static JsonValue EncodeInts(Dictionary<string, int> d)
        {
            var o = JsonValue.NewObject();
            foreach (var kv in d) o.Set(kv.Key, kv.Value);
            return o;
        }

        private static JsonValue EncodeHistory(Wallet w)
        {
            var a = JsonValue.NewArray();
            foreach (var t in w.history)
                a.Add(JsonValue.NewObject().Set("utc", t.utc ?? "").Set("amount", JsonValue.Number((double)t.amount))
                    .Set("balance", JsonValue.Number((double)t.balanceAfter)).Set("reason", t.reason ?? ""));
            return a;
        }

        private static JsonValue EncodeChallenges(PlayerProfile p)
        {
            var o = JsonValue.NewObject();
            foreach (var c in p.challenges.Values)
                o.Set(c.id, JsonValue.NewObject().Set("v", c.value).Set("done", c.completed).Set("claimed", c.claimed).Set("day", c.dayKey ?? ""));
            return o;
        }

        public PlayerProfile Decode(JsonValue j, int version)
        {
            var p = new PlayerProfile
            {
                profileId = j.GetString("id", Guid.NewGuid().ToString("N")),
                createdUtc = j.GetString("created", DateTime.UtcNow.ToString("o")),
                selectedVehicleId = j.GetString("vehicle", "placeholder_rwd"),
                totalDriftPoints = MathUtil.Max(0f, j.GetFloat("totalPoints")),
                bestChain = MathUtil.Max(0f, j.GetFloat("bestChain")),
                distanceDrivenKm = MathUtil.Max(0f, j.GetFloat("distanceKm")),
                playTimeSec = MathUtil.Max(0f, j.GetFloat("playTime")),
                lastLayoutName = j.GetString("lastLayout", ""),
            };
            var tunings = j.GetObject("tunings");
            if (tunings != null)
                foreach (var k in tunings.Keys)
                    if (tunings[k].IsObject) p.tunings[k] = TuningJson.Decode(tunings[k]);
            var setups = j.GetArray("setups");
            if (setups != null)
                foreach (var s in setups.Items)
                    if (s.IsObject && p.savedSetups.Count < 50) p.savedSetups.Add(TuningJson.Decode(s));
            var drills = j.GetArray("drills");
            if (drills != null)
                foreach (var d in drills.Items)
                {
                    if (!d.IsObject) continue;
                    string id = d.GetString("id");
                    if (string.IsNullOrEmpty(id)) continue;
                    float bt = d.GetFloat("bestTime");
                    p.drills[id] = new DrillRecord { drillId = id, bestTimeSec = bt > 0f ? bt : float.MaxValue, bestScore = d.GetFloat("bestScore"), completions = d.GetInt("count") };
                }
            var customs = j.GetArray("customVehicles");
            if (customs != null)
                foreach (var c in customs.Items)
                    if (c.Type == JsonType.String) p.customVehicleIds.Add(c.AsString);
            var credits = j["credits"];
            if (credits != null && credits.Type == JsonType.Number) p.wallet.credits = (long)Math.Max(0, Math.Min(1e12, credits.AsDouble));
            var earned = j["earned"];
            if (earned != null && earned.Type == JsonType.Number) p.wallet.lifetimeEarned = (long)Math.Max(0, Math.Min(1e13, earned.AsDouble));
            var owned = j.GetArray("owned");
            if (owned != null) foreach (var o in owned.Items) if (o.Type == JsonType.String && p.ownedItems.Count < 5000) p.ownedItems.Add(o.AsString);
            var vehicles = j.GetArray("vehicles");
            if (vehicles != null)
            {
                p.ownedVehicles.Clear();
                foreach (var v in vehicles.Items) if (v.Type == JsonType.String && !p.ownedVehicles.Contains(v.AsString)) p.ownedVehicles.Add(v.AsString);
                if (!p.ownedVehicles.Contains("placeholder_rwd")) p.ownedVehicles.Add("placeholder_rwd");
            }
            var ch = j.GetObject("challenges");
            if (ch != null)
                foreach (var k in ch.Keys)
                {
                    var c = ch[k];
                    if (!c.IsObject) continue;
                    p.challenges[k] = new ChallengeProgress { id = k, value = c.GetFloat("v"), completed = c.GetBool("done"), claimed = c.GetBool("claimed"), dayKey = c.GetString("day", "") };
                }
            var best = j.GetObject("best");
            if (best != null) foreach (var k in best.Keys) p.bestScores[k] = MathUtil.Max(0f, best.GetFloat(k));
            p.integrityCompromised = !j.GetBool("integrity", true);
            p.nickname = Account.PlayerIdentity.Sanitize(j.GetString("nickname", "")) ?? "";
            var ch2 = j.GetObject("character");
            if (ch2 != null) p.character = Character.CharacterJson.Decode(ch2);
            var spent = j["spent"];
            if (spent != null && spent.Type == JsonType.Number) p.wallet.lifetimeSpent = (long)Math.Max(0, Math.Min(1e13, spent.AsDouble));
            var hist = j.GetArray("walletHistory");
            if (hist != null)
                foreach (var h in hist.Items)
                {
                    if (!h.IsObject || p.wallet.history.Count >= Wallet.HistoryLimit) continue;
                    p.wallet.history.Add(new WalletTransaction
                    {
                        utc = h.GetString("utc", ""), amount = (long)(h["amount"]?.Type == JsonType.Number ? h["amount"].AsDouble : 0),
                        balanceAfter = (long)(h["balance"]?.Type == JsonType.Number ? h["balance"].AsDouble : 0), reason = h.GetString("reason", "wallet.reason.other")
                    });
                }
            var medals = j.GetObject("medals");
            if (medals != null) foreach (var k in medals.Keys) p.bestMedals[k] = MathUtil.Clamp(medals.GetInt(k), 0, 3);
            return p;
        }
    }
}

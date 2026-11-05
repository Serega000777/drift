using System;
using System.Collections.Generic;
using DriftCareer.Core.Assists;
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
        public HealthSettings health = new HealthSettings();

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
                .Set("diffType", t.diffType.ToString());
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
                .Set("blowouts", s.health.blowouts);
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
                .Set("playTime", p.playTimeSec).Set("lastLayout", p.lastLayoutName ?? "").Set("customVehicles", customs);
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
            return p;
        }
    }
}

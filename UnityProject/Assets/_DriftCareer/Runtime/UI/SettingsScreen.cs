using System;
using DriftCareer.Core.Assists;
using DriftCareer.Core.Persistence;
using DriftCareer.Core.Vehicle;
using UnityEngine;
using UnityEngine.UIElements;

namespace DriftCareer.Runtime.UI
{
    /// <summary>Detailed settings: game, controls, assists, camera, graphics, HUD/accessibility, audio, account.</summary>
    public sealed class SettingsScreen : UiScreen
    {
        private enum Tab { Game, Controls, Assists, Camera, Graphics, Hud, Audio, Account }

        private Tab _tab;
        private ScrollView _content;

        private SettingsData Set => S.Settings;

        protected override void Build()
        {
            Root.pickingMode = PickingMode.Position;
            Root.Add(Ui.El("dc-dim"));
            var panel = Ui.El("dc-panel");
            panel.style.position = Position.Absolute;
            panel.style.left = 120; panel.style.right = 120; panel.style.top = 70; panel.style.bottom = 70;
            panel.Add(Ui.Label(T("settings.title"), "dc-h1"));
            var tabs = Ui.El("dc-tabs");
            foreach (Tab t in Enum.GetValues(typeof(Tab)))
            {
                var tab = t;
                string key = t == Tab.Game ? "settings.tab.game" : t == Tab.Controls ? "settings.tab.controls" : t == Tab.Assists ? "settings.tab.assists"
                    : t == Tab.Camera ? "settings.tab.camera" : t == Tab.Graphics ? "settings.tab.graphics" : t == Tab.Hud ? "settings.tab.hud"
                    : t == Tab.Audio ? "settings.tab.audio" : "settings.account";
                var b = new Button(() => { _tab = tab; Rebuild(); Nav.FocusFirst(); }) { text = T(key) };
                b.AddToClassList("dc-tab");
                b.EnableInClassList("is-selected", t == _tab);
                tabs.Add(b);
                Nav.Register(b, () => { _tab = tab; Rebuild(); Nav.FocusFirst(); });
            }
            panel.Add(tabs);
            _content = new ScrollView();
            _content.AddToClassList("dc-scroll");
            panel.Add(_content);
            var back = Ui.Button(T("common.back"), OnBack, "dc-btn--primary");
            back.style.alignSelf = Align.FlexEnd;
            panel.Add(back);
            Nav.Register(back, OnBack);
            Root.Add(panel);
            BuildTab();
        }

        private void Live() => Game.ApplySettingsLive();

        private static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";

        private void BuildTab()
        {
            var c = _content;
            var s = Set;
            switch (_tab)
            {
                case Tab.Game:
                    CyclerRow(c, "settings.language_label", new[] { "settings.language.ru", "settings.language.en" }, () => s.language == "en" ? 1 : 0,
                        i => { s.language = i == 1 ? "en" : "ru"; Live(); });
                    CyclerRow(c, "settings.units_label", new[] { "settings.units.Kmh", "settings.units.Mph" }, () => (int)s.units, i => s.units = (SpeedUnits)i);
                    CyclerRow(c, "settings.damage_label", new[] { "settings.damage.Off", "settings.damage.VisualOnly", "settings.damage.Full" }, () => (int)s.health.damage,
                        i => { s.health.damage = (DamageMode)i; Live(); });
                    SliderRow(c, T("settings.tire_wear"), 0f, 5f, 0.5f, () => s.health.tireWearMultiplier, v => { s.health.tireWearMultiplier = v; Live(); }, v => "×" + v.ToString("0.0"));
                    ToggleRow(c, "settings.blowouts", () => s.health.blowouts, v => { s.health.blowouts = v; Live(); });
                    ToggleRow(c, "settings.intro", () => s.playIntro, v => s.playIntro = v);
                    ToggleRow(c, "settings.autorollout", () => s.autoRollOut, v => s.autoRollOut = v);
                    ToggleRow(c, "settings.ghost", () => s.ghostEnabled, v => s.ghostEnabled = v);
                    ToggleRow(c, "settings.vibration", () => s.vibration, v => s.vibration = v);
                    break;
                case Tab.Controls:
                    CyclerRow(c, "settings.control_scheme", new[] { "settings.scheme.Auto", "settings.scheme.TouchWheel", "settings.scheme.TouchSlider", "settings.scheme.TouchButtons", "settings.scheme.Tilt" },
                        () => (int)s.controlScheme, i => { s.controlScheme = (ControlScheme)i; Live(); });
                    SliderRow(c, T("settings.steer_sens"), 0.3f, 2f, 0.05f, () => s.steeringSensitivity, v => s.steeringSensitivity = v, v => "×" + v.ToString("0.00"));
                    SliderRow(c, T("settings.linearity"), 0.5f, 2.5f, 0.05f, () => s.steeringLinearity, v => s.steeringLinearity = v, v => v.ToString("0.00"));
                    SliderRow(c, T("settings.deadzone"), 0f, 0.3f, 0.01f, () => s.steeringDeadzone, v => s.steeringDeadzone = v, Pct);
                    SliderRow(c, T("settings.tilt_sens"), 0.3f, 3f, 0.1f, () => s.tiltSensitivity, v => s.tiltSensitivity = v, v => "×" + v.ToString("0.0"));
                    ToggleRow(c, "settings.left_handed", () => s.leftHanded, v => { s.leftHanded = v; Live(); });
                    ToggleRow(c, "settings.hb_toggle", () => s.handbrakeToggle, v => s.handbrakeToggle = v);
                    ToggleRow(c, "settings.manual", () => s.manualTransmission, v => { s.manualTransmission = v; Live(); });
                    break;
                case Tab.Assists:
                    CyclerRow(c, "settings.assist_preset", new[] { "settings.assist.Simulation", "settings.assist.Balanced", "settings.assist.Assisted" },
                        () => (int)s.assistPreset, i => { s.assistPreset = (AssistPreset)i; Live(); });
                    c.Add(Ui.Label(T("settings.assist_note"), "dc-muted"));
                    break;
                case Tab.Camera:
                    CyclerRow(c, "settings.camera_label", new[] { "settings.camera.Chase", "settings.camera.ChaseFar", "settings.camera.Hood", "settings.camera.Cockpit", "settings.camera.Bumper" },
                        () => (int)s.cameraMode, i => s.cameraMode = (CameraMode)i);
                    SliderRow(c, T("settings.fov"), 45f, 90f, 1f, () => s.fieldOfView, v => { s.fieldOfView = v; Live(); }, v => v.ToString("0") + "°");
                    SliderRow(c, T("settings.cam_distance"), 0.7f, 1.6f, 0.05f, () => s.chaseDistance, v => s.chaseDistance = v, v => "×" + v.ToString("0.00"));
                    SliderRow(c, T("settings.cam_height"), 0.6f, 1.8f, 0.05f, () => s.chaseHeight, v => s.chaseHeight = v, v => "×" + v.ToString("0.00"));
                    SliderRow(c, T("settings.cam_shake"), 0f, 1f, 0.05f, () => s.cameraShake, v => s.cameraShake = v, Pct);
                    SliderRow(c, T("settings.drift_look"), 0f, 1.5f, 0.05f, () => s.driftLook, v => s.driftLook = v, Pct);
                    break;
                case Tab.Graphics:
                    CyclerRow(c, "settings.quality_label", new[] { "settings.quality.Auto", "settings.quality.Low", "settings.quality.Medium", "settings.quality.High", "settings.quality.Ultra" },
                        () => (int)s.quality, i => { s.quality = (QualityTier)i; Live(); });
                    SliderRow(c, T("settings.render_scale"), 0.5f, 1.5f, 0.05f, () => s.renderScale, v => { s.renderScale = v; Live(); }, Pct);
                    CyclerRow(c, "settings.shadows_label", new[] { "settings.shadows.0", "settings.shadows.1", "settings.shadows.2", "settings.shadows.3" }, () => s.shadowQuality, i => { s.shadowQuality = i; Live(); });
                    SliderRow(c, T("settings.shadow_distance"), 30f, 300f, 10f, () => s.shadowDistance, v => { s.shadowDistance = v; Live(); }, v => v.ToString("0") + " m");
                    CyclerRow(c, "settings.aa_label", new[] { "settings.aa.0", "settings.aa.1", "settings.aa.2", "settings.aa.3" }, () => s.antiAliasing, i => { s.antiAliasing = i; Live(); });
                    ToggleRow(c, "settings.post", () => s.postProcessing, v => { s.postProcessing = v; Live(); });
                    ToggleRow(c, "settings.bloom", () => s.bloom, v => { s.bloom = v; Live(); });
                    ToggleRow(c, "settings.ssao", () => s.ambientOcclusion, v => { s.ambientOcclusion = v; Live(); });
                    ToggleRow(c, "settings.motion_blur", () => s.motionBlur, v => { s.motionBlur = v; Live(); });
                    ToggleRow(c, "settings.vignette", () => s.vignette, v => { s.vignette = v; Live(); });
                    ToggleRow(c, "settings.film_grain", () => s.filmGrain, v => { s.filmGrain = v; Live(); });
                    ToggleRow(c, "settings.reflections", () => s.reflections, v => { s.reflections = v; Live(); });
                    ToggleRow(c, "settings.hdr", () => s.hdr, v => { s.hdr = v; Live(); });
                    ToggleRow(c, "settings.vsync", () => s.vSync, v => { s.vSync = v; Live(); });
                    CyclerRow(c, "settings.fps_label", new[] { "settings.fps.30", "settings.fps.60", "settings.fps.90", "settings.fps.120" },
                        () => s.targetFps <= 30 ? 0 : s.targetFps <= 60 ? 1 : s.targetFps <= 90 ? 2 : 3, i => { s.targetFps = i == 0 ? 30 : i == 1 ? 60 : i == 2 ? 90 : 120; Live(); });
                    SliderRow(c, T("settings.particles"), 0.2f, 1.5f, 0.05f, () => s.particleDensity, v => s.particleDensity = v, Pct);
                    break;
                case Tab.Hud:
                    SliderRow(c, T("settings.hud_scale"), 0.7f, 1.4f, 0.05f, () => s.hudScale, v => s.hudScale = v, Pct);
                    SliderRow(c, T("settings.hud_opacity"), 0.3f, 1f, 0.05f, () => s.hudOpacity, v => s.hudOpacity = v, Pct);
                    ToggleRow(c, "settings.telemetry", () => s.showTelemetry, v => { s.showTelemetry = v; Game.Hud.TelemetryVisible = v; });
                    ToggleRow(c, "settings.show_score", () => s.showScore, v => s.showScore = v);
                    ToggleRow(c, "settings.show_tires", () => s.showTireDiagram, v => s.showTireDiagram = v);
                    ToggleRow(c, "settings.show_angle", () => s.showAngleMeter, v => s.showAngleMeter = v);
                    ToggleRow(c, "settings.show_buttons", () => s.showHudButtons, v => s.showHudButtons = v);
                    ToggleRow(c, "settings.reduce_motion", () => s.reduceMotion, v => { s.reduceMotion = v; Live(); });
                    ToggleRow(c, "settings.colorblind", () => s.colorblindHud, v => s.colorblindHud = v);
                    break;
                case Tab.Audio:
                    SliderRow(c, T("settings.master"), 0f, 1f, 0.05f, () => s.masterVolume, v => { s.masterVolume = v; Live(); }, Pct);
                    SliderRow(c, T("settings.engine"), 0f, 1f, 0.05f, () => s.engineVolume, v => { s.engineVolume = v; Live(); }, Pct);
                    SliderRow(c, T("settings.tires_volume"), 0f, 1f, 0.05f, () => s.tiresVolume, v => { s.tiresVolume = v; Live(); }, Pct);
                    SliderRow(c, T("settings.sfx"), 0f, 1f, 0.05f, () => s.effectsVolume, v => { s.effectsVolume = v; Live(); }, Pct);
                    SliderRow(c, T("settings.music"), 0f, 1f, 0.05f, () => s.musicVolume, v => s.musicVolume = v, Pct);
                    SliderRow(c, T("settings.ui_volume"), 0f, 1f, 0.05f, () => s.uiVolume, v => s.uiVolume = v, Pct);
                    break;
                case Tab.Account:
                    c.Add(Ui.Label(T("settings.account_offline"), "dc-muted"));
                    c.Add(Ui.Label("ID: " + S.Account.Current.accountId, "dc-small"));
                    c.Add(Ui.Label("Store: " + S.Account.Policy.store, "dc-small"));
                    break;
            }
        }

        public override void OnBack() => Game.CloseSettings();
    }
}

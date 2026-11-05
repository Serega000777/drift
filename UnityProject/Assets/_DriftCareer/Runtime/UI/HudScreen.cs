using DriftCareer.Core;
using DriftCareer.Core.Gameplay;
using DriftCareer.Core.Persistence;
using DriftCareer.Core.Vehicle;
using DriftCareer.Runtime.Input;
using DriftCareer.Runtime.Track;
using UnityEngine;
using UnityEngine.UIElements;

namespace DriftCareer.Runtime.UI
{
    /// <summary>RPM arc gauge drawn with Painter2D (Unity 2022.1+), bar fallback otherwise.</summary>
    public sealed class RpmGauge : VisualElement
    {
        public float rpm;
        public float redline = 7000f;
        public float max = 8000f;
        public float angleDeg;
        private readonly VisualElement _fallbackFill;

        public RpmGauge()
        {
#if UNITY_2022_1_OR_NEWER
            generateVisualContent += Draw;
#else
            var bar = new VisualElement();
            bar.style.position = Position.Absolute; bar.style.left = 40; bar.style.right = 40; bar.style.top = 70; bar.style.height = 16;
            bar.style.backgroundColor = new Color(1f, 1f, 1f, 0.15f);
            _fallbackFill = new VisualElement();
            _fallbackFill.style.height = 16;
            _fallbackFill.style.backgroundColor = new Color(0.91f, 0.2f, 0.18f);
            bar.Add(_fallbackFill);
            Add(bar);
#endif
        }

        public void SetValues(float rpmValue, float redlineValue, float maxValue)
        {
            rpm = rpmValue; redline = redlineValue; max = Mathf.Max(redlineValue + 500f, maxValue);
#if UNITY_2022_1_OR_NEWER
            MarkDirtyRepaint();
#else
            if (_fallbackFill != null) _fallbackFill.style.width = Length.Percent(Mathf.Clamp01(rpm / max) * 100f);
#endif
        }

#if UNITY_2022_1_OR_NEWER
        private void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var r = contentRect;
            Vector2 c = new Vector2(r.width * 0.5f, r.height * 0.58f);
            float radius = Mathf.Min(r.width, r.height * 1.4f) * 0.44f;
            const float start = 150f, sweep = 240f;
            float k = Mathf.Clamp01(rpm / max), red = Mathf.Clamp01(redline / max);
            p.lineCap = LineCap.Round;
            p.lineWidth = 14f;
            p.strokeColor = new Color(1f, 1f, 1f, 0.12f);
            p.BeginPath(); p.Arc(c, radius, start, start + sweep); p.Stroke();
            p.strokeColor = new Color(1f, 0.23f, 0.19f, 0.45f);
            p.BeginPath(); p.Arc(c, radius, start + sweep * red, start + sweep); p.Stroke();
            p.lineWidth = 16f;
            Color fill = rpm > redline ? new Color(1f, 0.25f, 0.2f) : Color.Lerp(new Color(1f, 1f, 1f), new Color(1f, 0.55f, 0.1f), k);
            p.strokeColor = fill;
            if (k > 0.005f) { p.BeginPath(); p.Arc(c, radius, start, start + sweep * k); p.Stroke(); }
            // Tick marks every 1000 rpm.
            p.lineWidth = 3f;
            p.strokeColor = new Color(1f, 1f, 1f, 0.5f);
            for (float t = 0; t <= max; t += 1000f)
            {
                float a = (start + sweep * (t / max)) * Mathf.Deg2Rad;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                p.BeginPath(); p.MoveTo(c + d * (radius - 24f)); p.LineTo(c + d * (radius - 12f)); p.Stroke();
            }
        }
#endif
    }

    /// <summary>
    /// Driving HUD: animated drift score (chain, multiplier, transitions/перекладки), speed + RPM cluster with shift light,
    /// gear, drift-angle meter, tire temperature/blowout diagram, body integrity, drill progress, toasts and touch controls.
    /// </summary>
    public sealed class HudScreen : UiScreen
    {
        private Label _chain, _mult, _event, _total, _speed, _units, _gear, _assist, _angleText, _toast, _hint, _drillTitle, _timer, _credits;
        private VisualElement _angleNeedle, _drillBox, _drillFill, _integrityFill, _telemetryBox, _top, _tiresBox, _angleBox, _buttonsBox;
        private Label _telemetry;
        private readonly VisualElement[] _tires = new VisualElement[4];
        private RpmGauge _gauge;
        private TouchControls _touch;
        private float _eventTimer, _toastTimer, _popTimer;
        private TrackSession _session;
        private int _fpsFrames;
        private float _fpsTime, _fps;

        public bool TelemetryVisible { get; set; }

        public void Bind(TrackSession session)
        {
            if (_session != null) Unbind();
            _session = session;
            var sc = session.Drift.Scoring;
            sc.TransitionScored += OnTransition;
            sc.ChainEnded += OnChainEnded;
            session.Car.Health.TireBlown += OnBlown;
            session.Car.Health.TirePunctured += OnPuncture;
            session.Drift.DrillCompleted += OnDrillDone;
        }

        private void Unbind()
        {
            var sc = _session.Drift.Scoring;
            sc.TransitionScored -= OnTransition;
            sc.ChainEnded -= OnChainEnded;
            _session.Car.Health.TireBlown -= OnBlown;
            _session.Car.Health.TirePunctured -= OnPuncture;
            _session.Drift.DrillCompleted -= OnDrillDone;
        }

        protected override void Build()
        {
            var top = Ui.El("hud-top");
            _top = top;
            top.pickingMode = PickingMode.Ignore;
            _timer = Ui.Label("", "hud-mult");
            top.Add(_timer);
            _chain = Ui.Label("", "hud-chain");
            _mult = Ui.Label("", "hud-mult");
            _event = Ui.Label("", "hud-event");
            _total = Ui.Label("", "hud-total");
            top.Add(_chain); top.Add(_mult); top.Add(_event); top.Add(_total);
            Root.Add(top);

            var left = Ui.El("hud-left");
            _assist = Ui.Label("", "hud-assist");
            left.Add(_assist);
            _credits = Ui.Label("", "hud-assist");
            left.Add(_credits);
            _drillBox = Ui.El("hud-drill");
            _drillTitle = Ui.Label("", "dc-small");
            _drillBox.Add(_drillTitle);
            var bar = Ui.El("hud-drill-bar");
            _drillFill = Ui.El("hud-drill-fill");
            bar.Add(_drillFill);
            _drillBox.Add(bar);
            left.Add(_drillBox);
            Root.Add(left);

            var cluster = Ui.El("hud-cluster");
            _gauge = new RpmGauge();
            _gauge.AddToClassList("hud-gauge");
            cluster.Add(_gauge);
            _speed = Ui.Label("0", "hud-speed");
            _units = Ui.Label(T("common.kmh"), "hud-units");
            _gear = Ui.Label("N", "hud-gear");
            cluster.Add(_speed); cluster.Add(_units); cluster.Add(_gear);
            Root.Add(cluster);

            var angle = Ui.El("hud-angle");
            _angleBox = angle;
            var abar = Ui.El("hud-angle-bar");
            _angleNeedle = Ui.El("hud-angle-needle");
            abar.Add(_angleNeedle);
            angle.Add(abar);
            _angleText = Ui.Label("", "hud-angle-text");
            angle.Add(_angleText);
            Root.Add(angle);

            var tires = Ui.El("hud-tires");
            _tiresBox = tires;
            Vector2[] pos = { new Vector2(10, 0), new Vector2(80, 0), new Vector2(10, 100), new Vector2(80, 100) };
            for (int i = 0; i < 4; i++)
            {
                _tires[i] = Ui.El("hud-tire");
                _tires[i].style.left = pos[i].x;
                _tires[i].style.top = pos[i].y;
                tires.Add(_tires[i]);
            }
            var integrity = Ui.El("hud-integrity");
            _integrityFill = Ui.El("hud-integrity-fill");
            integrity.Add(_integrityFill);
            tires.Add(integrity);
            Root.Add(tires);

            var buttons = Ui.El("hud-buttons");
            _buttonsBox = buttons;
            buttons.Add(Ui.Button(T("hud.pause"), () => Game.Pause(), "dc-btn--chip"));
            buttons.Add(Ui.Button(T("hud.camera"), () => Game.CycleCamera(), "dc-btn--chip"));
            buttons.Add(Ui.Button(T("hud.reset"), () => Game.ResetCar(false), "dc-btn--chip"));
            buttons.Add(Ui.Button(T("hud.editor"), () => Game.EnterEditor(), "dc-btn--chip"));
            Root.Add(buttons);

            _toast = Ui.Label("", "hud-toast");
            _toast.pickingMode = PickingMode.Ignore;
            Root.Add(_toast);
            _hint = Ui.Label("", "hud-hint");
            _hint.pickingMode = PickingMode.Ignore;
            Root.Add(_hint);

            _telemetryBox = Ui.El("hud-telemetry");
            _telemetry = Ui.Label("");
            _telemetryBox.Add(_telemetry);
            Root.Add(_telemetryBox);

            _touch = new TouchControls(S.Input.Touch, S.Settings, UiRoot);
            Root.Add(_touch.Root);
            RefreshTouchVisibility();
        }

        public void RefreshTouchVisibility()
        {
            if (_touch == null) return;
            bool show = S.Defaults.showTouchControls || S.Input.ActiveDevice == InputDevice.Touch;
            _touch.Rebuild(S.Settings);
            Ui.SetVisible(_touch.Root, show);
        }

        private void OnTransition(int n)
        {
            ShowEvent(T("hud.transition") + " +" + Mathf.RoundToInt(_session.Drift.Scoring.Config.transitionBonus * _session.Drift.Scoring.Multiplier));
        }

        private void OnChainEnded(float points, ChainEndReason reason)
        {
            if (reason == ChainEndReason.Completed && points > 1f)
            {
                ShowEvent(T("hud.banked", Mathf.RoundToInt(points).ToString("N0")));
                S.Profile.totalDriftPoints += points;
                if (points > S.Profile.bestChain) S.Profile.bestChain = points;
                return;
            }
            string key = reason == ChainEndReason.Collision ? "hud.chain_lost.collision" : reason == ChainEndReason.Spin ? "hud.chain_lost.spin" : reason == ChainEndReason.OffTrack ? "hud.chain_lost.offtrack" : null;
            if (key != null)
            {
                ShowToast(T(key));
                _chain.AddToClassList("hud-chain--lost");
                _chain.schedule.Execute(() => _chain.RemoveFromClassList("hud-chain--lost")).StartingIn(700);
            }
        }

        private void OnBlown(int wheel) { ShowToast(T("hud.tire_blown")); Game.Feedback(1f, 0.35f); }
        private void OnPuncture(int wheel) => ShowToast(T("hud.puncture"));

        private void OnDrillDone(TrainingDrill d)
        {
            ShowToast(T("hud.drill_done"));
            S.Profile.RecordDrill(d.Id, d.ElapsedSeconds, _session.Drift.Scoring.TotalPoints);
            S.SaveProfile();
        }

        private void ShowEvent(string text)
        {
            _event.text = text;
            _event.AddToClassList("hud-event--show");
            _eventTimer = 1.4f;
        }

        public void ShowToast(string text)
        {
            if (_toast == null) return;
            _toast.text = text;
            _toast.AddToClassList("hud-toast--show");
            _toastTimer = 2f;
        }

        public void SetHint(string text) { if (_hint != null) _hint.text = text ?? ""; }

        public override void Tick(float dt)
        {
            if (_session == null || _session.Car == null) return;
            var car = _session.Car;
            var t = car.Telemetry;
            var sc = _session.Drift.Scoring;
            var settings = S.Settings;

            // Score.
            int chain = Mathf.RoundToInt(sc.ChainPoints);
            string chainText = sc.ChainActive ? chain.ToString("N0") : "";
            if (chainText != _chain.text)
            {
                _chain.text = chainText;
                _popTimer -= dt;
                if (_popTimer <= 0f && sc.ChainActive)
                {
                    _chain.AddToClassList("hud-chain--pop");
                    _chain.schedule.Execute(() => _chain.RemoveFromClassList("hud-chain--pop")).StartingIn(90);
                    _popTimer = 0.25f;
                }
            }
            _mult.text = sc.ChainActive ? "x" + sc.Multiplier.ToString("0.0") : "";
            _total.text = T("hud.total") + " " + Mathf.RoundToInt(sc.TotalPoints).ToString("N0") + "   " + T("hud.best_chain") + " " + Mathf.RoundToInt(sc.BestChain).ToString("N0");
            if (_eventTimer > 0f && (_eventTimer -= dt) <= 0f) _event.RemoveFromClassList("hud-event--show");
            if (_toastTimer > 0f && (_toastTimer -= dt) <= 0f) _toast.RemoveFromClassList("hud-toast--show");

            // Cluster.
            bool mph = settings.units == SpeedUnits.Mph;
            _speed.text = Mathf.RoundToInt(mph ? t.SpeedMph : t.SpeedKmh).ToString();
            _units.text = T(mph ? "common.mph" : "common.kmh");
            _gauge.SetValues(t.engineRpm, t.redlineRpm, t.limiterRpm + 500f);
            _gear.text = t.gear < 0 ? "R" : (t.gear == 0 ? "N" : t.gear.ToString());
            _gear.EnableInClassList("hud-gear--shift", t.engineRpm > t.redlineRpm - 150f && (Time.unscaledTime * 10f % 2f) < 1f);

            // Drift angle meter (-90..90 mapped across the bar).
            float a = Mathf.Clamp(t.driftAngleDeg, -90f, 90f);
            _angleNeedle.style.left = 150f + a / 90f * 147f - 3f;
            _angleText.text = Mathf.Abs(t.driftAngleDeg) > 3f ? Mathf.RoundToInt(Mathf.Abs(t.driftAngleDeg)) + "°" : "";

            // Tires: color by temperature, red outline when blown.
            for (int i = 0; i < 4; i++)
            {
                var tc = car.Health.tires[i];
                float k = Mathf.InverseLerp(30f, 160f, tc.temperatureC);
                // Colorblind mode uses a blue -> white -> orange ramp instead of green/red.
                Color cold = new Color(0.2f, 0.5f, 1f), ok = settings.colorblindHud ? new Color(0.95f, 0.95f, 0.95f) : new Color(0.25f, 0.85f, 0.4f), hot = settings.colorblindHud ? new Color(1f, 0.6f, 0f) : new Color(1f, 0.25f, 0.15f);
                Color c = k < 0.5f ? Color.Lerp(cold, ok, k * 2f) : Color.Lerp(ok, hot, (k - 0.5f) * 2f);
                c.a = Mathf.Lerp(0.95f, 0.35f, tc.wear);
                _tires[i].style.backgroundColor = tc.blown ? new Color(0.1f, 0.1f, 0.1f) : c;
                _tires[i].style.borderTopColor = _tires[i].style.borderBottomColor = _tires[i].style.borderLeftColor = _tires[i].style.borderRightColor =
                    tc.blown || tc.leakRatePerSec > 0f ? new Color(1f, 0.2f, 0.2f) : new Color(1f, 1f, 1f, 0.4f);
            }
            float integrity = car.Health.BodyIntegrity;
            _integrityFill.style.width = Length.Percent(integrity * 100f);
            _integrityFill.style.backgroundColor = integrity > 0.6f ? new Color(0.24f, 0.86f, 0.52f) : integrity > 0.3f ? new Color(0.96f, 0.77f, 0f) : new Color(1f, 0.23f, 0.19f);

            _assist.text = T("hud.assist", T("settings.assist." + settings.assistPreset));
            _credits.text = T("hud.credits", S.Profile.wallet.credits.ToString("N0"));

            // Run timer / countdown / tandem score.
            var clock = Game.Clock;
            if (clock.Rules != null && clock.Rules.kind != RunKind.FreeDrive)
            {
                if (clock.Phase == RunPhase.Countdown) _timer.text = Mathf.CeilToInt(clock.CountdownLeft).ToString();
                else if (clock.Phase == RunPhase.Running && clock.Time < 0.8f) _timer.text = T("hud.go");
                else if (clock.Phase == RunPhase.Running)
                {
                    string time = T("hud.time") + " " + Mathf.CeilToInt(clock.Remaining);
                    _timer.text = clock.Rules.kind == RunKind.GhostTandem ? time + "   " + T("hud.tandem", Mathf.RoundToInt(Game.Tandem.Score)) : time;
                }
                else _timer.text = "";
            }
            else _timer.text = "";

            // HUD visibility/accessibility settings.
            Root.style.opacity = settings.hudOpacity;
            Ui.SetVisible(_chain, settings.showScore);
            Ui.SetVisible(_mult, settings.showScore);
            Ui.SetVisible(_total, settings.showScore);
            Ui.SetVisible(_tiresBox, settings.showTireDiagram);
            Ui.SetVisible(_angleBox, settings.showAngleMeter);
            Ui.SetVisible(_buttonsBox, settings.showHudButtons);
            var drill = _session.Drift.Drill;
            Ui.SetVisible(_drillBox, drill != null);
            if (drill != null)
            {
                _drillTitle.text = T(drill.TitleKey) + "  " + Mathf.RoundToInt(drill.Progress * 100f) + "%";
                _drillFill.style.width = Length.Percent(drill.Progress * 100f);
            }

            Ui.SetVisible(_telemetryBox, TelemetryVisible);
            if (TelemetryVisible) UpdateTelemetry(t, car, dt);
            _touch?.Tick(dt);
        }

        private void UpdateTelemetry(VehicleTelemetry t, Vehicle.VehicleController car, float dt)
        {
            _fpsFrames++;
            _fpsTime += Time.unscaledDeltaTime;
            if (_fpsTime >= 0.5f) { _fps = _fpsFrames / _fpsTime; _fpsFrames = 0; _fpsTime = 0f; }
            var sb = new System.Text.StringBuilder(512);
            sb.AppendFormat("FPS {0:0}  fixed {1:0.000}s  rpm {2:0}  gear {3}  clutch {4:0.00}  drive {5:0} Nm\n", _fps, Time.fixedDeltaTime, t.engineRpm, t.gear, t.clutch, t.driveTorque);
            sb.AppendFormat("v {0:0.0} m/s  drift {1:0.0}°  yaw {2:0} °/s  steer {3:0.0}°  NaN-resets {4}\n", t.speedMs, t.driftAngleDeg, t.yawRateDegPerSec, t.steerAngleDeg, t.nonFiniteRecoveries);
            string[] names = { "FL", "FR", "RL", "RR" };
            for (int i = 0; i < 4; i++)
            {
                var w = t.wheels[i];
                var tc = car.Health.tires[i];
                sb.AppendFormat("{0}: Fz {1,5:0} N  sr {2,5:0.00}  sa {3,5:0.0}°  P {4,4:0} kW  {5,3:0}°C  wear {6:0.00}{7}\n", names[i], w.normalLoad, w.slipRatio,
                    w.slipAngleRad * MathUtil.Rad2Deg, w.slipPower / 1000f, tc.temperatureC, tc.wear, tc.blown ? "  BLOWN" : "");
            }
            _telemetry.text = sb.ToString();
        }
    }
}

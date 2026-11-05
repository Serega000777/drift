using System;
using DriftCareer.Core;
using DriftCareer.Core.Assists;
using DriftCareer.Core.Gameplay;
using DriftCareer.Core.Garage;
using DriftCareer.Core.Layout;
using DriftCareer.Core.Persistence;
using DriftCareer.Runtime.Cameras;
using DriftCareer.Runtime.Gameplay;
using DriftCareer.Runtime.Input;
using DriftCareer.Runtime.LayoutEditor;
using DriftCareer.Runtime.Track;
using DriftCareer.Runtime.UI;
using DriftCareer.Runtime.Vehicle;
using DriftCareer.Runtime.Security;
using UnityEngine;

namespace DriftCareer.Runtime
{
    /// <summary>
    /// Game orchestration: state machine (menu, garage, intro, driving, pause, editor, results), run modes,
    /// tournaments, ghosts, challenges and rewards. One seamless scene: the Academy hangar doubles as the
    /// menu/garage backdrop, so there are no loading screens between menu, garage and driving.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        public GameServices Services { get; private set; }
        public GameFlow Flow { get; } = new GameFlow();
        public UiRoot Ui { get; private set; }
        public CameraRig Cam { get; private set; }
        public TrackSession Session { get; private set; }
        public LayoutEditorController Editor { get; private set; }
        public IntroDirector Intro { get; private set; }
        public RunClock Clock { get; } = new RunClock();
        public RunKind CurrentRun { get; private set; } = RunKind.FreeDrive;
        public TournamentRun Tournament { get; private set; }

        private MainMenuScreen _menu;
        private HudScreen _hud;
        private PauseScreen _pause;
        private GarageScreen _garage;
        private SettingsScreen _settings;
        private EditorScreen _editorScreen;
        private ResultsScreen _results;
        private ChallengesScreen _challenges;
        private readonly GhostRecorder _recorder = new GhostRecorder();
        private GhostRecording _ghost;
        private GhostCar _ghostCar;
        private readonly TandemScoring _tandem = new TandemScoring();
        private ObstacleLayout _savedPlayerLayout;
        private bool _handbrakeLatched;
        private int _lastTransitions;
        private float _saveTimer;
        private float _authTimer;
        private string _pendingMode;

        public HudScreen Hud => _hud;
        public TandemScoring Tandem => _tandem;
        public GhostRecording Ghost => _ghost;

        public void Boot(GameServices services)
        {
            Services = services;
            Time.fixedDeltaTime = 0.01f;
            Physics.defaultSolverIterations = 8;
            Physics.defaultSolverVelocityIterations = 2;
            Physics.sleepThreshold = 0.02f;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            if (Application.isMobilePlatform)
            {
                Screen.orientation = ScreenOrientation.AutoRotation;
                Screen.autorotateToPortrait = false;
                Screen.autorotateToPortraitUpsideDown = false;
                Screen.autorotateToLandscapeLeft = true;
                Screen.autorotateToLandscapeRight = true;
            }

            Cam = CameraRig.Create(transform);
            Cam.baseFov = services.Settings.fieldOfView;
            Ui = UiRoot.Create(this, transform, services.Defaults.uiScale * services.Settings.hudScale);
            _hud = Ui.Register(new HudScreen(), true);
            _menu = Ui.Register(new MainMenuScreen());
            _pause = Ui.Register(new PauseScreen());
            _garage = Ui.Register(new GarageScreen());
            _settings = Ui.Register(new SettingsScreen());
            _editorScreen = Ui.Register(new EditorScreen());
            _results = Ui.Register(new ResultsScreen());
            _challenges = Ui.Register(new ChallengesScreen());
            services.Loc.LanguageChanged += _ => Ui.RebuildAll();
            services.Input.DeviceChanged += _ => _hud.RefreshTouchVisibility();
            services.Challenges.Completed += d => Ui.Toast(services.Loc.Format("hud.challenge_done", d.reward), 2.5f, "#3DDC84");

            float quality = QualityScale();
            Session = TrackSession.BuildAcademy(services, quality);
            Editor = Session.gameObject.AddComponent<LayoutEditorController>();
            Editor.Initialize(Session, Cam);
            Editor.Status += (key, detail) => Ui.Toast(services.Loc.Format(key, detail != null ? services.T(detail) : ""), 1.8f, "#FF3B30");
            Intro = gameObject.AddComponent<IntroDirector>();
            Cam.SetTarget(Session.Car, Session.Rig);
            _hud.Bind(Session);
            HookSession();
            _ghostCar = GhostCar.Create(Session.transform);
            ApplySettingsLive();
            Clock.Finished += OnRunFinished;

            Flow.TryTransition(GameState.MainMenu);
            ShowOnly(_menu);
            ShowMenuBackdrop();
        }

        private float QualityScale()
        {
            switch (Services.Defaults.quality)
            {
                case QualityTier.Low: return 0.35f;
                case QualityTier.Medium: return 0.7f;
                case QualityTier.High: return 1f;
                default: return 1.2f;
            }
        }

        private void HookSession()
        {
            var sc = Session.Drift.Scoring;
            sc.ChainEnded += (points, reason) =>
            {
                if (reason != ChainEndReason.Completed || points < 1f) return;
                int credits = EconomyRules.CreditsForChain(points);
                if (credits > 0) Services.Profile.wallet.Earn(credits, "wallet.reason.chain");
            };
            Session.Drift.DrillCompleted += d =>
            {
                bool first = !Services.Profile.drills.ContainsKey(d.Id);
                Services.Profile.wallet.Earn(first ? EconomyRules.FirstDrillReward : EconomyRules.RepeatDrillReward, "wallet.reason.drill");
                Services.Challenges.Report(new RunStats { drillCompleted = d.Id });
            };
            Session.Car.Impact += (impulse, p, c) =>
            {
                if (impulse > 4000f) Feedback(Mathf.Clamp01(impulse / 25000f), 0.25f);
            };
        }

        private void ShowOnly(UiScreen screen)
        {
            foreach (var s in new UiScreen[] { _menu, _pause, _garage, _settings, _editorScreen, _results, _challenges })
                if (s != screen && s.Visible) s.Hide();
            if (screen != null && !screen.Visible) screen.Show();
            bool hud = Flow.State == GameState.Driving || Flow.State == GameState.Intro;
            if (hud && !_hud.Visible) _hud.Show();
            if (!hud && _hud.Visible) _hud.Hide();
            screen?.Nav.FocusFirst();
        }

        // ---------------------------------------------------------------- menu / garage / settings

        public void ShowMenuBackdrop()
        {
            Time.timeScale = 1f;
            Session.Car.SetFrozen(true);
            Session.ResetCar(false);
            Session.Car.SetFrozen(true);
            Cam.showroomAutoRotate = true;
            Cam.showroomDistance = 7.5f;
            Cam.SetMode(RigMode.Showroom);
            _ghostCar.Hide();
        }

        public void OpenGarage()
        {
            if (!Flow.TryTransition(GameState.Garage)) return;
            Time.timeScale = 1f;
            Session.Car.SetFrozen(true);
            Cam.showroomAutoRotate = false;
            Cam.showroomDistance = 6.5f;
            Cam.SetMode(RigMode.Showroom);
            ShowOnly(_garage);
        }

        public void CloseGarage()
        {
            if (Flow.Previous == GameState.Paused) { Flow.TryTransition(GameState.Driving); Resume(); return; }
            Flow.TryTransition(GameState.MainMenu);
            ShowOnly(_menu);
            ShowMenuBackdrop();
        }

        public void OpenSettings()
        {
            if (!Flow.TryTransition(GameState.Settings)) return;
            ShowOnly(_settings);
        }

        public void CloseSettings()
        {
            Services.SaveSettings();
            ApplySettingsLive();
            if (Flow.Previous == GameState.Paused) { Flow.TryTransition(GameState.Paused); ShowOnly(_pause); }
            else { Flow.TryTransition(GameState.MainMenu); ShowOnly(_menu); }
        }

        public void OpenChallenges()
        {
            Services.Challenges.SetDay(DateTime.UtcNow);
            ShowOnly(_challenges);
        }

        public void CloseChallenges() => ShowOnly(_menu);

        public void ApplySettingsLive()
        {
            var s = Services.Settings;
            Services.Loc.SetLanguage(s.language);
            Cam.baseFov = s.fieldOfView;
            Cam.distanceScale = s.chaseDistance;
            Cam.heightScale = s.chaseHeight;
            Cam.driftLookScale = s.driftLook;
            Session.Car.Assists.Settings = Services.AssistSettings();
            Session.Car.Health.Settings = s.health;
            Session.Audio.SetVolumes(s.masterVolume, s.engineVolume, s.effectsVolume, s.tiresVolume);
            GraphicsManager.Apply(s, Services.Defaults, Cam.Cam);
            _hud.RefreshTouchVisibility();
        }

        /// <summary>Applies the garage tuning to the live car (try-on).</summary>
        public void PreviewTuning(VehicleTuningState t) => Session.ApplyTuning(Services.BaseProfile, t);

        public void SwitchVehicle(string vehicleId)
        {
            Services.SelectVehicle(vehicleId);
            Session.BuildCar(Services, QualityScale());
            Cam.SetTarget(Session.Car, Session.Rig);
            _hud.Bind(Session);
            HookSession();
            Session.Car.SetFrozen(true);
        }

        // ---------------------------------------------------------------- driving

        public void StartAcademy(string modeId)
        {
            var reason = Services.Catalog.CanLaunch("academy", modeId);
            if (reason != Core.Catalog.LaunchBlockReason.None)
            {
                Ui.Info(Services.T("common.coming_soon"), Services.T("common.coming_soon_note"));
                return;
            }
            if (!Flow.TryTransition(GameState.Loading)) return;
            _pendingMode = modeId;
            Ui.Fade(true, () =>
            {
                ConfigureRun(_pendingMode);
                Session.Car.SetFrozen(false);
                Session.ResetCar(false);
                Session.Obstacles.ResetDynamic();
                Skidmarks.Instance?.Clear();
                Ui.Fade(false);
                if (Services.Settings.playIntro && CurrentRun == RunKind.FreeDrive && Flow.Previous != GameState.Garage)
                {
                    Flow.TryTransition(GameState.Intro);
                    ShowOnly(null);
                    Intro.rollOut = Services.Settings.autoRollOut;
                    Intro.Play(Cam, Session.Car, Session.Rig, CameraRig.FromSetting(Services.Settings.cameraMode), Session.World.walkStart, Session.World.exitPoint, OnIntroDone);
                    _hud.SetHint(Services.T("intro.skip"));
                }
                else
                {
                    Flow.TryTransition(GameState.Driving);
                    Cam.SetMode(CameraRig.FromSetting(Services.Settings.cameraMode), true);
                    ShowOnly(null);
                    BeginRunClock();
                }
            });
        }

        private void OnIntroDone()
        {
            if (Flow.State == GameState.Intro) Flow.TryTransition(GameState.Driving);
            _hud.SetHint(Session.Car.Sim.EngineRunning ? "" : Services.T("hud.start_engine"));
            BeginRunClock();
        }

        private void ConfigureRun(string modeId)
        {
            RestorePlayerLayout();
            Session.Drift.StartDrill(DrillKind.None);
            Session.Drift.Scoring.Reset();
            _ghostCar.Hide();
            _tandem.Reset();
            switch (modeId)
            {
                case "training": CurrentRun = RunKind.Training; break;
                case "score_attack": CurrentRun = RunKind.ScoreAttack; break;
                case "gymkhana": CurrentRun = RunKind.Gymkhana; break;
                case "clipping_attack": CurrentRun = RunKind.ClippingAttack; break;
                case "ghost_tandem": CurrentRun = RunKind.GhostTandem; break;
                case "tournament_academy":
                    CurrentRun = RunKind.TournamentRound;
                    if (Tournament == null || Tournament.Done)
                    {
                        var def = TournamentRun.AcademyCup();
                        if (!Services.Profile.wallet.Spend(def.entryFee, "wallet.reason.entry_fee")) { Ui.Toast(Services.T("garage.not_enough")); CurrentRun = RunKind.FreeDrive; break; }
                        Tournament = new TournamentRun(def);
                    }
                    break;
                default: CurrentRun = RunKind.FreeDrive; break;
            }
            var rules = CurrentRules();
            if (rules != null && !string.IsNullOrEmpty(rules.layoutPreset))
            {
                _savedPlayerLayout = Session.Layout.Layout.Clone();
                Session.Layout.ApplyPreset(LayoutPresets.Create(rules.layoutPreset, Services.Site));
            }
            if (CurrentRun == RunKind.Training) Ui.Pick(Services.T("drill.choose"), new[]
            {
                Services.T("drill.donut.title"), Services.T("drill.figure8.title"), Services.T("drill.slalom.title"), Services.T("drill.clipping.title")
            }, i => StartDrill((DrillKind)(i + 1)));
            if (CurrentRun == RunKind.GhostTandem)
            {
                _ghost = Services.LoadGhost("academy_best");
                if (_ghost == null || _ghost.frames.Count < 10) { Ui.Toast(Services.T("hud.ghost_none"), 4f); CurrentRun = RunKind.FreeDrive; }
                else _ghostCar.Show(_ghost);
            }
        }

        public void StartDrill(DrillKind kind)
        {
            Session.Drift.StartDrill(kind);
            string hint = kind == DrillKind.Donut ? "drill.donut.hint" : kind == DrillKind.FigureEight ? "drill.figure8.hint" : kind == DrillKind.Slalom ? "drill.slalom.hint" : "drill.clipping.hint";
            _hud.SetHint(Services.T(hint));
        }

        private RunRules CurrentRules()
        {
            if (CurrentRun == RunKind.TournamentRound) return Tournament?.CurrentRound;
            if (CurrentRun == RunKind.FreeDrive || CurrentRun == RunKind.Training) return null;
            return RunRules.For(CurrentRun);
        }

        private void BeginRunClock()
        {
            var rules = CurrentRules();
            Session.Drift.Scoring.Reset();
            _lastTransitions = 0;
            if (rules == null) { Clock.Begin(RunRules.For(RunKind.FreeDrive)); return; }
            Clock.Begin(rules);
            _recorder.Start("academy", Services.Profile.selectedVehicleId, "player");
        }

        private void OnRunFinished()
        {
            var rules = Clock.Rules;
            if (rules == null || rules.kind == RunKind.FreeDrive) return;
            Session.Drift.Scoring.EndChain(ChainEndReason.Completed);
            float score = CurrentRun == RunKind.GhostTandem ? _tandem.Score : Session.Drift.Scoring.TotalPoints;
            _recorder.Stop(score);
            var medal = rules.MedalFor(score);
            int reward = rules.RewardFor(medal);
            string key = "academy." + rules.kind;
            bool record = Services.Profile.RecordScore(key, score, medal);
            if (rules.kind == RunKind.ScoreAttack && record && _recorder.Recording.frames.Count > 20)
                Services.SaveGhost(_recorder.Recording, "academy_best");
            if (CurrentRun == RunKind.TournamentRound && Tournament != null)
            {
                Tournament.SubmitRound(score);
                if (Tournament.Done) reward += Tournament.Prize();
            }
            Services.Profile.wallet.Earn(reward, CurrentRun == RunKind.TournamentRound ? "wallet.reason.tournament" : "wallet.reason.run");
            Services.Challenges.Report(new RunStats { runScore = score });
            Services.SaveProfile();
            Session.Car.SetFrozen(true);
            Flow.TryTransition(GameState.Paused);
            _results.SetResult(rules.kind, score, medal, reward, record, Tournament);
            ShowOnly(_results);
        }

        public void RetryRun()
        {
            string mode = ModeIdFor(CurrentRun);
            Flow.TryTransition(GameState.Loading);
            Flow.TryTransition(GameState.Driving);
            ConfigureRunKeepTournament(mode);
            Session.Car.SetFrozen(false);
            Session.ResetCar(false);
            Session.Obstacles.ResetDynamic();
            Cam.SetMode(CameraRig.FromSetting(Services.Settings.cameraMode), true);
            Time.timeScale = 1f;
            ShowOnly(null);
            BeginRunClock();
        }

        private void ConfigureRunKeepTournament(string mode)
        {
            var t = Tournament;
            ConfigureRun(mode);
            if (mode == "tournament_academy") Tournament = t != null && !t.Done ? t : Tournament;
        }

        private static string ModeIdFor(RunKind k)
        {
            switch (k)
            {
                case RunKind.ScoreAttack: return "score_attack";
                case RunKind.Gymkhana: return "gymkhana";
                case RunKind.ClippingAttack: return "clipping_attack";
                case RunKind.GhostTandem: return "ghost_tandem";
                case RunKind.TournamentRound: return "tournament_academy";
                case RunKind.Training: return "training";
                default: return "free_drive";
            }
        }

        private void RestorePlayerLayout()
        {
            if (_savedPlayerLayout == null) return;
            Session.Layout.ReplaceLayout(_savedPlayerLayout);
            _savedPlayerLayout = null;
        }

        public void Pause()
        {
            if (Flow.State == GameState.Intro) { Intro.Skip(); return; }
            if (!Flow.TryTransition(GameState.Paused)) return;
            Time.timeScale = 0f;
            ShowOnly(_pause);
        }

        public void Resume()
        {
            if (Flow.State == GameState.Paused) Flow.TryTransition(GameState.Driving);
            Time.timeScale = 1f;
            Session.Car.SetFrozen(false);
            ShowOnly(null);
        }

        public void ResetCar(bool repair)
        {
            Session.ResetCar(repair);
            Session.Obstacles.ResetDynamic();
            if (Flow.State == GameState.Paused) Resume();
        }

        public void ExitToMenu()
        {
            Time.timeScale = 1f;
            Clock.Finish();
            RestorePlayerLayout();
            Services.SaveProfile();
            Ui.Fade(true, () =>
            {
                Flow.TryTransition(GameState.MainMenu);
                Ui.Fade(false);
                ShowOnly(_menu);
                ShowMenuBackdrop();
            });
        }

        public void CycleCamera()
        {
            var s = Services.Settings;
            s.cameraMode = (CameraMode)(((int)s.cameraMode + 1) % 5);
            Cam.SetMode(CameraRig.FromSetting(s.cameraMode));
        }

        public void EnterEditor()
        {
            if (Flow.State == GameState.Paused) { Time.timeScale = 1f; }
            if (!Flow.TryTransition(GameState.LayoutEditor)) return;
            Editor.IsTouch = Services.Input.ActiveDevice == InputDevice.Touch;
            Editor.Enter();
            ShowOnly(_editorScreen);
        }

        public void ExitEditor()
        {
            Editor.Exit();
            Services.SaveLayout(Session.Layout.Layout);
            Flow.TryTransition(GameState.Driving);
            Cam.SetMode(CameraRig.FromSetting(Services.Settings.cameraMode));
            ShowOnly(null);
        }

        public void Feedback(float strength, float seconds)
        {
            if (!Services.Settings.reduceMotion) Cam.AddShake(strength * 0.6f * Services.Settings.cameraShake);
            Services.Input.Rumble(strength, seconds, this);
        }

        // ---------------------------------------------------------------- loop

        private void Update()
        {
            if (Services == null) return;
            float dt = Time.unscaledDeltaTime;
            var input = Services.Input;
            input.Update(dt);
            var menu = input.Menu;
            var actions = input.Actions;

            if (Ui.HasModal) Ui.HandleModal(menu);
            else
                switch (Flow.State)
                {
                    case GameState.MainMenu:
                        if (_challenges.Visible) _challenges.HandleMenu(menu); else _menu.HandleMenu(menu);
                        break;
                    case GameState.Garage: _garage.HandleMenu(menu); break;
                    case GameState.Settings: _settings.HandleMenu(menu); break;
                    case GameState.Paused:
                        if (_results.Visible) _results.HandleMenu(menu); else if (actions.pause) Resume(); else _pause.HandleMenu(menu);
                        break;
                    case GameState.LayoutEditor:
                        if (menu.back) ExitEditor();
                        break;
                    case GameState.Intro:
                        if (actions.skip || actions.pause) Intro.Skip();
                        break;
                    case GameState.Driving:
                        if (actions.pause) Pause();
                        else if (actions.reset) ResetCar(false);
                        else if (actions.repair) ResetCar(true);
                        else if (actions.camera) CycleCamera();
                        else if (actions.editor) EnterEditor();
                        if (actions.telemetry) _hud.TelemetryVisible = !_hud.TelemetryVisible;
                        break;
                }

            if (Flow.State == GameState.Driving)
                DriveTick(Time.deltaTime);
            else if (Flow.State == GameState.Intro && Intro.InRollOut)
            {
                // The intro autopilot owns input until the player touches the controls (instant takeover).
                var d = input.Drive;
                if (d.throttle > 0.05f || d.brake > 0.05f || Mathf.Abs(d.steer) > 0.2f) Intro.Skip();
            }
            Ui.Tick(dt);

            _authTimer += dt;
            if (_authTimer >= 1f) { _authTimer = 0f; Services.Auth.Tick(); }

            _saveTimer += dt;
            if (_saveTimer > 30f)
            {
                _saveTimer = 0f;
                Services.Profile.playTimeSec += 30f;
                Services.SaveProfile();
            }
        }

        private void DriveTick(float dt)
        {
            var s = Services.Settings;
            var d = Services.Input.Drive;
            if (Mathf.Abs(s.steeringLinearity - 1f) > 0.01f && !d.digitalSteer) d.steer = Mathf.Sign(d.steer) * Mathf.Pow(Mathf.Abs(d.steer), s.steeringLinearity);
            if (s.handbrakeToggle)
            {
                if (d.handbrake > 0.5f && !_hbPrev) _handbrakeLatched = !_handbrakeLatched;
                _hbPrev = d.handbrake > 0.5f;
                d.handbrake = _handbrakeLatched ? 1f : 0f;
            }
            if (Clock.Phase == RunPhase.Countdown) { d.throttle = 0f; d.handbrake = 1f; }
            Session.Car.Input = d;
            if (d.throttle > 0.05f) _hud.SetHint("");

            Clock.Update(dt);
            if (Clock.Phase == RunPhase.Running || Clock.Rules == null || Clock.Rules.kind == RunKind.FreeDrive) Session.Drift.Update(dt);
            var t = Session.Car.Telemetry;
            var p = Session.Car.transform.position;
            if (_recorder.IsRecording) _recorder.Update(dt, p.x, p.y, p.z, Session.Car.transform.eulerAngles.y, t.driftAngleDeg, t.speedMs, t.steerInput);
            if (CurrentRun == RunKind.GhostTandem && _ghost != null && Clock.Phase == RunPhase.Running)
            {
                var g = _ghost.Sample(Clock.Time);
                _ghostCar.SetPose(g);
                _tandem.Update(dt, new Vec2(p.x, p.z), t.driftAngleDeg, Session.Drift.Scoring.IsDrifting, new Vec2(g.x, g.z), g.driftAngleDeg, Mathf.Abs(g.driftAngleDeg) > 12f && g.speedMs > 5f);
            }
            var sc = Session.Drift.Scoring;
            Services.Challenges.Report(new RunStats
            {
                dt = dt,
                runScore = Clock.Rules != null && Clock.Rules.kind != RunKind.FreeDrive ? sc.TotalPoints + sc.ChainPoints : 0f,
                bankedChain = sc.BestChain,
                driftAngleDeg = t.driftAngleDeg,
                drifting = sc.IsDrifting,
                transitionsDelta = Math.Max(0, sc.Transitions - _lastTransitions),
                speedMs = t.speedMs,
                collision = false
            });
            _lastTransitions = sc.ChainActive ? sc.Transitions : 0;
            Services.Profile.distanceDrivenKm += t.speedMs * dt / 1000f;
        }

        private bool _hbPrev;

        private void OnApplicationPause(bool paused)
        {
            if (!paused || Services == null) return;
            Services.SaveProfile();
            Services.SaveSettings();
            if (Flow.State == GameState.Driving) Pause();
        }

        private void OnApplicationQuit()
        {
            if (Services == null) return;
            Services.SaveProfile();
            Services.SaveSettings();
        }
    }
}

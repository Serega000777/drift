using System.Collections.Generic;
using DriftCareer.Core.Gameplay;
using DriftCareer.Core.Layout;
using DriftCareer.Runtime.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace DriftCareer.Runtime.UI
{
    public sealed class PauseScreen : UiScreen
    {
        protected override void Build()
        {
            Root.pickingMode = PickingMode.Position;
            Root.Add(Ui.El("dc-dim"));
            var col = Ui.El("dc-center-col");
            var box = Ui.El("dc-panel", "dc-menu-col");
            box.Add(Ui.Label(T("pause.title"), "dc-h1"));
            MenuButton(box, "pause.resume", () => Game.Resume(), "dc-btn--primary");
            MenuButton(box, "pause.drills", () =>
            {
                Game.Resume();
                UiRoot.Pick(T("drill.choose"), new[] { T("drill.donut.title"), T("drill.figure8.title"), T("drill.slalom.title"), T("drill.clipping.title") },
                    i => Game.StartDrill((DrillKind)(i + 1)));
            });
            MenuButton(box, "pause.reset_car", () => Game.ResetCar(false));
            MenuButton(box, "pause.repair", () => Game.ResetCar(true));
            MenuButton(box, "pause.editor", () => Game.EnterEditor());
            MenuButton(box, "pause.garage", () => Game.OpenGarage());
            MenuButton(box, "pause.settings", () => Game.OpenSettings());
            MenuButton(box, "pause.exit_menu", () => Game.ExitToMenu());
            col.Add(box);
            Root.Add(col);
        }

        public override void OnBack() => Game.Resume();
    }

    public sealed class ResultsScreen : UiScreen
    {
        private RunKind _kind;
        private float _score;
        private Medal _medal;
        private int _reward;
        private bool _record;
        private TournamentRun _tournament;

        public void SetResult(RunKind kind, float score, Medal medal, int reward, bool record, TournamentRun tournament)
        {
            _kind = kind; _score = score; _medal = medal; _reward = reward; _record = record; _tournament = tournament;
            if (Root != null) Rebuild();
        }

        protected override void Build()
        {
            Root.pickingMode = PickingMode.Position;
            Root.Add(Ui.El("dc-dim"));
            var col = Ui.El("dc-center-col");
            var box = Ui.El("dc-panel", "dc-menu-col");
            box.Add(Ui.Label(T("run." + _kind), "dc-h1"));
            string scoreText = _kind == RunKind.GhostTandem ? Mathf.RoundToInt(_score) + " / 100" : Mathf.RoundToInt(_score).ToString("N0");
            box.Add(Ui.Label(T("results.score", scoreText), "dc-h2"));
            var medal = Ui.Label(T("medal." + _medal), "dc-badge");
            medal.style.alignSelf = Align.FlexStart;
            medal.style.backgroundColor = _medal == Medal.Gold ? new Color(0.96f, 0.77f, 0f) : _medal == Medal.Silver ? new Color(0.75f, 0.78f, 0.8f) : _medal == Medal.Bronze ? new Color(0.8f, 0.5f, 0.25f) : new Color(0.3f, 0.3f, 0.3f);
            box.Add(medal);
            if (_reward > 0) box.Add(Ui.Label(T("hud.reward", _reward.ToString("N0")), "dc-h2"));
            if (_record) box.Add(Ui.Label(T("hud.new_record"), "dc-h2"));
            bool tournamentNext = _tournament != null && _kind == RunKind.TournamentRound && !_tournament.Done;
            if (_tournament != null && _kind == RunKind.TournamentRound)
            {
                box.Add(Ui.Label(T("tournament.round", _tournament.RoundIndex, _tournament.Definition.rounds.Count), "dc-muted"));
                box.Add(Ui.Label(T("tournament.total", Mathf.RoundToInt(_tournament.Total).ToString("N0")), "dc-muted"));
                if (_tournament.Done) box.Add(Ui.Label(T("medal." + _tournament.FinalMedal()) + " · " + T("tournament.prize", _tournament.Prize()), "dc-h2"));
            }
            MenuButton(box, tournamentNext ? "results.next_round" : "results.retry", () => Game.RetryRun(), "dc-btn--primary");
            MenuButton(box, "results.continue", () => Game.ExitToMenu());
            col.Add(box);
            Root.Add(col);
        }

        public override void OnBack() => Game.ExitToMenu();
    }

    public sealed class ChallengesScreen : UiScreen
    {
        protected override void Build()
        {
            Root.pickingMode = PickingMode.Position;
            Root.Add(Ui.El("dc-dim"));
            var panel = Ui.El("dc-panel");
            panel.style.position = Position.Absolute;
            panel.style.left = 120; panel.style.right = 120; panel.style.top = 80; panel.style.bottom = 80;
            var head = Ui.Row();
            head.Add(Ui.Label(T("challenges.title"), "dc-h1"));
            var grow = Ui.El("dc-grow"); head.Add(grow);
            head.Add(Ui.Label(T("hud.credits", S.Profile.wallet.credits.ToString("N0")), "dc-h2"));
            panel.Add(head);
            var scroll = new ScrollView();
            scroll.AddToClassList("dc-scroll");
            var tracker = S.Challenges;
            foreach (bool daily in new[] { true, false })
            {
                scroll.Add(Ui.Label(T(daily ? "challenges.daily" : "challenges.permanent"), "dc-h2"));
                foreach (var d in tracker.Active)
                {
                    if (d.daily != daily) continue;
                    var p = tracker.Progress(d);
                    var row = Ui.El("dc-setting");
                    row.Add(Ui.Label(S.Loc.Format(d.TitleKey, d.target.ToString("N0"), d.param.ToString("0")), "dc-setting-label"));
                    row.Add(Ui.Label(T("challenges.progress", Mathf.Min(p.value, d.target).ToString("N0"), d.target.ToString("N0")), "dc-setting-value"));
                    var ctrl = Ui.El("dc-setting-control");
                    var def = d;
                    if (p.claimed) ctrl.Add(Ui.Label(T("challenges.claimed"), "dc-badge", "dc-badge--soon"));
                    else
                    {
                        var b = Ui.Button(T("challenges.claim", d.reward.ToString("N0")), () =>
                        {
                            int got = tracker.Claim(def, S.Profile.wallet);
                            if (got > 0) { S.SaveProfile(); UiRoot.Toast(T("hud.reward", got.ToString("N0")), 1.6f, "#3DDC84"); Rebuild(); Nav.FocusFirst(); }
                        }, "dc-btn--chip");
                        b.SetEnabled(p.completed);
                        ctrl.Add(b);
                    }
                    row.Add(ctrl);
                    scroll.Add(row);
                    Nav.Register(row, () => { if (p.completed && !p.claimed) { tracker.Claim(def, S.Profile.wallet); S.SaveProfile(); Rebuild(); Nav.FocusFirst(); } });
                }
            }
            panel.Add(scroll);
            var back = Ui.Button(T("common.back"), () => Game.CloseChallenges());
            panel.Add(back);
            Nav.Register(back, () => Game.CloseChallenges());
            Root.Add(panel);
        }

        public override void OnBack() => Game.CloseChallenges();
    }

    /// <summary>Layout editor UI: object palette, tools, presets, save/open, budget and a pointer viewport.</summary>
    public sealed class EditorScreen : UiScreen
    {
        private readonly List<VisualElement> _items = new List<VisualElement>();
        private VisualElement _budgetFill;
        private Label _count;
        private VisualElement _crosshair;

        protected override void Build()
        {
            _items.Clear();
            Root.pickingMode = PickingMode.Ignore;
            var editor = Game.Editor;

            // Full-screen viewport receiving pointer gestures for the 3D view.
            var viewport = Ui.El("ed-viewport");
            viewport.pickingMode = PickingMode.Position;
            viewport.RegisterCallback<PointerDownEvent>(e =>
            {
                viewport.CapturePointer(e.pointerId);
                editor.IsTouch = e.pointerType == "touch";
                editor.PointerDown(e.pointerId, UiRoot.PanelToScreen(e.position), e.button);
            });
            viewport.RegisterCallback<PointerMoveEvent>(e =>
            {
                var sp = UiRoot.PanelToScreen(e.position);
                editor.Hover(sp);
                editor.PointerMove(e.pointerId, sp);
            });
            viewport.RegisterCallback<PointerUpEvent>(e =>
            {
                if (viewport.HasPointerCapture(e.pointerId)) viewport.ReleasePointer(e.pointerId);
                editor.PointerUp(e.pointerId, UiRoot.PanelToScreen(e.position), e.button);
            });
            viewport.RegisterCallback<WheelEvent>(e => editor.Scroll(-e.delta.y * 0.3f));
            Root.Add(viewport);
            _crosshair = Ui.El("ed-crosshair");
            _crosshair.pickingMode = PickingMode.Ignore;
            Root.Add(_crosshair);

            var palette = Ui.El("dc-panel", "ed-palette");
            palette.Add(Ui.Label(T("editor.palette"), "dc-h2"));
            var list = new ScrollView();
            list.AddToClassList("dc-scroll");
            foreach (var def in Game.Session.Layout.Library.All)
            {
                var d = def;
                var item = Ui.Button(T(d.titleKey), () => { editor.SelectDef(d.id); RefreshPalette(); }, "ed-item");
                item.userData = d.id;
                list.Add(item);
                _items.Add(item);
                Nav.Register(item, () => { editor.SelectDef(d.id); RefreshPalette(); });
            }
            palette.Add(list);
            Root.Add(palette);

            var bar = Ui.El("ed-toolbar");
            void Tool(string key, System.Action a, string cls = null)
            {
                var b = cls != null ? Ui.Button(T(key), a, "dc-btn--chip", cls) : Ui.Button(T(key), a, "dc-btn--chip");
                bar.Add(b);
                Nav.Register(b, a);
            }
            Tool("editor.rotate_left", () => editor.RotateSelectedOrGhost(-15f));
            Tool("editor.rotate_right", () => editor.RotateSelectedOrGhost(15f));
            Tool("editor.duplicate", () => editor.DuplicateSelected());
            Tool("editor.delete", () => editor.DeleteSelected(), "dc-btn--danger");
            Tool("editor.undo", () => editor.Undo());
            Tool("editor.redo", () => editor.Redo());
            Tool("editor.snap", () => { editor.ToggleSnap(); UiRoot.Toast(T("editor.snap") + ": " + T(editor.Model.SnapToGrid ? "common.on" : "common.off"), 1f); });
            Tool("editor.presets", ShowPresets);
            Tool("editor.save", SaveLayout);
            Tool("editor.load", LoadLayout);
            Tool("editor.clear", () => UiRoot.Confirm(T("editor.clear_confirm"), () => editor.Clear()), "dc-btn--danger");
            Root.Add(bar);

            var bottom = Ui.El("ed-bottom");
            var place = Ui.Button("+", () => editor.PlaceAtCenter(), "dc-btn--icon");
            bottom.Add(place);
            _count = Ui.Label("", "dc-small");
            bottom.Add(_count);
            var budget = Ui.El("ed-budget");
            _budgetFill = Ui.El("ed-budget-fill");
            budget.Add(_budgetFill);
            bottom.Add(budget);
            bottom.Add(Ui.Label(T(S.Input.ActiveDevice == Input.InputDevice.Touch ? "editor.hint_touch" : "editor.hint_desktop"), "ed-hint"));
            var drive = Ui.Button(T("editor.drive"), () => Game.ExitEditor(), "dc-btn--primary");
            bottom.Add(drive);
            Nav.Register(drive, () => Game.ExitEditor());
            Root.Add(bottom);
            RefreshPalette();
        }

        private void RefreshPalette()
        {
            foreach (var i in _items) i.EnableInClassList("is-selected", (string)i.userData == Game.Editor.SelectedDef);
        }

        private void ShowPresets()
        {
            var labels = new List<string>();
            foreach (var id in LayoutPresets.Ids) labels.Add(T("layout_preset." + id));
            UiRoot.Pick(T("editor.presets"), labels, i =>
            {
                int skipped = Game.Editor.ApplyPreset(LayoutPresets.Ids[i]);
                if (skipped > 0) UiRoot.Toast("−" + skipped, 1.5f);
            });
        }

        private void SaveLayout()
        {
            UiRoot.Prompt(T("editor.layout_name"), Game.Session.Layout.Layout.name, name =>
            {
                Game.Session.Layout.Layout.name = LayoutNames.Sanitize(name);
                S.SaveLayout(Game.Session.Layout.Layout);
                UiRoot.Toast(T("editor.saved"), 1.5f, "#3DDC84");
            });
        }

        private void LoadLayout()
        {
            var files = S.ListLayoutFiles();
            files.RemoveAll(f => f.Contains(".corrupt-") || f.EndsWith(".bak") || f.EndsWith(".tmp"));
            if (files.Count == 0) { UiRoot.Toast(T("editor.no_layouts")); return; }
            var names = new List<string>();
            foreach (var f in files) names.Add(System.IO.Path.GetFileNameWithoutExtension(f));
            UiRoot.Pick(T("editor.load"), names, i =>
            {
                var layout = S.LoadLayoutFile(files[i]);
                Game.Session.Layout.ReplaceLayout(layout);
                UiRoot.Toast(T("editor.loaded"), 1.5f, "#3DDC84");
            });
        }

        public override void Tick(float dt)
        {
            var m = Game.Session.Layout;
            if (_count != null) _count.text = T("editor.count", m.Count);
            if (_budgetFill != null)
            {
                float k = Mathf.Clamp01(m.BudgetUsed / Mathf.Max(1f, m.BudgetTotal));
                _budgetFill.style.width = Length.Percent(k * 100f);
                _budgetFill.style.backgroundColor = k < 0.75f ? new Color(0.24f, 0.86f, 0.52f) : k < 0.95f ? new Color(0.96f, 0.77f, 0f) : new Color(1f, 0.23f, 0.19f);
            }
            if (_crosshair != null) Ui.SetVisible(_crosshair, Game.Editor.IsTouch);
        }

        public override void OnBack() => Game.ExitEditor();
    }
}

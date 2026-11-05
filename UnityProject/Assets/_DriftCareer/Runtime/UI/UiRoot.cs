using System;
using System.Collections.Generic;
using DriftCareer.Runtime.Security;
using UnityEngine;
using UnityEngine.UIElements;

namespace DriftCareer.Runtime.UI
{
    /// <summary>
    /// Creates the runtime UI Toolkit panel (scale-with-screen for phones, PC and TVs), layers
    /// (screens, HUD, overlays), toasts, dialogs and the fade curtain. One UIDocument for the whole game.
    /// </summary>
    public sealed class UiRoot : MonoBehaviour
    {
        public GameController Game { get; private set; }
        public VisualElement Root { get; private set; }
        public VisualElement ScreenLayer { get; private set; }
        public VisualElement HudLayer { get; private set; }
        public VisualElement OverlayLayer { get; private set; }

        private UIDocument _doc;
        private VisualElement _fade;
        private Label _toast;
        private IVisualElementScheduledItem _toastHide;
        private VisualElement _dialogDim;
        private readonly List<UiScreen> _screens = new List<UiScreen>();
        private readonly Stack<FocusNavigator> _modal = new Stack<FocusNavigator>();
        private Action _modalBack;

        public LoadingScreen Loading { get; private set; }
        public TransitionOverlay Transition { get; private set; }

        public bool HasModal => _modal.Count > 0;
        public FocusNavigator ModalNav => _modal.Count > 0 ? _modal.Peek() : null;

        public static UiRoot Create(GameController game, Transform parent, float uiScale)
        {
            var go = new GameObject("UI");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var ui = go.AddComponent<UiRoot>();
            ui.Game = game;
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = "DC_PanelSettings";
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(Mathf.RoundToInt(1920 / uiScale), Mathf.RoundToInt(1080 / uiScale));
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            ps.sortingOrder = 10;
            var theme = Resources.Load<ThemeStyleSheet>("DriftCareer/UI/DriftTheme");
            if (theme != null) ps.themeStyleSheet = theme;
            else SafeLog.Warn("UI theme not found (Resources/DriftCareer/UI/DriftTheme.tss)");
            ui._doc = go.AddComponent<UIDocument>();
            ui._doc.panelSettings = ps;
            go.SetActive(true);
            ui.BuildLayers();
            return ui;
        }

        private void BuildLayers()
        {
            Root = _doc.rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("DriftCareer/UI/Main");
            if (sheet != null) Root.styleSheets.Add(sheet);
            Root.AddToClassList("dc-root");
            Root.pickingMode = PickingMode.Ignore;
            HudLayer = Ui.El("dc-layer");
            ScreenLayer = Ui.El("dc-layer");
            OverlayLayer = Ui.El("dc-layer");
            foreach (var l in new[] { HudLayer, ScreenLayer, OverlayLayer }) { l.pickingMode = PickingMode.Ignore; Root.Add(l); }
            Loading = new LoadingScreen(OverlayLayer);
            Transition = new TransitionOverlay(OverlayLayer);
            _toast = Ui.Label("", "dc-toast");
            _toast.pickingMode = PickingMode.Ignore;
            OverlayLayer.Add(_toast);
            _fade = Ui.El("dc-fade");
            _fade.pickingMode = PickingMode.Ignore;
            OverlayLayer.Add(_fade);
        }

        public T Register<T>(T screen, bool hudLayer = false) where T : UiScreen
        {
            screen.Attach(this, hudLayer ? HudLayer : ScreenLayer);
            _screens.Add(screen);
            return screen;
        }

        /// <summary>Rebuilds every screen (language change).</summary>
        public void RebuildAll()
        {
            foreach (var s in _screens)
            {
                s.Rebuild();
                if (s.Visible) s.Nav.FocusFirst();
            }
        }

        public void Tick(float dt)
        {
            foreach (var s in _screens) if (s.Visible) s.Tick(dt);
        }

        /// <summary>Converts a UI Toolkit panel position to screen pixels (origin bottom-left).</summary>
        public Vector2 PanelToScreen(Vector2 panel)
        {
            var size = Root.layout.size;
            if (size.x <= 0f || size.y <= 0f) return panel;
            return new Vector2(panel.x * Screen.width / size.x, Screen.height - panel.y * Screen.height / size.y);
        }

        public void Fade(bool toBlack, Action then = null, long ms = 520)
        {
            if (toBlack) _fade.AddToClassList("dc-fade--on");
            else _fade.RemoveFromClassList("dc-fade--on");
            _fade.pickingMode = toBlack ? PickingMode.Position : PickingMode.Ignore;
            if (then != null) _fade.schedule.Execute(then).StartingIn(ms);
        }

        /// <summary>Angled wipe (or fade with reduce-motion); <paramref name="midpoint"/> runs while the screen is covered.</summary>
        public void Wipe(Action midpoint)
        {
            bool calm = Game != null && Game.Services != null && Game.Services.Settings.reduceMotion;
            Transition.Wipe(midpoint, calm, this);
        }

        public void Toast(string text, float seconds = 2.2f, string color = null)
        {
            _toast.text = text;
            _toast.style.borderLeftColor = color != null ? new StyleColor(Ui.Hex(color, Color.yellow)) : new StyleColor(StyleKeyword.Null);
            _toast.AddToClassList("dc-toast--show");
            _toastHide?.Pause();
            _toastHide = _toast.schedule.Execute(() => _toast.RemoveFromClassList("dc-toast--show"));
            _toastHide.ExecuteLater((long)(seconds * 1000));
        }

        // ---------------- dialogs ----------------

        private VisualElement OpenDialog(string title, out FocusNavigator nav, Action onBack)
        {
            CloseDialog();
            _dialogDim = Ui.El("dc-dim");
            var center = Ui.El("dc-center-col");
            var box = Ui.El("dc-dialog");
            box.Add(Ui.Label(title, "dc-h2"));
            center.Add(box);
            _dialogDim.Add(center);
            OverlayLayer.Insert(0, _dialogDim);
            Ui.AddClassNextFrame(box, "dc-dialog--open");
            nav = new FocusNavigator();
            _modal.Push(nav);
            _modalBack = onBack;
            return box;
        }

        public void CloseDialog()
        {
            if (_dialogDim != null) _dialogDim.RemoveFromHierarchy();
            _dialogDim = null;
            _modal.Clear();
            _modalBack = null;
        }

        public bool HandleModal(Input.MenuInput m)
        {
            if (!HasModal) return false;
            if (m.back) { var b = _modalBack; CloseDialog(); b?.Invoke(); return true; }
            ModalNav.Handle(m);
            return true;
        }

        public void Confirm(string text, Action onYes, Action onNo = null)
        {
            var box = OpenDialog(text, out var nav, onNo);
            var row = Ui.Row();
            var yes = Ui.Button(Game.Services.T("common.yes"), () => { CloseDialog(); onYes?.Invoke(); }, "dc-btn--primary");
            var no = Ui.Button(Game.Services.T("common.no"), () => { CloseDialog(); onNo?.Invoke(); });
            row.Add(yes); row.Add(no);
            box.Add(row);
            nav.Register(yes, () => { CloseDialog(); onYes?.Invoke(); });
            nav.Register(no, () => { CloseDialog(); onNo?.Invoke(); });
            nav.FocusFirst();
        }

        public void Info(string title, string text)
        {
            var box = OpenDialog(title, out var nav, null);
            box.Add(Ui.Label(text, "dc-dialog-text"));
            var ok = Ui.Button(Game.Services.T("common.close"), CloseDialog, "dc-btn--primary");
            box.Add(ok);
            nav.Register(ok, CloseDialog);
            nav.FocusFirst();
        }

        public void Prompt(string title, string initial, Action<string> onOk)
        {
            var box = OpenDialog(title, out var nav, null);
            var field = new TextField { value = initial, maxLength = 40 };
            field.AddToClassList("dc-text-field");
            box.Add(field);
            var row = Ui.Row();
            void Ok() { string v = field.value; CloseDialog(); onOk?.Invoke(v); }
            var ok = Ui.Button(Game.Services.T("common.save"), Ok, "dc-btn--primary");
            var cancel = Ui.Button(Game.Services.T("common.cancel"), CloseDialog);
            row.Add(ok); row.Add(cancel);
            box.Add(row);
            nav.Register(ok, Ok);
            nav.Register(cancel, CloseDialog);
            field.schedule.Execute(() => field.Focus()).StartingIn(50);
        }

        public void Pick(string title, IList<string> labels, Action<int> onPick)
        {
            var box = OpenDialog(title, out var nav, null);
            var list = new ScrollView();
            list.AddToClassList("dc-list");
            for (int i = 0; i < labels.Count; i++)
            {
                int idx = i;
                void Choose() { CloseDialog(); onPick?.Invoke(idx); }
                var b = Ui.Button(labels[i], Choose);
                list.Add(b);
                nav.Register(b, Choose);
            }
            box.Add(list);
            var cancel = Ui.Button(Game.Services.T("common.cancel"), CloseDialog);
            box.Add(cancel);
            nav.Register(cancel, CloseDialog);
            nav.FocusFirst();
        }
    }
}

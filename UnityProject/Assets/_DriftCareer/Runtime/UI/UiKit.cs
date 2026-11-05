using System;
using System.Collections.Generic;
using DriftCareer.Runtime.Input;
using UnityEngine;
using UnityEngine.UIElements;

namespace DriftCareer.Runtime.UI
{
    /// <summary>Small factory helpers so screens stay readable. Styles live in Resources/DriftCareer/UI/Main.uss.</summary>
    public static class Ui
    {
        public static VisualElement El(params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) e.AddToClassList(c);
            return e;
        }

        public static Label Label(string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) l.AddToClassList(c);
            return l;
        }

        public static Button Button(string text, Action onClick, params string[] classes)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("dc-btn");
            foreach (var c in classes) b.AddToClassList(c);
            b.focusable = false; // focus is handled by FocusNavigator for consistent gamepad/keyboard behaviour
            return b;
        }

        public static VisualElement Row(params string[] classes)
        {
            var e = El(classes);
            e.AddToClassList("dc-row");
            return e;
        }

        public static void SetVisible(VisualElement e, bool visible) => e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>Adds a class next frame so USS transitions run from the initial state.</summary>
        public static void AddClassNextFrame(VisualElement e, string cls, long delayMs = 16)
        {
            e.schedule.Execute(() => e.AddToClassList(cls)).StartingIn(delayMs);
        }

        public static Color Hex(string hex, Color fallback) => Track.MaterialLibrary.ParseHex(hex, fallback);

        /// <summary>Vertical gradient texture (used for vignettes and card art backgrounds).</summary>
        public static Texture2D Gradient(Color a, Color b, bool horizontal, int size = 64)
        {
            var t = new Texture2D(horizontal ? size : 2, horizontal ? 2 : size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < size; i++)
            {
                var c = Color.Lerp(a, b, i / (float)(size - 1));
                if (horizontal) { t.SetPixel(i, 0, c); t.SetPixel(i, 1, c); }
                else { t.SetPixel(0, i, c); t.SetPixel(1, i, c); }
            }
            t.Apply();
            return t;
        }
    }

    /// <summary>
    /// Spatial focus navigation for gamepad, keyboard and TV remotes (console-ready). Entries carry their own
    /// submit/adjust callbacks so the same UI works with touch (click) and pads (move/submit/left-right).
    /// </summary>
    public sealed class FocusNavigator
    {
        private sealed class Entry
        {
            public VisualElement element;
            public Action submit;
            public Action<int> adjust;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private int _index = -1;

        public VisualElement Focused => _index >= 0 && _index < _entries.Count ? _entries[_index].element : null;
        public event Action<VisualElement> FocusChanged;

        public void Clear()
        {
            if (Focused != null) Focused.RemoveFromClassList("is-focused");
            _entries.Clear();
            _index = -1;
        }

        public void Register(VisualElement e, Action submit, Action<int> adjust = null)
        {
            _entries.Add(new Entry { element = e, submit = submit, adjust = adjust });
            e.RegisterCallback<PointerEnterEvent>(_ => Focus(e, false));
        }

        public void Focus(VisualElement e, bool scroll = true)
        {
            int i = _entries.FindIndex(x => x.element == e);
            if (i < 0) return;
            SetIndex(i, scroll);
        }

        public void FocusFirst()
        {
            for (int i = 0; i < _entries.Count; i++)
                if (IsUsable(_entries[i].element)) { SetIndex(i, true); return; }
        }

        private void SetIndex(int i, bool scroll)
        {
            if (Focused != null) Focused.RemoveFromClassList("is-focused");
            _index = i;
            var e = Focused;
            if (e == null) return;
            e.AddToClassList("is-focused");
            if (scroll)
            {
                var sv = e.GetFirstAncestorOfType<ScrollView>();
                sv?.ScrollTo(e);
            }
            FocusChanged?.Invoke(e);
        }

        private static bool IsUsable(VisualElement e)
        {
            for (var p = e; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None) return false;
            return e.enabledInHierarchy;
        }

        /// <summary>Handles menu input. Returns true if consumed.</summary>
        public bool Handle(MenuInput m)
        {
            if (_entries.Count == 0) return false;
            if (_index < 0 && (m.moveX != 0 || m.moveY != 0 || m.submit)) { FocusFirst(); return true; }
            var cur = _index >= 0 ? _entries[_index] : null;
            if (m.submit && cur != null) { cur.submit?.Invoke(); return true; }
            if (m.moveX != 0 && cur?.adjust != null) { cur.adjust(m.moveX); return true; }
            if (m.moveX != 0 || m.moveY != 0) { MoveSpatial(m.moveX, -m.moveY); return true; }
            return false;
        }

        private void MoveSpatial(int dx, int dy)
        {
            var cur = Focused;
            if (cur == null) { FocusFirst(); return; }
            Vector2 from = cur.worldBound.center;
            Vector2 dir = new Vector2(dx, dy).normalized;
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (i == _index || !IsUsable(_entries[i].element)) continue;
                Vector2 d = _entries[i].element.worldBound.center - from;
                float along = Vector2.Dot(d, dir);
                if (along <= 1f) continue;
                float across = Mathf.Abs(dir.x != 0 ? d.y : d.x);
                float score = along + across * 2.5f;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best >= 0) SetIndex(best, true);
        }
    }

    /// <summary>Base class of all screens. Screens are code-built (diffable, AI-editable) and localized on build.</summary>
    public abstract class UiScreen
    {
        public VisualElement Root { get; private set; }
        public FocusNavigator Nav { get; } = new FocusNavigator();
        public bool Visible { get; private set; }
        protected GameServices S => GameServices.Instance;
        protected UiRoot UiRoot { get; private set; }
        protected GameController Game => UiRoot.Game;

        protected string T(string key) => S.T(key);
        protected string T(string key, params object[] args) => S.Loc.Format(key, args);

        public void Attach(UiRoot root, VisualElement layer)
        {
            UiRoot = root;
            Root = Ui.El("dc-screen", "dc-screen--off");
            Root.pickingMode = PickingMode.Ignore;
            layer.Add(Root);
            Ui.SetVisible(Root, false);
        }

        /// <summary>(Re)builds the content. Called on first show and when the language changes.</summary>
        public void Rebuild()
        {
            Root.Clear();
            Nav.Clear();
            Build();
        }

        protected abstract void Build();

        public virtual void Show()
        {
            if (Root.childCount == 0) Rebuild();
            Visible = true;
            Ui.SetVisible(Root, true);
            Root.schedule.Execute(() => Root.RemoveFromClassList("dc-screen--off")).StartingIn(16);
            OnShow();
        }

        public virtual void Hide()
        {
            if (!Visible) return;
            Visible = false;
            Root.AddToClassList("dc-screen--off");
            Root.schedule.Execute(() => { if (!Visible) Ui.SetVisible(Root, false); }).StartingIn(380);
            OnHide();
        }

        protected virtual void OnShow() { }
        protected virtual void OnHide() { }
        public virtual void Tick(float dt) { }

        /// <summary>Menu input routing. Back -> <see cref="OnBack"/>.</summary>
        public virtual bool HandleMenu(MenuInput m)
        {
            if (m.back) { OnBack(); return true; }
            return Nav.Handle(m);
        }

        public virtual void OnBack() { }

        // ---------- reusable controls ----------

        protected Button MenuButton(VisualElement parent, string key, Action action, params string[] classes)
        {
            var b = Ui.Button(T(key), action, classes);
            parent.Add(b);
            Nav.Register(b, action);
            return b;
        }

        /// <summary>Row with label and ◀ value ▶ cycler (gamepad left/right friendly).</summary>
        protected VisualElement CyclerRow(VisualElement parent, string labelKey, string[] optionKeys, Func<int> get, Action<int> set)
        {
            var row = Ui.El("dc-setting");
            row.Add(Ui.Label(T(labelKey), "dc-setting-label"));
            var ctrl = Ui.El("dc-setting-control");
            var cycler = Ui.El("dc-cycler");
            var value = Ui.Label("", "dc-cycler-value");
            void Refresh() => value.text = T(optionKeys[Mathf.Clamp(get(), 0, optionKeys.Length - 1)]);
            void Step(int d)
            {
                int n = optionKeys.Length;
                set(((get() + d) % n + n) % n);
                Refresh();
            }
            cycler.Add(Ui.Button("‹", () => Step(-1), "dc-cycler-btn"));
            cycler.Add(value);
            cycler.Add(Ui.Button("›", () => Step(1), "dc-cycler-btn"));
            ctrl.Add(cycler);
            row.Add(ctrl);
            parent.Add(row);
            Refresh();
            Nav.Register(row, () => Step(1), Step);
            return row;
        }

        protected VisualElement ToggleRow(VisualElement parent, string labelKey, Func<bool> get, Action<bool> set)
            => CyclerRow(parent, labelKey, new[] { "common.off", "common.on" }, () => get() ? 1 : 0, i => set(i == 1));

        /// <summary>Row with label, slider and formatted value.</summary>
        protected VisualElement SliderRow(VisualElement parent, string label, float min, float max, float step, Func<float> get, Action<float> set, Func<float, string> format)
        {
            var row = Ui.El("dc-setting");
            row.Add(Ui.Label(label, "dc-setting-label"));
            var slider = new Slider(min, max) { value = get() };
            slider.AddToClassList("dc-slider");
            var value = Ui.Label(format(get()), "dc-setting-value");
            slider.RegisterValueChangedCallback(e =>
            {
                float v = step > 0f ? Mathf.Round(e.newValue / step) * step : e.newValue;
                set(v);
                value.text = format(get());
            });
            row.Add(slider);
            row.Add(value);
            parent.Add(row);
            Nav.Register(row, null, d =>
            {
                float v = Mathf.Clamp(get() + d * (step > 0f ? step : (max - min) / 20f), min, max);
                slider.value = v;
            });
            return row;
        }
    }
}

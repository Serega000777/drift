using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DriftCareer.Runtime.UI
{
    /// <summary>
    /// Full-screen loading overlay: animated diagonal stripes, logo reveal (splash), spinning tire, smoothed
    /// progress bar with real stage names, rotating drift tips. Lives above all screens. Respects "reduce motion".
    /// </summary>
    public sealed class LoadingScreen
    {
        private const int TipCount = 12;

        private readonly UiRoot _ui;
        private readonly VisualElement _root;
        private readonly VisualElement _stripes;
        private readonly VisualElement _logo;
        private readonly VisualElement _fill;
        private readonly VisualElement _spinner;
        private readonly Label _stage;
        private readonly Label _percent;
        private readonly Label _tip;
        private float _target, _shown, _spin, _stripeOffset, _tipTimer;
        private int _tipIndex;
        private bool _visible;
        private float _visibleSince;
        private IVisualElementScheduledItem _tick;

        public bool Visible => _visible;

        public LoadingScreen(UiRoot ui, VisualElement layer)
        {
            _ui = ui;
            _root = Ui.El("ld-root");
            _root.pickingMode = PickingMode.Position;
            _stripes = Ui.El("ld-stripes");
            _stripes.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < 14; i++)
            {
                var s = Ui.El("ld-stripe");
                s.style.left = new Length(i * 220 - 400, LengthUnit.Pixel);
                if (i % 3 == 0) s.AddToClassList("ld-stripe--accent");
                _stripes.Add(s);
            }
            _root.Add(_stripes);
            _root.Add(Ui.El("ld-vignette"));

            _logo = Ui.El("ld-logo");
            var row = Ui.Row();
            row.Add(Ui.Label("DRIFT ", "dc-title"));
            row.Add(Ui.Label("CAREER", "dc-title", "dc-title-accent"));
            _logo.Add(row);
            _logo.Add(Ui.El("mm-logo-stripe"));
            _root.Add(_logo);

            var bottom = Ui.El("ld-bottom");
            var line = Ui.Row();
            _spinner = Ui.El("ld-spinner");
            _spinner.Add(Ui.El("ld-spinner-hub"));
            line.Add(_spinner);
            var texts = Ui.El();
            texts.style.flexGrow = 1;
            _stage = Ui.Label("", "ld-stage");
            _tip = Ui.Label("", "ld-tip");
            texts.Add(_stage);
            texts.Add(_tip);
            line.Add(texts);
            _percent = Ui.Label("0%", "ld-percent");
            line.Add(_percent);
            bottom.Add(line);
            var bar = Ui.El("ld-bar");
            _fill = Ui.El("ld-fill");
            bar.Add(_fill);
            bottom.Add(bar);
            _root.Add(bottom);

            layer.Add(_root);
            Ui.SetVisible(_root, false);
        }

        private GameServices S => GameServices.Instance;

        public void Show(bool splash)
        {
            _visible = true;
            _visibleSince = Time.realtimeSinceStartup;
            _target = _shown = 0f;
            _tipIndex = UnityEngine.Random.Range(0, TipCount);
            _tipTimer = 0f;
            Ui.SetVisible(_root, true);
            _root.RemoveFromClassList("ld-root--out");
            _root.AddToClassList("ld-root--in");
            _logo.RemoveFromClassList("ld-logo--in");
            if (splash) Ui.AddClassNextFrame(_logo, "ld-logo--in", 60);
            else _logo.AddToClassList("ld-logo--in");
            SetTip();
            _tick?.Pause();
            _tick = _root.schedule.Execute(Tick).Every(16);
        }

        public void SetProgress(float value01, string stageKey)
        {
            _target = Mathf.Max(_target, Mathf.Clamp01(value01));
            if (!string.IsNullOrEmpty(stageKey) && S != null) _stage.text = S.T(stageKey);
        }

        /// <summary>Completes the bar, keeps the screen up for a minimum time (no flicker), then fades out.</summary>
        public void Hide(Action after, float minVisibleSeconds = 0.6f)
        {
            SetProgress(1f, "loading.stage.ready");
            float elapsed = Time.realtimeSinceStartup - _visibleSince;
            long wait = (long)(Mathf.Max(0f, minVisibleSeconds - elapsed) * 1000f) + 250;
            _root.schedule.Execute(() =>
            {
                _root.RemoveFromClassList("ld-root--in");
                _root.AddToClassList("ld-root--out");
                after?.Invoke();
                _root.schedule.Execute(() =>
                {
                    _visible = false;
                    _tick?.Pause();
                    Ui.SetVisible(_root, false);
                }).StartingIn(450);
            }).StartingIn(wait);
        }

        private void SetTip()
        {
            if (S == null) return;
            _tip.text = S.T("loading.tip." + (_tipIndex % TipCount + 1));
            _tip.RemoveFromClassList("ld-tip--out");
        }

        private void Tick()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            bool calm = S != null && S.Settings.reduceMotion;
            _shown = Mathf.MoveTowards(_shown, _target, dt * 1.6f);
            _fill.style.width = Length.Percent(_shown * 100f);
            _percent.text = Mathf.RoundToInt(_shown * 100f) + "%";
            if (!calm)
            {
                _spin = (_spin + dt * 540f) % 360f;
                _spinner.style.rotate = new Rotate(_spin);
                _stripeOffset = (_stripeOffset + dt * 60f) % 220f;
                _stripes.style.translate = new Translate(new Length(_stripeOffset, LengthUnit.Pixel), new Length(0, LengthUnit.Pixel), 0);
            }
            _tipTimer += dt;
            if (_tipTimer > 4.2f)
            {
                _tipTimer = 0f;
                _tip.AddToClassList("ld-tip--out");
                _tip.schedule.Execute(() => { _tipIndex++; SetTip(); }).StartingIn(260);
            }
        }
    }

    /// <summary>
    /// Screen transitions: an angled accent "wipe" (three skewed stripes sweep across, the action runs while the
    /// screen is covered) or a plain fade when "reduce motion" is on.
    /// </summary>
    public sealed class TransitionOverlay
    {
        private readonly VisualElement _root;
        private readonly VisualElement[] _bands = new VisualElement[3];
        private bool _busy;

        public TransitionOverlay(VisualElement layer)
        {
            _root = Ui.El("tr-root");
            _root.pickingMode = PickingMode.Ignore;
            string[] cls = { "tr-band--dark", "tr-band--accent", "tr-band--light" };
            for (int i = 0; i < 3; i++)
            {
                _bands[i] = Ui.El("tr-band", cls[i]);
                _bands[i].style.transitionDelay = new StyleList<TimeValue>(new System.Collections.Generic.List<TimeValue> { new TimeValue(i * 0.06f, TimeUnit.Second) });
                _root.Add(_bands[i]);
            }
            layer.Add(_root);
            Ui.SetVisible(_root, false);
        }

        public void Wipe(Action midpoint, bool reduceMotion, UiRoot ui)
        {
            if (_busy) { midpoint?.Invoke(); return; }
            if (reduceMotion) { ui.Fade(true, () => { midpoint?.Invoke(); ui.Fade(false); }, 300); return; }
            _busy = true;
            Ui.SetVisible(_root, true);
            _root.pickingMode = PickingMode.Position;
            // Snap bands to the start position without animating, then sweep in.
            foreach (var b in _bands) { b.AddToClassList("tr-band--reset"); b.RemoveFromClassList("tr-band--exit"); b.RemoveFromClassList("tr-band--cover"); }
            _root.schedule.Execute(() =>
            {
                foreach (var b in _bands) { b.RemoveFromClassList("tr-band--reset"); b.AddToClassList("tr-band--cover"); }
            }).StartingIn(32);
            _root.schedule.Execute(() =>
            {
                midpoint?.Invoke();
                foreach (var b in _bands) b.AddToClassList("tr-band--exit");
                _root.schedule.Execute(() =>
                {
                    Ui.SetVisible(_root, false);
                    _root.pickingMode = PickingMode.Ignore;
                    _busy = false;
                }).StartingIn(520);
            }).StartingIn(460);
        }
    }
}

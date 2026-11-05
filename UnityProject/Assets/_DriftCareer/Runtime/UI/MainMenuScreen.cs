using System.Collections.Generic;
using DriftCareer.Core.Catalog;
using DriftCareer.Runtime.Input;
using UnityEngine;
using UnityEngine.UIElements;

namespace DriftCareer.Runtime.UI
{
    /// <summary>
    /// Wide cinematic main menu over the live 3D hangar: "press start" splash, sliding logo, a horizontal
    /// carousel of big animated cards built from the content catalog (Playable vs «Скоро»), and a side
    /// detail panel for coming-soon content. Original design; no third-party branding.
    /// </summary>
    public sealed class MainMenuScreen : UiScreen
    {
        private readonly List<VisualElement> _cards = new List<VisualElement>();
        private readonly List<MenuSection> _sections = new List<MenuSection>();
        private VisualElement _track;
        private VisualElement _logo;
        private VisualElement _carousel;
        private VisualElement _detail;
        private Label _detailTitle, _detailText, _detailBadge;
        private Label _press;
        private int _selected;
        private VisualElement _credits;
        private bool _splash = true;
        private float _pulse;
        private const float CardStep = 396f; // width + margins

        protected override void Build()
        {
            _cards.Clear();
            _sections.Clear();
            Root.pickingMode = PickingMode.Position;
            var vignette = Ui.El("mm-vignette");
            vignette.style.backgroundImage = new StyleBackground(Ui.Gradient(new Color(0f, 0f, 0f, 0.85f), new Color(0f, 0f, 0f, 0f), true));
            vignette.pickingMode = PickingMode.Ignore;
            Root.Add(vignette);
            var bottomShade = Ui.El("dc-layer");
            bottomShade.style.backgroundImage = new StyleBackground(Ui.Gradient(new Color(0f, 0f, 0f, 0.75f), new Color(0f, 0f, 0f, 0f), false));
            bottomShade.pickingMode = PickingMode.Ignore;
            Root.Add(bottomShade);

            var top = Ui.El("mm-top");
            _logo = Ui.El("mm-logo");
            var title = Ui.Row();
            title.Add(Ui.Label("DRIFT ", "dc-title"));
            title.Add(Ui.Label("CAREER", "dc-title", "dc-title-accent"));
            _logo.Add(title);
            _logo.Add(Ui.El("mm-logo-stripe"));
            _logo.Add(Ui.Label(T("main.tagline"), "dc-muted"));
            top.Add(_logo);
            var stats = Ui.El("mm-stats");
            stats.Add(Stat(T("main.profile_points"), Mathf.RoundToInt(S.Profile.totalDriftPoints).ToString("N0")));
            stats.Add(Stat(T("main.best_chain"), Mathf.RoundToInt(S.Profile.bestChain).ToString("N0")));
            _credits = Stat(T("main.credits"), S.Profile.wallet.credits.ToString("N0") + " CR");
            stats.Add(_credits);
            top.Add(stats);
            Root.Add(top);

            _carousel = Ui.El("mm-carousel");
            _track = Ui.El("mm-track");
            _carousel.Add(_track);
            Root.Add(_carousel);

            int index = 0;
            foreach (var s in S.Catalog.MenuSections)
            {
                if (!s.IsVisible) continue;
                _sections.Add(s);
                var card = BuildCard(s, index);
                _cards.Add(card);
                _track.Add(card);
                int i = index;
                card.RegisterCallback<ClickEvent>(_ =>
                {
                    if (_splash) { LeaveSplash(); return; }
                    if (_selected == i) Activate(i); else Select(i);
                });
                index++;
            }

            _press = Ui.Label(T("main.press_start"), "mm-press");
            Root.Add(_press);
            var hint = Ui.El("mm-hint");
            hint.Add(Ui.Label(T("main.hint_nav"), "dc-muted"));
            hint.Add(Ui.Label(T("main.version") + " · " + Application.version, "dc-muted"));
            Root.Add(hint);

            _detail = Ui.El("mm-detail");
            _detailBadge = Ui.Label(T("common.coming_soon"), "dc-badge", "dc-badge--soon");
            _detailBadge.style.alignSelf = Align.FlexStart;
            _detail.Add(_detailBadge);
            _detailTitle = Ui.Label("", "dc-h1");
            _detailTitle.style.whiteSpace = WhiteSpace.Normal;
            _detail.Add(_detailTitle);
            _detailText = Ui.Label("", "mm-detail-text");
            _detail.Add(_detailText);
            var close = Ui.Button(T("common.back"), CloseDetail);
            close.style.marginTop = 40;
            _detail.Add(close);
            Root.Add(_detail);

            Root.RegisterCallback<PointerDownEvent>(_ => { if (_splash) LeaveSplash(); });
            ApplySplashState();
            Select(Mathf.Clamp(_selected, 0, Mathf.Max(0, _cards.Count - 1)), true);
        }

        private VisualElement Stat(string label, string value)
        {
            var e = Ui.El("mm-stat");
            e.Add(Ui.Label(label, "dc-muted"));
            e.Add(Ui.Label(value, "mm-stat-value"));
            return e;
        }

        private VisualElement BuildCard(MenuSection s, int index)
        {
            var card = Ui.El("mm-card");
            if (!s.IsPlayable) card.AddToClassList("mm-card--soon");
            Color accent = Ui.Hex(s.accentColor, new Color(0.9f, 0.2f, 0.18f));
            var stripe = Ui.El("mm-card-stripe");
            stripe.style.backgroundColor = accent;
            card.Add(stripe);
            var art = Ui.El("mm-card-art");
            art.style.backgroundImage = new StyleBackground(Ui.Gradient(new Color(accent.r * 0.25f, accent.g * 0.25f, accent.b * 0.25f, 0.95f), new Color(accent.r, accent.g, accent.b, 0.85f), false));
            art.Add(Ui.Label((index + 1).ToString("00"), "mm-card-glyph"));
            card.Add(art);
            var body = Ui.El("mm-card-body");
            body.Add(Ui.Label(T(s.titleKey), "mm-card-title"));
            body.Add(Ui.Label(T(s.descriptionKey), "mm-card-desc"));
            card.Add(body);
            var foot = Ui.El("mm-card-foot");
            var badge = s.IsPlayable ? Ui.Label(T("common.playable"), "dc-badge", "dc-badge--ok") : Ui.Label(T("common.coming_soon"), "dc-badge", "dc-badge--soon");
            foot.Add(badge);
            foot.Add(Ui.Label(s.IsPlayable ? T("common.play") + " ›" : "", "dc-small"));
            card.Add(foot);
            // Staggered entrance.
            card.schedule.Execute(() => { if (!_splash) card.AddToClassList("mm-card--in"); }).StartingIn(120 + index * 60);
            return card;
        }

        private void ApplySplashState()
        {
            Ui.SetVisible(_press, _splash);
            _carousel.style.opacity = _splash ? 0f : 1f;
            if (!_splash)
            {
                Ui.AddClassNextFrame(_logo, "mm-logo--in");
                for (int i = 0; i < _cards.Count; i++)
                {
                    var c = _cards[i];
                    c.schedule.Execute(() => c.AddToClassList("mm-card--in")).StartingIn(80 + i * 55);
                }
            }
            else _logo.AddToClassList("mm-logo--in");
        }

        private void LeaveSplash()
        {
            if (!_splash) return;
            _splash = false;
            ApplySplashState();
            Select(_selected, true);
        }

        protected override void OnShow()
        {
            CloseDetail();
            if (_credits != null && _credits.childCount > 1 && _credits[1] is Label l) l.text = S.Profile.wallet.credits.ToString("N0") + " CR";
            Game.ShowMenuBackdrop();
            if (!_splash) Select(_selected, true);
        }

        public override void Tick(float dt)
        {
            if (!_splash || _press == null) return;
            _pulse += dt;
            if (_pulse > 0.9f)
            {
                _pulse = 0f;
                _press.ToggleInClassList("mm-press--dim");
            }
        }

        private void Select(int i, bool instant = false)
        {
            if (_cards.Count == 0) return;
            _selected = Mathf.Clamp(i, 0, _cards.Count - 1);
            for (int k = 0; k < _cards.Count; k++) _cards[k].EnableInClassList("is-focused", k == _selected);
            float width = Root.layout.width > 0 ? Root.layout.width : 1920f;
            float x = width * 0.5f - (_selected * CardStep + CardStep * 0.5f);
            _track.style.translate = new Translate(new Length(x, LengthUnit.Pixel), new Length(0, LengthUnit.Pixel), 0);
            if (instant) Root.schedule.Execute(() => Select(_selected)).StartingIn(30);
        }

        private void Activate(int i)
        {
            var s = _sections[i];
            if (!s.IsPlayable) { OpenDetail(s); return; }
            switch (s.targetKind)
            {
                case MenuTargetKind.Track: Game.StartAcademy("free_drive"); break;
                case MenuTargetKind.Mode: Game.StartAcademy(s.targetId); break;
                case MenuTargetKind.Screen:
                    if (s.targetId == "garage") Game.OpenGarage();
                    else if (s.targetId == "settings") Game.OpenSettings();
                    else if (s.targetId == "challenges") Game.OpenChallenges();
                    else OpenDetail(s);
                    break;
            }
        }

        private void OpenDetail(MenuSection s)
        {
            _detailTitle.text = T(s.titleKey);
            string text = T(s.descriptionKey) + "\n\n" + T("common.coming_soon_note");
            var track = s.targetKind == MenuTargetKind.Track ? S.Catalog.Track(s.targetId) : null;
            if (track != null)
            {
                text = T(track.descriptionKey) + "\n\n" + T("common.coming_soon_note");
                if (track.license == LicenseStatus.RequiresLicense) text += "\n\n" + T("common.license_note");
            }
            _detailText.text = text;
            _detail.AddToClassList("mm-detail--open");
        }

        private void CloseDetail() => _detail?.RemoveFromClassList("mm-detail--open");

        private bool DetailOpen => _detail != null && _detail.ClassListContains("mm-detail--open");

        public override bool HandleMenu(MenuInput m)
        {
            if (_splash)
            {
                if (m.submit || m.back || m.moveX != 0 || m.moveY != 0) LeaveSplash();
                return true;
            }
            if (DetailOpen)
            {
                if (m.back || m.submit) CloseDetail();
                return true;
            }
            if (m.moveX != 0) { Select(_selected + m.moveX); return true; }
            if (m.submit) { Activate(_selected); return true; }
            if (m.tabRight) { Game.OpenGarage(); return true; }
            return true;
        }

        /// <summary>Any key also leaves the splash (touch, mouse handled via pointer).</summary>
        public void NotifyAnyInput()
        {
            if (_splash) LeaveSplash();
        }
    }
}

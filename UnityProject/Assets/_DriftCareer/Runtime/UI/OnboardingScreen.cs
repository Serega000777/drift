using System;
using DriftCareer.Core.Account;
using UnityEngine;
using UnityEngine.UIElements;

namespace DriftCareer.Runtime.UI
{
    /// <summary>
    /// First-run flow (and re-run when the privacy policy version changes):
    /// 1) neutral age question, 2) opt-in consents with the policy text, 3) guest or platform sign-in + nickname.
    /// Nothing optional is pre-selected; minors never see ad/marketing opt-ins.
    /// </summary>
    public sealed class OnboardingScreen : UiScreen
    {
        private enum Step { Age, Privacy, SignIn }

        private Step _step = Step.Age;
        private Action _onDone;
        private PrivacySettings _draft;

        public void Begin(Action onDone)
        {
            _onDone = onDone;
            _draft = new PrivacySettings
            {
                ageBand = S.Privacy.ageBand, analytics = S.Privacy.analytics, crashReports = S.Privacy.crashReports,
                personalizedAds = S.Privacy.personalizedAds, marketing = S.Privacy.marketing
            };
            _step = S.Privacy.ageBand == AgeBand.Unknown ? Step.Age : Step.Privacy;
            if (Visible) { Rebuild(); Nav.FocusFirst(); }
        }

        protected override void Build()
        {
            if (_draft == null) _draft = new PrivacySettings();
            Root.pickingMode = PickingMode.Position;
            var bg = Ui.El("dc-layer", "ob-bg");
            Root.Add(bg);
            var col = Ui.El("dc-center-col");
            var card = Ui.El("dc-panel", "ob-card");
            var steps = Ui.Row("ob-steps");
            for (int i = 0; i < 3; i++)
            {
                var dot = Ui.El("ob-step");
                dot.EnableInClassList("is-selected", i == (int)_step);
                dot.EnableInClassList("is-done", i < (int)_step);
                steps.Add(dot);
            }
            card.Add(steps);
            switch (_step)
            {
                case Step.Age: BuildAge(card); break;
                case Step.Privacy: BuildPrivacy(card); break;
                default: BuildSignIn(card); break;
            }
            col.Add(card);
            Root.Add(col);
            Ui.AddClassNextFrame(card, "ob-card--in");
        }

        private void BuildAge(VisualElement card)
        {
            card.Add(Ui.Label(T("onboarding.age.title"), "dc-h1"));
            card.Add(Ui.Label(T("onboarding.age.text"), "dc-dialog-text"));
            var row = Ui.Row("ob-choices");
            void Choose(AgeBand band) { _draft.ageBand = band; _draft.Enforce(); Next(Step.Privacy); }
            foreach (var (band, key) in new[] { (AgeBand.Under13, "onboarding.age.under13"), (AgeBand.Teen, "onboarding.age.teen"), (AgeBand.Adult, "onboarding.age.adult") })
            {
                var b = band;
                var btn = Ui.Button(T(key), () => Choose(b), "ob-choice");
                row.Add(btn);
                Nav.Register(btn, () => Choose(b));
            }
            card.Add(row);
        }

        private void BuildPrivacy(VisualElement card)
        {
            card.Add(Ui.Label(T("onboarding.privacy.title"), "dc-h1"));
            card.Add(Ui.Label(T("onboarding.privacy.text"), "dc-dialog-text"));
            bool adult = _draft.ageBand == AgeBand.Adult;
            bool child = _draft.ageBand == AgeBand.Under13;
            ToggleRow(card, "privacy.crash_reports", () => _draft.crashReports, v => _draft.crashReports = v);
            if (!child) ToggleRow(card, "privacy.analytics", () => _draft.analytics, v => _draft.analytics = v);
            if (adult)
            {
                ToggleRow(card, "privacy.personalized_ads", () => _draft.personalizedAds, v => _draft.personalizedAds = v);
                ToggleRow(card, "privacy.marketing", () => _draft.marketing, v => _draft.marketing = v);
            }
            else card.Add(Ui.Label(T("onboarding.privacy.minor_note"), "dc-muted"));
            var row = Ui.Row();
            var policy = Ui.Button(T("privacy.policy"), () => UiRoot.Info(T("privacy.policy"), T("privacy.policy_text")));
            var accept = Ui.Button(T("onboarding.accept"), Accept, "dc-btn--primary");
            var back = Ui.Button(T("common.back"), () => Next(Step.Age));
            row.Add(back); row.Add(policy); row.Add(accept);
            card.Add(row);
            Nav.Register(back, () => Next(Step.Age));
            Nav.Register(policy, () => UiRoot.Info(T("privacy.policy"), T("privacy.policy_text")));
            Nav.Register(accept, Accept);
        }

        private void Accept()
        {
            var p = S.Privacy;
            p.ageBand = _draft.ageBand;
            p.analytics = _draft.analytics;
            p.crashReports = _draft.crashReports;
            p.personalizedAds = _draft.personalizedAds;
            p.marketing = _draft.marketing;
            p.Accept(DateTime.UtcNow);
            S.SavePrivacy();
            Next(Step.SignIn);
        }

        private void BuildSignIn(VisualElement card)
        {
            card.Add(Ui.Label(T("onboarding.signin.title"), "dc-h1"));
            card.Add(Ui.Label(T("onboarding.signin.text"), "dc-dialog-text"));
            var nickRow = Ui.El("dc-setting");
            nickRow.Add(Ui.Label(T("profile.nickname"), "dc-setting-label"));
            var field = new TextField { value = S.Nickname, maxLength = PlayerIdentity.MaxLength };
            field.AddToClassList("dc-text-field");
            field.style.flexGrow = 1;
            nickRow.Add(field);
            card.Add(nickRow);
            var hint = Ui.Label(T("profile.nickname_hint"), "dc-muted");
            card.Add(hint);

            bool SaveNick()
            {
                if (field.value == S.Nickname) return true;
                var err = S.SetNickname(field.value);
                if (err == NicknameError.None) return true;
                hint.text = T("profile.nick_error." + err);
                hint.AddToClassList("ob-error");
                return false;
            }

            var guest = Ui.Button(T("onboarding.guest"), () => { if (SaveNick()) Finish(); }, "dc-btn--primary");
            card.Add(guest);
            Nav.Register(guest, () => { if (SaveNick()) Finish(); });
            var provider = S.PlatformAuthProvider;
            string platform = T("auth.provider." + provider.Id);
            var signIn = Ui.Button(T("onboarding.signin_with", platform), () =>
            {
                if (!SaveNick()) return;
                S.Auth.SignIn(provider, (ok, error) =>
                {
                    if (ok) { Finish(); return; }
                    UiRoot.Info(T("auth.unavailable.title"), T("auth.unavailable.text", platform));
                });
            });
            card.Add(signIn);
            Nav.Register(signIn, () => S.Auth.SignIn(provider, (ok, e) => { if (ok) Finish(); else UiRoot.Info(T("auth.unavailable.title"), T("auth.unavailable.text", platform)); }));
            card.Add(Ui.Label(T("onboarding.signin.note"), "dc-muted"));
        }

        private void Next(Step step)
        {
            _step = step;
            Rebuild();
            Nav.FocusFirst();
        }

        private void Finish()
        {
            if (S.Auth.Session == null) S.Auth.ContinueAsGuest();
            _onDone?.Invoke();
        }

        public override void OnBack()
        {
            if (_step == Step.Privacy) Next(Step.Age);
            else if (_step == Step.SignIn) Next(Step.Privacy);
        }
    }
}

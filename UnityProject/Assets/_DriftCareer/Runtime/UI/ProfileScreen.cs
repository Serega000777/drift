using System;
using System.Collections.Generic;
using DriftCareer.Core;
using DriftCareer.Core.Account;
using DriftCareer.Core.Character;
using DriftCareer.Core.Gameplay;
using DriftCareer.Core.Security;
using UnityEngine;
using UnityEngine.UIElements;

namespace DriftCareer.Runtime.UI
{
    /// <summary>
    /// Personal account ("личный кабинет"): player card (nickname, level, stats), driver character & gear,
    /// records/medals, wallet with history, account & sign-in, privacy (consents, export, erase) and security.
    /// </summary>
    public sealed class ProfileScreen : UiScreen
    {
        private enum Tab { Overview, Driver, Records, Wallet, Account, Privacy, Security }

        private Tab _tab;
        private ScrollView _content;
        private CharacterAppearance _draft;
        private GearSlot _slot = GearSlot.Top;

        protected override void OnShow()
        {
            _draft = S.Profile.character.Clone();
            Game.FocusPilot(true);
        }

        protected override void OnHide() => Game.FocusPilot(false);

        protected override void Build()
        {
            if (_draft == null) _draft = S.Profile.character.Clone();
            Root.pickingMode = PickingMode.Ignore;
            var panel = Ui.El("dc-panel", "pf-panel");
            panel.pickingMode = PickingMode.Position;
            panel.Add(PlayerCard());
            var tabs = Ui.El("dc-tabs");
            foreach (Tab t in Enum.GetValues(typeof(Tab)))
            {
                var tab = t;
                var b = new Button(() => { _tab = tab; Rebuild(); Nav.FocusFirst(); }) { text = T("profile.tab." + t) };
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
            switch (_tab)
            {
                case Tab.Overview: BuildOverview(); break;
                case Tab.Driver: BuildDriver(); break;
                case Tab.Records: BuildRecords(); break;
                case Tab.Wallet: BuildWallet(); break;
                case Tab.Account: BuildAccount(); break;
                case Tab.Privacy: BuildPrivacy(); break;
                default: BuildSecurity(); break;
            }
        }

        private VisualElement PlayerCard()
        {
            var p = S.Profile;
            int level = PlayerIdentity.Level(p.totalDriftPoints, out float progress);
            var card = Ui.Row("pf-card");
            var avatar = Ui.Label(PlayerIdentity.Initials(S.Nickname), "pf-avatar");
            avatar.style.backgroundColor = Ui.Hex(p.character.Gear(GearSlot.Top).primary, new Color(0.9f, 0.2f, 0.18f));
            card.Add(avatar);
            var info = Ui.El();
            info.style.flexGrow = 1;
            var nameRow = Ui.Row();
            nameRow.Add(Ui.Label(S.Nickname, "dc-h1"));
            var edit = Ui.Button(T("profile.edit_nickname"), EditNickname, "dc-btn--chip");
            nameRow.Add(edit);
            Nav.Register(edit, EditNickname);
            info.Add(nameRow);
            info.Add(Ui.Label(T("profile.level", level) + " · " + (S.Auth.Session != null && !S.Auth.Session.isGuest ? T("auth.provider." + S.Auth.Session.provider) : T("auth.guest_status")), "dc-muted"));
            var bar = Ui.El("gr-bar");
            var fill = Ui.El("gr-bar-fill");
            fill.style.width = Length.Percent(progress * 100f);
            bar.Add(fill);
            info.Add(bar);
            card.Add(info);
            var credits = Ui.El("mm-stat");
            credits.Add(Ui.Label(T("main.credits"), "dc-muted"));
            credits.Add(Ui.Label(p.wallet.credits.ToString("N0") + " CR", "mm-stat-value"));
            card.Add(credits);
            return card;
        }

        private void EditNickname()
        {
            UiRoot.Prompt(T("profile.nickname"), S.Nickname, nick =>
            {
                var err = S.SetNickname(nick);
                if (err == NicknameError.None) { UiRoot.Toast(T("profile.saved"), 1.4f, "#3DDC84"); Rebuild(); Nav.FocusFirst(); }
                else UiRoot.Toast(T("profile.nick_error." + err), 2.2f, "#FF3B30");
            });
        }

        // ------------------------------------------------------------ overview / records / wallet

        private void Stat(string key, string value)
        {
            var row = Ui.El("gr-stat-row");
            row.Add(Ui.Label(T(key), "dc-muted"));
            row.Add(Ui.Label(value, "gr-stat-num"));
            _content.Add(row);
        }

        private void BuildOverview()
        {
            var p = S.Profile;
            Stat("profile.stat.points", Mathf.RoundToInt(p.totalDriftPoints).ToString("N0"));
            Stat("profile.stat.best_chain", Mathf.RoundToInt(p.bestChain).ToString("N0"));
            Stat("profile.stat.distance", p.distanceDrivenKm.ToString("0.0") + " km");
            Stat("profile.stat.playtime", TimeSpan.FromSeconds(p.playTimeSec).ToString(@"hh\:mm"));
            Stat("profile.stat.cars", p.ownedVehicles.Count.ToString());
            Stat("profile.stat.parts", p.ownedItems.Count.ToString());
            int done = 0;
            foreach (var c in p.challenges.Values) if (c.completed) done++;
            Stat("profile.stat.challenges", done.ToString());
            Stat("profile.stat.earned", p.wallet.lifetimeEarned.ToString("N0") + " CR");
            Stat("profile.stat.member_since", p.createdUtc != null && p.createdUtc.Length >= 10 ? p.createdUtc.Substring(0, 10) : "—");
        }

        private void BuildRecords()
        {
            if (S.Profile.bestScores.Count == 0) { _content.Add(Ui.Label(T("profile.no_records"), "dc-muted")); return; }
            foreach (var kv in S.Profile.bestScores)
            {
                int medal = S.Profile.bestMedals.TryGetValue(kv.Key, out var m) ? m : 0;
                string mode = kv.Key.Contains(".") ? kv.Key.Substring(kv.Key.IndexOf('.') + 1) : kv.Key;
                var row = Ui.El("dc-setting");
                row.Add(Ui.Label(T("run." + mode), "dc-setting-label"));
                row.Add(Ui.Label(Mathf.RoundToInt(kv.Value).ToString("N0"), "dc-setting-value"));
                var badge = Ui.Label(T("medal." + (Medal)medal), "dc-badge");
                badge.style.backgroundColor = medal == 3 ? new Color(0.96f, 0.77f, 0f) : medal == 2 ? new Color(0.75f, 0.78f, 0.8f) : medal == 1 ? new Color(0.8f, 0.5f, 0.25f) : new Color(0.3f, 0.3f, 0.3f);
                row.Add(badge);
                _content.Add(row);
            }
            foreach (var d in S.Profile.drills.Values)
            {
                var row = Ui.El("dc-setting");
                row.Add(Ui.Label(T(d.drillId + ".title"), "dc-setting-label"));
                row.Add(Ui.Label(d.bestTimeSec < float.MaxValue ? d.bestTimeSec.ToString("0.0") + " s" : "—", "dc-setting-value"));
                row.Add(Ui.Label("×" + d.completions, "dc-small"));
                _content.Add(row);
            }
        }

        private void BuildWallet()
        {
            var w = S.Profile.wallet;
            Stat("profile.wallet.balance", w.credits.ToString("N0") + " CR");
            Stat("profile.wallet.earned", w.lifetimeEarned.ToString("N0") + " CR");
            Stat("profile.wallet.spent", w.lifetimeSpent.ToString("N0") + " CR");
            _content.Add(Ui.Label(T("profile.wallet.history"), "dc-h2"));
            if (w.history.Count == 0) _content.Add(Ui.Label(T("profile.wallet.empty"), "dc-muted"));
            foreach (var t in w.history)
            {
                var row = Ui.El("dc-setting");
                string when = t.utc != null && t.utc.Length >= 16 ? t.utc.Substring(0, 16).Replace('T', ' ') : "";
                row.Add(Ui.Label(T(t.reason), "dc-setting-label"));
                row.Add(Ui.Label(when, "dc-small"));
                row.Add(Ui.Label((t.amount > 0 ? "+" : "") + t.amount.ToString("N0"), t.amount >= 0 ? "gr-delta-up" : "gr-delta-down"));
                _content.Add(row);
            }
            _content.Add(Ui.Label(T("profile.wallet.note"), "dc-muted"));
        }

        // ------------------------------------------------------------ driver character

        private void BuildDriver()
        {
            var a = _draft;
            var presets = Ui.El("gr-groups");
            foreach (var id in CharacterCatalog.PresetIds)
            {
                var pid = id;
                Action apply = () => { _draft = CharacterCatalog.Sanitize(CharacterCatalog.Preset(pid)); Preview(); Rebuild(); };
                var chip = Ui.Button(T("character.preset." + id), apply, "dc-btn--chip");
                presets.Add(chip);
                Nav.Register(chip, apply);
            }
            _content.Add(presets);
            CyclerRow(_content, "character.body_label", new[] { "character.body.Slim", "character.body.Regular", "character.body.Strong" }, () => (int)a.body, i => { a.body = (BodyType)i; Preview(); });
            SliderRow(_content, T("character.height"), 0.9f, 1.1f, 0.01f, () => a.height, v => { a.height = v; Preview(); }, v => (v * CharacterCatalog.BaseHeightM).ToString("0.00") + " m");
            Swatches("character.skin", CharacterCatalog.SkinTones, () => a.skinTone, c => { a.skinTone = c; Preview(); });
            var hair = new string[CharacterCatalog.HairStyleCount];
            for (int i = 0; i < hair.Length; i++) hair[i] = "character.hair." + i;
            CyclerRow(_content, "character.hair_style", hair, () => a.hairStyle, i => { a.hairStyle = i; Preview(); });
            Swatches("character.hair_color", CharacterCatalog.HairColors, () => a.hairColor, c => { a.hairColor = c; Preview(); });
            var faces = new string[CharacterCatalog.FaceCount];
            for (int i = 0; i < faces.Length; i++) faces[i] = "character.face." + i;
            CyclerRow(_content, "character.face_label", faces, () => a.face, i => { a.face = i; Preview(); });

            _content.Add(Ui.Label(T("character.gear"), "dc-h2"));
            var slots = Ui.El("gr-groups");
            foreach (GearSlot slot in Enum.GetValues(typeof(GearSlot)))
            {
                var sl = slot;
                Action pick = () => { _slot = sl; Rebuild(); };
                var chip = Ui.Button(T("character.slot." + slot), pick, "dc-btn--chip");
                chip.EnableInClassList("is-selected", slot == _slot);
                slots.Add(chip);
                Nav.Register(chip, pick);
            }
            _content.Add(slots);
            var current = a.Gear(_slot);
            foreach (var item in CharacterCatalog.ForSlot(_slot))
            {
                var it = item;
                bool owned = it.price == 0 || S.Profile.ownedItems.Contains(it.id);
                var row = Ui.El("dc-setting");
                row.Add(Ui.Label(T(it.titleKey), "dc-setting-label"));
                row.Add(Ui.Label(owned ? T("garage.owned") : T("garage.price", it.price.ToString("N0")), "dc-setting-value"));
                Action wear = () =>
                {
                    var g = a.Gear(_slot);
                    g.itemId = it.id;
                    a.gear[_slot] = g;
                    Preview();
                    Rebuild();
                };
                var b = Ui.Button(current.itemId == it.id ? "✓" : T("common.select"), wear, "dc-btn--chip");
                b.EnableInClassList("is-selected", current.itemId == it.id);
                row.Add(b);
                _content.Add(row);
                Nav.Register(row, wear);
            }
            var selected = CharacterCatalog.Find(current.itemId);
            if (selected != null && selected.style > 0 || _slot == GearSlot.Top || _slot == GearSlot.Bottom || _slot == GearSlot.Shoes)
            {
                Swatches("character.color_primary", Core.Garage.VisualPartCatalog.PaintPalette, () => a.Gear(_slot).primary, c => { var g = a.Gear(_slot); g.primary = c; a.gear[_slot] = g; Preview(); });
                if (selected != null && selected.twoColors)
                    Swatches("character.color_secondary", Core.Garage.VisualPartCatalog.PaintPalette, () => a.Gear(_slot).secondary, c => { var g = a.Gear(_slot); g.secondary = c; a.gear[_slot] = g; Preview(); });
            }

            _content.Add(Ui.Label(T("character.custom_model"), "dc-h2"));
            _content.Add(Ui.Label(T(S.Capabilities.userModelImport ? "character.custom_model_pc" : "character.custom_model_soon"), "dc-muted"));
            foreach (var id in S.UserCharacters.Keys)
            {
                var cid = id;
                Action use = () => { _draft.customModelId = _draft.customModelId == cid ? "" : cid; Preview(); Rebuild(); };
                var b = Ui.Button((a.customModelId == cid ? "✓ " : "") + S.UserCharacters[cid], use, "dc-btn--chip");
                _content.Add(b);
                Nav.Register(b, use);
            }

            long cost = CharacterCatalog.MissingCost(_draft, S.Profile.ownedItems);
            var save = Ui.Button(cost > 0 ? T("garage.buy_apply", cost.ToString("N0")) : T("garage.apply_free"), SaveCharacter, "dc-btn--primary");
            save.SetEnabled(S.Profile.wallet.CanAfford(cost));
            _content.Add(save);
            Nav.Register(save, SaveCharacter);
        }

        private void Swatches(string labelKey, IList<string> colors, Func<string> get, Action<string> set)
        {
            _content.Add(Ui.Label(T(labelKey), "dc-small"));
            var row = Ui.El("dc-swatches");
            var all = new List<VisualElement>();
            foreach (var hex in colors)
            {
                var h = hex;
                var sw = Ui.El("dc-swatch");
                sw.style.backgroundColor = Ui.Hex(h, Color.gray);
                sw.EnableInClassList("is-selected", string.Equals(get(), h, StringComparison.OrdinalIgnoreCase));
                Action pick = () => { set(h); foreach (var x in all) x.EnableInClassList("is-selected", x == sw); };
                sw.RegisterCallback<ClickEvent>(_ => pick());
                row.Add(sw);
                all.Add(sw);
                Nav.Register(sw, pick);
            }
            _content.Add(row);
        }

        private void Preview()
        {
            _draft = CharacterCatalog.Sanitize(_draft);
            Game.PreviewCharacter(_draft);
        }

        private void SaveCharacter()
        {
            long cost = CharacterCatalog.MissingCost(_draft, S.Profile.ownedItems);
            if (!S.Profile.wallet.Spend(cost, "wallet.reason.gear")) { UiRoot.Toast(T("garage.not_enough")); return; }
            CharacterCatalog.MarkOwned(_draft, S.Profile.ownedItems);
            S.Profile.character = CharacterCatalog.Sanitize(_draft);
            S.SaveProfile();
            Game.PreviewCharacter(S.Profile.character);
            UiRoot.Toast(T("profile.saved"), 1.4f, "#3DDC84");
            Rebuild();
        }

        // ------------------------------------------------------------ account / privacy / security

        private void BuildAccount()
        {
            var auth = S.Auth;
            bool guest = auth.Session == null || auth.Session.isGuest;
            Stat("profile.account.status", guest ? T("auth.guest_status") : T("auth.state." + auth.State));
            Stat("profile.account.id", Crypto.Mask(auth.Session?.accountId ?? auth.InstallId));
            Stat("profile.account.platform", T("auth.provider." + AuthProviders.ForStore(S.Policy.store)));
            Stat("profile.account.server", T(auth.Backend.Online ? "profile.account.online" : "profile.account.offline"));
            if (!string.IsNullOrEmpty(auth.LastError)) Stat("profile.account.last_error", auth.LastError);
            _content.Add(Ui.Label(T("profile.account.cross_note"), "dc-muted"));
            if (guest)
            {
                var provider = S.PlatformAuthProvider;
                string name = T("auth.provider." + provider.Id);
                Action signIn = () => auth.SignIn(provider, (ok, err) =>
                {
                    if (ok) { UiRoot.Toast(T("auth.state.SignedIn"), 1.4f, "#3DDC84"); Rebuild(); }
                    else UiRoot.Info(T("auth.unavailable.title"), T("auth.unavailable.text", name));
                });
                var b = Ui.Button(T("onboarding.signin_with", name), signIn, "dc-btn--primary");
                _content.Add(b);
                Nav.Register(b, signIn);
            }
            else
            {
                Action signOut = () => UiRoot.Confirm(T("profile.account.sign_out_confirm"), () => { auth.SignOut(); Rebuild(); });
                var b = Ui.Button(T("auth.sign_out"), signOut);
                _content.Add(b);
                Nav.Register(b, signOut);
            }
        }

        private void BuildPrivacy()
        {
            var p = S.Privacy;
            Stat("profile.privacy.age_label", T("profile.privacy.age." + p.ageBand));
            Stat("profile.privacy.policy_label", p.acceptedPolicyVersion + " · " + (p.acceptedUtc != null && p.acceptedUtc.Length >= 10 ? p.acceptedUtc.Substring(0, 10) : ""));
            ToggleRow(_content, "privacy.crash_reports", () => p.crashReports, v => { p.crashReports = v; S.SavePrivacy(); });
            if (p.ageBand != AgeBand.Under13) ToggleRow(_content, "privacy.analytics", () => p.analytics, v => { p.analytics = v; S.SavePrivacy(); });
            if (p.ageBand == AgeBand.Adult)
            {
                ToggleRow(_content, "privacy.personalized_ads", () => p.personalizedAds, v => { p.personalizedAds = v; S.SavePrivacy(); });
                ToggleRow(_content, "privacy.marketing", () => p.marketing, v => { p.marketing = v; S.SavePrivacy(); });
            }
            var buttons = Ui.El("gr-groups");
            void Btn(string key, Action a, string cls = "dc-btn--chip")
            {
                var b = Ui.Button(T(key), a, "dc-btn--chip", cls);
                buttons.Add(b);
                Nav.Register(b, a);
            }
            Btn("privacy.policy", () => UiRoot.Info(T("privacy.policy"), T("privacy.policy_text")));
            Btn("profile.privacy.change_age", () => { p.ageBand = AgeBand.Unknown; S.SavePrivacy(); Game.ShowOnboarding(() => Game.OpenProfile()); });
            Btn("profile.privacy.export", () =>
            {
                string path = S.ExportPlayerData();
                UiRoot.Info(T("profile.privacy.export"), T("profile.privacy.exported", path));
            });
            Btn("profile.privacy.erase", () => UiRoot.Confirm(T("profile.privacy.erase_confirm"), () =>
                UiRoot.Confirm(T("profile.privacy.erase_confirm2"), () => Game.EraseAllDataAndRestart())), "dc-btn--danger");
            _content.Add(buttons);
            var caps = S.Capabilities;
            _content.Add(Ui.Label(T("profile.privacy.effective"), "dc-h2"));
            Stat("profile.cap.chat", T(caps.textChat ? "common.on" : "common.off"));
            Stat("profile.cap.ugc", T(caps.ugcUpload ? "common.on" : "common.off"));
            Stat("profile.cap.models", T(caps.userModelImport ? "common.on" : "common.off"));
            Stat("profile.cap.purchases", T(caps.purchases ? "common.on" : "common.off"));
            Stat("profile.cap.leaderboards", T(caps.publicNameOnLeaderboards ? "common.on" : "common.off"));
        }

        private void BuildSecurity()
        {
            Stat("profile.security.install_id", Crypto.Mask(S.Auth.InstallId));
            Stat("profile.security.integrity", T(S.Profile.integrityCompromised ? "profile.security.integrity_bad" : "profile.security.integrity_ok"));
            Stat("profile.security.tokens", T("profile.security.tokens_value"));
            Stat("profile.security.saves", T("profile.security.saves_value"));
            _content.Add(Ui.Label(T("profile.security.note"), "dc-muted"));
            var b = Ui.Button(T("profile.security.sign_out_all"), () => UiRoot.Info(T("profile.security.sign_out_all"), T("profile.security.sign_out_all_note")), "dc-btn--chip");
            _content.Add(b);
            Nav.Register(b, () => UiRoot.Info(T("profile.security.sign_out_all"), T("profile.security.sign_out_all_note")));
        }

        public override void OnBack()
        {
            Game.PreviewCharacter(S.Profile.character);
            Game.CloseProfile();
        }
    }
}

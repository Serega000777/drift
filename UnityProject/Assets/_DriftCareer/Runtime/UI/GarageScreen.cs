using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DriftCareer.Core;
using DriftCareer.Core.Gameplay;
using DriftCareer.Core.Garage;
using DriftCareer.Core.Surfaces;
using DriftCareer.Core.Vehicle;
using UnityEngine;
using UnityEngine.UIElements;

namespace DriftCareer.Runtime.UI
{
    /// <summary>
    /// Garage: cars, performance tuning (data-driven sliders), engine swaps, korch build, visual try-on and
    /// simulated specs. Edits a draft copy applied live to the car; "Buy & apply" commits and pays for missing parts.
    /// </summary>
    public sealed class GarageScreen : UiScreen
    {
        private enum Tab { Cars, Tuning, Engine, Build, Visual, Stats }

        private Tab _tab = Tab.Tuning;
        private TuningGroup _group = TuningGroup.Suspension;
        private VehicleTuningState _draft;
        private VisualElement _content;
        private Button _applyButton;
        private Label _credits;
        private Task<PerformanceReport> _statsTask;
        private PerformanceReport _stats, _savedStats;
        private float _statsDelay = -1f;
        private VisualElement _statsBox;
        private bool _dirtyPreview;

        private string VehicleId => S.Profile.selectedVehicleId;
        private VehicleTuningState Saved => S.Profile.TuningFor(VehicleId);

        protected override void OnShow()
        {
            _draft = Saved.Clone();
            _savedStats = null;
            RequestStats(true);
            Rebuild();
            Nav.FocusFirst();
        }

        protected override void Build()
        {
            if (_draft == null) _draft = Saved.Clone();
            Root.pickingMode = PickingMode.Ignore;
            var left = Ui.El("dc-panel", "gr-left");
            var head = Ui.Row();
            head.Add(Ui.Label(T("garage.title"), "dc-h1"));
            head.Add(Ui.El("dc-grow"));
            _credits = Ui.Label("", "dc-h2");
            head.Add(_credits);
            left.Add(head);
            var tabs = Ui.El("dc-tabs");
            foreach (Tab t in Enum.GetValues(typeof(Tab)))
            {
                var tab = t;
                string key = tab == Tab.Cars ? "garage.tab.cars" : tab == Tab.Tuning ? "garage.tab.performance" : tab == Tab.Engine ? "garage.tab.engine"
                    : tab == Tab.Build ? "garage.tab.build" : tab == Tab.Visual ? "garage.tab.visual" : "garage.tab.stats";
                var b = new Button(() => SelectTab(tab)) { text = T(key) };
                b.AddToClassList("dc-tab");
                b.EnableInClassList("is-selected", tab == _tab);
                tabs.Add(b);
                Nav.Register(b, () => SelectTab(tab));
            }
            left.Add(tabs);
            _content = new ScrollView();
            _content.AddToClassList("dc-scroll");
            left.Add(_content);
            Root.Add(left);

            var right = Ui.El("dc-panel", "gr-right");
            _statsBox = Ui.El();
            right.Add(_statsBox);
            Root.Add(right);

            var bottom = Ui.El("gr-bottom");
            var reset = Ui.Button(T("garage.reset_stock"), () => { _draft = VehicleTuningState.Stock(VehicleId); Changed(true); });
            _applyButton = Ui.Button("", Apply, "dc-btn--primary");
            var test = Ui.Button(T("garage.test_drive"), () => { Apply(); Game.StartAcademy("free_drive"); });
            var back = Ui.Button(T("common.back"), OnBack);
            bottom.Add(reset); bottom.Add(_applyButton); bottom.Add(test); bottom.Add(back);
            Root.Add(bottom);
            Nav.Register(_applyButton, Apply);
            Nav.Register(test, () => { Apply(); Game.StartAcademy("free_drive"); });
            Nav.Register(reset, () => { _draft = VehicleTuningState.Stock(VehicleId); Changed(true); });
            Nav.Register(back, OnBack);
            BuildTab();
            RefreshApply();
            RenderStats();
        }

        private void SelectTab(Tab t)
        {
            _tab = t;
            Rebuild();
            Nav.FocusFirst();
        }

        private void BuildTab()
        {
            switch (_tab)
            {
                case Tab.Cars: BuildCars(); break;
                case Tab.Tuning: BuildTuning(); break;
                case Tab.Engine: BuildEngines(); break;
                case Tab.Build: BuildKorch(); break;
                case Tab.Visual: BuildVisual(); break;
                case Tab.Stats: _content.Add(Ui.Label(T("garage.computed_note"), "dc-muted")); break;
            }
        }

        // ---------------------------------------------------------------- tabs

        private void BuildCars()
        {
            foreach (var v in S.Catalog.Vehicles)
            {
                if (!v.IsVisible) continue;
                var id = v.id;
                bool owned = S.Profile.ownedVehicles.Contains(id);
                bool selected = id == VehicleId;
                var row = Ui.El("dc-setting");
                var info = Ui.El();
                info.style.flexGrow = 1;
                info.Add(Ui.Label(T(v.titleKey), "dc-h2"));
                info.Add(Ui.Label(T(v.descriptionKey), "dc-muted"));
#if DC_SHOW_REFERENCE_NAMES
                if (v.physicsProfileId != null && v.physicsProfileId.StartsWith("spec:"))
                    info.Add(Ui.Label(T("garage.reference", SpecTemplates.Create(v.physicsProfileId.Substring(5)).referenceNote), "dc-small"));
#endif
                row.Add(info);
                int price = GameServices.VehiclePrice(id);
                string label = selected ? T("garage.selected") : owned ? T("garage.select_car") : T("garage.buy_car", price.ToString("N0"));
                Action act = () =>
                {
                    if (selected) return;
                    if (!owned)
                    {
                        if (!S.Profile.wallet.Spend(price, "wallet.reason.car")) { UiRoot.Toast(T("garage.not_enough")); return; }
                        S.Profile.ownedVehicles.Add(id);
                    }
                    Game.SwitchVehicle(id, () =>
                    {
                        _draft = Saved.Clone();
                        _savedStats = null;
                        RequestStats(true);
                        Rebuild();
                    });
                };
                var b = Ui.Button(label, act, "dc-btn--chip");
                b.SetEnabled(!selected);
                row.Add(b);
                _content.Add(row);
                Nav.Register(row, act);
            }
        }

        private void BuildTuning()
        {
            var groups = Ui.El("gr-groups");
            foreach (TuningGroup g in Enum.GetValues(typeof(TuningGroup)))
            {
                if (g == TuningGroup.Visual) continue;
                var grp = g;
                var chip = Ui.Button(T("tuning.group." + g), () => { _group = grp; Rebuild(); }, "dc-btn--chip");
                chip.EnableInClassList("is-selected", g == _group);
                groups.Add(chip);
                Nav.Register(chip, () => { _group = grp; Rebuild(); });
            }
            _content.Add(groups);
            if (_group == TuningGroup.Tires)
            {
                var compounds = new[] { "tuning.compound.Street", "tuning.compound.Sport", "tuning.compound.DriftSemiSlick", "tuning.compound.Winter", "tuning.compound.Studded" };
                CyclerRow(_content, "tuning.compound_front", compounds, () => (int)_draft.compoundFront, i => { _draft.compoundFront = (TireCompound)i; Changed(false); });
                CyclerRow(_content, "tuning.compound_rear", compounds, () => (int)_draft.compoundRear, i => { _draft.compoundRear = (TireCompound)i; Changed(false); });
                var p = TuningApplier.Apply(S.BaseProfile, _draft);
                _content.Add(Ui.Label(T("garage.tire_info", _draft.tireWidthRear.ToString("0"), _draft.tireAspectRear.ToString("0"), _draft.visual.rimSizeInch.ToString("0"),
                    (p.rear.wheelRadius * 1000f).ToString("0")), "dc-muted"));
            }
            if (_group == TuningGroup.Differential)
                CyclerRow(_content, "tuning.diff_type", new[] { "tuning.diff.Open", "tuning.diff.Lsd", "tuning.diff.Locked" }, () => (int)_draft.diffType, i => { _draft.diffType = (DifferentialType)i; Changed(false); });
            foreach (var param in TuningCatalog.InGroup(_group))
            {
                var p = param;
                float max = p.max;
                if (p.id == "track_front" || p.id == "track_rear") max = TuningApplier.MaxTrackOffset(_draft.visual);
                SliderRow(_content, T(p.titleKey), p.min, max, p.step, () => p.Get(_draft), v => { p.Set(_draft, v); Changed(false); }, v => Format(p, v));
            }
            if (_group == TuningGroup.Gearbox)
            {
                var stock = S.BaseProfile.transmission.gearRatios;
                var ratios = _draft.gearRatios ?? (float[])stock.Clone();
                for (int i = 0; i < ratios.Length; i++)
                {
                    int gi = i;
                    SliderRow(_content, T("tuning.gear_n", i + 1), 0.5f, 5f, 0.01f, () => (_draft.gearRatios ?? stock)[gi], v =>
                    {
                        if (_draft.gearRatios == null) _draft.gearRatios = (float[])stock.Clone();
                        _draft.gearRatios[gi] = v;
                        Changed(false);
                    }, v => v.ToString("0.00"));
                }
            }
        }

        private static string Format(TuningParameter p, float v)
        {
            switch (p.unit)
            {
                case "mm": return (p.id.StartsWith("tire") ? v : v * 1000f).ToString("0") + " mm";
                case "%": return p.max <= 1.01f ? (v * 100f).ToString("0") + "%" : v.ToString("0") + "%";
                case "x": return "×" + v.ToString("0.00");
                case "deg": return v.ToString("0.0") + "°";
                case "kg": return (v > 0 ? "+" : "") + v.ToString("0") + " kg";
                case "rpm": return "+" + v.ToString("0");
                case "bar": return v.ToString("0.00") + " bar";
                case "Nm": return v.ToString("0") + " Nm";
                case "s": return v <= 0f ? "—" : v.ToString("0.00") + " s";
                default: return v <= 0f && p.min <= 0f && p.id == "final_drive" ? "—" : v.ToString("0.00");
            }
        }

        private void BuildEngines()
        {
            AddEngineRow(null);
            foreach (var e in EngineCatalog.Engines) AddEngineRow(e);
        }

        private void AddEngineRow(EngineData e)
        {
            string id = e?.id ?? "";
            bool selected = _draft.engineSwapId == id;
            var row = Ui.El("dc-setting");
            var info = Ui.El();
            info.style.flexGrow = 1;
            info.Add(Ui.Label(e == null ? T("garage.engine_stock") : T(e.titleKey), "dc-h2"));
            if (e != null)
            {
                info.Add(Ui.Label($"{e.PowerHp:0} hp @ {e.powerRpm:0} · {e.torqueNm:0} Nm @ {e.torqueRpm:0} · {e.displacementL:0.0} L · {T("garage.swap_mass", e.swapMassKg.ToString("0"))} · {T("garage.confidence." + e.confidence)}", "dc-muted"));
#if DC_SHOW_REFERENCE_NAMES
                info.Add(Ui.Label(T("garage.reference", e.referenceCode), "dc-small"));
#endif
            }
            row.Add(info);
            bool owned = e == null || S.Profile.ownedItems.Contains(id);
            row.Add(Ui.Label(owned ? T("garage.owned") : T("garage.price", e.price.ToString("N0")), "dc-setting-value"));
            Action act = () => { _draft.engineSwapId = id; Changed(true); };
            var b = Ui.Button(selected ? "✓" : T("common.select"), act, "dc-btn--chip");
            b.EnableInClassList("is-selected", selected);
            row.Add(b);
            _content.Add(row);
            Nav.Register(row, act);
        }

        private void BuildKorch()
        {
            _content.Add(Ui.Label(T("garage.korch_desc"), "dc-muted"));
            var korch = Ui.Button(T("garage.make_korch"), () => { _draft = KorchBuilder.Apply(_draft); Changed(true); }, "dc-btn--primary");
            _content.Add(korch);
            Nav.Register(korch, () => { _draft = KorchBuilder.Apply(_draft); Changed(true); });
            ToggleRow(_content, "garage.kit.stripped", () => _draft.strippedInterior, v => { _draft.strippedInterior = v; Changed(false); });
            ToggleRow(_content, "garage.kit.cage", () => _draft.rollCage, v => { _draft.rollCage = v; Changed(false); });
            ToggleRow(_content, "garage.kit.hydro", () => _draft.hydraulicHandbrake, v => { _draft.hydraulicHandbrake = v; Changed(false); });
            ToggleRow(_content, "garage.kit.battery", () => _draft.batteryInTrunk, v => { _draft.batteryInTrunk = v; Changed(false); });
            _content.Add(Ui.Label(T("garage.presets"), "dc-h2"));
            var presets = Ui.El("gr-groups");
            foreach (var id in TuningPresets.Ids)
            {
                var pid = id;
                var chip = Ui.Button(T("tuning.preset." + id), () => { var v = _draft.visual.Clone(); _draft = TuningPresets.Create(pid, VehicleId); if (pid != "korch") _draft.visual = v; Changed(true); }, "dc-btn--chip");
                presets.Add(chip);
                Nav.Register(chip, () => { var v = _draft.visual.Clone(); _draft = TuningPresets.Create(pid, VehicleId); if (pid != "korch") _draft.visual = v; Changed(true); });
            }
            _content.Add(presets);
        }

        private void BuildVisual()
        {
            var v = _draft.visual;
            Swatches("garage.paint", VisualPartCatalog.PaintPalette, () => v.paintColor, c => { v.paintColor = c; Changed(false); });
            CyclerRow(_content, "garage.finish_label", new[] { "garage.finish.Gloss", "garage.finish.Matte", "garage.finish.Metallic", "garage.finish.Pearl", "garage.finish.Chrome", "garage.finish.Satin" },
                () => (int)v.paintFinish, i => { v.paintFinish = (PaintFinish)i; Changed(false); });
            Swatches("garage.secondary", VisualPartCatalog.PaintPalette, () => v.secondaryColor, c => { v.secondaryColor = c; Changed(false); });
            foreach (PartSlot slot in Enum.GetValues(typeof(PartSlot)))
            {
                var parts = new List<VisualPart>(VisualPartCatalog.ForSlot(slot));
                var keys = new string[parts.Count];
                for (int i = 0; i < parts.Count; i++) keys[i] = parts[i].titleKey;
                var sl = slot;
                CyclerRow(_content, "garage.slot." + slot, keys, () => Math.Max(0, parts.FindIndex(p => p.id == v.PartFor(sl))), i =>
                {
                    v.parts[sl] = parts[i].id;
                    Changed(sl == PartSlot.Fenders);
                });
            }
            Swatches("garage.rim_color", new[] { "#B8B8B8", "#1A1A1A", "#F2F2F2", "#D4AF37", "#C8102E", "#1E50A0", "#B87333", "#3B3B3B" }, () => v.rimColor, c => { v.rimColor = c; Changed(false); });
            Swatches("garage.caliper", new[] { "#E0B000", "#C8102E", "#1E50A0", "#2E8B57", "#F2F2F2", "#111111" }, () => v.caliperColor, c => { v.caliperColor = c; Changed(false); });
            SliderRow(_content, T("garage.rim_size"), 15f, 20f, 1f, () => v.rimSizeInch, x => { v.rimSizeInch = x; Changed(false); }, x => x.ToString("0") + "\"");
            SliderRow(_content, T("garage.tint"), 0f, 1f, 0.05f, () => v.windowTint, x => { v.windowTint = x; Changed(false); }, x => (x * 100f).ToString("0") + "%");
            Swatches("garage.underglow", new[] { "", "#00B5D8", "#FF2D95", "#A8E10C", "#7B61FF", "#FF8A00" }, () => v.underglowColor ?? "", c => { v.underglowColor = c; Changed(false); });
        }

        private void Swatches(string labelKey, IList<string> colors, Func<string> get, Action<string> set)
        {
            _content.Add(Ui.Label(T(labelKey), "dc-h2"));
            var row = Ui.El("dc-swatches");
            var all = new List<VisualElement>();
            foreach (var hex in colors)
            {
                var h = hex;
                var sw = Ui.El("dc-swatch");
                sw.style.backgroundColor = string.IsNullOrEmpty(h) ? new Color(0f, 0f, 0f, 0.3f) : Ui.Hex(h, Color.gray);
                sw.EnableInClassList("is-selected", string.Equals(get(), h, StringComparison.OrdinalIgnoreCase));
                sw.RegisterCallback<ClickEvent>(_ => { set(h); foreach (var a in all) a.EnableInClassList("is-selected", a == sw); });
                row.Add(sw);
                all.Add(sw);
                Nav.Register(sw, () => { set(h); foreach (var a in all) a.EnableInClassList("is-selected", a == sw); });
            }
            _content.Add(row);
        }

        // ---------------------------------------------------------------- apply / preview / stats

        private void Changed(bool rebuild)
        {
            _draft = TuningApplier.Sanitize(_draft);
            _dirtyPreview = true;
            RequestStats(false);
            RefreshApply();
            if (rebuild) { Rebuild(); }
        }

        private void RefreshApply()
        {
            if (_applyButton == null) return;
            long cost = EconomyRules.MissingCost(_draft, S.Profile.ownedItems);
            _applyButton.text = cost > 0 ? T("garage.buy_apply", cost.ToString("N0")) : T("garage.apply_free");
            _applyButton.SetEnabled(S.Profile.wallet.CanAfford(cost));
            if (_credits != null) _credits.text = T("hud.credits", S.Profile.wallet.credits.ToString("N0"));
        }

        private void Apply()
        {
            if (!EconomyRules.BuyMissing(_draft, S.Profile.ownedItems, S.Profile.wallet)) { UiRoot.Toast(T("garage.not_enough")); return; }
            S.Profile.tunings[VehicleId] = _draft.Clone();
            S.SaveProfile();
            Game.PreviewTuning(_draft);
            _savedStats = _stats;
            UiRoot.Toast(T("garage.saved"), 1.4f, "#3DDC84");
            RefreshApply();
        }

        public override void OnBack()
        {
            Game.PreviewTuning(Saved);
            _draft = null;
            Game.CloseGarage();
        }

        private void RequestStats(bool immediate) => _statsDelay = immediate ? 0f : 0.45f;

        public override void Tick(float dt)
        {
            if (_dirtyPreview)
            {
                _dirtyPreview = false;
                Game.PreviewTuning(_draft);
            }
            if (_statsDelay >= 0f)
            {
                _statsDelay -= dt;
                if (_statsDelay < 0f && (_statsTask == null || _statsTask.IsCompleted))
                {
                    var profile = TuningApplier.Apply(S.BaseProfile, _draft);
                    // Pure C# physics: safe to run on a worker thread (no Unity API inside).
                    _statsTask = Task.Run(() => PerformanceEstimator.Estimate(profile, 25f));
                    RenderStats(true);
                }
            }
            if (_statsTask != null && _statsTask.IsCompleted)
            {
                if (_statsTask.Status == TaskStatus.RanToCompletion)
                {
                    _stats = _statsTask.Result;
                    if (_savedStats == null) _savedStats = _stats;
                }
                _statsTask = null;
                RenderStats();
            }
        }

        private void RenderStats(bool calculating = false)
        {
            if (_statsBox == null) return;
            _statsBox.Clear();
            _statsBox.Add(Ui.Label(T("garage.tab.stats"), "dc-h2"));
            if (calculating || _stats == null) { _statsBox.Add(Ui.Label(T("garage.calculating"), "dc-muted")); if (_stats == null) return; }
            var r = _stats;
            var b = _savedStats ?? r;
            Row(T("garage.stats.power"), $"{r.peakPowerHp:0} hp", r.peakPowerHp - b.peakPowerHp, false);
            Row(T("garage.stats.torque"), $"{r.peakTorqueNm:0} Nm", r.peakTorqueNm - b.peakTorqueNm, false);
            Row(T("garage.stats.mass"), $"{r.massKg:0} kg", r.massKg - b.massKg, true);
            Row(T("garage.stats.weight_dist"), $"{r.frontWeightPercent:0}/{100f - r.frontWeightPercent:0}", 0f, false);
            Row(T("garage.stats.pw"), $"{r.powerToWeightHpPerTon:0} hp/t", r.powerToWeightHpPerTon - b.powerToWeightHpPerTon, false);
            Row(T("garage.stats.zero100"), r.zeroTo100 > 0f ? $"{r.zeroTo100:0.0} s" : "—", r.zeroTo100 - b.zeroTo100, true);
            Row(T("garage.stats.top"), $"{r.topSpeedKmh:0} km/h", r.topSpeedKmh - b.topSpeedKmh, false);
            Row(T("garage.stats.braking"), $"{r.braking100To0M:0.0} m", r.braking100To0M - b.braking100To0M, true);
            Row(T("garage.stats.layout"), T("tuning.layout." + r.layout) + " · " + r.gears + " " + T("garage.stats.gears"), 0f, false);
            Bar(T("garage.rating.acceleration"), r.accelerationRating);
            Bar(T("garage.rating.braking"), r.brakingRating);
            Bar(T("garage.rating.grip"), r.gripRating);
            Bar(T("garage.rating.drift"), r.driftRating);
            _statsBox.Add(Ui.Label(T("garage.computed_note"), "dc-muted"));
        }

        private void Row(string label, string value, float delta, bool lowerIsBetter)
        {
            var row = Ui.El("gr-stat-row");
            row.Add(Ui.Label(label, "dc-muted"));
            var right = Ui.Row();
            if (Mathf.Abs(delta) > 0.05f)
            {
                bool better = lowerIsBetter ? delta < 0f : delta > 0f;
                right.Add(Ui.Label((delta > 0 ? "+" : "") + delta.ToString("0.#") + "  ", better ? "gr-delta-up" : "gr-delta-down"));
            }
            right.Add(Ui.Label(value, "gr-stat-num"));
            row.Add(right);
            _statsBox.Add(row);
        }

        private void Bar(string label, float rating)
        {
            _statsBox.Add(Ui.Label(label + "  " + rating.ToString("0.0"), "dc-small"));
            var bar = Ui.El("gr-bar");
            var fill = Ui.El("gr-bar-fill");
            fill.style.width = Length.Percent(Mathf.Clamp01(rating / 10f) * 100f);
            bar.Add(fill);
            _statsBox.Add(bar);
        }
    }
}

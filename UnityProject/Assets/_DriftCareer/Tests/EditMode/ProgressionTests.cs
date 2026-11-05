using System;
using System.Collections.Generic;
using DriftCareer.Core;
using DriftCareer.Core.Account;
using DriftCareer.Core.Gameplay;
using DriftCareer.Core.Garage;
using DriftCareer.Core.Persistence;
using DriftCareer.Core.Surfaces;
using DriftCareer.Core.Vehicle;
using NUnit.Framework;

namespace DriftCareer.Tests.EditMode
{
    public class ProgressionTests
    {
        private static VehiclePhysicsProfile Classic() => SpecToProfileConverter.Convert(SpecTemplates.Create("classic_sedan_16"));

        [Test]
        public void ClassicSedan_IsSlow_AndSwapMakesItFast_AndNoseHeavier()
        {
            var baseP = Classic();
            var stock = PerformanceEstimator.Estimate(baseP, 25f);
            var t = VehicleTuningState.Stock("classic_sedan_16");
            t.engineSwapId = "jdm_i6_25_jz";
            var swapped = TuningApplier.Apply(baseP, t);
            var fast = PerformanceEstimator.Estimate(swapped, 25f);
            TestContext.WriteLine($"classic: {stock.peakPowerHp:0} hp 0-100 {stock.zeroTo100:0.0}s mass {stock.massKg:0}; 1JZ swap: {fast.peakPowerHp:0} hp 0-100 {fast.zeroTo100:0.0}s mass {fast.massKg:0} front {fast.frontWeightPercent:0.0}%");
            Assert.AreEqual(80f, stock.peakPowerHp * 0.7355f / 0.7355f, 8f);
            Assert.Greater(stock.zeroTo100, 10f, "80 hp classic should take >10 s to 100");
            Assert.Greater(fast.peakPowerHp, 250f);
            Assert.Less(fast.zeroTo100, stock.zeroTo100 * 0.75f);
            Assert.Greater(swapped.chassis.massKg, baseP.chassis.massKg + 100f, "I6 swap adds weight");
            Assert.Greater(swapped.chassis.frontWeightFraction, baseP.chassis.frontWeightFraction);
            Assert.AreEqual(0, swapped.Validate().Count, string.Join("; ", swapped.Validate()));
        }

        [Test]
        public void EngineCatalog_EntriesHaveSourcesAndProducePlausibleCurves()
        {
            foreach (var e in EngineCatalog.Engines)
            {
                Assert.IsFalse(string.IsNullOrEmpty(e.source), e.id);
                var c = e.TorqueCurve();
                Assert.IsTrue(c.IsValid(out var err), e.id + ": " + err);
                Assert.AreEqual(e.torqueNm, c.MaxY(), e.torqueNm * 0.02f, e.id);
                float peakKw = 0f;
                for (float rpm = 800; rpm < e.redlineRpm; rpm += 50) peakKw = Math.Max(peakKw, c.Evaluate(rpm) * rpm * MathUtil.RpmToRadPerSec / 1000f);
                Assert.AreEqual(e.powerKw, peakKw, e.powerKw * 0.1f, e.id);
            }
        }

        [Test]
        public void WiderTires_MoreGrip_AndRimSizeChangesRadius()
        {
            var baseP = VehiclePhysicsProfile.CreatePlaceholderRwd();
            var narrow = VehicleTuningState.Stock(); narrow.tireWidthRear = 185f;
            var wide = VehicleTuningState.Stock(); wide.tireWidthRear = 285f;
            var pn = TuningApplier.Apply(baseP, narrow);
            var pw = TuningApplier.Apply(baseP, wide);
            Assert.Greater(pw.rear.tire.peakFrictionLat, pn.rear.tire.peakFrictionLat);
            var big = VehicleTuningState.Stock(); big.visual.rimSizeInch = 19f;
            var small = VehicleTuningState.Stock(); small.visual.rimSizeInch = 15f;
            Assert.Greater(TuningApplier.Apply(baseP, big).rear.wheelRadius, TuningApplier.Apply(baseP, small).rear.wheelRadius);
        }

        [Test]
        public void TrackWidening_IsLimitedByFenderKit()
        {
            var t = VehicleTuningState.Stock();
            t.trackOffsetRear = 0.09f;
            Assert.AreEqual(0.02f, TuningApplier.Sanitize(t).trackOffsetRear, 1e-5);
            t.visual.parts[PartSlot.Fenders] = "fenders.overfenders";
            Assert.AreEqual(0.09f, TuningApplier.Sanitize(t).trackOffsetRear, 1e-5);
        }

        [Test]
        public void Korch_FromAnyCar_IsValidAndDriftsHarder()
        {
            var baseP = Classic();
            var korch = TuningPresets.Create("korch", "classic_sedan_16");
            var p = TuningApplier.Apply(baseP, korch);
            Assert.AreEqual(0, p.Validate().Count, string.Join("; ", p.Validate()));
            Assert.AreEqual(DifferentialType.Locked, p.differential.type);
            Assert.Greater(p.steering.maxAngleDeg, 60f);
            Assert.IsNotEmpty(korch.engineSwapId);
            var stock = PerformanceEstimator.Estimate(baseP, 10f);
            var tuned = PerformanceEstimator.Estimate(p, 10f);
            TestContext.WriteLine($"drift rating stock {stock.driftRating:0.0} -> korch {tuned.driftRating:0.0}");
            Assert.GreaterOrEqual(tuned.driftRating, stock.driftRating);
        }

        [Test]
        public void Economy_BuyMissingParts_AndNoNegativeBalance()
        {
            var wallet = new Wallet { credits = 1000 };
            var owned = new HashSet<string>();
            var t = TuningPresets.Create("korch");
            long cost = EconomyRules.MissingCost(t, owned);
            Assert.Greater(cost, 1000);
            Assert.IsFalse(EconomyRules.BuyMissing(t, owned, wallet));
            Assert.AreEqual(1000, wallet.credits);
            wallet.Earn(cost);
            Assert.IsTrue(EconomyRules.BuyMissing(t, owned, wallet));
            Assert.AreEqual(1000, wallet.credits);
            Assert.AreEqual(0, EconomyRules.MissingCost(t, owned));
            Assert.IsFalse(wallet.Spend(-5));
            Assert.AreEqual(0, EconomyRules.PartPrice("spoiler.stock"));
            Assert.AreEqual(40, EconomyRules.CreditsForChain(2000f));
        }

        [Test]
        public void Challenges_CompleteAndClaimOnce_DailyRotationDeterministic()
        {
            var store = new Dictionary<string, ChallengeProgress>();
            var day = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
            var tracker = new ChallengeTracker(store, day);
            var a = ChallengeTracker.DailyFor(day.Date);
            var b = ChallengeTracker.DailyFor(day.Date);
            Assert.AreEqual(a[0].id, b[0].id);
            ChallengeDefinition done = null;
            tracker.Completed += d => { if (d.id == "p.first_chain") done = d; };
            tracker.Report(new RunStats { dt = 0.1f, bankedChain = 1500f });
            Assert.IsNotNull(done);
            var wallet = new Wallet();
            Assert.AreEqual(500, tracker.Claim(done, wallet));
            Assert.AreEqual(0, tracker.Claim(done, wallet));
            for (int i = 0; i < 40; i++) tracker.Report(new RunStats { dt = 0.1f, drifting = true, driftAngleDeg = 65f });
            Assert.IsTrue(tracker.Progress(ChallengeTracker.Permanent[2]).completed, "hold 60 deg for 3 s");
        }

        [Test]
        public void RunClock_AndTournament_Medals()
        {
            var clock = new RunClock();
            bool finished = false;
            clock.Finished += () => finished = true;
            clock.Begin(RunRules.For(RunKind.ScoreAttack));
            for (int i = 0; i < 100 * 95; i++) clock.Update(0.01f);
            Assert.IsTrue(finished);
            var cup = new TournamentRun(TournamentRun.AcademyCup());
            cup.SubmitRound(70000); cup.SubmitRound(50000); cup.SubmitRound(40000);
            Assert.IsTrue(cup.Done);
            Assert.AreEqual(Medal.Gold, cup.FinalMedal());
            Assert.AreEqual(8000, cup.Prize());
        }

        [Test]
        public void Account_PaidCurrencyStaysOnStore_EarnedIsShared_MergeIsSafe()
        {
            var acc = new AccountState();
            acc.paidCurrency[StoreId.PlayStation] = 1000;
            acc.paidCurrency[StoreId.Steam] = 500;
            Assert.AreEqual(500, acc.SpendablePaidCurrency(StoreId.Steam, PlatformPolicy.For));
            Assert.AreEqual(1000, acc.SpendablePaidCurrency(StoreId.PlayStation, PlatformPolicy.For));
            acc.entitlements.Add(new Entitlement { itemId = "livery.pack1", purchasedIn = StoreId.PlayStation, crossPlatform = false });
            acc.entitlements.Add(new Entitlement { itemId = "engine.earned", purchasedIn = StoreId.None });
            Assert.IsFalse(acc.CanUse("livery.pack1", StoreId.Steam));
            Assert.IsTrue(acc.CanUse("livery.pack1", StoreId.PlayStation));
            Assert.IsTrue(acc.CanUse("engine.earned", StoreId.GooglePlay));
            Assert.IsFalse(PlatformPolicy.For(StoreId.PlayStation).allowUserModelImport);
            Assert.IsTrue(PlatformPolicy.For(StoreId.Steam).allowUserModelImport);

            var server = new AccountState { earnedCredits = 10000, revision = 5 };
            server.records["academy.score_attack"] = 30000f;
            var local = server.Clone();
            local.records["academy.score_attack"] = 45000f;
            local.entitlements.Add(new Entitlement { itemId = "kit.angle_kit" });
            var merged = AccountState.Merge(server, local, 1200);
            Assert.AreEqual(11200, merged.earnedCredits);
            Assert.AreEqual(45000f, merged.records["academy.score_attack"]);
            Assert.IsTrue(merged.CanUse("kit.angle_kit", StoreId.Xbox));
            Assert.Greater(merged.revision, server.revision);
        }

        [Test]
        public void Ghost_RecordSerializeSample_AndTandemScoring()
        {
            var rec = new GhostRecorder();
            rec.Start("academy", "placeholder_rwd", "tester");
            for (int i = 0; i < 400; i++) rec.Update(0.01f, i * 0.1f, 0f, 0f, i * 0.5f, 40f, 10f, 0.5f);
            rec.Stop(12345f);
            var g = rec.Recording;
            Assert.Greater(g.frames.Count, 70);
            var back = GhostRecording.FromJson(JsonParser.Parse(JsonWriter.Write(g.ToJson(), false)));
            Assert.AreEqual(g.frames.Count, back.frames.Count);
            Assert.AreEqual(12345f, back.score);
            var mid = back.Sample(2f);
            Assert.AreEqual(20f, mid.x, 0.6f);
            var corrupt = GhostRecording.FromJson(JsonParser.Parse("{\"frames\":[[0,1,2,3,4,5,6,7],[\"x\"],[0.5,1,2,3,4,5,6,7],[0.2,1,2,3,4,5,6,7]]}"));
            Assert.AreEqual(2, corrupt.frames.Count, "bad and non-monotonic frames are dropped");

            var tandem = new TandemScoring();
            for (int i = 0; i < 100; i++) tandem.Update(0.05f, new Vec2(0f, 0f), 40f, true, new Vec2(0f, 3f), 42f, true);
            float close = tandem.Score;
            tandem.Reset();
            for (int i = 0; i < 100; i++) tandem.Update(0.05f, new Vec2(0f, 0f), 10f, true, new Vec2(0f, 14f), 45f, true);
            Assert.Greater(close, 85f);
            Assert.Less(tandem.Score, close * 0.5f);
        }

        [Test]
        public void Profile_PersistsWalletOwnedChallenges()
        {
            var fs = new MemoryFileStore();
            var store = new VersionedDocumentStore<PlayerProfile>(fs, new ProfileCodec());
            var p = new PlayerProfile();
            p.wallet.Earn(777);
            p.ownedItems.Add("jdm_i6_25_jz");
            p.challenges["p.first_chain"] = new ChallengeProgress { id = "p.first_chain", value = 1000, completed = true, claimed = true };
            p.RecordScore("academy.score_attack", 42000f, Medal.Silver);
            var t = TuningPresets.Create("korch");
            p.tunings["placeholder_rwd"] = t;
            store.Save("p.json", p);
            var back = store.Load("p.json", out var report);
            Assert.AreEqual(LoadStatus.Loaded, report.status);
            Assert.AreEqual(p.wallet.credits, back.wallet.credits);
            Assert.IsTrue(back.ownedItems.Contains("jdm_i6_25_jz"));
            Assert.IsTrue(back.challenges["p.first_chain"].claimed);
            Assert.AreEqual(2, back.bestMedals["academy.score_attack"]);
            var bt = back.TuningFor("placeholder_rwd");
            Assert.AreEqual("jdm_i6_25_jz", bt.engineSwapId);
            Assert.IsTrue(bt.hydraulicHandbrake && bt.rollCage && bt.strippedInterior);
            Assert.AreEqual(t.tireWidthRear, bt.tireWidthRear, 1e-3);
        }
    }
}

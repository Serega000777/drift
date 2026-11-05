using System;
using System.Collections.Generic;
using DriftCareer.Core.Garage;

namespace DriftCareer.Core.Gameplay
{
    /// <summary>
    /// Soft currency ("кредиты", CR) earned only by playing. Premium/paid currency is a future, platform-bound
    /// wallet (see Account) and never buys handling advantages that are not also earnable (no pay-to-win).
    /// </summary>
    [Serializable]
    public struct WalletTransaction
    {
        public string utc;
        public long amount;
        public long balanceAfter;
        /// <summary>Localization key of the reason ("wallet.reason.chain", ...).</summary>
        public string reason;
    }

    [Serializable]
    public sealed class Wallet
    {
        public const int HistoryLimit = 60;

        public long credits;
        /// <summary>Lifetime earned, for stats/anti-cheat sanity checks.</summary>
        public long lifetimeEarned;
        public long lifetimeSpent;
        /// <summary>Most recent transactions first (shown in the personal account).</summary>
        public List<WalletTransaction> history = new List<WalletTransaction>();

        public bool CanAfford(long price) => price <= 0 || credits >= price;

        public void Earn(long amount, string reason = "wallet.reason.other")
        {
            if (amount <= 0) return;
            credits = Math.Min(long.MaxValue / 4, credits + amount);
            lifetimeEarned += amount;
            Record(amount, reason);
        }

        public bool Spend(long price, string reason = "wallet.reason.purchase")
        {
            if (price < 0 || !CanAfford(price)) return false;
            if (price == 0) return true;
            credits -= price;
            lifetimeSpent += price;
            Record(-price, reason);
            return true;
        }

        private void Record(long amount, string reason)
        {
            history.Insert(0, new WalletTransaction { utc = DateTime.UtcNow.ToString("o"), amount = amount, balanceAfter = credits, reason = reason });
            if (history.Count > HistoryLimit) history.RemoveRange(HistoryLimit, history.Count - HistoryLimit);
        }
    }

    public static class EconomyRules
    {
        /// <summary>Drift points per credit when a chain is banked.</summary>
        public const float PointsPerCredit = 50f;
        public const int MaxCreditsPerChain = 2500;
        public const int FirstDrillReward = 1500;
        public const int RepeatDrillReward = 250;

        public static int CreditsForChain(float points) => (int)Math.Min(MaxCreditsPerChain, Math.Floor(Math.Max(0f, points) / PointsPerCredit));

        /// <summary>Price of a tuning item. Stock items are free. Prices scale with performance impact.</summary>
        public static int PartPrice(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || itemId.EndsWith(".stock", StringComparison.Ordinal)) return 0;
            var engine = EngineCatalog.Find(itemId);
            if (engine != null) return engine.price;
            var part = VisualPartCatalog.Find(itemId);
            if (part != null) return 800 + part.style * 400 + (int)(Math.Abs(part.downforceDelta) * 4000f);
            switch (itemId)
            {
                case "kit.welded_diff": return 1200;
                case "kit.lsd": return 4500;
                case "kit.angle_kit": return 6000;
                case "kit.hydro_handbrake": return 2500;
                case "kit.coilovers": return 7000;
                case "kit.roll_cage": return 5000;
                case "kit.stripped_interior": return 0;
                case "kit.ecu_stage1": return 3000;
                case "kit.ecu_stage2": return 9000;
                case "tire.Street": return 400;
                case "tire.Sport": return 1200;
                case "tire.DriftSemiSlick": return 2400;
                case "tire.Winter": return 1400;
                case "tire.Studded": return 2200;
                default: return 1000;
            }
        }

        /// <summary>Items a tuning state needs (for ownership checks before applying in the garage).</summary>
        public static List<string> RequiredItems(VehicleTuningState t)
        {
            var list = new List<string>();
            if (!string.IsNullOrEmpty(t.engineSwapId)) list.Add(t.engineSwapId);
            if (t.diffType == Vehicle.DifferentialType.Locked) list.Add("kit.welded_diff");
            if (t.diffType == Vehicle.DifferentialType.Lsd && (t.diffPowerLock > 0.7f || t.diffPreloadNm > 200f)) list.Add("kit.lsd");
            if (t.steeringLockDeg > 55f) list.Add("kit.angle_kit");
            if (t.hydraulicHandbrake) list.Add("kit.hydro_handbrake");
            if (t.rideHeightOffset < -0.015f || t.springFront > 1.25f || t.springRear > 1.25f) list.Add("kit.coilovers");
            if (t.rollCage) list.Add("kit.roll_cage");
            if (t.powerScale > 1.05f) list.Add(t.powerScale > 1.3f ? "kit.ecu_stage2" : "kit.ecu_stage1");
            list.Add("tire." + t.compoundFront);
            list.Add("tire." + t.compoundRear);
            foreach (var kv in t.visual.parts) list.Add(kv.Value);
            return list;
        }

        public static long MissingCost(VehicleTuningState t, ICollection<string> owned)
        {
            long sum = 0;
            var seen = new HashSet<string>();
            foreach (var id in RequiredItems(t))
            {
                if (!seen.Add(id) || owned.Contains(id)) continue;
                sum += PartPrice(id);
            }
            return sum;
        }

        /// <summary>Buys every missing item. Returns false (and changes nothing) if credits are insufficient.</summary>
        public static bool BuyMissing(VehicleTuningState t, ICollection<string> owned, Wallet wallet)
        {
            long cost = MissingCost(t, owned);
            if (!wallet.Spend(cost, "wallet.reason.parts")) return false;
            foreach (var id in RequiredItems(t)) if (!owned.Contains(id)) owned.Add(id);
            return true;
        }
    }
}

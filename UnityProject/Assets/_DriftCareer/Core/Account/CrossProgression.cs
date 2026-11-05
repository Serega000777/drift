using System;
using System.Collections.Generic;
using DriftCareer.Core.Platform;

namespace DriftCareer.Core.Account
{
    public enum StoreId { None = 0, GooglePlay = 1, AppStore = 2, Steam = 3, PlayStation = 4, Xbox = 5, Nintendo = 6, Epic = 7 }

    /// <summary>
    /// What a platform allows. Defaults reflect typical store/console policies and must be re-checked against each
    /// platform holder's current agreement before certification (they are NOT legal advice).
    /// </summary>
    [Serializable]
    public sealed class PlatformPolicy
    {
        public StoreId store;
        public bool allowUserModelImport;        // FBX/GLB import of own cars
        public bool allowUgcBrowsing;            // community tracks/cars
        public bool allowTextChat;
        public bool allowCrossPlay = true;
        public bool allowExternalLinks;
        public bool paidCurrencyCrossPlatform;   // can premium currency bought here be spent elsewhere?
        public bool requiresPlatformAccount;     // e.g. console sign-in required

        public static PlatformPolicy For(StoreId s)
        {
            switch (s)
            {
                case StoreId.Steam:
                case StoreId.Epic:
                    return new PlatformPolicy { store = s, allowUserModelImport = true, allowUgcBrowsing = true, allowTextChat = true, allowExternalLinks = true };
                case StoreId.GooglePlay:
                case StoreId.AppStore:
                    return new PlatformPolicy { store = s, allowUserModelImport = false, allowUgcBrowsing = true, allowTextChat = true };
                case StoreId.PlayStation:
                case StoreId.Xbox:
                case StoreId.Nintendo:
                    return new PlatformPolicy { store = s, allowUserModelImport = false, allowUgcBrowsing = true, allowTextChat = false, requiresPlatformAccount = true };
                default:
                    return new PlatformPolicy { store = s, allowUserModelImport = true, allowUgcBrowsing = false };
            }
        }

        public static StoreId DefaultStore(PlatformKind kind) =>
            kind == PlatformKind.Mobile ? StoreId.GooglePlay : kind == PlatformKind.Console ? StoreId.PlayStation : StoreId.Steam;
    }

    [Serializable]
    public sealed class LinkedIdentity
    {
        public StoreId store;
        public string externalId;
        public string linkedUtc;
    }

    /// <summary>An owned item and where the right to it came from.</summary>
    [Serializable]
    public sealed class Entitlement
    {
        public string itemId;
        /// <summary>None = earned in game (usable everywhere); otherwise bought in that store.</summary>
        public StoreId purchasedIn;
        public bool crossPlatform = true;
    }

    /// <summary>
    /// One account across mobile, PC and consoles. Earned progress (credits, parts, records) is shared; real-money
    /// purchases stay usable on their store unless that store allows cross-platform use. The backend is authoritative;
    /// the client keeps a cached copy and merges deterministically after offline play.
    /// </summary>
    [Serializable]
    public sealed class AccountState
    {
        public string accountId = Guid.NewGuid().ToString("N");
        public long revision;
        public List<LinkedIdentity> identities = new List<LinkedIdentity>();
        public long earnedCredits;
        public Dictionary<StoreId, long> paidCurrency = new Dictionary<StoreId, long>();
        public List<Entitlement> entitlements = new List<Entitlement>();
        public Dictionary<string, float> records = new Dictionary<string, float>(StringComparer.Ordinal);

        public bool IsLinked(StoreId s) => identities.Exists(i => i.store == s);

        public void Link(StoreId store, string externalId)
        {
            if (string.IsNullOrEmpty(externalId) || IsLinked(store)) return;
            identities.Add(new LinkedIdentity { store = store, externalId = externalId, linkedUtc = DateTime.UtcNow.ToString("o") });
        }

        /// <summary>Paid currency spendable on the current store under its policy.</summary>
        public long SpendablePaidCurrency(StoreId current, Func<StoreId, PlatformPolicy> policy)
        {
            long sum = 0;
            foreach (var kv in paidCurrency)
                if (kv.Key == current || (policy(kv.Key).paidCurrencyCrossPlatform && policy(current).paidCurrencyCrossPlatform)) sum += kv.Value;
            return sum;
        }

        public bool CanUse(string itemId, StoreId current)
        {
            foreach (var e in entitlements)
                if (e.itemId == itemId && (e.purchasedIn == StoreId.None || e.crossPlatform || e.purchasedIn == current)) return true;
            return false;
        }

        /// <summary>
        /// Merge a server copy with local offline changes: entitlements = union, records = max,
        /// earned credits = server value + local delta since the base revision (never negative).
        /// </summary>
        public static AccountState Merge(AccountState server, AccountState local, long localEarnedDelta)
        {
            var m = server.Clone();
            foreach (var e in local.entitlements)
                if (!m.entitlements.Exists(x => x.itemId == e.itemId && x.purchasedIn == e.purchasedIn)) m.entitlements.Add(e);
            foreach (var kv in local.records)
                if (!m.records.TryGetValue(kv.Key, out var v) || kv.Value > v) m.records[kv.Key] = kv.Value;
            foreach (var id in local.identities) if (!m.IsLinked(id.store)) m.identities.Add(id);
            m.earnedCredits = Math.Max(0, server.earnedCredits + localEarnedDelta);
            m.revision = Math.Max(server.revision, local.revision) + 1;
            return m;
        }

        public AccountState Clone()
        {
            var c = (AccountState)MemberwiseClone();
            c.identities = new List<LinkedIdentity>(identities);
            c.paidCurrency = new Dictionary<StoreId, long>(paidCurrency);
            c.entitlements = new List<Entitlement>(entitlements);
            c.records = new Dictionary<string, float>(records, StringComparer.Ordinal);
            return c;
        }
    }

    /// <summary>Backend contract (future service on the VPS / cloud). MVP uses <see cref="LocalAccountService"/>.</summary>
    public interface IAccountService
    {
        bool Online { get; }
        AccountState Current { get; }
        PlatformPolicy Policy { get; }
        void SignIn(StoreId store, string platformUserId, Action<bool, string> done);
        void Sync(Action<bool, string> done);
    }

    public sealed class LocalAccountService : IAccountService
    {
        public LocalAccountService(StoreId store)
        {
            Policy = PlatformPolicy.For(store);
            Current = new AccountState();
        }

        public bool Online => false;
        public AccountState Current { get; private set; }
        public PlatformPolicy Policy { get; }

        public void SignIn(StoreId store, string platformUserId, Action<bool, string> done)
        {
            Current.Link(store, platformUserId);
            done?.Invoke(true, "local");
        }

        public void Sync(Action<bool, string> done) => done?.Invoke(false, "offline: no backend configured");
    }
}

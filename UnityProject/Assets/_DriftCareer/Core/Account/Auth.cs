using System;
using DriftCareer.Core.Security;

namespace DriftCareer.Core.Account
{
    public enum AuthProviderId { Guest = 0, Steam = 1, GooglePlay = 2, GameCenter = 3, Apple = 4, PlayStation = 5, Xbox = 6, Nintendo = 7, Epic = 8 }

    public enum AuthState { SignedOut = 0, SigningIn = 1, SignedIn = 2, Refreshing = 3, Error = 4 }

    public interface IClock { DateTime UtcNow { get; } }

    public sealed class SystemClock : IClock { public DateTime UtcNow => DateTime.UtcNow; }

    /// <summary>
    /// Server-issued session. Access tokens are short-lived (minutes) and kept only in memory;
    /// the rotating refresh token is persisted exclusively through <see cref="ISecureStore"/>.
    /// </summary>
    public sealed class AuthSession
    {
        public string accountId;
        public AuthProviderId provider;
        public bool isGuest;
        public string accessToken;
        public string refreshToken;
        public DateTime accessExpiresUtc;
        public string displayName;

        public bool AccessValid(DateTime now, TimeSpan skew) => !isGuest && !string.IsNullOrEmpty(accessToken) && now + skew < accessExpiresUtc;
    }

    /// <summary>
    /// One-time proof from a platform SDK (Steam session ticket, Google Play Games server auth code, Game Center
    /// signature, Apple identity token, PSN/Xbox auth code). Opaque to the client, verified ONLY by the backend,
    /// never persisted and never logged.
    /// </summary>
    public struct PlatformCredential
    {
        public AuthProviderId provider;
        public string proof;
        public string platformUserId;
    }

    public sealed class AuthResult
    {
        public bool ok;
        public string error;
        /// <summary>Network/server hiccup - try again later. False = credential rejected/revoked.</summary>
        public bool retryable;
        public AuthSession session;

        public static AuthResult Fail(string error, bool retryable) => new AuthResult { ok = false, error = error, retryable = retryable };
    }

    /// <summary>Platform sign-in (SDK specific). Implementations live in platform plugins.</summary>
    public interface IAuthProvider
    {
        AuthProviderId Id { get; }
        bool IsAvailable { get; }
        void GetCredential(Action<bool, PlatformCredential, string> done);
    }

    /// <summary>Backend contract (docs/BACKEND_API.md). All calls over HTTPS; the server verifies platform proofs.</summary>
    public interface IAuthBackend
    {
        bool Online { get; }
        void SignIn(PlatformCredential credential, Action<AuthResult> done);
        void Refresh(string refreshToken, Action<AuthResult> done);
        void SignOut(string refreshToken, Action<bool> done);
        void DeleteAccount(string accessToken, Action<bool, string> done);
    }

    /// <summary>No server configured: only local guest play.</summary>
    public sealed class OfflineAuthBackend : IAuthBackend
    {
        public bool Online => false;
        public void SignIn(PlatformCredential c, Action<AuthResult> done) => done?.Invoke(AuthResult.Fail("offline", true));
        public void Refresh(string t, Action<AuthResult> done) => done?.Invoke(AuthResult.Fail("offline", true));
        public void SignOut(string t, Action<bool> done) => done?.Invoke(true);
        public void DeleteAccount(string t, Action<bool, string> done) => done?.Invoke(false, "offline");
    }

    /// <summary>
    /// Client auth state machine: guest by default, platform sign-in, proactive token refresh with backoff,
    /// revocation handling and sign-out. Play never blocks on the network - the local guest profile always works.
    /// </summary>
    public sealed class AuthService
    {
        public const string KeyRefresh = "auth.refresh";
        public const string KeyAccount = "auth.account";
        public const string KeyProvider = "auth.provider";
        public const string KeyInstall = "auth.install";
        public static readonly TimeSpan RefreshAhead = TimeSpan.FromMinutes(2);

        private readonly IAuthBackend _backend;
        private readonly ISecureStore _store;
        private readonly IClock _clock;
        private DateTime _nextRetryUtc = DateTime.MinValue;
        private int _failures;

        public AuthState State { get; private set; } = AuthState.SignedOut;
        public AuthSession Session { get; private set; }
        public string LastError { get; private set; }
        public bool IsSignedInOnline => Session != null && !Session.isGuest && State != AuthState.SignedOut;
        public event Action<AuthState> StateChanged;

        public AuthService(IAuthBackend backend, ISecureStore store, IClock clock = null)
        {
            _backend = backend ?? new OfflineAuthBackend();
            _store = store;
            _clock = clock ?? new SystemClock();
        }

        public IAuthBackend Backend => _backend;

        /// <summary>Stable anonymous install id (random, not derived from hardware ids).</summary>
        public string InstallId
        {
            get
            {
                if (_store.TryGet(KeyInstall, out var id) && !string.IsNullOrEmpty(id)) return id;
                id = Crypto.ToHex(Crypto.RandomBytes(16));
                _store.Set(KeyInstall, id);
                return id;
            }
        }

        private void SetState(AuthState s)
        {
            if (State == s) return;
            State = s;
            StateChanged?.Invoke(s);
        }

        public void ContinueAsGuest()
        {
            Session = new AuthSession { accountId = "guest-" + InstallId, provider = AuthProviderId.Guest, isGuest = true };
            LastError = null;
            SetState(AuthState.SignedIn);
        }

        /// <summary>Restores a previous online session from the secure store; otherwise falls back to guest.</summary>
        public void Restore()
        {
            if (_store.TryGet(KeyRefresh, out var refresh) && !string.IsNullOrEmpty(refresh))
            {
                _store.TryGet(KeyAccount, out var account);
                _store.TryGet(KeyProvider, out var provider);
                Enum.TryParse(provider, out AuthProviderId pid);
                Session = new AuthSession { accountId = account, provider = pid, refreshToken = refresh, isGuest = false };
                SetState(AuthState.Refreshing);
                DoRefresh();
                return;
            }
            ContinueAsGuest();
        }

        public void SignIn(IAuthProvider provider, Action<bool, string> done = null)
        {
            if (provider == null || !provider.IsAvailable) { done?.Invoke(false, "provider_unavailable"); return; }
            if (provider.Id == AuthProviderId.Guest) { ContinueAsGuest(); done?.Invoke(true, null); return; }
            if (!_backend.Online) { LastError = "offline"; done?.Invoke(false, "offline"); return; }
            SetState(AuthState.SigningIn);
            provider.GetCredential((ok, credential, error) =>
            {
                if (!ok) { Fallback(error ?? "platform_signin_failed"); done?.Invoke(false, LastError); return; }
                _backend.SignIn(credential, result =>
                {
                    if (!result.ok || result.session == null) { Fallback(result.error ?? "signin_failed"); done?.Invoke(false, LastError); return; }
                    Accept(result.session);
                    done?.Invoke(true, null);
                });
            });
        }

        private void Accept(AuthSession s)
        {
            s.isGuest = false;
            Session = s;
            _failures = 0;
            LastError = null;
            if (!string.IsNullOrEmpty(s.refreshToken)) _store.Set(KeyRefresh, s.refreshToken);
            _store.Set(KeyAccount, s.accountId ?? "");
            _store.Set(KeyProvider, s.provider.ToString());
            SetState(AuthState.SignedIn);
        }

        private void Fallback(string error)
        {
            ContinueAsGuest();
            LastError = error; // keep the reason visible after falling back to guest
        }

        /// <summary>Call regularly (e.g. once per second). Refreshes ahead of expiry with exponential backoff.</summary>
        public void Tick()
        {
            if (Session == null || Session.isGuest || State == AuthState.SigningIn || State == AuthState.Refreshing) return;
            var now = _clock.UtcNow;
            if (Session.AccessValid(now, RefreshAhead) || now < _nextRetryUtc) return;
            SetState(AuthState.Refreshing);
            DoRefresh();
        }

        private void DoRefresh()
        {
            string token = Session?.refreshToken;
            if (string.IsNullOrEmpty(token)) { SignOutLocal(); return; }
            _backend.Refresh(token, result =>
            {
                if (result.ok && result.session != null) { Accept(result.session); return; }
                if (!result.retryable)
                {
                    // Revoked / reused refresh token: drop credentials, keep playing as guest.
                    SignOutLocal();
                    LastError = result.error ?? "revoked";
                    return;
                }
                _failures++;
                double seconds = Math.Min(300, 5 * Math.Pow(3, _failures - 1));
                _nextRetryUtc = _clock.UtcNow.AddSeconds(seconds);
                LastError = result.error ?? "network";
                // Session stays known (offline grace); online features check AccessValid before use.
                SetState(AuthState.SignedIn);
            });
        }

        public void SignOut()
        {
            if (Session != null && !Session.isGuest && !string.IsNullOrEmpty(Session.refreshToken))
                _backend.SignOut(Session.refreshToken, _ => { });
            SignOutLocal();
        }

        private void SignOutLocal()
        {
            _store.Delete(KeyRefresh);
            _store.Delete(KeyAccount);
            _store.Delete(KeyProvider);
            Session = null;
            SetState(AuthState.SignedOut);
            ContinueAsGuest();
        }

        /// <summary>"Authorization: Bearer ..." for backend calls, or null if there is no valid online session.</summary>
        public string AuthorizationHeader()
        {
            if (Session == null || !Session.AccessValid(_clock.UtcNow, TimeSpan.FromSeconds(10))) return null;
            return "Bearer " + Session.accessToken;
        }
    }

    /// <summary>Local guest provider (always available).</summary>
    public sealed class GuestAuthProvider : IAuthProvider
    {
        public AuthProviderId Id => AuthProviderId.Guest;
        public bool IsAvailable => true;
        public void GetCredential(Action<bool, PlatformCredential, string> done) => done?.Invoke(true, new PlatformCredential { provider = AuthProviderId.Guest }, null);
    }

    /// <summary>Placeholder for a platform SDK provider until the SDK plugin is integrated.</summary>
    public sealed class UnavailableAuthProvider : IAuthProvider
    {
        public UnavailableAuthProvider(AuthProviderId id) { Id = id; }
        public AuthProviderId Id { get; }
        public bool IsAvailable => false;
        public void GetCredential(Action<bool, PlatformCredential, string> done) => done?.Invoke(false, default, "sdk_not_integrated");
    }

    public static class AuthProviders
    {
        /// <summary>Which platform login a build offers (by store).</summary>
        public static AuthProviderId ForStore(StoreId store)
        {
            switch (store)
            {
                case StoreId.Steam: return AuthProviderId.Steam;
                case StoreId.GooglePlay: return AuthProviderId.GooglePlay;
                case StoreId.AppStore: return AuthProviderId.GameCenter;
                case StoreId.PlayStation: return AuthProviderId.PlayStation;
                case StoreId.Xbox: return AuthProviderId.Xbox;
                case StoreId.Nintendo: return AuthProviderId.Nintendo;
                case StoreId.Epic: return AuthProviderId.Epic;
                default: return AuthProviderId.Guest;
            }
        }
    }
}

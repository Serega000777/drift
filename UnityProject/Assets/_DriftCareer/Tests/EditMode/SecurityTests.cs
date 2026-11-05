using System;
using System.Collections.Generic;
using DriftCareer.Core;
using DriftCareer.Core.Account;
using DriftCareer.Core.Persistence;
using DriftCareer.Core.Security;
using NUnit.Framework;

namespace DriftCareer.Tests.EditMode
{
    public class SecurityTests
    {
        private static IKeyProvider Keys(byte seed = 7)
        {
            var k = new byte[32];
            for (int i = 0; i < k.Length; i++) k[i] = (byte)(seed + i);
            return new StaticKeyProvider(k);
        }

        // ---------------- crypto ----------------
        [Test]
        public void Encrypt_RoundTrips_AndUsesRandomIv()
        {
            var key = Keys().MasterKey("t");
            string a = Crypto.Encrypt("refresh-token-секрет", key);
            string b = Crypto.Encrypt("refresh-token-секрет", key);
            Assert.AreNotEqual(a, b, "random IV per encryption");
            Assert.IsTrue(Crypto.TryDecrypt(a, key, out var plain));
            Assert.AreEqual("refresh-token-секрет", plain);
            StringAssert.DoesNotContain("refresh", a);
        }

        [Test]
        public void Decrypt_RejectsTamperingAndWrongKey()
        {
            var key = Keys().MasterKey("t");
            string blob = Crypto.Encrypt("secret", key);
            char[] c = blob.ToCharArray();
            c[10] = c[10] == 'A' ? 'B' : 'A';
            Assert.IsFalse(Crypto.TryDecrypt(new string(c), key, out _), "bit flip detected by MAC");
            Assert.IsFalse(Crypto.TryDecrypt(blob, Keys(99).MasterKey("t"), out _), "wrong key");
            Assert.IsFalse(Crypto.TryDecrypt("garbage", key, out _));
            Assert.IsFalse(Crypto.TryDecrypt("dc1:!!!", key, out _));
        }

        [Test]
        public void SecureStore_HidesKeyNamesAndValues()
        {
            var files = new MemoryFileStore();
            var store = new EncryptedFileSecureStore(files, Keys());
            store.Set("auth.refresh", "rt_123456789");
            Assert.IsTrue(store.TryGet("auth.refresh", out var v));
            Assert.AreEqual("rt_123456789", v);
            foreach (var kv in files.Files)
            {
                StringAssert.DoesNotContain("auth.refresh", kv.Key);
                StringAssert.DoesNotContain("rt_123456789", kv.Value);
            }
            var otherInstall = new EncryptedFileSecureStore(files, Keys(42));
            Assert.IsFalse(otherInstall.TryGet("auth.refresh", out _), "another key cannot read it");
            store.DeleteAll();
            Assert.IsFalse(store.TryGet("auth.refresh", out _));
        }

        [Test]
        public void SignedSave_DetectsWalletEdit_ButKeepsPlayerData()
        {
            var files = new MemoryFileStore();
            var signer = new HmacSaveSigner(Keys());
            var store = new VersionedDocumentStore<PlayerProfile>(files, new ProfileCodec(), signer);
            var p = new PlayerProfile();
            p.wallet.credits = 100;
            store.Save("profile.json", p);
            var ok = store.Load("profile.json", out var r1);
            Assert.IsFalse(r1.tampered);
            Assert.AreEqual(100, ok.wallet.credits);

            // Cheater edits credits and recomputes the (non-secret) CRC - the HMAC still catches it.
            var env = JsonParser.Parse(files.Files["profile.json"]);
            var data = env.GetObject("data");
            data.Set("credits", JsonValue.Number(999999.0));
            env.Set("crc", Crc32.ToHex(Crc32.Compute(JsonWriter.Write(data, false))));
            files.Files["profile.json"] = JsonWriter.Write(env, true);
            var edited = store.Load("profile.json", out var r2);
            Assert.IsTrue(r2.tampered);
            Assert.AreEqual(999999, edited.wallet.credits, "data is not destroyed; the game marks it untrusted instead");
        }

        [Test]
        public void IntegrityFlag_IsSticky()
        {
            var files = new MemoryFileStore();
            var store = new VersionedDocumentStore<PlayerProfile>(files, new ProfileCodec(), new HmacSaveSigner(Keys()));
            var p = new PlayerProfile { integrityCompromised = true };
            store.Save("p.json", p);
            Assert.IsTrue(store.Load("p.json", out _).integrityCompromised);
        }

        [Test]
        public void LogRedactor_RemovesSecretsAndPersonalData()
        {
            string line = "POST /auth token=abc123SECRET&x=1 Authorization: Bearer eyJhbGciOi.eyJzdWIiOiIx.c2ln mail me: ivan.petrov@example.com " +
                          "key 9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08 path C:\\Users\\Ivan\\AppData /Users/ivan/Library";
            string r = LogRedactor.Redact(line);
            StringAssert.DoesNotContain("abc123SECRET", r);
            StringAssert.DoesNotContain("eyJhbGciOi", r);
            StringAssert.DoesNotContain("ivan.petrov@example.com", r);
            StringAssert.DoesNotContain("9f86d081884c7d659a2feaa0c55ad015", r);
            StringAssert.DoesNotContain("Ivan", r);
            StringAssert.DoesNotContain("/Users/ivan", r);
            StringAssert.Contains("POST /auth", r);
        }

        [Test]
        public void Mask_ShowsOnlyEnds()
        {
            Assert.AreEqual("a1b2…9f0e", Crypto.Mask("a1b2c3d4e5f6a7b89f0e"));
            Assert.AreEqual("••••", Crypto.Mask("abcd"));
        }

        // ---------------- auth ----------------
        private sealed class FakeClock : IClock { public DateTime UtcNow { get; set; } = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc); }

        private sealed class FakeBackend : IAuthBackend
        {
            public bool Online => true;
            public int refreshCalls, signOutCalls;
            public AuthResult nextRefresh;
            public FakeClock clock;
            public void SignIn(PlatformCredential c, Action<AuthResult> done)
            {
                if (c.proof != "valid-ticket") { done(AuthResult.Fail("invalid_proof", false)); return; }
                done(new AuthResult { ok = true, session = Session("rt1") });
            }
            public AuthSession Session(string rt) => new AuthSession
            {
                accountId = "acc-42", provider = AuthProviderId.Steam, accessToken = "at-" + rt, refreshToken = rt, accessExpiresUtc = clock.UtcNow.AddMinutes(15)
            };
            public void Refresh(string t, Action<AuthResult> done) { refreshCalls++; done(nextRefresh ?? new AuthResult { ok = true, session = Session(t + "+") }); }
            public void SignOut(string t, Action<bool> done) { signOutCalls++; done(true); }
            public void DeleteAccount(string t, Action<bool, string> done) => done(true, null);
        }

        private sealed class FakeProvider : IAuthProvider
        {
            public string proof = "valid-ticket";
            public AuthProviderId Id => AuthProviderId.Steam;
            public bool IsAvailable => true;
            public void GetCredential(Action<bool, PlatformCredential, string> done) => done(true, new PlatformCredential { provider = Id, proof = proof }, null);
        }

        [Test]
        public void Auth_GuestByDefault_WithStableRandomInstallId()
        {
            var store = new MemorySecureStore();
            var auth = new AuthService(new OfflineAuthBackend(), store);
            auth.Restore();
            Assert.AreEqual(AuthState.SignedIn, auth.State);
            Assert.IsTrue(auth.Session.isGuest);
            string id = auth.InstallId;
            Assert.AreEqual(32, id.Length);
            Assert.AreEqual(id, new AuthService(new OfflineAuthBackend(), store).InstallId);
            Assert.IsNull(auth.AuthorizationHeader(), "guests have no bearer token");
        }

        [Test]
        public void Auth_SignIn_PersistsOnlyRefreshToken_AndRefreshesAhead()
        {
            var clock = new FakeClock();
            var backend = new FakeBackend { clock = clock };
            var store = new MemorySecureStore();
            var auth = new AuthService(backend, store, clock);
            bool ok = false;
            auth.SignIn(new FakeProvider(), (s, e) => ok = s);
            Assert.IsTrue(ok);
            Assert.IsFalse(auth.Session.isGuest);
            Assert.AreEqual("Bearer at-rt1", auth.AuthorizationHeader());
            Assert.IsTrue(store.TryGet(AuthService.KeyRefresh, out var rt));
            Assert.AreEqual("rt1", rt);
            Assert.IsFalse(store.TryGet("auth.access", out _), "access token is never persisted");

            clock.UtcNow = clock.UtcNow.AddMinutes(14); // within the 2-minute refresh window
            auth.Tick();
            Assert.AreEqual(1, backend.refreshCalls);
            Assert.IsTrue(store.TryGet(AuthService.KeyRefresh, out rt));
            Assert.AreEqual("rt1+", rt, "rotating refresh token stored");
        }

        [Test]
        public void Auth_RejectedProof_FallsBackToGuest()
        {
            var clock = new FakeClock();
            var auth = new AuthService(new FakeBackend { clock = clock }, new MemorySecureStore(), clock);
            string error = null;
            auth.SignIn(new FakeProvider { proof = "forged" }, (s, e) => error = e);
            Assert.AreEqual("invalid_proof", error);
            Assert.IsTrue(auth.Session.isGuest);
        }

        [Test]
        public void Auth_RevokedRefresh_SignsOut_NetworkErrorBacksOff()
        {
            var clock = new FakeClock();
            var backend = new FakeBackend { clock = clock };
            var store = new MemorySecureStore();
            var auth = new AuthService(backend, store, clock);
            auth.SignIn(new FakeProvider());
            backend.nextRefresh = AuthResult.Fail("network", true);
            clock.UtcNow = clock.UtcNow.AddMinutes(14);
            auth.Tick();
            Assert.AreEqual(1, backend.refreshCalls);
            auth.Tick();
            Assert.AreEqual(1, backend.refreshCalls, "backoff: no immediate retry");
            clock.UtcNow = clock.UtcNow.AddSeconds(6);
            auth.Tick();
            Assert.AreEqual(2, backend.refreshCalls);
            Assert.IsFalse(auth.Session.isGuest, "offline grace keeps the account");

            backend.nextRefresh = AuthResult.Fail("refresh_reused", false);
            clock.UtcNow = clock.UtcNow.AddMinutes(10);
            auth.Tick();
            Assert.IsTrue(auth.Session.isGuest, "revoked token -> local guest");
            Assert.IsFalse(store.TryGet(AuthService.KeyRefresh, out _));
        }

        [Test]
        public void Auth_SignOut_ClearsSecrets_AndNotifiesServer()
        {
            var clock = new FakeClock();
            var backend = new FakeBackend { clock = clock };
            var store = new MemorySecureStore();
            var auth = new AuthService(backend, store, clock);
            auth.SignIn(new FakeProvider());
            auth.SignOut();
            Assert.AreEqual(1, backend.signOutCalls);
            Assert.IsFalse(store.TryGet(AuthService.KeyRefresh, out _));
            Assert.IsTrue(auth.Session.isGuest);
        }

        // ---------------- privacy ----------------
        [Test]
        public void Privacy_DefaultsAreOptIn_AndChildModeIsRestrictive()
        {
            var p = new PrivacySettings();
            Assert.IsFalse(p.analytics || p.crashReports || p.personalizedAds || p.marketing);
            Assert.IsTrue(p.NeedsOnboarding);
            var steam = PlatformPolicy.For(StoreId.Steam);
            var unknown = PrivacyRules.Resolve(steam, p, true, true);
            Assert.IsFalse(unknown.textChat || unknown.ugcUpload || unknown.personalizedAds || unknown.purchases, "unknown age = child");

            p.ageBand = AgeBand.Under13;
            p.analytics = true; p.personalizedAds = true;
            p.Accept(DateTime.UtcNow);
            Assert.IsFalse(p.analytics || p.personalizedAds, "minors cannot opt into these");
            var child = PrivacyRules.Resolve(steam, p, true, true);
            Assert.IsFalse(child.textChat || child.userModelImport || child.publicNameOnLeaderboards);

            var adult = new PrivacySettings { ageBand = AgeBand.Adult, personalizedAds = true };
            adult.Accept(DateTime.UtcNow);
            var a = PrivacyRules.Resolve(steam, adult, true, true);
            Assert.IsTrue(a.textChat && a.userModelImport && a.personalizedAds && a.purchases);
            Assert.IsFalse(PrivacyRules.Resolve(steam, adult, true, false).competitiveSubmissions, "edited saves cannot submit records");
            Assert.IsFalse(PrivacyRules.Resolve(PlatformPolicy.For(StoreId.PlayStation), adult, true, true).textChat);
        }

        [Test]
        public void AgeBand_FromBirthYear()
        {
            Assert.AreEqual(AgeBand.Under13, PrivacyRules.FromBirthYear(2016, 2026));
            Assert.AreEqual(AgeBand.Teen, PrivacyRules.FromBirthYear(2011, 2026));
            Assert.AreEqual(AgeBand.Adult, PrivacyRules.FromBirthYear(1990, 2026));
            Assert.AreEqual(AgeBand.Unknown, PrivacyRules.FromBirthYear(3000, 2026));
        }

        [Test]
        public void Privacy_ExportExcludesSecrets_EraseRemovesEverything()
        {
            var files = new MemoryFileStore();
            var secure = new EncryptedFileSecureStore(files, Keys());
            secure.Set(AuthService.KeyRefresh, "rt-very-secret");
            var settingsStore = new VersionedDocumentStore<SettingsData>(files, new SettingsCodec());
            settingsStore.Save("settings.json", new SettingsData { language = "en" });
            new VersionedDocumentStore<PlayerProfile>(files, new ProfileCodec()).Save("profile.json", new PlayerProfile());
            files.Files["layouts/my.json"] = "{}";
            files.Files["profile.json.corrupt-20261105"] = "x";
            string export = JsonWriter.Write(PrivacyData.Export(files, "ab12…cd34"), true);
            StringAssert.Contains("settings.json", export);
            StringAssert.Contains("layouts/my.json", export);
            StringAssert.DoesNotContain("rt-very-secret", export);
            StringAssert.DoesNotContain("dc1:", export);
            int removed = PrivacyData.EraseAll(files, secure);
            Assert.Greater(removed, 3);
            Assert.AreEqual(0, files.Files.Count, string.Join(", ", new List<string>(files.Files.Keys)));
        }

        [Test]
        public void PrivacyCodec_EnforcesRulesOnLoad()
        {
            var j = JsonParser.Parse("{\"age\":\"Teen\",\"ads\":true,\"marketing\":true,\"analytics\":true}");
            var p = new PrivacyCodec().Decode(j, 1);
            Assert.IsFalse(p.personalizedAds || p.marketing, "teens cannot be opted in, even by editing the file");
            Assert.IsTrue(p.analytics);
        }

        [Test]
        public void SaveReader_RejectsHugeFiles()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dc_big_" + Guid.NewGuid().ToString("N"));
            try
            {
                var fs = new LocalFileStore(dir);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "big.json"), new byte[LocalFileStore.MaxFileBytes + 1]);
                Assert.Throws<System.IO.IOException>(() => fs.ReadText("big.json"));
                var store = new VersionedDocumentStore<SettingsData>(fs, new SettingsCodec());
                var s = store.Load("big.json", out var r);
                Assert.IsNotNull(s);
                Assert.AreEqual(LoadStatus.CorruptReplacedWithDefault, r.status);
            }
            finally { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
        }
    }
}

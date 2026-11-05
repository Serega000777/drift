using System;
using System.Collections.Generic;
using System.IO;
using DriftCareer.Core.Persistence;
using DriftCareer.Core.Security;

namespace DriftCareer.Core.Account
{
    public enum AgeBand { Unknown = 0, Under13 = 1, Teen = 2, Adult = 3 }

    /// <summary>
    /// Player privacy choices. Everything optional is OFF by default (opt-in). Re-asked when the policy version changes.
    /// </summary>
    [Serializable]
    public sealed class PrivacySettings
    {
        public const string CurrentPolicyVersion = "2026-11";

        public AgeBand ageBand = AgeBand.Unknown;
        public bool analytics;
        public bool crashReports;
        public bool personalizedAds;
        public bool marketing;
        public string acceptedPolicyVersion = "";
        public string acceptedUtc = "";

        public bool NeedsOnboarding => ageBand == AgeBand.Unknown || acceptedPolicyVersion != CurrentPolicyVersion;

        public void Accept(DateTime utc)
        {
            acceptedPolicyVersion = CurrentPolicyVersion;
            acceptedUtc = utc.ToString("o");
            Enforce();
        }

        /// <summary>Minors can never opt into personalized ads or marketing; under-13 gets no analytics either.</summary>
        public void Enforce()
        {
            if (ageBand != AgeBand.Adult) { personalizedAds = false; marketing = false; }
            if (ageBand == AgeBand.Under13 || ageBand == AgeBand.Unknown) analytics = false;
        }
    }

    /// <summary>What the current player may use, after combining platform policy, age and consent.</summary>
    public struct Capabilities
    {
        public bool onlinePlay;
        public bool textChat;
        public bool ugcBrowse;
        public bool ugcUpload;
        public bool userModelImport;
        public bool publicNameOnLeaderboards;
        public bool purchases;
        public bool analytics;
        public bool crashReports;
        public bool personalizedAds;
        public bool competitiveSubmissions;
    }

    public static class PrivacyRules
    {
        public static AgeBand FromBirthYear(int birthYear, int currentYear)
        {
            int age = currentYear - birthYear;
            if (birthYear <= 1900 || age < 0 || age > 120) return AgeBand.Unknown;
            return age < 13 ? AgeBand.Under13 : (age < 18 ? AgeBand.Teen : AgeBand.Adult);
        }

        /// <summary>
        /// Safe-by-default resolution. Unknown age is treated like a child until answered.
        /// <paramref name="profileTrusted"/> = false when local saves were edited outside the game.
        /// </summary>
        public static Capabilities Resolve(PlatformPolicy platform, PrivacySettings privacy, bool signedInOnline, bool profileTrusted)
        {
            bool child = privacy.ageBand == AgeBand.Under13 || privacy.ageBand == AgeBand.Unknown;
            bool teen = privacy.ageBand == AgeBand.Teen;
            var c = new Capabilities
            {
                onlinePlay = signedInOnline,
                textChat = signedInOnline && platform.allowTextChat && !child && !teen,
                ugcBrowse = platform.allowUgcBrowsing && !child,
                ugcUpload = signedInOnline && platform.allowUgcBrowsing && !child,
                userModelImport = platform.allowUserModelImport && !child,
                publicNameOnLeaderboards = signedInOnline && !child,
                purchases = !child,
                analytics = privacy.analytics && !child,
                crashReports = privacy.crashReports,
                personalizedAds = privacy.personalizedAds && privacy.ageBand == AgeBand.Adult,
                competitiveSubmissions = signedInOnline && profileTrusted,
            };
            return c;
        }
    }

    public sealed class PrivacyCodec : IDocumentCodec<PrivacySettings>
    {
        public string SchemaId => "drift.privacy";
        public int CurrentVersion => 1;
        public PrivacySettings CreateDefault() => new PrivacySettings();

        public JsonValue Encode(PrivacySettings p) => JsonValue.NewObject()
            .Set("age", p.ageBand.ToString()).Set("analytics", p.analytics).Set("crash", p.crashReports).Set("ads", p.personalizedAds)
            .Set("marketing", p.marketing).Set("policy", p.acceptedPolicyVersion ?? "").Set("accepted", p.acceptedUtc ?? "");

        public PrivacySettings Decode(JsonValue j, int version)
        {
            var p = new PrivacySettings
            {
                ageBand = j.GetEnum("age", AgeBand.Unknown), analytics = j.GetBool("analytics"), crashReports = j.GetBool("crash"),
                personalizedAds = j.GetBool("ads"), marketing = j.GetBool("marketing"),
                acceptedPolicyVersion = j.GetString("policy", ""), acceptedUtc = j.GetString("accepted", "")
            };
            p.Enforce();
            return p;
        }
    }

    /// <summary>GDPR-style data rights for local data: export (access/portability) and erase.</summary>
    public static class PrivacyData
    {
        private static readonly string[] Documents = { "settings.json", "profile.json", "privacy.json" };

        /// <summary>
        /// Builds a human-readable export of everything the game stores about the player locally.
        /// Secrets (tokens, keys) are never included.
        /// </summary>
        public static JsonValue Export(IFileStore files, string installIdMasked)
        {
            var root = JsonValue.NewObject()
                .Set("exportedUtc", DateTime.UtcNow.ToString("o"))
                .Set("policyVersion", PrivacySettings.CurrentPolicyVersion)
                .Set("installId", installIdMasked)
                .Set("note", "Local data only. Server-side data (when accounts launch) is exported via the account service.");
            var docs = JsonValue.NewObject();
            foreach (var d in Documents)
            {
                try
                {
                    if (!files.Exists(d)) continue;
                    if (JsonParser.TryParse(files.ReadText(d), out var v, out _)) docs.Set(d, v.GetObject("data") ?? v);
                }
                catch (IOException) { }
            }
            root.Set("documents", docs);
            var layouts = JsonValue.NewArray();
            foreach (var f in files.List("layouts", ".json")) layouts.Add(JsonValue.String(f));
            root.Set("layouts", layouts);
            var ghosts = JsonValue.NewArray();
            foreach (var f in files.List("ghosts", ".json")) ghosts.Add(JsonValue.String(f));
            root.Set("ghosts", ghosts);
            return root;
        }

        /// <summary>Deletes all local player data (incl. backups, quarantined files, exports) and secrets. Returns files removed.</summary>
        public static int EraseAll(IFileStore files, ISecureStore secure)
        {
            int n = 0;
            foreach (var dir in new[] { "", "layouts", "ghosts", "exports" })
                foreach (var f in new List<string>(files.List(dir, "")))
                {
                    try { if (files.Exists(f)) { files.Delete(f); n++; } }
                    catch (IOException) { }
                }
            secure?.DeleteAll();
            return n;
        }
    }
}

using System.Text;
using DriftCareer.Core.Security;
using UnityEngine;

namespace DriftCareer.Runtime.Security
{
    /// <summary>
    /// Per-install master key = HMAC(app pepper, random install secret + device id + bundle id).
    /// The random install secret lives in PlayerPrefs (app-private storage on Android/iOS, user registry on Windows).
    /// This deters casual save editing and keeps tokens out of plain files; it is NOT hardware-backed.
    /// Mobile/console production builds should replace it with Keychain / Android Keystore / platform storage
    /// (see docs/SECURITY_PRIVACY.md, AI_TODO).
    /// </summary>
    public sealed class DeviceKeyProvider : IKeyProvider
    {
        private const string SecretPref = "dc.install.secret.v1";
        // Compiled-in pepper: rotate per release line; changing it invalidates save signatures (marks them untrusted).
        private static readonly byte[] Pepper = Encoding.UTF8.GetBytes("DriftCareer.v1.pepper.7c1f2e9a");
        private readonly byte[] _master;

        public DeviceKeyProvider()
        {
            string secret = PlayerPrefs.GetString(SecretPref, "");
            if (string.IsNullOrEmpty(secret) || secret.Length < 32)
            {
                secret = Crypto.ToHex(Crypto.RandomBytes(32));
                PlayerPrefs.SetString(SecretPref, secret);
                PlayerPrefs.Save();
            }
            string device = SystemInfo.deviceUniqueIdentifier;
            if (string.IsNullOrEmpty(device) || device == SystemInfo.unsupportedIdentifier) device = "no-device-id";
            _master = Crypto.Hmac(Pepper, secret + "|" + device + "|" + Application.identifier);
        }

        public byte[] MasterKey(string purpose) => Crypto.DeriveKey(_master, purpose);

        /// <summary>Forgets the install secret (used by "delete my data").</summary>
        public static void Forget()
        {
            PlayerPrefs.DeleteKey(SecretPref);
            PlayerPrefs.Save();
        }
    }

    /// <summary>All runtime logging goes through here so tokens, e-mails and user paths never reach logs/crash reports.</summary>
    public static class SafeLog
    {
        public static void Info(string message) => Debug.Log("[DriftCareer] " + LogRedactor.Redact(message));
        public static void Warn(string message) => Debug.LogWarning("[DriftCareer] " + LogRedactor.Redact(message));
        public static void Error(string message) => Debug.LogError("[DriftCareer] " + LogRedactor.Redact(message));
    }
}

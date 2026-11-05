using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DriftCareer.Core.Security
{
    /// <summary>
    /// Small, dependency-free crypto helpers built only on primitives available in Unity (Mono/IL2CPP) and .NET:
    /// HMAC-SHA256, AES-256-CBC with Encrypt-then-MAC, a secure RNG and constant-time comparison.
    /// Not a replacement for platform key storage (Keychain/Keystore) - see docs/SECURITY_PRIVACY.md.
    /// </summary>
    public static class Crypto
    {
        public const string BlobPrefix = "dc1:";
        private const int KeySize = 32;
        private const int IvSize = 16;
        private const int MacSize = 32;

        public static byte[] RandomBytes(int count)
        {
            var b = new byte[count];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return b;
        }

        public static byte[] Hmac(byte[] key, byte[] data)
        {
            using (var h = new HMACSHA256(key)) return h.ComputeHash(data);
        }

        public static byte[] Hmac(byte[] key, string text) => Hmac(key, Encoding.UTF8.GetBytes(text ?? string.Empty));

        public static string HmacHex(byte[] key, string text)
        {
            var mac = Hmac(key, text);
            var sb = new StringBuilder(mac.Length * 2);
            foreach (var b in mac) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static byte[] Sha256(string text)
        {
            using (var s = SHA256.Create()) return s.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
        }

        /// <summary>Constant-time equality (no early exit) to avoid timing leaks when comparing MACs.</summary>
        public static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            return ConstantTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
        }

        /// <summary>Derives an independent sub-key for a purpose (HKDF-expand style, single block).</summary>
        public static byte[] DeriveKey(byte[] master, string purpose)
        {
            if (master == null || master.Length < 16) throw new ArgumentException("master key too short");
            var info = Encoding.UTF8.GetBytes("DriftCareer|" + purpose + "|\x01");
            return Hmac(master, info);
        }

        /// <summary>AES-256-CBC + HMAC-SHA256 (encrypt-then-MAC). Output: "dc1:" + base64(iv | ciphertext | mac).</summary>
        public static string Encrypt(string plaintext, byte[] masterKey)
        {
            var encKey = DeriveKey(masterKey, "enc");
            var macKey = DeriveKey(masterKey, "mac");
            var iv = RandomBytes(IvSize);
            byte[] cipher;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = encKey;
                aes.IV = iv;
                using (var enc = aes.CreateEncryptor())
                {
                    var plain = Encoding.UTF8.GetBytes(plaintext ?? string.Empty);
                    cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
                }
            }
            var body = new byte[IvSize + cipher.Length];
            Buffer.BlockCopy(iv, 0, body, 0, IvSize);
            Buffer.BlockCopy(cipher, 0, body, IvSize, cipher.Length);
            var mac = Hmac(macKey, Concat(Encoding.ASCII.GetBytes(BlobPrefix), body));
            return BlobPrefix + Convert.ToBase64String(Concat(body, mac));
        }

        public static bool TryDecrypt(string blob, byte[] masterKey, out string plaintext)
        {
            plaintext = null;
            if (string.IsNullOrEmpty(blob) || !blob.StartsWith(BlobPrefix, StringComparison.Ordinal)) return false;
            byte[] all;
            try { all = Convert.FromBase64String(blob.Substring(BlobPrefix.Length)); }
            catch (FormatException) { return false; }
            if (all.Length < IvSize + 16 + MacSize) return false;
            var body = new byte[all.Length - MacSize];
            var mac = new byte[MacSize];
            Buffer.BlockCopy(all, 0, body, 0, body.Length);
            Buffer.BlockCopy(all, body.Length, mac, 0, MacSize);
            var expected = Hmac(DeriveKey(masterKey, "mac"), Concat(Encoding.ASCII.GetBytes(BlobPrefix), body));
            if (!ConstantTimeEquals(mac, expected)) return false; // tampered or wrong key: never decrypt
            try
            {
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = DeriveKey(masterKey, "enc");
                    var iv = new byte[IvSize];
                    Buffer.BlockCopy(body, 0, iv, 0, IvSize);
                    aes.IV = iv;
                    using (var dec = aes.CreateDecryptor())
                    {
                        var plain = dec.TransformFinalBlock(body, IvSize, body.Length - IvSize);
                        plaintext = Encoding.UTF8.GetString(plain);
                        return true;
                    }
                }
            }
            catch (CryptographicException) { return false; }
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            var r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }

        public static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>Masks an identifier for display/logs: "a1b2…9f0e".</summary>
        public static string Mask(string id)
        {
            if (string.IsNullOrEmpty(id)) return "—";
            if (id.Length <= 8) return new string('•', id.Length);
            return id.Substring(0, 4) + "…" + id.Substring(id.Length - 4);
        }
    }

    /// <summary>Provides per-install master keys. The Unity host derives them from platform/device secrets.</summary>
    public interface IKeyProvider
    {
        byte[] MasterKey(string purpose);
    }

    /// <summary>Fixed-key provider for tests and tools.</summary>
    public sealed class StaticKeyProvider : IKeyProvider
    {
        private readonly byte[] _key;
        public StaticKeyProvider(byte[] key) { _key = key; }
        public byte[] MasterKey(string purpose) => Crypto.DeriveKey(_key, purpose);
    }

    /// <summary>Key/value store for secrets (refresh tokens, install id). Values never appear in plain files or logs.</summary>
    public interface ISecureStore
    {
        void Set(string key, string value);
        bool TryGet(string key, out string value);
        void Delete(string key);
        void DeleteAll();
    }

    /// <summary>
    /// Encrypted secret store on top of <see cref="Persistence.IFileStore"/>. File names are HMACs of the key names,
    /// contents are AES+HMAC blobs. Production mobile/console builds should swap in a Keychain/Keystore-backed store.
    /// </summary>
    public sealed class EncryptedFileSecureStore : ISecureStore
    {
        public const string Dir = "secure";
        private readonly Persistence.IFileStore _files;
        private readonly byte[] _key;

        public EncryptedFileSecureStore(Persistence.IFileStore files, IKeyProvider keys)
        {
            _files = files;
            _key = keys.MasterKey("secure-store");
        }

        private string PathFor(string key) => Dir + "/" + Crypto.HmacHex(_key, "name:" + key).Substring(0, 32) + ".sec";

        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException(nameof(key));
            _files.WriteTextAtomic(PathFor(key), Crypto.Encrypt(value ?? string.Empty, _key));
        }

        public bool TryGet(string key, out string value)
        {
            value = null;
            string p = PathFor(key);
            try
            {
                if (!_files.Exists(p)) return false;
                return Crypto.TryDecrypt(_files.ReadText(p), _key, out value);
            }
            catch (IOException) { return false; }
        }

        public void Delete(string key)
        {
            string p = PathFor(key);
            try { _files.Delete(p); _files.Delete(p + ".bak"); }
            catch (IOException) { }
        }

        public void DeleteAll()
        {
            foreach (var f in new System.Collections.Generic.List<string>(_files.List(Dir, ".sec")))
                try { _files.Delete(f); } catch (IOException) { }
            foreach (var f in new System.Collections.Generic.List<string>(_files.List(Dir, ".bak")))
                try { _files.Delete(f); } catch (IOException) { }
        }
    }

    /// <summary>In-memory secure store (tests, editor).</summary>
    public sealed class MemorySecureStore : ISecureStore
    {
        private readonly System.Collections.Generic.Dictionary<string, string> _map = new System.Collections.Generic.Dictionary<string, string>();
        public void Set(string key, string value) => _map[key] = value;
        public bool TryGet(string key, out string value) => _map.TryGetValue(key, out value);
        public void Delete(string key) => _map.Remove(key);
        public void DeleteAll() => _map.Clear();
        public int Count => _map.Count;
    }

    /// <summary>Signs save payloads so casual edits (e.g. of the wallet) are detected. Server data stays authoritative.</summary>
    public interface ISaveSigner
    {
        string Sign(string canonicalData);
        bool Verify(string canonicalData, string signature);
    }

    public sealed class HmacSaveSigner : ISaveSigner
    {
        private readonly byte[] _key;
        public HmacSaveSigner(IKeyProvider keys) { _key = keys.MasterKey("save-signature"); }
        public string Sign(string canonicalData) => Crypto.HmacHex(_key, canonicalData);
        public bool Verify(string canonicalData, string signature) => Crypto.ConstantTimeEquals(Sign(canonicalData), signature ?? "");
    }
}

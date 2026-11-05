using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DriftCareer.Core.Persistence
{
    /// <summary>
    /// File storage abstraction. Local files on mobile/PC; console builds implement this over
    /// the platform save API (behind IPlatformServices).
    /// </summary>
    public interface IFileStore
    {
        bool Exists(string relativePath);
        string ReadText(string relativePath);
        /// <summary>Writes atomically: temp file, backup of the previous version, then swap.</summary>
        void WriteTextAtomic(string relativePath, string content);
        void Delete(string relativePath);
        void Move(string fromRelative, string toRelative);
        IEnumerable<string> List(string relativeDirectory, string extension);
    }

    public sealed class LocalFileStore : IFileStore
    {
        private readonly string _root;

        public LocalFileStore(string rootDirectory)
        {
            if (string.IsNullOrEmpty(rootDirectory)) throw new ArgumentException(nameof(rootDirectory));
            _root = Path.GetFullPath(rootDirectory);
            Directory.CreateDirectory(_root);
        }

        public string Root => _root;

        /// <summary>Resolves a relative path and rejects traversal outside the root.</summary>
        public string Resolve(string relative)
        {
            if (string.IsNullOrEmpty(relative)) throw new ArgumentException("Empty path");
            string full = Path.GetFullPath(Path.Combine(_root, relative));
            string rootWithSep = _root.EndsWith(Path.DirectorySeparatorChar.ToString()) ? _root : _root + Path.DirectorySeparatorChar;
            if (full != _root && !full.StartsWith(rootWithSep, StringComparison.Ordinal)) throw new UnauthorizedAccessException("Path escapes save root: " + relative);
            return full;
        }

        public bool Exists(string relativePath) => File.Exists(Resolve(relativePath));

        public string ReadText(string relativePath) => File.ReadAllText(Resolve(relativePath), Encoding.UTF8);

        public void WriteTextAtomic(string relativePath, string content)
        {
            string path = Resolve(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            string bak = path + ".bak";
            var bytes = new UTF8Encoding(false).GetBytes(content);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            if (File.Exists(path))
            {
                File.Copy(path, bak, true);
                File.Delete(path);
            }
            File.Move(tmp, path);
        }

        public void Delete(string relativePath)
        {
            string p = Resolve(relativePath);
            if (File.Exists(p)) File.Delete(p);
        }

        public void Move(string fromRelative, string toRelative)
        {
            string a = Resolve(fromRelative), b = Resolve(toRelative);
            if (!File.Exists(a)) return;
            if (File.Exists(b)) File.Delete(b);
            File.Move(a, b);
        }

        public IEnumerable<string> List(string relativeDirectory, string extension)
        {
            string dir = Resolve(string.IsNullOrEmpty(relativeDirectory) ? "." : relativeDirectory);
            if (!Directory.Exists(dir)) yield break;
            foreach (var f in Directory.GetFiles(dir, "*" + extension))
                yield return Path.Combine(relativeDirectory ?? "", Path.GetFileName(f)).Replace('\\', '/');
        }
    }

    /// <summary>In-memory store for tests and editor previews.</summary>
    public sealed class MemoryFileStore : IFileStore
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal);

        public bool Exists(string p) => Files.ContainsKey(p);
        public string ReadText(string p) => Files.TryGetValue(p, out var s) ? s : throw new FileNotFoundException(p);

        public void WriteTextAtomic(string p, string c)
        {
            if (Files.TryGetValue(p, out var old)) Files[p + ".bak"] = old;
            Files[p] = c;
        }

        public void Delete(string p) => Files.Remove(p);

        public void Move(string a, string b)
        {
            if (!Files.TryGetValue(a, out var s)) return;
            Files.Remove(a);
            Files[b] = s;
        }

        public IEnumerable<string> List(string dir, string ext)
        {
            string prefix = string.IsNullOrEmpty(dir) ? "" : dir.TrimEnd('/') + "/";
            foreach (var k in new List<string>(Files.Keys))
                if (k.StartsWith(prefix, StringComparison.Ordinal) && k.EndsWith(ext, StringComparison.Ordinal) && k.IndexOf('/', prefix.Length) < 0)
                    yield return k;
        }
    }

    /// <summary>Converts a document to/from JSON with explicit schema versioning and migrations.</summary>
    public interface IDocumentCodec<T>
    {
        string SchemaId { get; }
        int CurrentVersion { get; }
        JsonValue Encode(T value);
        /// <summary>Decode data written with <paramref name="version"/> (migrate older versions here).</summary>
        T Decode(JsonValue data, int version);
        T CreateDefault();
    }

    public enum LoadStatus { Loaded, Migrated, RecoveredFromBackup, RecoveredFromTemp, CreatedDefault, CorruptReplacedWithDefault }

    public struct LoadReport
    {
        public LoadStatus status;
        public int fromVersion;
        public string error;
        public string quarantinedPath;
    }

    /// <summary>
    /// Envelope: { "schema": id, "version": n, "crc": hex, "data": {...} }.
    /// Load order: main file -> .tmp -> .bak -> default. Corrupt files are quarantined (renamed), never silently deleted.
    /// </summary>
    public sealed class VersionedDocumentStore<T> where T : class
    {
        private readonly IFileStore _files;
        private readonly IDocumentCodec<T> _codec;

        public VersionedDocumentStore(IFileStore files, IDocumentCodec<T> codec)
        {
            _files = files;
            _codec = codec;
        }

        public string Serialize(T value)
        {
            var data = _codec.Encode(value);
            string dataJson = JsonWriter.Write(data, false);
            var env = JsonValue.NewObject()
                .Set("schema", _codec.SchemaId)
                .Set("version", _codec.CurrentVersion)
                .Set("crc", Crc32.ToHex(Crc32.Compute(dataJson)))
                .Set("data", data);
            return JsonWriter.Write(env, true);
        }

        public bool TryDeserialize(string text, out T value, out int version, out string error)
        {
            value = null; version = 0; error = null;
            try
            {
                var env = JsonParser.Parse(text);
                if (!env.IsObject) { error = "root_not_object"; return false; }
                if (env.GetString("schema") != _codec.SchemaId) { error = "schema_mismatch"; return false; }
                version = env.GetInt("version", -1);
                if (version < 1 || version > _codec.CurrentVersion) { error = "unsupported_version_" + version; return false; }
                var data = env.GetObject("data");
                if (data == null) { error = "no_data"; return false; }
                string crc = env.GetString("crc");
                if (crc != null && crc != Crc32.ToHex(Crc32.Compute(JsonWriter.Write(data, false)))) { error = "checksum_mismatch"; return false; }
                value = _codec.Decode(data, version);
                if (value == null) { error = "decode_failed"; return false; }
                return true;
            }
            catch (JsonException e) { error = "json:" + e.Message; return false; }
            catch (Exception e) when (e is InvalidCastException || e is ArgumentException || e is NullReferenceException || e is FormatException)
            {
                error = "decode:" + e.Message;
                return false;
            }
        }

        public T Load(string path, out LoadReport report)
        {
            report = new LoadReport();
            string firstError = null;
            bool mainExisted = _files.Exists(path);
            string[] candidates = { path, path + ".tmp", path + ".bak" };
            for (int i = 0; i < candidates.Length; i++)
            {
                string p = candidates[i];
                if (!_files.Exists(p)) continue;
                string text;
                try { text = _files.ReadText(p); }
                catch (IOException e) { firstError = firstError ?? e.Message; continue; }
                if (TryDeserialize(text, out var value, out int version, out string error))
                {
                    report.fromVersion = version;
                    if (i == 0) report.status = version < _codec.CurrentVersion ? LoadStatus.Migrated : LoadStatus.Loaded;
                    else report.status = i == 1 ? LoadStatus.RecoveredFromTemp : LoadStatus.RecoveredFromBackup;
                    report.error = firstError;
                    if (i > 0 || version < _codec.CurrentVersion)
                    {
                        if (i == 0 && mainExisted) { /* migration rewrite below */ }
                        else if (mainExisted) report.quarantinedPath = Quarantine(path);
                        Save(path, value);
                    }
                    return value;
                }
                firstError = firstError ?? error;
            }
            var def = _codec.CreateDefault();
            if (mainExisted)
            {
                report.status = LoadStatus.CorruptReplacedWithDefault;
                report.quarantinedPath = Quarantine(path);
            }
            else report.status = LoadStatus.CreatedDefault;
            report.error = firstError;
            return def;
        }

        private string Quarantine(string path)
        {
            string q = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            try { _files.Move(path, q); return q; }
            catch (IOException) { return null; }
        }

        public void Save(string path, T value) => _files.WriteTextAtomic(path, Serialize(value));
    }
}

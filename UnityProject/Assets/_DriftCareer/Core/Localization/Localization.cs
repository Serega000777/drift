using System;
using System.Collections.Generic;
using System.Globalization;

namespace DriftCareer.Core.Localization
{
    /// <summary>
    /// Key/value string tables per language (RU and EN at minimum). Missing key -> fallback language -> key itself,
    /// so missing strings are visible instead of crashing.
    /// </summary>
    public sealed class LocalizationService
    {
        public const string Russian = "ru";
        public const string English = "en";

        private readonly Dictionary<string, Dictionary<string, string>> _tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        public string Language { get; private set; } = Russian;
        public string FallbackLanguage { get; set; } = English;
        public event Action<string> LanguageChanged;

        public IEnumerable<string> Languages => _tables.Keys;

        public void LoadTable(string language, string json)
        {
            var root = JsonParser.Parse(json);
            if (!root.IsObject) throw new JsonException("Localization root must be an object");
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            Flatten(root, "", table);
            _tables[language] = table;
        }

        private static void Flatten(JsonValue node, string prefix, Dictionary<string, string> into)
        {
            foreach (var key in node.Keys)
            {
                var v = node[key];
                string full = prefix.Length == 0 ? key : prefix + "." + key;
                if (v.Type == JsonType.String) into[full] = v.AsString;
                else if (v.Type == JsonType.Object) Flatten(v, full, into);
            }
        }

        public void SetLanguage(string language)
        {
            if (language == null || !_tables.ContainsKey(language) || language == Language) return;
            Language = language;
            LanguageChanged?.Invoke(language);
        }

        public bool Has(string key) => key != null && _tables.TryGetValue(Language, out var t) && t.ContainsKey(key);

        public bool HasIn(string language, string key) => key != null && _tables.TryGetValue(language, out var t) && t.ContainsKey(key);

        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (_tables.TryGetValue(Language, out var t) && t.TryGetValue(key, out var s)) return s;
            if (_tables.TryGetValue(FallbackLanguage, out var f) && f.TryGetValue(key, out s)) return s;
            return key;
        }

        public string Format(string key, params object[] args)
        {
            try { return string.Format(CultureInfo.InvariantCulture, Get(key), args); }
            catch (FormatException) { return Get(key); }
        }

        /// <summary>Keys present in one language but missing in another (used by tests/CI).</summary>
        public List<string> MissingKeys(string language, string reference)
        {
            var missing = new List<string>();
            if (!_tables.TryGetValue(reference, out var refTable)) return missing;
            _tables.TryGetValue(language, out var t);
            foreach (var k in refTable.Keys) if (t == null || !t.ContainsKey(k)) missing.Add(k);
            return missing;
        }
    }
}

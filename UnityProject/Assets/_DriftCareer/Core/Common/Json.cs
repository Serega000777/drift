using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DriftCareer.Core
{
    public enum JsonType { Null, Bool, Number, String, Array, Object }

    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }

    /// <summary>
    /// Minimal JSON DOM. Dependency-free so the same code runs in Unity (Mono/IL2CPP) and in dotnet tests.
    /// Saves use explicit mapping (no reflection, no polymorphic type names) for safety and migrations.
    /// </summary>
    public sealed class JsonValue
    {
        public JsonType Type { get; private set; }
        private bool _bool;
        private double _number;
        private string _string;
        private List<JsonValue> _array;
        private Dictionary<string, JsonValue> _object;
        private List<string> _keyOrder;

        private JsonValue(JsonType type) { Type = type; }

        public static JsonValue Null() => new JsonValue(JsonType.Null);
        public static JsonValue Bool(bool v) => new JsonValue(JsonType.Bool) { _bool = v };
        public static JsonValue Number(double v) => new JsonValue(JsonType.Number) { _number = v };

        /// <summary>Stores a float using its shortest round-trip decimal form (0.3f is written as 0.3).</summary>
        public static JsonValue Number(float v) =>
            Number(double.Parse(v.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));

        public static JsonValue Number(int v) => Number((double)v);
        public static JsonValue String(string v) => v == null ? Null() : new JsonValue(JsonType.String) { _string = v };
        public static JsonValue NewArray() => new JsonValue(JsonType.Array) { _array = new List<JsonValue>() };
        public static JsonValue NewObject() => new JsonValue(JsonType.Object)
        {
            _object = new Dictionary<string, JsonValue>(StringComparer.Ordinal),
            _keyOrder = new List<string>()
        };

        public bool IsNull => Type == JsonType.Null;
        public bool IsObject => Type == JsonType.Object;
        public bool IsArray => Type == JsonType.Array;

        public bool AsBool => Type == JsonType.Bool ? _bool : throw new JsonException("Not a bool");
        public double AsDouble => Type == JsonType.Number ? _number : throw new JsonException("Not a number");
        public float AsFloat => (float)AsDouble;
        public int AsInt => (int)Math.Round(AsDouble);
        public string AsString => Type == JsonType.String ? _string : throw new JsonException("Not a string");

        public int Count => Type == JsonType.Array ? _array.Count : (Type == JsonType.Object ? _object.Count : 0);
        public IReadOnlyList<JsonValue> Items => Type == JsonType.Array ? _array : (IReadOnlyList<JsonValue>)Array.Empty<JsonValue>();
        public IReadOnlyList<string> Keys => Type == JsonType.Object ? _keyOrder : (IReadOnlyList<string>)Array.Empty<string>();

        public JsonValue this[int index] => Type == JsonType.Array ? _array[index] : throw new JsonException("Not an array");

        public JsonValue this[string key]
        {
            get
            {
                if (Type != JsonType.Object) throw new JsonException("Not an object");
                return _object.TryGetValue(key, out var v) ? v : null;
            }
        }

        public bool Has(string key) => Type == JsonType.Object && _object.ContainsKey(key);

        public bool TryGet(string key, out JsonValue value)
        {
            value = null;
            return Type == JsonType.Object && _object.TryGetValue(key, out value);
        }

        public JsonValue Add(JsonValue item)
        {
            if (Type != JsonType.Array) throw new JsonException("Not an array");
            _array.Add(item ?? Null());
            return this;
        }

        public JsonValue Set(string key, JsonValue value)
        {
            if (Type != JsonType.Object) throw new JsonException("Not an object");
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (!_object.ContainsKey(key)) _keyOrder.Add(key);
            _object[key] = value ?? Null();
            return this;
        }

        public JsonValue Set(string key, string v) => Set(key, String(v));
        public JsonValue Set(string key, float v) => Set(key, Number(v));
        public JsonValue Set(string key, int v) => Set(key, Number(v));
        public JsonValue Set(string key, bool v) => Set(key, Bool(v));

        // ---- Tolerant getters used by codecs (missing/wrong type -> fallback) ----
        public string GetString(string key, string fallback = null)
        {
            var v = this[key];
            return v != null && v.Type == JsonType.String ? v._string : fallback;
        }

        public float GetFloat(string key, float fallback = 0f)
        {
            var v = this[key];
            if (v == null || v.Type != JsonType.Number) return fallback;
            float f = (float)v._number;
            return MathUtil.IsFinite(f) ? f : fallback;
        }

        public int GetInt(string key, int fallback = 0)
        {
            var v = this[key];
            if (v == null || v.Type != JsonType.Number) return fallback;
            double d = v._number;
            if (double.IsNaN(d) || d > int.MaxValue || d < int.MinValue) return fallback;
            return (int)Math.Round(d);
        }

        public bool GetBool(string key, bool fallback = false)
        {
            var v = this[key];
            return v != null && v.Type == JsonType.Bool ? v._bool : fallback;
        }

        public JsonValue GetArray(string key)
        {
            var v = this[key];
            return v != null && v.Type == JsonType.Array ? v : null;
        }

        public JsonValue GetObject(string key)
        {
            var v = this[key];
            return v != null && v.Type == JsonType.Object ? v : null;
        }

        public TEnum GetEnum<TEnum>(string key, TEnum fallback) where TEnum : struct
        {
            var s = GetString(key);
            if (s != null && Enum.TryParse(s, true, out TEnum parsed) && Enum.IsDefined(typeof(TEnum), parsed)) return parsed;
            return fallback;
        }

        public override string ToString() => JsonWriter.Write(this, false);
    }

    public static class JsonParser
    {
        public const int DefaultMaxDepth = 64;
        public const int DefaultMaxLength = 8 * 1024 * 1024;

        public static JsonValue Parse(string text, int maxDepth = DefaultMaxDepth, int maxLength = DefaultMaxLength)
        {
            if (text == null) throw new JsonException("Input is null");
            if (text.Length > maxLength) throw new JsonException("Input too large");
            var p = new Parser(text, maxDepth);
            p.SkipWs();
            var v = p.ParseValue(0);
            p.SkipWs();
            if (!p.End) throw new JsonException($"Unexpected trailing data at {p.Pos}");
            return v;
        }

        public static bool TryParse(string text, out JsonValue value, out string error)
        {
            try
            {
                value = Parse(text);
                error = null;
                return true;
            }
            catch (JsonException e)
            {
                value = null;
                error = e.Message;
                return false;
            }
        }

        private sealed class Parser
        {
            private readonly string _s;
            private readonly int _maxDepth;
            public int Pos;

            public Parser(string s, int maxDepth) { _s = s; _maxDepth = maxDepth; }

            public bool End => Pos >= _s.Length;

            public void SkipWs()
            {
                while (Pos < _s.Length)
                {
                    char c = _s[Pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '﻿') Pos++;
                    else break;
                }
            }

            private char Peek() => Pos < _s.Length ? _s[Pos] : '\0';

            private void Expect(char c)
            {
                if (Peek() != c) throw new JsonException($"Expected '{c}' at {Pos}");
                Pos++;
            }

            public JsonValue ParseValue(int depth)
            {
                if (depth > _maxDepth) throw new JsonException("Max depth exceeded");
                SkipWs();
                char c = Peek();
                switch (c)
                {
                    case '{': return ParseObject(depth);
                    case '[': return ParseArray(depth);
                    case '"': return JsonValue.String(ParseString());
                    case 't': Literal("true"); return JsonValue.Bool(true);
                    case 'f': Literal("false"); return JsonValue.Bool(false);
                    case 'n': Literal("null"); return JsonValue.Null();
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw new JsonException($"Unexpected character '{c}' at {Pos}");
                }
            }

            private void Literal(string lit)
            {
                if (string.CompareOrdinal(_s, Pos, lit, 0, lit.Length) != 0) throw new JsonException($"Invalid literal at {Pos}");
                Pos += lit.Length;
            }

            private JsonValue ParseObject(int depth)
            {
                Expect('{');
                var obj = JsonValue.NewObject();
                SkipWs();
                if (Peek() == '}') { Pos++; return obj; }
                while (true)
                {
                    SkipWs();
                    if (Peek() != '"') throw new JsonException($"Expected key at {Pos}");
                    string key = ParseString();
                    SkipWs();
                    Expect(':');
                    var val = ParseValue(depth + 1);
                    obj.Set(key, val);
                    SkipWs();
                    char c = Peek();
                    if (c == ',') { Pos++; continue; }
                    if (c == '}') { Pos++; return obj; }
                    throw new JsonException($"Expected ',' or '}}' at {Pos}");
                }
            }

            private JsonValue ParseArray(int depth)
            {
                Expect('[');
                var arr = JsonValue.NewArray();
                SkipWs();
                if (Peek() == ']') { Pos++; return arr; }
                while (true)
                {
                    arr.Add(ParseValue(depth + 1));
                    SkipWs();
                    char c = Peek();
                    if (c == ',') { Pos++; continue; }
                    if (c == ']') { Pos++; return arr; }
                    throw new JsonException($"Expected ',' or ']' at {Pos}");
                }
            }

            private string ParseString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    if (End) throw new JsonException("Unterminated string");
                    char c = _s[Pos++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw new JsonException($"Control character in string at {Pos}");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw new JsonException("Unterminated escape");
                    char e = _s[Pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (Pos + 4 > _s.Length) throw new JsonException("Bad unicode escape");
                            if (!int.TryParse(_s.Substring(Pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                throw new JsonException("Bad unicode escape");
                            sb.Append((char)code);
                            Pos += 4;
                            break;
                        default: throw new JsonException($"Invalid escape '\\{e}'");
                    }
                }
            }

            private JsonValue ParseNumber()
            {
                int start = Pos;
                if (Peek() == '-') Pos++;
                while (Pos < _s.Length)
                {
                    char c = _s[Pos];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') Pos++;
                    else break;
                }
                string token = _s.Substring(start, Pos - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsNaN(d) || double.IsInfinity(d))
                    throw new JsonException($"Invalid number '{token}'");
                return JsonValue.Number(d);
            }
        }
    }

    public static class JsonWriter
    {
        public static string Write(JsonValue value, bool pretty = true)
        {
            var sb = new StringBuilder(256);
            WriteValue(sb, value, pretty, 0);
            return sb.ToString();
        }

        private static void Indent(StringBuilder sb, int level)
        {
            sb.Append('\n');
            sb.Append(' ', level * 2);
        }

        private static void WriteValue(StringBuilder sb, JsonValue v, bool pretty, int level)
        {
            if (v == null) { sb.Append("null"); return; }
            switch (v.Type)
            {
                case JsonType.Null: sb.Append("null"); break;
                case JsonType.Bool: sb.Append(v.AsBool ? "true" : "false"); break;
                case JsonType.Number:
                    double d = v.AsDouble;
                    if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append('0');
                    else if (Math.Abs(d % 1) < double.Epsilon && Math.Abs(d) < 1e15) sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
                    else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case JsonType.String: WriteString(sb, v.AsString); break;
                case JsonType.Array:
                    sb.Append('[');
                    for (int i = 0; i < v.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (pretty) Indent(sb, level + 1);
                        WriteValue(sb, v[i], pretty, level + 1);
                    }
                    if (pretty && v.Count > 0) Indent(sb, level);
                    sb.Append(']');
                    break;
                case JsonType.Object:
                    sb.Append('{');
                    var keys = v.Keys;
                    for (int i = 0; i < keys.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (pretty) Indent(sb, level + 1);
                        WriteString(sb, keys[i]);
                        sb.Append(pretty ? ": " : ":");
                        WriteValue(sb, v[keys[i]], pretty, level + 1);
                    }
                    if (pretty && keys.Count > 0) Indent(sb, level);
                    sb.Append('}');
                    break;
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}

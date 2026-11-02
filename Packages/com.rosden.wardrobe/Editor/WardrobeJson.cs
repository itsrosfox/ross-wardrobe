using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// A small json tree for editing Ros' Wardrobe's json files without losing what the editor doesn't know about: objects
    /// keep their key order, numbers keep how they were written, and <see cref="Write"/> lays the file out like Python's
    /// <c>json.dumps(indent=4, ensure_ascii=False)</c> (what the pack generators write), so untouched files don't change.
    /// Values are <see cref="JsonObject"/>, <c>List&lt;object&gt;</c>, string, <see cref="JsonNumber"/>, bool or null.
    /// </summary>
    public static class WardrobeJson {
        public static JsonObject Parse(string text) {
            var reader = new Reader(text);
            reader.Skip();
            var value = reader.Value();
            reader.Skip();
            if (!reader.End) throw reader.Error("unexpected text after the end");
            return value as JsonObject ?? throw new FormatException("The file isn't a json object");
        }

        public static string Write(object value) {
            var sb = new StringBuilder();
            Write(sb, value, 0);
            return sb.Append('\n').ToString();
        }

        private static void Write(StringBuilder sb, object value, int depth) {
            switch (value) {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: Quote(sb, s); break;
                case JsonNumber n: sb.Append(n.Raw); break;
                case JsonObject o:
                    if (o.Count == 0) { sb.Append("{}"); break; }
                    sb.Append('{');
                    for (int i = 0; i < o.Count; i++) {
                        sb.Append(i == 0 ? "\n" : ",\n").Append(' ', (depth + 1) * 4);
                        Quote(sb, o.Keys[i]);
                        sb.Append(": ");
                        Write(sb, o.Values[i], depth + 1);
                    }
                    sb.Append('\n').Append(' ', depth * 4).Append('}');
                    break;
                case List<object> a:
                    if (a.Count == 0) { sb.Append("[]"); break; }
                    sb.Append('[');
                    for (int i = 0; i < a.Count; i++) {
                        sb.Append(i == 0 ? "\n" : ",\n").Append(' ', (depth + 1) * 4);
                        Write(sb, a[i], depth + 1);
                    }
                    sb.Append('\n').Append(' ', depth * 4).Append(']');
                    break;
                default: throw new ArgumentException($"Can't write a {value.GetType().Name} as json");
            }
        }

        private static void Quote(StringBuilder sb, string s) {
            sb.Append('"');
            foreach (char c in s) {
                switch (c) {
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

        private class Reader {
            private readonly string _s;
            private int _i;

            public Reader(string s) => _s = s ?? "";

            public bool End => _i >= _s.Length;

            public FormatException Error(string what) {
                int line = 1 + _s.Take(Math.Min(_i, _s.Length)).Count(c => c == '\n');
                return new FormatException($"Line {line}: {what}");
            }

            public void Skip() {
                while (_i < _s.Length && (char.IsWhiteSpace(_s[_i]) || _s[_i] == '﻿')) _i++;
            }

            public object Value() {
                if (End) throw Error("unexpected end of file");
                char c = _s[_i];
                if (c == '{') return Object();
                if (c == '[') return Array();
                if (c == '"') return String();
                if (c == '-' || char.IsDigit(c)) return Number();
                if (Word("true")) return true;
                if (Word("false")) return false;
                if (Word("null")) return null;
                throw Error($"unexpected '{c}'");
            }

            private bool Word(string w) {
                if (string.CompareOrdinal(_s, _i, w, 0, w.Length) != 0) return false;
                _i += w.Length;
                return true;
            }

            private void Expect(char c) {
                Skip();
                if (End || _s[_i] != c) throw Error($"expected '{c}'");
                _i++;
            }

            private JsonObject Object() {
                var o = new JsonObject();
                _i++;
                Skip();
                if (!End && _s[_i] == '}') { _i++; return o; }
                while (true) {
                    Skip();
                    if (End || _s[_i] != '"') throw Error("expected a key");
                    string key = String();
                    Expect(':');
                    Skip();
                    o[key] = Value();
                    Skip();
                    if (!End && _s[_i] == ',') { _i++; continue; }
                    Expect('}');
                    return o;
                }
            }

            private List<object> Array() {
                var a = new List<object>();
                _i++;
                Skip();
                if (!End && _s[_i] == ']') { _i++; return a; }
                while (true) {
                    Skip();
                    a.Add(Value());
                    Skip();
                    if (!End && _s[_i] == ',') { _i++; continue; }
                    Expect(']');
                    return a;
                }
            }

            private string String() {
                var sb = new StringBuilder();
                _i++;
                while (true) {
                    if (End) throw Error("unterminated string");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw Error("unterminated string");
                    char e = _s[_i++];
                    switch (e) {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw Error("bad \\u escape");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber));
                            _i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
            }

            private JsonNumber Number() {
                int start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                string raw = _s.Substring(start, _i - start);
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) throw Error($"bad number '{raw}'");
                return new JsonNumber(raw);
            }
        }
    }

    /// <summary>A json number, kept as written.</summary>
    public sealed class JsonNumber {
        public readonly string Raw;

        public JsonNumber(string raw) => Raw = raw;

        public JsonNumber(double value) =>
            Raw = Math.Abs(value % 1) < 1e-9 && Math.Abs(value) < 1e15 ? ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture)
                : value.ToString("R", CultureInfo.InvariantCulture);

        public double Value => double.Parse(Raw, NumberStyles.Float, CultureInfo.InvariantCulture);

        public override string ToString() => Raw;
    }

    /// <summary>A json object that keeps its keys in order.</summary>
    public sealed class JsonObject {
        public readonly List<string> Keys = new List<string>();
        public readonly List<object> Values = new List<object>();

        public int Count => Keys.Count;

        public bool Has(string key) => Keys.IndexOf(key) >= 0;

        /// <summary>Getting a missing key gives null; setting one adds it at the end (or where <see cref="Insert"/> puts it).</summary>
        public object this[string key] {
            get {
                int i = Keys.IndexOf(key);
                return i < 0 ? null : Values[i];
            }
            set {
                int i = Keys.IndexOf(key);
                if (i >= 0) Values[i] = value;
                else {
                    Keys.Add(key);
                    Values.Add(value);
                }
            }
        }

        /// <summary>Sets a key, adding it after <paramref name="after"/> (the first of them the object has) when it's new.</summary>
        public void Insert(string key, object value, params string[] after) {
            if (Has(key)) {
                this[key] = value;
                return;
            }
            int at = Keys.Count;
            foreach (string a in after) {
                int i = Keys.IndexOf(a);
                if (i >= 0) {
                    at = i + 1;
                    break;
                }
            }
            Keys.Insert(at, key);
            Values.Insert(at, value);
        }

        /// <summary>
        /// Sets a key; a new one goes where <paramref name="order"/> (the usual key order) puts it among the keys already
        /// there, or at the end when the order doesn't name it.
        /// </summary>
        public void Put(string key, object value, string[] order) {
            if (Has(key)) {
                this[key] = value;
                return;
            }
            int at = System.Array.IndexOf(order, key);
            if (at >= 0) {
                for (int i = at - 1; i >= 0; i--) {
                    int k = Keys.IndexOf(order[i]);
                    if (k < 0) continue;
                    Keys.Insert(k + 1, key);
                    Values.Insert(k + 1, value);
                    return;
                }
                for (int i = at + 1; i < order.Length; i++) {
                    int k = Keys.IndexOf(order[i]);
                    if (k < 0) continue;
                    Keys.Insert(k, key);
                    Values.Insert(k, value);
                    return;
                }
            }
            this[key] = value;
        }

        public bool Remove(string key) {
            int i = Keys.IndexOf(key);
            if (i < 0) return false;
            Keys.RemoveAt(i);
            Values.RemoveAt(i);
            return true;
        }

        /// <summary>Renames a key in place (doing nothing when it's missing; replacing <paramref name="to"/> if both exist).</summary>
        public bool Rename(string from, string to) {
            int i = Keys.IndexOf(from);
            if (i < 0) return false;
            Remove(to);
            i = Keys.IndexOf(from);
            Keys[i] = to;
            return true;
        }

        public string String(string key, string fallback = "") => this[key] as string ?? fallback;

        public double Number(string key, double fallback = 0) => this[key] is JsonNumber n ? n.Value : fallback;

        public int Int(string key, int fallback = 0) => this[key] is JsonNumber n ? (int)Math.Round(n.Value) : fallback;

        public bool Bool(string key, bool fallback = false) => this[key] is bool b ? b : fallback;

        public JsonObject Object(string key) => this[key] as JsonObject;

        public List<object> Array(string key) => this[key] as List<object>;

        /// <summary>The array's objects (an empty list when it's missing).</summary>
        public IEnumerable<JsonObject> Objects(string key) => (Array(key) ?? new List<object>()).OfType<JsonObject>();
    }
}

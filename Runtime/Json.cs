// Minimal, dependency-free JSON parser/writer for the PermitCore Unity SDK.
//
// Unity ships JsonUtility, but it requires [Serializable] classes with public fields, has no
// Dictionary<string,object> support (needed for LicenseResult.CustomFields / meter's arbitrary
// `meta`), and silently drops/mishandles null for reference-type fields — all a poor fit for a
// wire protocol with several optional/nullable fields. Newtonsoft.Json (via Unity's own
// com.unity.nuget.newtonsoft-json package) would work but is an external package dependency this
// SDK deliberately avoids, matching the C++ SDK's "no JSON library — minimal header-only API"
// precedent for the same zero-dependency reasoning. Parses to a plain object tree
// (Dictionary<string,object> / List<object> / string / double / bool / null) — good enough for a
// handful of small, flat-ish request/response shapes, not a general-purpose JSON library.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PermitCore
{
    internal static class Json
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int i = 0;
            var value = ParseValue(json, ref i);
            return value;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON.");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't':
                    Expect(s, ref i, "true");
                    return true;
                case 'f':
                    Expect(s, ref i, "false");
                    return false;
                case 'n':
                    Expect(s, ref i, "null");
                    return null;
                default:
                    return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var dict = new Dictionary<string, object>();
            i++; // consume '{'
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return dict; }

            while (true)
            {
                SkipWhitespace(s, ref i);
                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("Expected ':' in JSON object.");
                i++;
                var value = ParseValue(s, ref i);
                dict[key] = value;
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated JSON object.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; break; }
                throw new FormatException("Expected ',' or '}' in JSON object.");
            }
            return dict;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // consume '['
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }

            while (true)
            {
                var value = ParseValue(s, ref i);
                list.Add(value);
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated JSON array.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; break; }
                throw new FormatException("Expected ',' or ']' in JSON array.");
            }
            return list;
        }

        private static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("Expected '\"' to start a JSON string.");
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("Unterminated JSON string.");
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\')
                {
                    if (i >= s.Length) throw new FormatException("Unterminated JSON escape.");
                    char esc = s[i++];
                    switch (esc)
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
                            if (i + 4 > s.Length) throw new FormatException("Truncated \\u escape.");
                            int code = int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            sb.Append((char)code);
                            i += 4;
                            break;
                        default: throw new FormatException("Unknown JSON escape \\" + esc);
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
            string num = s.Substring(start, i - start);
            return double.Parse(num, CultureInfo.InvariantCulture);
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static void Expect(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || s.Substring(i, literal.Length) != literal)
                throw new FormatException("Expected literal '" + literal + "' in JSON.");
            i += literal.Length;
        }

        // ── Writer ───────────────────────────────────────────────────────────────────────────
        // Only needs to serialize the small, flat-ish request bodies this SDK sends — a plain
        // Dictionary<string, object> tree (values: string, bool, int/long/double, null, nested
        // Dictionary<string, object>, or IEnumerable<object>).

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int or long:
                    sb.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                    break;
                case float or double:
                    sb.Append(Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                    break;
                case IDictionary<string, object> dict:
                    WriteObject(sb, dict);
                    break;
                case System.Collections.IEnumerable enumerable:
                    WriteArray(sb, enumerable);
                    break;
                default:
                    WriteString(sb, value.ToString());
                    break;
            }
        }

        private static void WriteObject(StringBuilder sb, IDictionary<string, object> dict)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kv in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteString(sb, kv.Key);
                sb.Append(':');
                WriteValue(sb, kv.Value);
            }
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, System.Collections.IEnumerable items)
        {
            sb.Append('[');
            bool first = true;
            foreach (var item in items)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteValue(sb, item);
            }
            sb.Append(']');
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
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ── Helpers for reading a parsed Dictionary<string, object> safely ─────────────────────

        public static string GetString(IDictionary<string, object> dict, string key)
            => dict != null && dict.TryGetValue(key, out var v) && v is string s ? s : null;

        public static bool GetBool(IDictionary<string, object> dict, string key, bool defaultValue = false)
            => dict != null && dict.TryGetValue(key, out var v) && v is bool b ? b : defaultValue;

        public static int? GetInt(IDictionary<string, object> dict, string key)
            => dict != null && dict.TryGetValue(key, out var v) && v is double d ? (int)d : (int?)null;

        public static Dictionary<string, object> GetObject(IDictionary<string, object> dict, string key)
            => dict != null && dict.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

        public static List<string> GetStringList(IDictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var v) || v is not List<object> list) return null;
            var result = new List<string>(list.Count);
            foreach (var item in list)
                if (item is string s) result.Add(s);
            return result;
        }

        public static Dictionary<string, string> GetStringDictionary(IDictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var v) || v is not Dictionary<string, object> obj) return null;
            var result = new Dictionary<string, string>();
            foreach (var kv in obj)
                result[kv.Key] = kv.Value?.ToString();
            return result;
        }
    }
}

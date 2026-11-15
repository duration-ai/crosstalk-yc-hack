using System.Globalization;
using System.Text;

namespace Dogpatch.Operator
{
    // Just enough JSON for the Convex envelopes: pull one top-level field out of an object as raw
    // JSON text (so JsonUtility can parse the "value" of a response into a typed class), and quote
    // strings for hand-built envelopes.
    public static class JsonText
    {
        public static string Field(string json, string name)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int i = 0;
            Skip(json, ref i);
            if (i >= json.Length || json[i] != '{') return null;
            i++;
            while (true)
            {
                Skip(json, ref i);
                if (i >= json.Length || json[i] == '}') return null;
                if (json[i] == ',') { i++; continue; }
                int keyStart = i;
                SkipValue(json, ref i);
                string key = Unquote(json.Substring(keyStart, i - keyStart));
                Skip(json, ref i);
                if (i >= json.Length || json[i] != ':') return null;
                i++;
                Skip(json, ref i);
                int valueStart = i;
                SkipValue(json, ref i);
                if (key == name) return json.Substring(valueStart, i - valueStart);
            }
        }

        public static string Unquote(string raw)
        {
            if (raw == null || raw.Length < 2 || raw[0] != '"') return raw;
            var sb = new StringBuilder();
            for (int i = 1; i < raw.Length - 1; i++)
            {
                char c = raw[i];
                if (c != '\\') { sb.Append(c); continue; }
                char e = raw[++i];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(raw.Substring(i + 1, 4), NumberStyles.HexNumber)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }

        public static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
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
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static void SkipValue(string s, ref int i)
        {
            if (i >= s.Length) return;
            char c = s[i];
            if (c == '"')
            {
                i++;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\') i++;
                    i++;
                }
                i++;
                return;
            }
            if (c == '{' || c == '[')
            {
                int depth = 0;
                while (i < s.Length)
                {
                    char d = s[i];
                    if (d == '"') { SkipValue(s, ref i); continue; }
                    if (d == '{' || d == '[') depth++;
                    else if (d == '}' || d == ']')
                    {
                        depth--;
                        if (depth == 0) { i++; return; }
                    }
                    i++;
                }
                return;
            }
            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']' && !char.IsWhiteSpace(s[i])) i++;
        }
    }
}

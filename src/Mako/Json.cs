using System.Text;
using System.Globalization;

namespace Mako;

/// Minimal JSON encoder/decoder over MAKO's runtime value types:
/// string | double | bool | null | List&lt;object?&gt; | Dictionary&lt;string, object?&gt;
static class Json
{
    public static string Encode(object? value)
    {
        var sb = new StringBuilder();
        EncodeValue(value, sb);
        return sb.ToString();
    }

    private static void EncodeValue(object? v, StringBuilder sb)
    {
        switch (v)
        {
            case null:
                sb.Append("null");
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case double d:
                sb.Append(d == Math.Floor(d) && !double.IsInfinity(d) && Math.Abs(d) < 1e15
                    ? ((long)d).ToString(CultureInfo.InvariantCulture)
                    : d.ToString("G17", CultureInfo.InvariantCulture));
                break;
            case string s:
                EncodeString(s, sb);
                break;
            case List<object?> list:
                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    EncodeValue(list[i], sb);
                }
                sb.Append(']');
                break;
            case Dictionary<string, object?> dict:
                sb.Append('{');
                bool first = true;
                foreach (var (k, val) in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    EncodeString(k, sb);
                    sb.Append(':');
                    EncodeValue(val, sb);
                }
                sb.Append('}');
                break;
            default:
                EncodeString(v.ToString() ?? "", sb);
                break;
        }
    }

    private static void EncodeString(string s, StringBuilder sb)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    if (c < 0x20) sb.Append($"\\u{(int)c:x4}");
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    public static object? Decode(string text)
    {
        int i = 0;
        var result = ParseValue(text, ref i);
        SkipWs(text, ref i);
        if (i != text.Length) throw new FormatException($"unexpected trailing content at {i}");
        return result;
    }

    private static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    private static object? ParseValue(string s, ref int i)
    {
        SkipWs(s, ref i);
        if (i >= s.Length) throw new FormatException("unexpected end of input");

        return s[i] switch
        {
            '{' => ParseObject(s, ref i),
            '[' => ParseArray(s, ref i),
            '"' => ParseString(s, ref i),
            't' => ParseLiteral(s, ref i, "true", true),
            'f' => ParseLiteral(s, ref i, "false", false),
            'n' => ParseLiteral(s, ref i, "null", null),
            _   => ParseNumber(s, ref i),
        };
    }

    private static object ParseLiteral(string s, ref int i, string lit, object? value)
    {
        if (i + lit.Length > s.Length || s.Substring(i, lit.Length) != lit)
            throw new FormatException($"invalid literal at {i}");
        i += lit.Length;
        return value!;
    }

    private static Dictionary<string, object?> ParseObject(string s, ref int i)
    {
        var dict = new Dictionary<string, object?>();
        i++; // {
        SkipWs(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return dict; }
        while (true)
        {
            SkipWs(s, ref i);
            var key = ParseString(s, ref i);
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != ':') throw new FormatException($"expected ':' at {i}");
            i++;
            dict[key] = ParseValue(s, ref i);
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == '}') { i++; break; }
            throw new FormatException($"expected ',' or '}}' at {i}");
        }
        return dict;
    }

    private static List<object?> ParseArray(string s, ref int i)
    {
        var list = new List<object?>();
        i++; // [
        SkipWs(s, ref i);
        if (i < s.Length && s[i] == ']') { i++; return list; }
        while (true)
        {
            list.Add(ParseValue(s, ref i));
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == ']') { i++; break; }
            throw new FormatException($"expected ',' or ']' at {i}");
        }
        return list;
    }

    private static string ParseString(string s, ref int i)
    {
        if (s[i] != '"') throw new FormatException($"expected string at {i}");
        i++;
        var sb = new StringBuilder();
        while (i < s.Length && s[i] != '"')
        {
            if (s[i] == '\\')
            {
                i++;
                if (i >= s.Length) throw new FormatException("unterminated escape");
                switch (s[i])
                {
                    case '"':  sb.Append('"');  break;
                    case '\\': sb.Append('\\'); break;
                    case '/':  sb.Append('/');  break;
                    case 'n':  sb.Append('\n'); break;
                    case 'r':  sb.Append('\r'); break;
                    case 't':  sb.Append('\t'); break;
                    case 'b':  sb.Append('\b'); break;
                    case 'f':  sb.Append('\f'); break;
                    case 'u':
                        var hex = s.Substring(i + 1, 4);
                        sb.Append((char)Convert.ToInt32(hex, 16));
                        i += 4;
                        break;
                    default: throw new FormatException($"bad escape at {i}");
                }
                i++;
            }
            else
            {
                sb.Append(s[i]);
                i++;
            }
        }
        if (i >= s.Length) throw new FormatException("unterminated string");
        i++; // closing "
        return sb.ToString();
    }

    private static double ParseNumber(string s, ref int i)
    {
        int start = i;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] is '.' or 'e' or 'E' or '+' or '-')) i++;
        if (i == start) throw new FormatException($"invalid number at {i}");
        return double.Parse(s[start..i], CultureInfo.InvariantCulture);
    }
}

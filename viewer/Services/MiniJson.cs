using System.Globalization;
using System.Text;

namespace ClxViewer.Services;

/// <summary>
/// Minimal JSON parser for the metadata document produced by clxcpp's to_json().
/// Parses objects, arrays, strings, numbers, booleans and null into plain
/// CLR types (Dictionary&lt;string,object?&gt;, List&lt;object?&gt;, string,
/// double, bool, null) so the project needs no System.Text.Json dependency.
/// </summary>
internal static class MiniJson
{
    public static Dictionary<string, object?>? Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var p = new Parser(json);
        return p.ParseValue() as Dictionary<string, object?>;
    }

    public static string? Str(object? node) => node as string;

    public static long Num(object? node) => node is double d ? (long)d : 0;

    public static bool Bool(object? node) => node is bool b && b;

    public static Dictionary<string, object?>? Obj(object? node) => node as Dictionary<string, object?>;

    public static List<object?>? Arr(object? node) => node as List<object?>;

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;

        public Parser(string s) { _s = s; }

        public object? ParseValue()
        {
            SkipWs();
            if (_i >= _s.Length) return null;
            switch (_s[_i])
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return ParseString();
                case 't': _i += 4; return true;
                case 'f': _i += 5; return false;
                case 'n': _i += 4; return null;
                default: return ParseNumber();
            }
        }

        private Dictionary<string, object?> ParseObject()
        {
            var d = new Dictionary<string, object?>(StringComparer.Ordinal);
            _i++; // {
            SkipWs();
            if (_i < _s.Length && _s[_i] == '}') { _i++; return d; }
            while (_i < _s.Length)
            {
                SkipWs();
                string key = ParseString();
                SkipWs();
                if (_i < _s.Length && _s[_i] == ':') _i++;
                SkipWs();
                d[key] = ParseValue();
                SkipWs();
                if (_i >= _s.Length) break;
                char c = _s[_i++];
                if (c == '}') break;
            }
            return d;
        }

        private List<object?> ParseArray()
        {
            var list = new List<object?>();
            _i++; // [
            SkipWs();
            if (_i < _s.Length && _s[_i] == ']') { _i++; return list; }
            while (_i < _s.Length)
            {
                SkipWs();
                list.Add(ParseValue());
                SkipWs();
                if (_i >= _s.Length) break;
                char c = _s[_i++];
                if (c == ']') break;
            }
            return list;
        }

        private string ParseString()
        {
            _i++; // opening quote
            var sb = new StringBuilder();
            while (_i < _s.Length)
            {
                char c = _s[_i++];
                if (c == '"') break;
                if (c == '\\' && _i < _s.Length)
                {
                    char e = _s[_i++];
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
                            if (_i + 4 <= _s.Length)
                            {
                                sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber));
                                _i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private double ParseNumber()
        {
            int start = _i;
            while (_i < _s.Length &&
                   (char.IsDigit(_s[_i]) || _s[_i] == '-' || _s[_i] == '+' ||
                    _s[_i] == '.' || _s[_i] == 'e' || _s[_i] == 'E'))
            {
                _i++;
            }
            return double.Parse(_s.Substring(start, _i - start), CultureInfo.InvariantCulture);
        }

        private void SkipWs()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }
    }
}

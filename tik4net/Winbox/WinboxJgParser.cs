using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using tik4net.Diagnostics;

namespace tik4net.Winbox
{
    /// <summary>
    /// A tolerant JavaScript-object-literal parser for the <c>.jg</c> catalogs (a port of <c>jg_analyze.py</c>'s
    /// <c>JgParser</c>): nested <c>Dictionary&lt;string,object&gt;</c> / <c>List&lt;object&gt;</c> / <c>string</c> /
    /// <c>int</c>. Used by <see cref="WinboxJgCatalog"/>.
    /// </summary>
    internal sealed class WinboxJgParser
    {
        private readonly string _s;
        private int _i;
        private readonly int _n;

        internal WinboxJgParser(string text) { _s = text ?? ""; _n = _s.Length; }

        internal object Parse() { Ws(); return Value(); }

        private void Ws() { while (_i < _n && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\r' || _s[_i] == '\n')) _i++; }

        private object Value()
        {
            Ws();
            if (_i >= _n) return null!; // only on malformed/truncated input; callers pattern-match the result and tolerate it
            char c = _s[_i];
            if (c == '{') return Obj();
            if (c == '[') return Arr();
            if (c == '\'' || c == '"') return Str();
            return Scalar();
        }

        private Dictionary<string, object> Obj()
        {
            _i++; // {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            Ws();
            if (_i < _n && _s[_i] == '}') { _i++; return d; }
            while (_i < _n)
            {
                Ws();
                string key;
                if (_i < _n && (_s[_i] == '\'' || _s[_i] == '"'))
                    key = Str();
                else
                {
                    int start = _i;
                    while (_i < _n && ":,}]".IndexOf(_s[_i]) < 0 && !char.IsWhiteSpace(_s[_i])) _i++;
                    key = _s.Substring(start, _i - start);
                }
                Ws();
                if (_i >= _n || _s[_i] != ':') throw new FormatException("expected ':'");
                _i++;
                object val = Value();
                d[key] = val; // last duplicate wins (we only read scalar leaves we care about)
                Ws();
                if (_i < _n && _s[_i] == ',')
                {
                    _i++; Ws();
                    if (_i < _n && _s[_i] == '}') { _i++; return d; } // trailing comma
                    continue;
                }
                if (_i < _n && _s[_i] == '}') { _i++; return d; }
                throw new FormatException("obj parse error");
            }
            return d;
        }

        private List<object> Arr()
        {
            _i++; // [
            var a = new List<object>();
            Ws();
            if (_i < _n && _s[_i] == ']') { _i++; return a; }
            while (_i < _n)
            {
                a.Add(Value());
                Ws();
                if (_i < _n && _s[_i] == ',')
                {
                    _i++; Ws();
                    if (_i < _n && _s[_i] == ']') { _i++; return a; }
                    continue;
                }
                if (_i < _n && _s[_i] == ']') { _i++; return a; }
                throw new FormatException("arr parse error");
            }
            return a;
        }

        private string Str()
        {
            char q = _s[_i]; _i++;
            var sb = new StringBuilder();
            while (_i < _n)
            {
                char c = _s[_i];
                if (c == '\\') { if (_i + 1 < _n) sb.Append(_s[_i + 1]); _i += 2; continue; }
                if (c == q) { _i++; return sb.ToString(); }
                sb.Append(c); _i++;
            }
            throw new FormatException("unterminated string");
        }

        private object Scalar()
        {
            int start = _i;
            while (_i < _n && ",}]".IndexOf(_s[_i]) < 0 && !char.IsWhiteSpace(_s[_i])) _i++;
            string tok = _s.Substring(start, _i - start);
            if (Regex.IsMatch(tok, "^-?\\d+$") && int.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
                return iv;
            return tok;
        }
    }
}

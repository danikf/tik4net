using System;
using System.Collections;
using System.Collections.Generic;

namespace tik4net.Winbox
{
    /// <summary>
    /// A <c>.jg</c> <c>type:'cond'</c> — the condition a field names with <c>on:</c> — evaluated against one M2
    /// record. A field whose condition is false on a row is a field RouterOS does not report for that row.
    /// </summary>
    /// <remarks>
    /// <para>The <c>.jg</c> cannot say which conditions RouterOS honours, and most it does not. webfig's list
    /// renders no cell for a column whose condition is false (<c>types.def.cell</c>), but the API decides per
    /// field, and a condition of identical shape goes either way: <c>/ip/hotspot/profile</c> with
    /// <c>use-radius=no</c> prints none of the fields on <c>on:'radius'</c> (a bool), while
    /// <c>/interface/l2tp-server/server</c> with <c>use-ipsec=no</c> still prints <c>ipsec-secret</c> on
    /// <c>on:'ipsec'</c> (a bool). Neither <c>hide</c> nor <c>hidedynamicly</c> separates them — honouring
    /// either dropped fields the API prints on six paths (7.24.4). So only the conditions in
    /// <see cref="HonouredByTheApi"/> are acted on, each measured both ways on a live router: the API omits the
    /// field while the M2 record carries a real value, and prints it once the condition holds.</para>
    /// <para>A condition is <c>c:[{on:label, pred}]</c>, all clauses required. <c>on</c> names a field of the
    /// same window, a nonpublic one included (<c>autoneg</c>); a missing value reads as that field's
    /// <c>def</c>. The predicates are webfig's <c>pred.*.isTrue</c>.</para>
    /// <para>The evaluation is three-valued, and only a definite <c>false</c> hides a field. A clause whose
    /// predicate depends on something outside the row (<c>addon</c>, <c>board</c>, …), on a field this catalog
    /// did not resolve, or on a predicate kind not ported here, is <c>unknown</c> — and an unknown condition
    /// keeps the field, because dropping a value the router did report is the worse error.</para>
    /// </remarks>
    internal sealed class WinboxJgCondition
    {
        private readonly List<Clause> _clauses;
        internal WinboxJgCondition? OrOn { get; set; }

        /// <summary>The condition's name in its window (<c>radius</c>, <c>noautoneg</c>).</summary>
        internal string Name { get; }

        private WinboxJgCondition(string name, List<Clause> clauses)
        {
            Name = name;
            _clauses = clauses;
        }

        private sealed class Clause
        {
            internal bool Resolved;         // false: `on` named a field the catalog does not know
            internal bool HasOn;
            internal int Key;
            internal long? Def;
            internal Dictionary<string, object>? Pred;
        }

        /// <summary>
        /// Builds the condition from its <c>.jg</c> node, or returns <c>null</c> for one this decode does not act
        /// on — any not in <see cref="HonouredByTheApi"/>.
        /// </summary>
        /// <param name="name">The condition's name in its window.</param>
        /// <param name="node">The <c>type:'cond'</c> node.</param>
        /// <param name="fieldOf">The window's field for an <c>on</c> label; <c>null</c> when it has none.</param>
        internal static WinboxJgCondition? From(string name, Dictionary<string, object> node,
            Func<string, WinboxJgField?> fieldOf)
        {
            if (!(node.TryGetValue("c", out var cv) && cv is List<object> list) || list.Count == 0) return null;
            string firstOn = list.Count > 0 && list[0] is Dictionary<string, object> c0
                             && c0.TryGetValue("on", out var o0) && o0 is string s0 ? s0 : "";
            if (!HonouredByTheApi.Contains(name + "|" + firstOn)) return null;
            if (OrOnName(node) != null) return null;   // none of the measured ones has a fallback
            var clauses = new List<Clause>();
            foreach (var item in list)
            {
                if (!(item is Dictionary<string, object> c)) continue;
                var clause = new Clause { Pred = c.TryGetValue("pred", out var pv) ? pv as Dictionary<string, object> : null };
                if (c.TryGetValue("on", out var onv))
                {
                    clause.HasOn = true;
                    // An `on` that is an array reaches into another record; webfig itself answers true there.
                    if (onv is string label && fieldOf(label) is WinboxJgField f)
                    {
                        clause.Resolved = true;
                        clause.Key = f.Key;
                        clause.Def = f.Def;
                    }
                }
                clauses.Add(clause);
            }
            return clauses.Count == 0 ? null : new WinboxJgCondition(name, clauses);
        }

        /// <summary>
        /// The conditions RouterOS's API is measured to honour, as <c>name|label its first clause reads</c> — the
        /// label keeps a same-named condition of another window out. Measured on 7.24.4, both ways (see the
        /// class remarks); add one only with the same two observations.
        /// </summary>
        private static readonly HashSet<string> HonouredByTheApi = new HashSet<string>(StringComparer.Ordinal)
        {
            "noautoneg|autoneg",        // /interface/ethernet speed, full-duplex
            "radius|Use RADIUS",        // /ip/hotspot/profile radius-*, nas-port-type
            "account|Use RADIUS",       // /ip/hotspot/profile radius-interim-update
            "trial|Login By",           // /ip/hotspot/profile trial-*
            "mac|Login By",             // /ip/hotspot/profile mac-auth-mode
            "macauth|Login By",         // /ip/hotspot/profile mac-auth-password
            "bsd|Remote Log Format",    // /system/logging/action syslog-facility, syslog-severity
            "cef|Remote Log Format",    // /system/logging/action cef-event-delimiter
            "timestamp|Remote Log Format", // /system/logging/action syslog-time-format
            "tls|Remote Log Protocol",  // /system/logging/action check-certificate
            "bsd|BSD Syslog",           // the same, in the catalogs before remote-log-format replaced the bool
        };

        /// <summary>The name of the condition this one's <c>oron</c> falls back to, if any.</summary>
        internal static string? OrOnName(Dictionary<string, object> node)
            => node.TryGetValue("oron", out var v) ? v as string : null;

        /// <summary>
        /// True only when the record DEFINITELY fails the condition — see the remarks on the class for why an
        /// unknown answer does not count.
        /// </summary>
        internal bool IsFalseOn(IReadOnlyDictionary<int, Tuple<string, object>> rec)
            => Evaluate(rec, 0) == false;

        private bool? Evaluate(IReadOnlyDictionary<int, Tuple<string, object>> rec, int depth)
        {
            bool? own = true;
            foreach (var c in _clauses)
            {
                bool? r;
                if (c.HasOn && !c.Resolved) r = null;
                else if (!c.HasOn) r = IsTrue(c.Pred, NoValue);
                else
                {
                    object? val = rec.TryGetValue(c.Key, out var t) && t?.Item2 != null ? t.Item2
                                : c.Def.HasValue ? c.Def.Value : null;
                    r = IsTrue(c.Pred, val);
                }
                if (r == false) { own = false; break; }
                if (r == null) own = null;
            }
            if (own == true) return true;
            // A cycle of oron references would be a catalog defect; it must not become a stack overflow.
            bool? other = OrOn != null && depth < 8 ? OrOn.Evaluate(rec, depth + 1) : false;
            if (other == true) return true;
            if (own == false && other == false) return false;
            return null;
        }

        // A clause with no `on` is about the router, not the row (addon, board, syscap, quickset): unknown.
        private static readonly object NoValue = new object();

        // webfig's pred.<type>.isTrue, for the kinds a row can answer. Anything else is unknown.
        private static bool? IsTrue(Dictionary<string, object>? pred, object? val)
        {
            if (pred == null || !(pred.TryGetValue("type", out var tv) && tv is string type)) return null;
            if (ReferenceEquals(val, NoValue) && type != "not" && type != "or") return null;
            switch (type)
            {
                case "bool":
                {
                    // attrs.value ? !!val : !val
                    bool? truthy = Truthy(val);
                    if (truthy == null) return null;
                    return IsSet(pred, "value") ? truthy.Value : !truthy.Value;
                }
                case "number":
                {
                    // val=val||0; an array compares its first element; value is a list of candidates
                    if (!TryNumber(val, out long n)) return null;
                    if (!(pred.TryGetValue("value", out var vv) && vv is List<object> candidates)) return false;
                    foreach (var cand in candidates)
                        if (cand is int ci && ci == n) return true;
                        else if (cand is long cl && cl == n) return true;
                    return false;
                }
                case "bitmap":
                {
                    if (!TryNumber(val, out long n)) return null;
                    long mask = pred.TryGetValue("mask", out var mv) && mv is int mi ? mi : 0;
                    long want = pred.TryGetValue("value", out var wv) && wv is int wi ? wi : 0;
                    return (n & mask) == want;
                }
                case "string":
                {
                    if (val != null && !(val is string)) return null;
                    string want = pred.TryGetValue("value", out var sv) ? sv as string ?? "" : "";
                    return string.Equals((string?)val ?? "", want, StringComparison.Ordinal);
                }
                case "not":
                {
                    bool? inner = IsTrue(pred.TryGetValue("pred", out var ip) ? ip as Dictionary<string, object> : null, val);
                    return inner == null ? (bool?)null : !inner.Value;
                }
                case "or":
                {
                    if (!(pred.TryGetValue("pred", out var ov) && ov is List<object> alternatives)) return null;
                    bool unknown = false;
                    foreach (var alt in alternatives)
                    {
                        bool? r = IsTrue(alt as Dictionary<string, object>, val);
                        if (r == true) return true;
                        if (r == null) unknown = true;
                    }
                    return unknown ? (bool?)null : false;
                }
                default:
                    return null;
            }
        }

        private static bool? Truthy(object? val)
        {
            if (val == null) return false;
            if (val is bool b) return b;
            if (val is string s) return s.Length > 0;
            if (TryNumber(val, out long n)) return n != 0;
            return null;
        }

        private static bool TryNumber(object? val, out long n)
        {
            n = 0;
            if (val == null) return true;               // val||0
            if (val is bool b) { n = b ? 1 : 0; return true; }
            if (val is string) return false;
            if (!(val is byte[]) && val is IEnumerable seq)
            {
                foreach (var first in seq) return first == null || WinboxFieldResolver.TryToInt64(first, out n);
                return true;                            // an empty array: webfig's val[0] is undefined → 0
            }
            return WinboxFieldResolver.TryToInt64(val, out n);
        }

        private static bool IsSet(Dictionary<string, object> node, string attr)
            => node.TryGetValue(attr, out var v) && (v is int i ? i != 0 : v is bool b ? b : v != null);
    }
}

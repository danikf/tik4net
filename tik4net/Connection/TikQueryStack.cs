using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace tik4net.Connection
{
    /// <summary>
    /// Client-side evaluator for RouterOS query (filter) words against a single record. RouterOS queries are
    /// a postfix stack: each <c>?name=value</c> pushes a predicate; <c>?#|</c> pops two and pushes OR,
    /// <c>?#&amp;</c> AND, <c>?#!</c> negates the top; whatever predicates remain on the stack are implicitly
    /// ANDed (no filters → matches everything). Used by transports that cannot push the query to the router
    /// for a given path (WinBox native getall, CLI async list) and must filter the returned rows themselves,
    /// matching RouterOS-side evaluation semantics.
    /// </summary>
    internal static class TikQueryStack
    {
        /// <summary>True when <paramref name="row"/> satisfies the postfix query <paramref name="filters"/>.</summary>
        public static bool Matches(TikRecordSentence row, IReadOnlyList<ITikCommandParameter> filters)
        {
            var stack = new Stack<bool>();
            foreach (var f in filters)
            {
                string name = f.Name;
                if (name == "#|") { bool a = Pop(stack), b = Pop(stack); stack.Push(a || b); }
                else if (name == "#&") { bool a = Pop(stack), b = Pop(stack); stack.Push(a && b); }
                else if (name == "#!") { stack.Push(!Pop(stack)); }
                else if (name.StartsWith("#", StringComparison.Ordinal)) { /* unsupported stack op — leave stack unchanged */ }
                else if (name.StartsWith(".", StringComparison.Ordinal) && name != TikSpecialProperties.Id) { stack.Push(true); }
                else stack.Push(EvalPredicate(row, name, f.Value));
            }
            return stack.All(b => b);
        }

        private static bool Pop(Stack<bool> s) => s.Count > 0 && s.Pop();

        private static bool EvalPredicate(TikRecordSentence row, string name, string value)
        {
            char op = name.Length > 0 ? name[0] : '=';
            string field = (op == '<' || op == '>') ? name.Substring(1) : name;
            bool has = row.TryGetResponseField(field, out var v);
            if (op == '<' || op == '>')
            {
                if (!has) return false;
                // Invariant, not the current culture: RouterOS writes a fixed wire format, so '1.5' is
                // 1.5 on every machine. Parsed with the culture, a decimal point fails to parse on cs-CZ,
                // the comparison falls through to the ordinal string compare below, and '9' > '10' — the
                // same query answering differently depending on the regional settings of the caller.
                if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv)
                    && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dq))
                    return op == '<' ? dv < dq : dv > dq;
                int cmp = string.CompareOrdinal(v, value);
                return op == '<' ? cmp < 0 : cmp > 0;
            }
            // existence (?name) vs equality (?name=value)
            if (string.IsNullOrEmpty(value))
                return has && !string.IsNullOrEmpty(v);
            return has && string.Equals(v, value, StringComparison.Ordinal);
        }
    }
}

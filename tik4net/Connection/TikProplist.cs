using System;
using System.Collections.Generic;
using System.Linq;

namespace tik4net.Connection
{
    /// <summary>
    /// The binary API's <c>.proplist</c> contract, for the transports that read the full field set and trim
    /// the rows themselves: only the listed fields are returned, a name the menu does not have is ignored,
    /// and <c>.id</c> is kept only when listed.
    /// </summary>
    internal static class TikProplist
    {
        /// <summary>The <c>.proplist</c> parameter of a read, or <c>null</c> when it has none.</summary>
        internal static ITikCommandParameter? Find(IEnumerable<ITikCommandParameter> parameters)
            => parameters.FirstOrDefault(p => p.Name == TikSpecialProperties.Proplist);

        /// <summary>Keeps only the fields <paramref name="proplist"/> names, in every row.</summary>
        internal static IList<TikRecordSentence> Trim(IList<TikRecordSentence> rows, string? proplist)
        {
            var wanted = new HashSet<string>(
                (proplist ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()),
                StringComparer.OrdinalIgnoreCase);
            var trimmed = new List<TikRecordSentence>(rows.Count);
            foreach (var row in rows)
            {
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in row.Words)
                    if (wanted.Contains(kv.Key)) fields[kv.Key] = kv.Value;
                trimmed.Add(new TikRecordSentence(fields));
            }
            return trimmed;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace tik4net.Connection
{
    /// <summary>
    /// What the <c>get</c> verb answers with, in one place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>get</c> is a narrowing of a read: one row, optionally one field of it. Only the CLI transports have
    /// a <c>get</c> of their own to send — REST addresses the row by URL and native WinBox reads the window
    /// and filters — so without a shared definition each transport would invent its own answer, which is
    /// exactly what happened: REST refused the verb outright, native WinBox ignored <c>value-name</c> and
    /// returned whichever field sorted first, and the CLI family dropped both inputs and read the menu as a
    /// singleton.
    /// </para>
    /// <para>
    /// The shape this enforces is the binary API's, because it is the one RouterOS itself defines:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>.id</c> + <c>value-name</c> → that field's value.</item>
    ///   <item><c>value-name</c> alone, on a singleton menu → that field's value.</item>
    ///   <item><c>.id</c> alone → the whole row as ONE <c>as-value</c> string
    ///     (<c>.id=*2;name=ether1;…</c>), which is what the API puts in <c>=ret=</c> — not a record. A caller
    ///     who wants a record should use <c>print</c>, which returns one everywhere.</item>
    ///   <item>an id that matches nothing → no rows, which the layer above reports as
    ///     <c>TikNoSuchItemException</c>.</item>
    /// </list>
    /// </remarks>
    internal static class TikGetResult
    {
        /// <summary>
        /// Narrows the rows a full read produced into what <c>get</c> answers with. Rows of any other verb
        /// are returned untouched.
        /// </summary>
        /// <param name="descriptor">The command being run — its verb decides whether anything happens here.</param>
        /// <param name="rows">The rows the transport's ordinary read produced.</param>
        /// <param name="rowAddressedByRouter">The router has already picked the row, as REST's URL makes it do: it may
        /// answer with the row's real <c>.id</c> to a <c>.id</c> that held a name.</param>
        /// <returns>The narrowed result, or <paramref name="rows"/> unchanged.</returns>
        internal static IList<TikRecordSentence> Shape(
            TikCommandDescriptor descriptor, IList<TikRecordSentence> rows, bool rowAddressedByRouter = false)
        {
            if (TikPath.Verb(descriptor.CommandText) != "get")
                return rows;

            string? id = TikParameterFormat.FindInput(descriptor.Parameters, TikSpecialProperties.Id);
            string? valueName = TikParameterFormat.FindInput(descriptor.Parameters, TikSpecialProperties.UnsetValueName);

            // A row that carries no .id was already selected by the transport and must be kept. Only rows that DO
            // carry one are filtered, which is the case native WinBox needs, where the whole window is read.
            if (!string.IsNullOrEmpty(id) && !rowAddressedByRouter)
                rows = rows.Where(r =>
                    !r.TryGetResponseField(TikSpecialProperties.Id, out var rowId)
                    || string.Equals(rowId, id, StringComparison.OrdinalIgnoreCase)).ToList();

            // Leave anything that is not a single row to the layer above: it already distinguishes "nothing
            // matched" from "more than one matched", and both of those are its answers to give, not ours.
            if (rows.Count != 1)
                return rows;

            var row = rows[0];

            if (!string.IsNullOrEmpty(valueName))
            {
                // A field the row does not carry is reported as no rows rather than as an empty value. The
                // row exists, but the answer to "give me this field of it" does not, and an empty string
                // would be indistinguishable from a field the router really did report as empty.
                if (!row.TryGetResponseField(valueName!, out var value))
                    return new List<TikRecordSentence>();

                return One(valueName!, value);
            }

            return One(TikSpecialProperties.Ret,
                string.Join(";", row.Words.Select(w => w.Key + "=" + w.Value).ToArray()));
        }

        /// <summary>
        /// One record of one field. The field is named after what was asked for, which is what makes both
        /// scalar spellings work: <c>ExecuteScalar()</c> reads the row's one field, and
        /// <c>ExecuteScalar("name")</c> asks for it by name.
        /// </summary>
        /// <param name="field">The field name.</param>
        /// <param name="value">Its value.</param>
        internal static IList<TikRecordSentence> One(string field, string value)
            => new List<TikRecordSentence>
            {
                new TikRecordSentence(new Dictionary<string, string> { { field, value } })
            };
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;
using tik4net.Diagnostics;

namespace tik4net.Cli
{
    /// <summary>
    /// The flag fields on RouterOS before 7.20, whose <c>print as-value</c> leaves them out: read by name with a second
    /// print where the router takes <c>proplist=</c>, and as the id list of each flag (<c>find</c>) where it does not
    /// (RouterOS 6). One per connection; what it learns about the router lives in <see cref="CliRouterKnowledge"/>.
    /// </summary>
    internal sealed class CliFlagSupplier
    {
        private readonly ICliCommandHost _host;

        internal CliFlagSupplier(ICliCommandHost host)
        {
            _host = host;
        }

        /// <summary>The flag names each menu knows, per requested list — see <see cref="KnownFlagFieldsAsync"/>.</summary>
        private readonly Dictionary<string, string[]> _knownFlagFields =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// On a router whose <c>print as-value</c> leaves the flag fields out, reads the ones the command names in
        /// <see cref="TikSpecialProperties.CliFlags"/> — the mapper's flag fields, or the fields a caller named in
        /// <c>.proplist</c> — with a second print of the same rows and merges them in.
        /// </summary>
        /// <remarks>
        /// <para>RouterOS before 7.20 prints no flag in <c>as-value</c> — no <c>disabled</c>, <c>dynamic</c>,
        /// <c>running</c> — with or without <c>detail</c>, and a read then maps each of them to its CLR default:
        /// an interface that is up reads <c>Running = false</c>, and nothing says so. Asked for by name, the same
        /// router answers them explicitly <c>true</c> or <c>false</c> (<c>print as-value proplist=running,disabled</c>,
        /// 7.17 and 7.19.6); 7.20 made that the default ("include flags by default when printing to value").</para>
        /// <para>The second print is built like the first — same filter, same windows — so it costs one command per
        /// page, and only on such a router. The CLI's <c>proplist=</c> refuses the whole read when one name is
        /// unknown to the menu, so the names are checked first (<see cref="KnownFlagFieldsAsync"/>); a name the
        /// menu does not have is one the binary API does not send either.</para>
        /// </remarks>
        internal async Task<IList<TikRecordSentence>> SupplyFlagsAsync(
            TikCommandDescriptor descriptor, IList<TikRecordSentence> records, CancellationToken cancellationToken)
        {
            var hint = descriptor.Parameters.FirstOrDefault(p => p.Name == TikSpecialProperties.CliFlags);
            if (hint == null || records.Count == 0)
                return records;

            // Only names no row carries: on 7.20 and later, or for a field the plain read already has, that is none,
            // and nothing more is sent. The flags read is merged by .id, so a menu without one (a singleton, whose
            // plain read carries its flags anyway) is left as it is.
            string[] wanted = (hint.Value ?? string.Empty)
                .Split(',').Select(n => n.Trim()).Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(n => !records.Any(r => r.Words.ContainsKey(n)))
                .ToArray();
            if (wanted.Length == 0 || records.Any(r => !r.Words.ContainsKey(TikSpecialProperties.Id)))
                return records;

            if (!await AsValueOmitsFlagsAsync(records, wanted, cancellationToken).ConfigureAwait(false))
                return records;

            if (!await SupportsProplistAsync(cancellationToken).ConfigureAwait(false))
                return await SupplyFlagsByFindAsync(descriptor, records, wanted, cancellationToken)
                    .ConfigureAwait(false);

            string[] known = await KnownFlagFieldsAsync(descriptor, wanted, cancellationToken).ConfigureAwait(false);
            if (known.Length == 0)
                return records;

            string proplist = string.Join(",", known);
            IList<TikRecordSentence> flagRecords = await _host.RunPrintQueryAsync(descriptor, false,
                (pars, from) => CliCommandBuilder.BuildPrintExpression(descriptor.CommandText, pars, from, proplist),
                cancellationToken).ConfigureAwait(false);
            return CliConnectionBase.MergeById(records, flagRecords);
        }

        /// <summary>
        /// Whether this router's <c>print</c> takes <c>proplist=</c>, asked once per connection with
        /// <see cref="CliCommandBuilder.ProplistSupportProbe"/>. RouterOS 6 does not have the argument at all.
        /// </summary>
        /// <remarks>
        /// Only a router that also leaves the flags out of <c>as-value</c> ever asks, so this costs one command on
        /// a pre-7.20 connection and none on a current one. A refusal that is not the parser's
        /// (<see cref="CliCommandBuilder.ArgumentRefusal"/>) is read as "supported": the flags read then runs as
        /// before and reports whatever the router says about it, rather than silently taking the other path.
        /// </remarks>
        private async Task<bool> SupportsProplistAsync(CancellationToken cancellationToken)
        {
            if (_host.Knowledge.Features.PrintProplist == null)
            {
                string output;
                bool answered = true;
                try
                {
                    output = await _host.ExecuteAsync(CliCommandBuilder.ProplistSupportProbe, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (TikCommandException)
                {
                    output = string.Empty;
                    answered = false;   // read as "supported" for this connection, but not a fact about the build
                }

                _host.Knowledge.Features.PrintProplist = output.IndexOf(CliCommandBuilder.ArgumentRefusal,
                    StringComparison.OrdinalIgnoreCase) < 0;
                if (answered)
                    _host.Knowledge.StoreFeature(CliRouterKnowledge.FeaturePrintProplist, _host.Knowledge.Features.PrintProplist.Value);
                if (_host.Knowledge.Features.PrintProplist == false)
                    TikWireTrace.Emit("cli.flags", TikWireDir.Note,
                        "this router's 'print' has no 'proplist=' argument (RouterOS 6) — the flags an entity maps "
                            + "are read as the id list of each flag for the rest of this connection");
            }

            return _host.Knowledge.Features.PrintProplist.Value;
        }

        /// <summary>
        /// Reads the flag fields on a router with no <c>proplist=</c>: one
        /// <c>find (&lt;flag&gt;=yes)</c> per flag, whose answer is the ids the flag is set on
        /// (<see cref="CliCommandBuilder.BuildFlagIdQuery"/>). Every row read is then given each flag
        /// explicitly — <c>true</c> for an id the query named, <c>false</c> for the rest — except a flag the API
        /// names only where it is set (<see cref="FlagsSentOnlyWhenSet"/>), which the other rows then lack too.
        /// </summary>
        /// <remarks>
        /// <para>The queries are not filtered or windowed like the read they supplement, because they do not need
        /// to be: the merge is by <c>.id</c>, so ids outside the page simply match nothing, and an id list costs a
        /// fraction of the rows it describes.</para>
        /// <para>A flag the menu does not have, or a menu with no <c>find</c> verb, refuses the command; that flag
        /// is then left out for this menu rather than failing the read, exactly as an unknown name is left out of a
        /// <c>proplist=</c>. Asked once per menu and flag.</para>
        /// </remarks>
        private async Task<IList<TikRecordSentence>> SupplyFlagsByFindAsync(TikCommandDescriptor descriptor,
            IList<TikRecordSentence> records, string[] wanted, CancellationToken cancellationToken)
        {
            var flagIds = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in wanted)
            {
                string key = descriptor.CommandText + "|" + name;
                if (_host.Knowledge.MenuFacts.NoFlagFind.Contains(key))
                    continue;

                string output;
                try
                {
                    output = await _host.ExecuteAsync(
                        CliCommandBuilder.BuildFlagIdQuery(descriptor.CommandText, name), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (TikCommandException)
                {
                    _host.Knowledge.MenuFacts.NoFlagFind.Add(key);
                    continue;
                }

                // The answer is ids or nothing at all; anything else is the router refusing the command, which
                // the CLI has no separate channel for. Recognised by shape rather than by phrase: every token
                // must be an id, so no wording of a refusal can be mistaken for an empty result.
                string[] tokens = output.Split(';').Select(s => s.Trim())
                    .Where(s => s.Length > 0).ToArray();
                if (tokens.Any(t => !t.StartsWith("*", StringComparison.Ordinal)))
                {
                    _host.Knowledge.MenuFacts.NoFlagFind.Add(key);
                    continue;
                }

                // No row named: a flag set nowhere, a field that is no flag at all (a .proplist name such as an unset
                // connection-mark), or a name the menu does not have — RouterOS 6 answers all three with nothing. The
                // first row's 'get' tells them apart (6.49.13): a flag it answers 'true'/'false', and every row then gets
                // 'false', as the API prints it; an unknown name it refuses; and it answers nothing for a text field
                // without a value and for a flag this menu prints only when set ('slave'), both of which the API leaves
                // out too — so they are left out. Asked once per menu and name.
                if (tokens.Length == 0 && !_host.Knowledge.MenuFacts.FlagFieldExists.Contains(key))
                {
                    string check;
                    try
                    {
                        check = await _host.ExecuteAsync(
                            CliCommandBuilder.BuildFieldValueQuery(descriptor.CommandText, name), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (TikCommandException)
                    {
                        _host.Knowledge.MenuFacts.NoFlagFind.Add(key);
                        continue;
                    }
                    string answer = check.Trim();
                    if (!string.Equals(answer, "true", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(answer, "false", StringComparison.OrdinalIgnoreCase))
                    {
                        _host.Knowledge.MenuFacts.NoFlagFind.Add(key);
                        continue;
                    }
                    _host.Knowledge.LearnFact(_host.Knowledge.MenuFacts.FlagFieldExists, CliRouterKnowledge.FactFlagFieldExists, key);
                }

                flagIds[name] = new HashSet<string>(tokens, StringComparer.OrdinalIgnoreCase);
            }

            if (flagIds.Count == 0)
                return records;

            var supplied = new List<TikRecordSentence>(records.Count);
            foreach (var row in records)
            {
                string? id = row.GetResponseFieldOrDefault(TikSpecialProperties.Id, null);
                if (id == null)
                {
                    supplied.Add(row);
                    continue;
                }

                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in row.Words)
                    fields[kv.Key] = kv.Value;
                foreach (var kv in flagIds)
                {
                    if (fields.ContainsKey(kv.Key))
                        continue;
                    if (kv.Value.Contains(id))
                        fields[kv.Key] = "true";
                    else if (!IsSentOnlyWhenSet(descriptor.CommandText, kv.Key))
                        fields[kv.Key] = "false";
                }
                supplied.Add(new TikRecordSentence(fields));
            }

            return supplied;
        }

        /// <summary>
        /// The flags the RouterOS 6 binary API names only on the rows they are set on, per menu. A route's origin
        /// is one numbered field there (the <c>.jg</c> <c>numflag</c> <c>u7</c>: connected, static, RIP, OSPF, MME,
        /// BGP), and the API prints the member the row is and nothing for the others — measured on 6.49.13: the
        /// connected route carries <c>connect=true</c> and no <c>static</c>, the static route the reverse. Every other
        /// flag of every mapped entity it sends as an explicit <c>true</c>/<c>false</c> or not at all (scan of all
        /// mapped menus with rows, 6.49.13), so the id-list read gives them <c>false</c> as before.
        /// </summary>
        private static readonly Dictionary<string, HashSet<string>> FlagsSentOnlyWhenSet =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["/ip/route"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "connect", "static", "rip", "ospf", "mme", "bgp" },
            };

        private static bool IsSentOnlyWhenSet(string commandText, string flag)
        {
            string trimmed = (commandText ?? string.Empty).TrimEnd('/');
            int lastSlash = trimmed.LastIndexOf('/');
            string menu = lastSlash > 0 ? trimmed.Substring(0, lastSlash) : trimmed;
            return FlagsSentOnlyWhenSet.TryGetValue(menu, out var set) && set.Contains(flag);
        }

        /// <summary>
        /// Whether this router's <c>print as-value</c> leaves the flag fields out, asked once per connection with
        /// <see cref="CliCommandBuilder.FlagsProbe"/>.
        /// </summary>
        /// <remarks>
        /// When the probe cannot answer (no row, or a refusal) the read itself decides, uncached: no row carrying
        /// any of the requested flags is how a pre-7.20 answer looks, since 7.20+ prints <c>disabled</c> on every
        /// row of a menu that has it.
        /// </remarks>
        private async Task<bool> AsValueOmitsFlagsAsync(IList<TikRecordSentence> records, string[] wanted,
            CancellationToken cancellationToken)
        {
            if (_host.Knowledge.Features.AsValueOmitsFlags == null)
            {
                IList<TikRecordSentence> probe;
                try
                {
                    string output = await _host.ExecuteAsync(CliCommandBuilder.FlagsProbe, cancellationToken)
                        .ConfigureAwait(false);
                    probe = CliOutputParser.ParseAsValue(output);
                }
                catch (TikCommandException)
                {
                    probe = new List<TikRecordSentence>();
                }

                if (probe.Count > 0)
                {
                    _host.Knowledge.Features.AsValueOmitsFlags = !probe[0].Words.ContainsKey("disabled");
                    _host.Knowledge.StoreFeature(CliRouterKnowledge.FeatureAsValueOmitsFlags, _host.Knowledge.Features.AsValueOmitsFlags.Value);
                    if (_host.Knowledge.Features.AsValueOmitsFlags == true)
                        TikWireTrace.Emit("cli.flags", TikWireDir.Note,
                            "this router's 'print as-value' carries no flag fields (RouterOS before 7.20) — the flags "
                                + "an entity maps are read by name with a second print for the rest of this connection");
                }
            }

            if (_host.Knowledge.Features.AsValueOmitsFlags != null)
                return _host.Knowledge.Features.AsValueOmitsFlags.Value;
            return !records.Any(r => wanted.Any(w => r.Words.ContainsKey(w)));
        }

        /// <summary>
        /// The names in <paramref name="wanted"/> that the menu knows, checked once per menu and list on this
        /// connection with <see cref="CliCommandBuilder.BuildProplistCheck"/>: the whole list first, and each
        /// name alone only when the router refuses the list.
        /// </summary>
        private async Task<string[]> KnownFlagFieldsAsync(TikCommandDescriptor descriptor, string[] wanted,
            CancellationToken cancellationToken)
        {
            string key = descriptor.CommandText + "|" + string.Join(",", wanted);
            if (_knownFlagFields.TryGetValue(key, out string[]? cached))
                return cached;

            string[] known;
            if (await ProplistKnownAsync(descriptor, string.Join(",", wanted), cancellationToken).ConfigureAwait(false))
            {
                known = wanted;
            }
            else
            {
                var list = new List<string>();
                foreach (string name in wanted)
                    if (await ProplistKnownAsync(descriptor, name, cancellationToken).ConfigureAwait(false))
                        list.Add(name);
                known = list.ToArray();
            }

            _knownFlagFields[key] = known;
            return known;
        }

        /// <summary>
        /// Whether the menu knows every name in <paramref name="proplist"/>: an empty answer means yes,
        /// <see cref="CliCommandBuilder.ProplistRefusal"/> means no. Anything else is the router refusing the
        /// command itself, and is thrown — read as "unknown name", it would drop every flag without a word.
        /// </summary>
        private async Task<bool> ProplistKnownAsync(TikCommandDescriptor descriptor, string proplist,
            CancellationToken cancellationToken)
        {
            string output = await _host.ExecuteAsync(
                CliCommandBuilder.BuildProplistCheck(descriptor.CommandText, proplist), cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(output))
                return true;
            if (output.IndexOf(CliCommandBuilder.ProplistRefusal, StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            throw new TikCommandTrapException(_host.CommandFor(descriptor),
                new TikTrapSentenceResult(CliErrorParser.ExtractErrorLine(output)));
        }
    }
}

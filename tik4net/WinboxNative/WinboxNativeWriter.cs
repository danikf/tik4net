using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;
using tik4net.Winbox;

namespace tik4net.WinboxNative
{
    /// <summary>
    /// The native transport's writes — <c>add</c>, <c>set</c>, <c>remove</c>, <c>move</c>, <c>enable</c>/<c>disable</c>,
    /// <c>unset</c>, <c>comment</c> — and the field encoding they share with the action and monitor paths: name=value
    /// parameters to M2 fields (references resolved under await), unset lists, the record id and move destination,
    /// <c>place-before</c>, and the singleton write. <see cref="WinboxNativeConnection"/> implements the write hooks by
    /// forwarding here. One per connection.
    /// </summary>
    internal sealed class WinboxNativeWriter
    {
        private readonly IWinboxNativeHost _host;

        internal WinboxNativeWriter(IWinboxNativeHost host)
        {
            _host = host;
        }

        internal async Task<string> RunAddAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            _host.TakeLabelHints(descriptor);
            _host.EnsureNativeOpen();
            // descriptor.CommandText is "/path/add"; the resolution path is the parent.
            string apiPath = TikPath.Parent(descriptor.CommandText);

            try
            {
                using (await _host.EnterCommandAsync(cancellationToken).ConfigureAwait(false))   // see RunPrint
                {
                    var (handler, resolver) = _host.ResolveHandlerAndFields(apiPath);
                    var fields = await EncodeNameValueFieldsAsync(handler, descriptor, resolver, skipId: true,
                        cancellationToken).ConfigureAwait(false);
                    // An interface SUBTYPE path shares the generic [20,0] handler, and the discriminator that
                    // filters a read is the same field that says what to CREATE. Without it the router has
                    // only a name to go on and answers 'unsupported device type' — every
                    // /interface/<kind>/add over native, eleven paths, refused outright.
                    if (_host.HandlerMap.TryResolveSubtypeFilter(apiPath, out int addTypeKey, out int addTypeValue))
                        fields.Add(M2Message.U32Sys(addTypeKey, addTypeValue));
                    await AddPlaceBeforeAsync(handler, resolver, descriptor, fields, cancellationToken).ConfigureAwait(false);
                    int? newId = await _host.Ops.AddAsync(handler, fields, cancellationToken).ConfigureAwait(false);
                    // A new row is a name the reference cache has never seen (see ForgetReferenceNames).
                    _host.Codec.ForgetReferenceNames();
                    if (!newId.HasValue)
                        throw TikAddIdNotRead.Create(_host.CommandFor(descriptor), null);
                    return "*" + unchecked((uint)newId.Value).ToString("X");
                }
            }
            catch (WinboxM2OperationException ex) { throw _host.TranslateM2Error(ex, descriptor.CommandText); }
        }

        internal async Task RunNonQueryAsync(TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            _host.TakeLabelHints(descriptor);
            _host.EnsureNativeOpen();
            // Try the whole command text as a standalone action window first (e.g. /tool/wol). Splitting it
            // into parent+verb would resolve '/tool' and look for a 'wol' action there, which is not how
            // these windows are addressed — the path IS the operation.
            // (ExecuteNonQuery on such a path is legitimate too — the caller simply discards the row the read
            // path would have produced.)
            int[]? actionHandler = _host.HandlerMap.Resolve(WinboxNativeConnection.ApiPathOf(descriptor.CommandText));
            if (_host.Catalog.IsActionOnlyHandler(actionHandler))
            {
                using (await _host.EnterCommandAsync(cancellationToken).ConfigureAwait(false))
                    // actionHandler is non-null here: IsActionOnlyHandler answers false for a null handler.
                    await _host.RunActionWindowAsync(WinboxNativeConnection.ApiPathOf(descriptor.CommandText), actionHandler!, descriptor, cancellationToken)
                        .ConfigureAwait(false);
                return;
            }

            string verb = TikPath.Verb(descriptor.CommandText);
            string apiPath = TikPath.Parent(descriptor.CommandText);

            try
            {
                using (await _host.EnterCommandAsync(cancellationToken).ConfigureAwait(false))   // see RunPrint
                {
                    var (handler, resolver) = _host.ResolveHandlerAndFields(apiPath);
                    await RunVerbAsync(verb, apiPath, handler, resolver, descriptor, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (WinboxM2OperationException ex) { throw _host.TranslateM2Error(ex, descriptor.CommandText); }
        }

        private async Task RunVerbAsync(string verb, string apiPath, int[] handler, WinboxFieldResolver resolver,
            TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            switch (verb)
            {
                case "add":
                {
                    // /path/add invoked via ExecuteNonQuery (the new id, if any, is discarded here).
                    var fields = await EncodeNameValueFieldsAsync(handler, descriptor, resolver, skipId: true,
                        cancellationToken).ConfigureAwait(false);
                    await AddPlaceBeforeAsync(handler, resolver, descriptor, fields, cancellationToken).ConfigureAwait(false);
                    await _host.Ops.AddAsync(handler, fields, cancellationToken).ConfigureAwait(false);
                    _host.Codec.ForgetReferenceNames();
                    break;
                }
                case "set":
                {
                    await WriteFieldsAsync(handler, resolver, descriptor,
                        () => EncodeNameValueFieldsAsync(handler, descriptor, resolver, skipId: true, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                    break;
                }
                case "enable":
                case "disable":
                {
                    await WriteFieldsAsync(handler, resolver, descriptor,
                        () => Task.FromResult(resolver.EncodeField("disabled", verb == "disable" ? "true" : "false")),
                        cancellationToken).ConfigureAwait(false);
                    break;
                }
                case "unset":
                {
                    // unset names its target in a PSEUDO-parameter — '=value-name=<field>' — exactly as the
                    // binary API spells it; the field itself carries no value. Encoding the parameter list
                    // verbatim therefore asked the resolver for an M2 key for a field called 'value-name'
                    // and threw WinboxFieldResolutionException. Translate instead: unset = set the named
                    // field back to empty/default.
                    await WriteFieldsAsync(handler, resolver, descriptor,
                        () => EncodeUnsetFieldsAsync(handler, descriptor, resolver, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                    break;
                }
                case "comment":
                {
                    // A real RouterOS menu command, and on the M2 layer simply a write of the comment
                    // field — WinBox has no separate comment operation. Without this it reached
                    // DispatchActionVerb and threw "not an action verb".
                    await WriteFieldsAsync(handler, resolver, descriptor,
                        () => EncodeNameValueFieldsAsync(handler, descriptor, resolver, skipId: true, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                    break;
                }
                case "remove":
                {
                    int id = await ResolveRecordIdAsync(handler, resolver, descriptor, required: true,
                        cancellationToken).ConfigureAwait(false);
                    await _host.Ops.RemoveAsync(handler, id, cancellationToken).ConfigureAwait(false);
                    _host.Codec.ForgetReferenceNames();
                    break;
                }
                case "move":
                {
                    int id = await ResolveRecordIdAsync(handler, resolver, descriptor, required: true,
                        cancellationToken, alternateIdParam: "numbers").ConfigureAwait(false);
                    int dest = await ResolveMoveDestAsync(handler, resolver, descriptor, cancellationToken)
                        .ConfigureAwait(false);
                    await _host.Ops.MoveAsync(handler, id, dest, cancellationToken).ConfigureAwait(false);
                    break;
                }
                default:
                    // Action verb (e.g. /system/script/run → a .jg doit/SYS_CMD on this handler), invoked via
                    // ExecuteNonQuery. Dispatch it fire-and-forget; throws NotSupported if it is not an action.
                    await _host.DispatchActionVerbAsync(verb, apiPath, handler, resolver, descriptor, cancellationToken)
                        .ConfigureAwait(false);
                    break;
            }
        }

        /// <summary>
        /// Awaited form of <see cref="EncodeNameValueFields"/>: the same encoding, with the name→id lookups a
        /// dynamic enum reference needs done under <c>await</c> instead of blocking the calling thread.
        /// </summary>
        /// <remarks>
        /// Two passes over the encoder rather than an async encoder. The first pass runs with a delegate that
        /// RECORDS what it is asked to resolve and answers with a placeholder; its bytes are thrown away, and
        /// what it leaves behind is the list of lookups — taken from the encoder itself, so it cannot drift
        /// from the encoder's own notion of which fields are references (<c>EncodeField</c>'s <c>enm</c> case,
        /// reached only after canonicalizing the name, the read-only gate and the <c>!</c>-prefix strip). Those
        /// are resolved in one awaited batch, and the second pass encodes for real against the answers.
        /// <para>A command that references nothing — the overwhelmingly common case — collects nothing and
        /// keeps the first pass's bytes, so it costs exactly what it did before: one pass, no round trip.
        /// <c>EncodeField</c> itself is unchanged and still synchronous; making it async would have rippled
        /// through 1000 lines of reverse-engineered encoder that no deterministic test covers.</para>
        /// </remarks>
        internal async Task<List<byte[]>> EncodeNameValueFieldsAsync(
            int[] handler, TikCommandDescriptor descriptor, WinboxFieldResolver resolver, bool skipId,
            CancellationToken cancellationToken, bool allowReadOnly = false, bool includeFilters = false,
            bool skipSnapshotModifier = false)
        {
            // A bit set whose members are a TABLE (/user/group policies, a script's policy) needs that table
            // BEFORE the encoder runs — it cannot be collected like a name→id lookup, because without the
            // members there is no value to encode and the encoder refuses rather than writing a clean zero.
            await PrimeBitSetMemberTablesAsync(descriptor, resolver, cancellationToken).ConfigureAwait(false);

            var requests = new List<KeyValuePair<int[], string>>();
            var fields = EncodeNameValueFields(handler, descriptor, resolver, skipId, ReferenceCollector(requests),
                allowReadOnly, includeFilters, skipSnapshotModifier);
            if (requests.Count == 0) return fields;

            var resolveRef = await _host.IdResolver.ResolveReferencesAsync(requests, cancellationToken).ConfigureAwait(false);
            return EncodeNameValueFields(handler, descriptor, resolver, skipId, resolveRef,
                allowReadOnly, includeFilters, skipSnapshotModifier);
        }

        /// <inheritdoc cref="EncodeUnsetFields"/>
        private async Task<List<byte[]>> EncodeUnsetFieldsAsync(
            int[] handler, TikCommandDescriptor descriptor, WinboxFieldResolver resolver,
            CancellationToken cancellationToken)
        {
            var requests = new List<KeyValuePair<int[], string>>();
            var fields = EncodeUnsetFields(handler, descriptor, resolver, ReferenceCollector(requests));
            if (requests.Count == 0) return fields;

            var resolveRef = await _host.IdResolver.ResolveReferencesAsync(requests, cancellationToken).ConfigureAwait(false);
            return EncodeUnsetFields(handler, descriptor, resolver, resolveRef);
        }

        // The delegate of the collecting pass (see EncodeNameValueFieldsAsync): notes down every lookup the
        // encoder asks for and answers with a placeholder id so the pass runs to the end and the questions are
        // complete. The bytes it produces are discarded — only a pass that collected nothing is kept, and that
        // pass never called this at all.
        private static Func<int[], string, int?> ReferenceCollector(List<KeyValuePair<int[], string>> into)
            => (refHandler, name) =>
            {
                into.Add(new KeyValuePair<int[], string>(refHandler, name));
                return 0;
            };

        // Encode every NameValue parameter (except client-side markers and, optionally, .id) into M2 fields.
        // Read-only fields (per .jg) are skipped by the encoder (returns no bytes). A network field expands
        // to two entries (address + mask); a dynamic enum reference is resolved name→id by resolveRef.
        internal List<byte[]> EncodeNameValueFields(
            int[] handler, TikCommandDescriptor descriptor, WinboxFieldResolver resolver, bool skipId,
            Func<int[], string, int?> resolveRef,
            bool allowReadOnly = false, bool includeFilters = false, bool skipSnapshotModifier = false)
        {
            var fields = new List<byte[]>();
            foreach (var p in descriptor.Parameters)
            {
                // Filters are query predicates and never inputs — except on an action window, where there is
                // nothing to filter and every parameter is an argument. This matters because the read path
                // rewrites Default-format parameters to Filter (TikGenericCommand.ResolveParamsForRead), so
                // an action reached through ExecuteSingleRow would otherwise silently lose its arguments and
                // the router would answer "required parameter … missing".
                if (!includeFilters && p.ParameterFormat == TikCommandParameterFormat.Filter) continue;
                if (p.Name.StartsWith(".") && p.Name != TikSpecialProperties.Id) continue; // .proplist/.tag/…
                if (p.Name == TikSpecialProperties.Id) { if (skipId) continue; }
                if (p.Name == "move-before" || p.Name == "destination") continue; // handled by move dest
                if (p.Name == PlaceBeforeParam) continue;                            // handled by AddPlaceBeforeAsync
                if (skipSnapshotModifier && IsMonitorSnapshotModifier(p.Name)) continue;

                // A bad field VALUE is what the router itself would trap on over the API, so surface it as a
                // trap here too — the native transport catches it client-side only because it has to resolve
                // references before sending. Consumers then handle one exception type across all transports.
                try
                {
                    // p.Value is non-null here: this loop handles name=value data parameters, and only a
                    // bare '?name' FILTER (handled elsewhere) carries a null value.
                    fields.AddRange(resolver.EncodeField(p.Name, p.Value!, resolveRef, allowReadOnly,
                        MemberTableReader));
                }
                catch (WinboxFieldValueException ex)
                {
                    throw new TikCommandTrapException(
                        _host.CommandFor(descriptor.CommandText),
                        new TikTrapSentenceResult(ex.Message));
                }
            }
            return fields;
        }

        // The delegate EncodeField asks for a bit set's member table. Cached per connection by the id
        // resolver, so the awaited path (which primes it first) never blocks here.
        private IReadOnlyDictionary<int, string>? MemberTableReader(int[] handler)
            => _host.IdResolver.ReadMemberTable(handler);

        // Reads, under await, the member table of every table-backed bit set the command writes. Which
        // fields those are is asked of the RESOLVER, so it cannot drift from what the encoder will look up.
        private async Task PrimeBitSetMemberTablesAsync(TikCommandDescriptor descriptor,
            WinboxFieldResolver resolver, CancellationToken cancellationToken)
        {
            List<int[]>? wanted = null;
            foreach (var p in descriptor.Parameters)
            {
                if (p.Name.StartsWith(".")) continue;
                int[]? table = resolver.BitSetMemberTable(p.Name);
                if (table == null) continue;
                wanted = wanted ?? new List<int[]>();
                if (!wanted.Any(h => h.SequenceEqual(table))) wanted.Add(table);
            }
            if (wanted == null) return;
            foreach (int[] table in wanted)
                await _host.IdResolver.PrimeMemberTableAsync(table, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Parameters that ask a monitor for a SINGLE reading rather than naming one of its inputs.
        /// </summary>
        /// <remarks>
        /// RouterOS needs <c>once</c> because a monitor on the API or a terminal runs until something stops it.
        /// A WinBox monitor window has no such input: the client starts a cycle, reads it and cancels it, so
        /// "one reading" is decided here (see <c>RunMonitorWindowSync</c>) and there is no M2 key to encode it
        /// to. Sending it anyway is what made <c>/interface/monitor-traffic interface=ether1 once</c> fail
        /// resolution on the field the caller never meant as data. Same idea as <c>.proplist</c>/<c>detail</c>
        /// being dropped per transport — what is a wire word on one is a client-side instruction on another.
        /// </remarks>
        /// <remarks>
        /// <c>duration</c> is the same kind of instruction: no <c>.jg</c> monitor window has an input of that name
        /// (checked across the 7.24 catalog), because WinBox runs a window until the user closes it. It is
        /// honoured by <c>RunMonitorWindowSync</c> keeping the window open for that long.
        /// </remarks>
        private static bool IsMonitorSnapshotModifier(string name)
            => string.Equals(name, "once", StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, MonitorDurationParam, StringComparison.OrdinalIgnoreCase);

        internal const string MonitorDurationParam = "duration";

        // Encode an 'unset' into M2 fields: every 'value-name=<field>' pseudo-parameter names a field to be
        // written back as empty. The parameter's own name is NOT a router field, so it must never reach the
        // resolver — its VALUE is the field name and the new value is the empty string.
        private List<byte[]> EncodeUnsetFields(
            int[] handler, TikCommandDescriptor descriptor, WinboxFieldResolver resolver,
            Func<int[], string, int?> resolveRef)
        {
            var fields = new List<byte[]>();
            var unsetIds = new List<int>();
            var unknown = new List<string>();
            int named = 0;
            foreach (var p in descriptor.Parameters)
            {
                if (!string.Equals(p.Name, TikSpecialProperties.UnsetValueName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrEmpty(p.Value))
                    continue;

                named++;
                int before = unsetIds.Count;
                try
                {
                    fields.AddRange(resolver.EncodeUnsetField(p.Value!, unsetIds)); // an unset carries the field NAME as its value
                }
                catch (WinboxFieldValueException ex)
                {
                    throw new TikCommandTrapException(
                        _host.CommandFor(descriptor.CommandText),
                        new TikTrapSentenceResult(ex.Message));
                }
                // A field the catalog cannot name has no id to list, so the request would carry the verb and
                // nothing to apply it to — a success that clears nothing. Name it instead.
                if (unsetIds.Count == before) unknown.Add(p.Value!);   // an unset carries the field NAME as its value
            }

            if (unsetIds.Count > 0)
                fields.Add(M2Message.U32ArraySys(WinboxM2Protocol.SysKey.UnsetFields, unsetIds.ToArray()));

            if (named == 0)
                throw new TikCommandTrapException(
                    _host.CommandFor(descriptor.CommandText),
                    new TikTrapSentenceResult(
                        "unset requires at least one '" + TikSpecialProperties.UnsetValueName
                        + "=<field>' parameter naming the field to clear."));

            if (unknown.Count > 0)
                throw new TikCommandTrapException(
                    _host.CommandFor(descriptor.CommandText),
                    new TikTrapSentenceResult(
                        "no M2 field id for " + string.Join(", ", unknown)
                        + " on this handler, so an unset naming it would clear nothing."));

            return fields;
        }

        // Write changed fields back to the router. An ordinary table addresses one record by .id (set); a
        // SINGLETON (.jg type:'item') window — /system/identity, /ip/dns, /ip/settings, /snmp, … — holds a
        // single object and no record list, so it has no .id to address and webfig writes it with
        // set-singleton instead (ObjectHolder.setObject). Without this split every singleton write over the
        // native transport failed with "no such item: could not resolve record .id ''", i.e. no
        // IsSingleton entity was saveable at all — the suite only ever read them, so it went unnoticed.
        // <paramref name="encodeFields"/> is deferred so the .id still resolves before the fields
        // are encoded, keeping "no such item" the error a caller sees when both are wrong.
        private async Task WriteFieldsAsync(int[] handler, WinboxFieldResolver resolver, TikCommandDescriptor descriptor,
            Func<Task<List<byte[]>>> encodeFields, CancellationToken cancellationToken)
        {
            if (_host.IsSingletonWindow(WinboxNativeConnection.ApiPathOf(descriptor.CommandText), handler))
            {
                _host.Catalog.GetSingletonCommands(_host.HandlerMap.ResolveDerivedKey(WinboxNativeConnection.ApiPathOf(descriptor.CommandText)),
                    out _, out int? setCommand);
                await _host.Ops.SetSingletonAsync(handler, await encodeFields().ConfigureAwait(false),
                    SingletonIdOf(descriptor), cancellationToken, setCommand).ConfigureAwait(false);
                _host.Codec.ForgetReferenceNames();
                return;
            }
            int id = await ResolveRecordIdAsync(handler, resolver, descriptor, required: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            _host.Codec.ForgetReferenceNames();   // a set can RENAME the row another table refers to
            await _host.Ops.SetAsync(handler, id, await encodeFields().ConfigureAwait(false), cancellationToken)
                .ConfigureAwait(false);
        }

        // The optional .id of a singleton write. Only the literal "*HEX" handle is honored — the hidden
        // holders that carry one address a record of another table (webfig's 'Change Password' holder targets
        // a user), and resolving a friendly name would mean a getall, which a singleton handler has no record
        // list to answer. Returns -1 (send no .id) otherwise, which is the normal case.
        private static int SingletonIdOf(TikCommandDescriptor descriptor)
        {
            string? idParam = WinboxNativeConnection.FindParam(descriptor, TikSpecialProperties.Id);
            // netstandard2.0's string.IsNullOrEmpty isn't annotated NotNullWhen, so the compiler can't narrow.
            if (!string.IsNullOrEmpty(idParam) && idParam!.StartsWith("*")
                && int.TryParse(idParam.Substring(1), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out int hexId))
                return hexId;
            return -1;
        }

        // Resolve the M2 numeric record id from the command's .id parameter. The .id may be the RouterOS
        // "*HEX" handle form, or a friendly name (e.g. "ether1") — names are resolved via getall.
        // alternateIdParam names the target record instead of ".id" on this verb, and is tried first. `move` is
        // the case: RouterOS spells its target "numbers", and that is what the mapper's Move<TEntity> sends.
        // Reading only ".id" made every mapper-level move fail here with "could not resolve record .id ''" — on
        // a command the API, REST and all four CLI transports carry out fine. It went unnoticed because the one
        // move test in the suite hand-builds the command with ".id".
        // (Prose kept as a plain comment: a private method with a lone <param> tag is an incomplete XML doc
        // comment — CS1573 for every other parameter — and the mapper's Move<TEntity> is in tik4net.objects,
        // which this assembly does not reference, so the cref could not resolve either (CS1574).)
        internal async Task<int> ResolveRecordIdAsync(int[] handler, WinboxFieldResolver resolver,
            TikCommandDescriptor descriptor, bool required, CancellationToken cancellationToken,
            string? alternateIdParam = null)
        {
            string? idParam = alternateIdParam != null ? WinboxNativeConnection.FindParam(descriptor, alternateIdParam) : null;
            if (string.IsNullOrEmpty(idParam))
                idParam = WinboxNativeConnection.FindParam(descriptor, TikSpecialProperties.Id);
            if (!string.IsNullOrEmpty(idParam))
            {
                if (idParam!.StartsWith("*") &&
                    int.TryParse(idParam.Substring(1), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out int hexId))
                    return hexId;

                // Friendly name (or a where-style key): match against the record 'name' field via getall.
                int byName = await _host.IdResolver
                    .FindIdByNameAsync(handler, resolver, idParam, cancellationToken).ConfigureAwait(false);
                if (byName >= 0) return byName;
            }

            if (required)
            {
                // The set/remove/move target does not exist (unresolvable .id) — same outcome as the API/CLI
                // transports' "no such item".
                var cmd = _host.CommandFor(descriptor.CommandText);
                throw new TikNoSuchItemException(cmd, new TikTrapSentenceResult(
                    $"no such item: could not resolve record .id '{idParam}' on '{descriptor.CommandText}'."));
            }
            return -1;
        }

        // The API's add argument that creates a row in place on an ordered menu, instead of appended.
        private const string PlaceBeforeParam = "place-before";

        // place-before=<row> on an add. M2 has no add argument for it: the anchor goes on the new record as its
        // next-id (RecordKey.NextId, the key move already uses), and the router creates the row immediately
        // before that record. Webfig never positions an add, so this is the router honouring the key rather than
        // a path the GUI exercises — measured on 7.24 (Docs/winbox-native-m2-protocol.md, command catalog).
        // An anchor that does not resolve is refused as "no such item", as over the API: silently appending
        // would put the row somewhere the caller did not ask for, on a menu where position is behaviour.
        private async Task AddPlaceBeforeAsync(int[] handler, WinboxFieldResolver resolver,
            TikCommandDescriptor descriptor, List<byte[]> fields, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(WinboxNativeConnection.FindParam(descriptor, PlaceBeforeParam)))
                return;
            int anchor = await ResolveRecordIdAsync(handler, resolver, descriptor, required: true,
                cancellationToken, alternateIdParam: PlaceBeforeParam).ConfigureAwait(false);
            fields.Add(M2Message.U32Sys(WinboxM2Protocol.RecordKey.NextId, anchor));
        }

        // Resolve the move destination (next-id) from a NameValue "destination"/"move-before" parameter.
        private async Task<int> ResolveMoveDestAsync(int[] handler, WinboxFieldResolver resolver,
            TikCommandDescriptor descriptor, CancellationToken cancellationToken)
        {
            string? dest = WinboxNativeConnection.FindParam(descriptor, "destination") ?? WinboxNativeConnection.FindParam(descriptor, "move-before");
            if (string.IsNullOrEmpty(dest)) return -1; // move to end
            if (dest!.StartsWith("*") &&
                int.TryParse(dest.Substring(1), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out int hexId))
                return hexId;
            int byName = await _host.IdResolver
                .FindIdByNameAsync(handler, resolver, dest, cancellationToken).ConfigureAwait(false);
            return byName; // -1 if not found → move to end
        }
    }
}

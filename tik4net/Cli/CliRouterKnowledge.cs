using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;
using tik4net.Diagnostics;

namespace tik4net.Cli
{
    /// <summary>
    /// What a CLI connection has learnt about its router — the features (<see cref="RouterFeatureSet"/>), the facts
    /// about single menus (<see cref="RouterMenuFacts"/>) — and the per-build store that keeps both, with the menu
    /// grammar, across connections (<see cref="CliSchemaStore"/>). One per open: an object reopened against another
    /// router would otherwise send it what only the first one took.
    /// </summary>
    internal sealed class CliRouterKnowledge
    {
        private readonly ICliCommandHost _host;

        internal CliRouterKnowledge(ICliCommandHost host)
        {
            _host = host;
        }

        // What this connection has learnt about its router (RouterFeatureSet.cs).
        private readonly RouterFeatureSet _features = new RouterFeatureSet();
        private readonly RouterMenuFacts _menuFacts = new RouterMenuFacts();

        internal RouterFeatureSet Features => _features;

        internal RouterMenuFacts MenuFacts => _menuFacts;

        /// <summary>The store for this router's build, once asked (<see cref="SchemaStore"/>); <c>null</c> when off.</summary>
        internal CliSchemaStore? Store => _schemaStore;

        // The store for this router's build, found once per open: the build is asked (one command) before the first
        // read, menu description or monitor — the first thing whose probes the store can save — not at open, so a
        // connection that only writes pays nothing.
        private CliSchemaStore? _schemaStore;
        private bool _schemaStoreAsked;

        internal CliSchemaStore? SchemaStore()
        {
            if (_schemaStoreAsked)
                return _schemaStore;
            _schemaStoreAsked = true;
            if (string.IsNullOrWhiteSpace(_host.CatalogCachePath))
                return null;
            _host.CountedReadDepth++;   // the answer carries its own length (CliSchemaStore.KeyCommand)
            try
            {
                return AdoptSchemaStore(_host.Execute(CliSchemaStore.KeyCommand));
            }
            catch (Exception ex) when (IsCacheProblem(ex))
            {
                return SchemaStoreOff(ex);
            }
            finally
            {
                _host.CountedReadDepth--;
            }
        }

        internal async Task EnsureSchemaStoreAsync(CancellationToken cancellationToken)
        {
            if (_schemaStoreAsked)
                return;
            _schemaStoreAsked = true;
            if (string.IsNullOrWhiteSpace(_host.CatalogCachePath))
                return;
            _host.CountedReadDepth++;   // the answer carries its own length (CliSchemaStore.KeyCommand)
            try
            {
                AdoptSchemaStore(await _host.ExecuteAsync(CliSchemaStore.KeyCommand, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (IsCacheProblem(ex))
            {
                SchemaStoreOff(ex);
            }
            finally
            {
                _host.CountedReadDepth--;
            }
        }

        // The connection failing, or the caller cancelling, is not a cache problem: those are the caller's to see.
        private static bool IsCacheProblem(Exception ex) => !(ex is TikConnectionException) && !(ex is OperationCanceledException);

        private CliSchemaStore? SchemaStoreOff(Exception ex)
        {
            TikWireTrace.Emit(CliSchemaStore.TraceChannel, TikWireDir.Note, "cache off for this connection: " + ex.Message);
            return _schemaStore = null;
        }

        private CliSchemaStore? AdoptSchemaStore(string? keyAnswer)
        {
            string dir = TikCacheFileSystem.ResolveDirectory(_host.CatalogCachePath)!;
            string? key = CliSchemaStore.KeyFromAnswer(keyAnswer);
            if (key == null)
                TikWireTrace.Emit(CliSchemaStore.TraceChannel, TikWireDir.Note, "cache off for this connection: no build key in the answer");
            _schemaStore = CliSchemaStore.For(dir, key);
            if (_schemaStore != null)
                LoadStoredFacts(_schemaStore);
            return _schemaStore;
        }

        // ── What the store keeps besides the menus ────────────────────────────
        //
        // Only what the router's own answer settled: a probe's reply, or a fallback taken after a recognised refusal
        // whose other form then answered. Never a fact that a timeout or a menu's current rows could have produced —
        // NoFlagFind (taken on any failure, and on row 0's value), SerializeJson (its "no" is taken on any command
        // failure), PagingUnavailable from a window that did not report its size. A stored fact can be wrong only if
        // the reader that learnt it was, which is what the store's FormatVersion bump is for.

        internal const string FeatureDsv = "dsv:";   // + the separator probed
        internal const string FeatureAsValueOmitsFlags = "asValueOmitsFlags";
        internal const string FeaturePrintProplist = "printProplist";
        internal const string FeatureConsoleInspect = "consoleInspect";

        internal const string FactShowSensitiveRefused = "showSensitiveRefused";
        internal const string FactPrintWithoutAsValue = "printWithoutAsValue";
        internal const string FactFlagFieldExists = "flagFieldExists";
        internal const string FactMonitorWithoutAsValue = "monitorWithoutAsValue";
        internal const string FactTorchWithoutProplist = "torchWithoutProplist";
        internal const string FactPagingUnavailable = "pagingUnavailable";
        internal const string FactWindowPrintsNoId = "windowPrintsNoId";

        private void LoadStoredFacts(CliSchemaStore store)
        {
            if (_host.CliFieldSeparator != null && _features.Dsv == null && store.TryGetFeature(FeatureDsv + _host.CliFieldSeparator, out bool dsv))
            {
                _features.DsvProbedSeparator = _host.CliFieldSeparator;
                _features.Dsv = dsv;
            }
            if (_features.AsValueOmitsFlags == null && store.TryGetFeature(FeatureAsValueOmitsFlags, out bool omits))
                _features.AsValueOmitsFlags = omits;
            if (_features.PrintProplist == null && store.TryGetFeature(FeaturePrintProplist, out bool proplist))
                _features.PrintProplist = proplist;
            if (_features.ConsoleInspect == null && store.TryGetFeature(FeatureConsoleInspect, out bool inspect))
                _features.ConsoleInspect = inspect;

            _menuFacts.ShowSensitiveRefused.UnionWith(store.Facts(FactShowSensitiveRefused));
            _menuFacts.PrintWithoutAsValue.UnionWith(store.Facts(FactPrintWithoutAsValue));
            _menuFacts.FlagFieldExists.UnionWith(store.Facts(FactFlagFieldExists));
            _menuFacts.MonitorWithoutAsValue.UnionWith(store.Facts(FactMonitorWithoutAsValue));
            _menuFacts.TorchWithoutProplist.UnionWith(store.Facts(FactTorchWithoutProplist));
            _menuFacts.PagingUnavailable.UnionWith(store.Facts(FactPagingUnavailable));
            _menuFacts.WindowPrintsNoId.UnionWith(store.Facts(FactWindowPrintsNoId));
        }

        internal void StoreFeature(string name, bool value) => _schemaStore?.PutFeature(name, value);

        // Remembers a fact for the connection and, where the store is on, for the build.
        internal void LearnFact(HashSet<string> set, string storedAs, string member)
        {
            set.Add(member);
            _schemaStore?.PutFact(storedAs, member);
        }

        // A live schema whose lists go into the store as they are asked. The value completions are router data and
        // are never stored.
        internal static TikMenuSchema Recording(CliSchemaStore store, string path, TikMenuSchema live)
        {
            var source = live.Source;
            return new TikMenuSchema(path, source,
                () =>
                {
                    var commands = live.Commands;
                    var submenus = live.Submenus;
                    store.Put(path, source, SchemaListCommands, commands);
                    store.Put(path, source, SchemaListSubmenus, submenus);
                    return (commands, submenus);
                },
                verb => Record(store, path, source, SchemaListArgs + verb, live.Arguments(verb)),
                () => Record(store, path, source, SchemaListReadable, live.ReadableFields),
                () => Record(store, path, source, SchemaListUnset, live.UnsetFields),
                (verb, argument) => live.ValuesOf(argument, verb),
                live.Explanations,
                (verb, argument) => live.ValueGrammar(argument, verb));
        }

        private static IReadOnlyCollection<string>? Record(CliSchemaStore store, string path, TikMenuSchemaSource source,
            string list, IReadOnlyCollection<string>? value)
        {
            store.Put(path, source, list, value);
            return value;
        }

        // A schema answered from the store; a list the store lacks is asked live (and recorded). A cached "no" can be
        // stale for this router (another board, a buggy older reader), so the checks that refuse on these lists
        // re-ask live before they refuse (TikMenuSchema.Relearn).
        internal TikMenuSchema CachedSchema(CliSchemaStore store, string path, TikMenuSchemaSource source)
        {
            var live = new Lazy<TikMenuSchema>(() => Recording(store, path, _host.DescribeMenuLive(path)));
            IReadOnlyCollection<string>? List(string name, Func<TikMenuSchema, IReadOnlyCollection<string>?> ask)
                => store.TryGet(path, name, out var cached) ? cached : ask(live.Value);

            var schema = new TikMenuSchema(path, source,
                () =>
                {
                    if (store.TryGet(path, SchemaListCommands, out var commands) && commands != null
                        && store.TryGet(path, SchemaListSubmenus, out var submenus))
                        return (commands, submenus);
                    return (live.Value.Commands, live.Value.Submenus);
                },
                verb => List(SchemaListArgs + verb, s => s.Arguments(verb)),
                () => List(SchemaListReadable, s => s.ReadableFields),
                () => List(SchemaListUnset, s => s.UnsetFields),
                (verb, argument) => live.Value.ValuesOf(argument, verb),
                verb => live.Value.Explanations(verb),
                (verb, argument) => live.Value.ValueGrammar(argument, verb));
            schema.Relearn = () =>
            {
                var fresh = Recording(store, path, _host.DescribeMenuLive(path));
                _host.MenuSchemas.Replace(path, fresh);
                return fresh;
            };
            return schema;
        }

        private const string SchemaListCommands = "commands";
        private const string SchemaListSubmenus = "submenus";
        private const string SchemaListReadable = "readable";
        private const string SchemaListUnset = "unset";
        private const string SchemaListArgs = "args:";
    }
}

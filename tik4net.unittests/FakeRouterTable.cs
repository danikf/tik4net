using System;
using System.Collections.Generic;
using System.Linq;
using tik4net.Objects;
using tik4net.Testing;

namespace tik4net.unittests
{
    /// <summary>
    /// A stateful, in-memory stand-in for one router menu (one <c>[TikEntity]</c> path), wired into a
    /// <see cref="TikFakeConnection"/>.
    /// <para>
    /// <see cref="TikFakeConnection.WithEntities{TEntity}(TEntity[])"/> answers reads from a fixed list, which is
    /// enough to test a single call. It is not enough to test <see cref="TikListMerge{TEntity}"/>: a merge issues a
    /// whole sequence of <c>/add</c>, <c>/set</c>, <c>/unset</c>, <c>/remove</c> and <c>/move</c> commands whose only
    /// meaningful result is the state — and, for an ordered menu, the <em>order</em> — they leave behind. This table
    /// applies those commands to a row list, so a test can assert the router state the merge produced instead of
    /// asserting the commands it happened to emit.
    /// </para>
    /// <para>
    /// Rows are held at wire level (field name → string value), exactly as the router holds them, so entity mapping
    /// and value conversion still run for real on the way in and out.
    /// </para>
    /// </summary>
    internal sealed class FakeRouterTable<TEntity>
        where TEntity : new()
    {
        private readonly TikEntityMetadata _metadata = TikEntityMetadataCache.GetMetadata<TEntity>();
        private readonly List<Dictionary<string, string>> _rows = new List<Dictionary<string, string>>();
        private int _nextId = 1;

        /// <summary>Commands the table applied, in order — for asserting <em>how</em> a merge got there.</summary>
        public List<string> AppliedCommands { get; } = new List<string>();

        /// <summary>Router path of the emulated menu (e.g. <c>/ip/firewall/mangle</c>).</summary>
        public string Path => _metadata.EntityPath;

        /// <summary>Ids of the current rows, in router order.</summary>
        public IEnumerable<string> Ids => _rows.Select(r => r[TikSpecialProperties.Id]);

        /// <summary>Adds rows to the table as if they had always been on the router (assigning ids).</summary>
        public FakeRouterTable<TEntity> Seed(params TEntity[] entities)
        {
            foreach (var entity in entities)
                _rows.Add(ToRow(entity, "*" + _nextId++));
            return this;
        }

        /// <summary>Reads the current table back as entities, in router order.</summary>
        public IList<TEntity> Load(ITikConnection connection)
            => connection.LoadAll<TEntity>().ToList();

        /// <summary>Registers the read and write handlers for this menu on <paramref name="connection"/>.</summary>
        public TikFakeConnection AttachTo(TikFakeConnection connection)
        {
            connection.WithResponse(
                rows => rows.First() == Path + _metadata.LoadCommand,
                _ => _rows
                    .Select(r => (ITikSentence)new TikFakeReSentence(new Dictionary<string, string>(r)))
                    .Concat(new ITikSentence[] { new TikFakeDoneSentence() })
                    .ToList());

            connection.WithResponse(rows => rows.First() == Path + "/add", rows =>
            {
                Record(rows);
                var row = ParseParameters(rows);
                string id = "*" + _nextId++;
                row[TikSpecialProperties.Id] = id;
                _rows.Add(row);   // the router appends — the merge is responsible for moving it into place
                return new ITikSentence[]
                {
                    new TikFakeDoneSentence(new Dictionary<string, string> { { TikSpecialProperties.Ret, id } })
                };
            });

            connection.WithResponse(rows => rows.First() == Path + "/set", rows =>
            {
                Record(rows);
                var values = ParseParameters(rows);
                var row = RowById(values[TikSpecialProperties.Id]);
                foreach (var pair in values.Where(p => p.Key != TikSpecialProperties.Id))
                    row[pair.Key] = pair.Value;
                return Done();
            });

            connection.WithResponse(rows => rows.First() == Path + "/unset", rows =>
            {
                Record(rows);
                var values = ParseParameters(rows);
                var row = RowById(values[TikSpecialProperties.Id]);
                row[values[TikSpecialProperties.UnsetValueName]] = "";
                return Done();
            });

            connection.WithResponse(rows => rows.First() == Path + "/remove", rows =>
            {
                Record(rows);
                var values = ParseParameters(rows);
                _rows.Remove(RowById(values[TikSpecialProperties.Id]));
                return Done();
            });

            connection.WithResponse(rows => rows.First() == Path + "/move", rows =>
            {
                Record(rows);
                var values = ParseParameters(rows);
                var moved = RowById(values["numbers"]);
                _rows.Remove(moved);

                string destination;
                if (values.TryGetValue("destination", out destination))
                    _rows.Insert(_rows.IndexOf(RowById(destination)), moved);
                else
                    _rows.Add(moved);   // no destination → move to end
                return Done();
            });

            return connection;
        }

        private Dictionary<string, string> ToRow(TEntity entity, string id)
        {
            var row = _metadata.Properties.ToDictionary(p => p.FieldName, p => p.GetEntityValue(entity) ?? "");
            row[TikSpecialProperties.Id] = id;
            return row;
        }

        private Dictionary<string, string> RowById(string id)
        {
            var row = _rows.FirstOrDefault(r => r[TikSpecialProperties.Id] == id);
            if (row == null)
                throw new InvalidOperationException($"{Path}: no such item '{id}'.");
            return row;
        }

        private void Record(IEnumerable<string> rows) => AppliedCommands.Add(string.Join(" ", rows));

        private static ITikSentence[] Done() => new ITikSentence[] { new TikFakeDoneSentence() };

        /// <summary>Splits <c>=name=value</c> command rows into a field dictionary (value may itself contain '=').</summary>
        private static Dictionary<string, string> ParseParameters(IEnumerable<string> rows)
        {
            var result = new Dictionary<string, string>();
            foreach (string row in rows.Skip(1).Where(r => r.StartsWith("=")))
            {
                int separator = row.IndexOf('=', 1);
                if (separator < 0)
                    continue;
                result[row.Substring(1, separator - 1)] = row.Substring(separator + 1);
            }
            return result;
        }
    }
}

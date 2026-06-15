using System;
using System.Collections.Generic;
using System.Globalization;

namespace tik4net.Winbox
{
    /// <summary>
    /// Resolves friendly RouterOS names to WinBox M2 numeric record ids by listing a table and matching its
    /// <c>name</c> field. Split out of <c>WinboxNativeConnection</c> as the reusable "name → id" lookup used both
    /// when resolving a command's <c>.id</c>/move target and when encoding a dynamic enum reference field.
    /// Pure lookup logic — depends only on the M2 operations channel, the record decoder and the <c>.jg</c> catalog.
    /// </summary>
    internal sealed class WinboxIdResolver
    {
        private readonly WinboxNativeM2Operations _ops;
        private readonly WinboxRecordCodec _codec;
        private readonly WinboxJgCatalog _catalog;

        private static readonly Dictionary<string, int> EmptyOverrides = new Dictionary<string, int>();

        internal WinboxIdResolver(WinboxNativeM2Operations ops, WinboxRecordCodec codec, WinboxJgCatalog catalog)
        {
            _ops = ops;
            _codec = codec;
            _catalog = catalog;
        }

        /// <summary>
        /// Resolves a dynamic enum reference (the referenced table handler + a friendly name) to that record's
        /// numeric M2 id, by listing the referenced table and matching its <c>name</c> field. Returns
        /// <c>null</c> when not found. Shape matches the <c>resolveRef</c> delegate <c>WinboxFieldResolver.EncodeField</c> expects.
        /// </summary>
        internal int? ResolveReference(int[] refHandler, string name)
        {
            var refResolver = new WinboxFieldResolver(null, refHandler, _catalog, EmptyOverrides);
            int id = FindIdByName(refHandler, refResolver, name);
            return id >= 0 ? (int?)id : null;
        }

        /// <summary>
        /// Looks up the M2 numeric record id whose <c>name</c> field equals <paramref name="name"/> on the given
        /// handler's table (used to map friendly names like <c>ether1</c> to a record id). Returns -1 when not found.
        /// </summary>
        internal int FindIdByName(int[] handler, WinboxFieldResolver resolver, string name)
        {
            var keyToName = resolver.BuildKeyToApiName();
            var keyToField = resolver.BuildKeyToField();
            foreach (var rec in _ops.GetAll(handler))
            {
                var decoded = _codec.DecodeRecord(rec, keyToName, keyToField);
                if (decoded.TryGetValue("name", out var nm) && string.Equals(nm, name, StringComparison.Ordinal)
                    && decoded.TryGetValue(TikSpecialProperties.Id, out var idStr)
                    && idStr.StartsWith("*") &&
                    int.TryParse(idStr.Substring(1), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out int id))
                    return id;
            }
            return -1;
        }
    }
}

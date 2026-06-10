using System.Collections.Generic;

namespace tik4net.Winbox
{
    /// <summary>
    /// One field of a WinBox <c>.jg</c> catalog window: the numeric M2 key, its wire type, and
    /// read-only flag. Produced by <see cref="WinboxJgCatalog"/> and consumed by the field resolver
    /// to translate between API field names and M2 keys.
    /// </summary>
    internal sealed class WinboxJgField
    {
        /// <summary>API-ish field name normalized from the <c>.jg</c> label (lower-case, spaces→'-').</summary>
        internal string ApiName { get; }

        /// <summary>Numeric M2 field key (e.g. 0x10006 for Name).</summary>
        internal int Key { get; }

        /// <summary>Wire-type code decoded from the <c>id</c> prefix (e.g. "string", "u32", "bool", "raw").</summary>
        internal string WireType { get; }

        /// <summary>True when the <c>.jg</c> marks the field read-only (<c>ro:1</c>).</summary>
        internal bool ReadOnly { get; }

        /// <summary>
        /// For <c>type:'enm'</c> fields with a static value list (<c>values:{map:['off','on',…]}</c>),
        /// the numeric value → API-string map (index → label). <c>null</c> for non-enum fields.
        /// </summary>
        internal IReadOnlyDictionary<int, string> EnumMap { get; }

        internal WinboxJgField(string apiName, int key, string wireType, bool readOnly,
            IReadOnlyDictionary<int, string> enumMap = null)
        {
            ApiName = apiName;
            Key = key;
            WireType = wireType;
            ReadOnly = readOnly;
            EnumMap = enumMap;
        }
    }
}

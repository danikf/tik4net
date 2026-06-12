using System.Collections.Generic;

namespace tik4net.Winbox
{
    /// <summary>
    /// One field of a WinBox <c>.jg</c> catalog window: the numeric M2 key, its wire type, read-only flag,
    /// and the UI-semantic type that drives typed value encoding. Produced by <see cref="WinboxJgCatalog"/>
    /// and consumed by the field resolver to translate between API field names and M2 keys/values.
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

        /// <summary>
        /// The <c>.jg</c> UI-semantic type (<c>type:</c>): e.g. <c>ipaddr</c>, <c>network</c>, <c>macaddr</c>,
        /// <c>addr</c>, <c>enm</c>, <c>number</c>, <c>bool</c>. Drives typed wire encoding/decoding — it is
        /// more specific than <see cref="WireType"/> (an <c>ipaddr</c> rides as a u32 but must pack the IP).
        /// </summary>
        internal string UiType { get; }

        /// <summary>
        /// For <c>type:'network'</c> fields, the separate M2 key (<c>maskid</c>) that carries the netmask
        /// u32 alongside the address u32 in <see cref="Key"/>. <c>0</c> when not a network field.
        /// </summary>
        internal int MaskKey { get; }

        /// <summary>
        /// For <c>type:'network'</c> fields marked <c>range:1</c> in the <c>.jg</c> (e.g. a hotspot
        /// IP-binding address), the <see cref="MaskKey"/> sibling is NOT a netmask but the range-END address:
        /// a single host has start==end (rendered as the bare address), a span renders as <c>start-end</c>.
        /// </summary>
        internal bool IsRange { get; }

        /// <summary>
        /// For <c>type:'enm'</c> reference fields (<c>values:{type:'dynamic',path:[…]}</c>), the handler of
        /// the referenced table — the value is resolved by matching a name against that table's records and
        /// sending the referenced object's <c>.id</c>. <c>null</c> for non-reference fields.
        /// </summary>
        internal int[] RefHandler { get; }

        /// <summary>
        /// For fields wrapped in a WinBox <c>type:'opt'</c> container (an optional/toggleable field such as a
        /// firewall <c>connection-state</c> flag set), the <c>opt</c> bool key that must be set to 1 on the wire
        /// to mark the option present. <c>0</c> when the field is not opt-wrapped.
        /// </summary>
        internal int OptKey { get; }

        /// <summary>
        /// For fields wrapped in a WinBox <c>type:'not'</c> container (an invertible field), the <c>not</c> bool
        /// key carrying the negation flag (1 = negated, e.g. API <c>!established</c>). <c>0</c> when not invertible.
        /// </summary>
        internal int NotKey { get; }

        internal WinboxJgField(string apiName, int key, string wireType, bool readOnly,
            IReadOnlyDictionary<int, string> enumMap = null, string uiType = null, int maskKey = 0,
            int[] refHandler = null, int optKey = 0, int notKey = 0, bool isRange = false)
        {
            ApiName = apiName;
            Key = key;
            WireType = wireType;
            ReadOnly = readOnly;
            EnumMap = enumMap;
            UiType = uiType;
            MaskKey = maskKey;
            RefHandler = refHandler;
            OptKey = optKey;
            NotKey = notKey;
            IsRange = isRange;
        }
    }
}

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
        /// For <c>type:'addr'</c> fields, the <c>allow</c> mask that says which address FORMS the field accepts
        /// — <c>4</c> IPv4, <c>6</c> IPv6, <c>D</c> DNS name, <c>m</c> MAC, <c>R</c> route distinguisher,
        /// <c>/</c> prefix length, <c>i</c> interface suffix, <c>v</c> VRF suffix (e.g. <c>"46v%Dm"</c> on the
        /// Ping window's target). Each form rides at its OWN sub-key inside the compound, so the mask decides
        /// how a value is encoded, not merely whether it is valid. <c>null</c> for non-addr fields.
        /// </summary>
        internal string Allow { get; }

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

        /// <summary>
        /// For a <c>type:'multitristatearray'</c> field — a list whose elements may each be negated, today only
        /// <c>/system/logging</c>'s <c>topics</c> — the SECOND array key (the <c>.jg</c> <c>oid</c>) carrying the
        /// negated members, while <see cref="Key"/> carries the plain ones. <c>0</c> for every other field.
        /// </summary>
        /// <remarks>
        /// webfig <c>types.multitristatearray.put</c> splits one list into two arrays by the element's negation
        /// flag and writes both; <c>.get</c> reads both back and tags each element. So <c>topics=info,!debug</c>
        /// is <c>{id:[info], oid:[debug]}</c> on the wire — the API's leading '!' is a different KEY, not a
        /// value prefix, which is what distinguishes this from the <see cref="NotKey"/> flag on a scalar.
        /// </remarks>
        internal int OffKey { get; }

        /// <summary>
        /// The <c>.jg</c> <c>scale</c> of an <c>interval</c> field — how many wire units make one second
        /// (<c>types.interval.tostr</c> divides by <c>attrs.scale||1</c>). <c>/system/watchdog</c>'s
        /// 'Ping Start After Boot' declares <c>scale:100</c>, so its 30000 is 300 s, which the API prints as
        /// <c>5m</c>. <c>1</c> when the field declares none.
        /// </summary>
        internal int Scale { get; }

        /// <summary>
        /// For a LIST field, the <c>type</c> of its unnamed element child (<c>c:[{type:'ipaddr'},…]</c>) —
        /// <c>null</c> when the field has no child. webfig renders a list by handing each element to this
        /// type's own <c>tostr</c> (<c>types.multi.tostr</c>), so it is what decides whether a u32 element is
        /// a number, an IP or an enum member.
        /// </summary>
        internal string ElementUiType { get; }

        /// <summary>
        /// The <c>.jg</c> <c>def</c> value, when the field declares one. Only interesting for the u32
        /// <b>unset marker</b> <c>0xFFFFFFFF</c>: a field declaring <c>def:4294967295</c> (e.g. a logging
        /// action's <c>Syslog Severity</c>, whose real domain is 0–7) carries that value when it is NOT SET,
        /// and RouterOS's own API omits the field from the record rather than printing a number. An ordinary
        /// default — an IPsec proposal's <c>def:1800</c> lifetime — is a real value the API does print, which
        /// is why only the sentinel is acted on. <c>null</c> when the field declares no <c>def</c>.
        /// </summary>
        internal long? Def { get; }

        /// <summary>The u32 "not set" marker RouterOS uses in a <c>def</c> (see <see cref="Def"/>).</summary>
        internal const long UnsetSentinel = 0xFFFFFFFFL;

        /// <summary>
        /// The <c>.jg</c> <c>opt:1</c> ATTRIBUTE — "this field may legitimately have no value" — which is a
        /// different thing from the <c>type:'opt'</c> WRAPPER that carries a present-flag bool in
        /// <see cref="OptKey"/>. An attribute-optional field has no flag key: the router says "not set" by
        /// sending a value the field's own domain does not contain.
        /// </summary>
        /// <remarks>
        /// webfig's <c>types.enm.tostr</c> ends exactly on this distinction: a value with no member in the
        /// map renders as the empty string when <c>attrs.opt</c> is set, and as the literal word
        /// <c>unknown</c> when it is not. So <c>opt</c> is what separates "not set" from "we do not know
        /// this value" — and RouterOS's own API takes the first branch by omitting the field.
        /// </remarks>
        internal bool IsOptional { get; }

        /// <summary>
        /// True when <paramref name="value"/> is a number this static enum has no member for AND the field is
        /// <see cref="IsOptional"/> — i.e. the router is saying the field is NOT SET, exactly as webfig reads
        /// it. Live on 7.23.2: an unsigned <c>/certificate</c> row carries <c>Digest Algorithm</c> (map
        /// <c>{4:md5,64:sha1,672:sha256,…}</c>, <c>opt:1</c>) as <c>0</c>, and the API omits
        /// <c>digest-algorithm</c> from that row altogether.
        /// </summary>
        /// <remarks>
        /// Deliberately NOT applied to a non-optional enum: there, an unmapped value is a <c>.jg</c>/router
        /// disagreement worth surfacing rather than swallowing (P2.25), and webfig itself says <c>unknown</c>
        /// instead of nothing.
        /// </remarks>
        internal bool IsUnmappedOptionalEnum(long value)
            => IsOptional && EnumMap != null && !EnumMap.ContainsKey(unchecked((int)value));

        /// <summary>
        /// For a field declared inside a <c>type:'deck'</c> PANE — the part of a window that WinBox shows only
        /// for one KIND of record (a logging action's memory/disk/remote settings, a queue type's
        /// pcq/red/sfq parameters) — the kind's label, normalized (<c>memory</c>, <c>fq-codel</c>).
        /// <c>null</c> for an ordinary field.
        /// </summary>
        /// <remarks>
        /// RouterOS has no panes: it puts every kind's parameters in one flat record and tells them apart by
        /// prefixing the kind (<c>memory-lines</c>, <c>pcq-rate</c>). Two panes therefore routinely use the
        /// SAME label for different fields — 'Stop on Full' is <c>b4</c> for memory and <c>b6</c> for disk,
        /// and codel/fq-codel repeat all five of theirs — and a per-label field map kept only the first,
        /// leaving the rest unaddressable.
        /// </remarks>
        internal string PaneKind { get; }

        /// <summary>
        /// The M2 key of the field that SELECTS the pane (the deck's <c>selon</c>, e.g. a logging action's
        /// 'Type' or a queue type's 'Kind'), and the selector values this pane is shown for. A record whose
        /// selector value is not among them is of another kind, and RouterOS omits this field from it
        /// entirely. <c>0</c>/<c>null</c> for an ordinary field.
        /// </summary>
        internal int PaneSelectorKey { get; }

        /// <inheritdoc cref="PaneSelectorKey"/>
        internal int[] PaneValues { get; }

        /// <summary>
        /// True when this field belongs to a pane that a record with selector value
        /// <paramref name="selectorValue"/> does NOT show — i.e. the field is another kind's, and the record
        /// is not describing it.
        /// </summary>
        internal bool IsForeignPane(long selectorValue)
        {
            if (PaneValues == null || PaneValues.Length == 0) return false;
            foreach (int v in PaneValues) if (v == selectorValue) return false;
            return true;
        }

        /// <summary>
        /// True when this field declares the <see cref="UnsetSentinel"/> as its default and the catalog gives
        /// that number no label of its own — so a record carrying it is telling us the field is not set.
        /// </summary>
        internal bool IsUnsetValue(long value)
            => value == UnsetSentinel && Def == UnsetSentinel
               && (EnumMap == null || !EnumMap.ContainsKey(unchecked((int)UnsetSentinel)));

        internal WinboxJgField(string apiName, int key, string wireType, bool readOnly,
            IReadOnlyDictionary<int, string> enumMap = null, string uiType = null, int maskKey = 0,
            int[] refHandler = null, int optKey = 0, int notKey = 0, bool isRange = false,
            string allow = null, long? def = null,
            string paneKind = null, int paneSelectorKey = 0, int[] paneValues = null, int offKey = 0,
            bool isOptional = false, string elementUiType = null, int scale = 1)
        {
            Scale = scale < 1 ? 1 : scale;
            ElementUiType = elementUiType;
            IsOptional = isOptional;
            OffKey = offKey;
            PaneKind = paneKind;
            PaneSelectorKey = paneSelectorKey;
            PaneValues = paneValues;
            Def = def;
            Allow = allow;
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

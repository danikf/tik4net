using System;
using System.Collections.Generic;

namespace tik4net.Winbox
{
    /// <summary>
    /// One PART of a <c>.jg</c> list element: either a value leaf (the key it rides under, its UI-semantic
    /// type and, for a <c>network</c>/<c>network6</c>, the sibling key carrying the mask or prefix length),
    /// or a <c>type:'union'</c> — one logical value with a per-family key, of which the element carries
    /// exactly one. See <see cref="WinboxJgField.ElementParts"/>.
    /// </summary>
    internal sealed class WinboxJgElementPart
    {
        internal int Key { get; }
        internal string UiType { get; }
        internal int MaskKey { get; }

        /// <summary>
        /// For a <c>union</c> part, its members in <c>.jg</c> order; the element is rendered through the
        /// FIRST of them it actually carries, which is what webfig's <c>types.union.get</c> with
        /// <c>single:1</c> does. <c>null</c> for a value leaf.
        /// </summary>
        internal IReadOnlyList<WinboxJgElementPart>? Alternatives { get; }

        /// <summary>The part's own static enum map, when it is an <c>enm</c> — a certificate's
        /// subject-alt-name type is <c>{1:'IP',2:'DNS',3:'Email'}</c> and the API prints the WORD.</summary>
        internal IReadOnlyDictionary<int, string>? EnumMap { get; }

        /// <summary>
        /// The handler of the table a part's dropdown refers to, when it is a DYNAMIC <c>enm</c> — the same
        /// thing <see cref="WinboxJgField.RefHandler"/> is for a whole field. <c>/snmp</c>'s trap-interfaces
        /// is a list whose element is one such dropdown, and the API prints <c>ether1</c> where the element
        /// carries the interface's numeric id.
        /// </summary>
        internal int[]? RefHandler { get; }

        internal WinboxJgElementPart(int key, string uiType, int maskKey,
            IReadOnlyList<WinboxJgElementPart>? alternatives = null,
            IReadOnlyDictionary<int, string>? enumMap = null,
            int[]? refHandler = null)
        {
            Key = key; UiType = uiType; MaskKey = maskKey; Alternatives = alternatives; EnumMap = enumMap;
            RefHandler = refHandler;
        }
    }

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
        internal IReadOnlyDictionary<int, string>? EnumMap { get; }

        /// <summary>
        /// The <c>.jg</c> UI-semantic type (<c>type:</c>): e.g. <c>ipaddr</c>, <c>network</c>, <c>macaddr</c>,
        /// <c>addr</c>, <c>enm</c>, <c>number</c>, <c>bool</c>. Drives typed wire encoding/decoding — it is
        /// more specific than <see cref="WireType"/> (an <c>ipaddr</c> rides as a u32 but must pack the IP).
        /// </summary>
        internal string? UiType { get; }

        /// <summary>
        /// For <c>type:'network'</c> fields, the separate M2 key (<c>maskid</c>) that carries the netmask
        /// u32 alongside the address u32 in <see cref="Key"/>. <c>0</c> when not a network field.
        /// </summary>
        internal int MaskKey { get; }

        /// <summary>
        /// The two halves — (upload, download) — of a field the API prints as one <c>upload/download</c>
        /// pair, for the <see cref="WinboxFieldResolver.PairUiType"/> fields.
        /// </summary>
        /// <remarks>
        /// Both halves are kept, not just the second one, because each is formatted by its OWN typed field:
        /// the composite's own <see cref="UiType"/> is taken by the pairing, so it can no longer say what
        /// its values are. The halves of <c>bucket-size</c> are fixedpoints whose wire 100 is the API's 0.1
        /// — formatting either of them by anything but its own rules is wrong in a way that still looks
        /// like a number.
        /// </remarks>
        internal Tuple<WinboxJgField, WinboxJgField>? PairHalves { get; }

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
        internal int[]? RefHandler { get; }

        /// <summary>
        /// For <c>type:'addr'</c> fields, the <c>allow</c> mask that says which address FORMS the field accepts
        /// — <c>4</c> IPv4, <c>6</c> IPv6, <c>D</c> DNS name, <c>m</c> MAC, <c>R</c> route distinguisher,
        /// <c>/</c> prefix length, <c>i</c> interface suffix, <c>v</c> VRF suffix (e.g. <c>"46v%Dm"</c> on the
        /// Ping window's target). Each form rides at its OWN sub-key inside the compound, so the mask decides
        /// how a value is encoded, not merely whether it is valid. <c>null</c> for non-addr fields.
        /// </summary>
        internal string? Allow { get; }

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
        /// The <c>.jg</c> <c>postfix</c> — the UNIT a numeric field is expressed in (<c>'s'</c>,
        /// <c>'min'</c>, …). <c>null</c> when the field declares none.
        /// </summary>
        /// <remarks>
        /// webfig does NOT put it in <c>tostr</c>: the view appends it beside the input box, so a
        /// <c>postfix</c> field's <c>tostr</c> is the bare number. RouterOS's API has no such split — it
        /// prints a duration as a duration — so <c>/ip/ipsec/profile</c>'s <c>dpd-interval</c> (an
        /// <c>enm</c>, <c>def:8</c>, <c>postfix:'s'</c>, whose only enum member is <c>disable-dpd</c> at 0)
        /// reads <c>8s</c> over the API and read a bare <c>8</c> here. Only <c>'s'</c> is acted on, and only
        /// when the value falls through the enum map — the postfix says what the NUMBER means, which is
        /// nothing to a value the map has already named.
        /// </remarks>
        internal string? Postfix { get; }

        /// <summary>
        /// For a LIST field, the <c>type</c> of its unnamed element child (<c>c:[{type:'ipaddr'},…]</c>) —
        /// <c>null</c> when the field has no child. webfig renders a list by handing each element to this
        /// type's own <c>tostr</c> (<c>types.multi.tostr</c>), so it is what decides whether a u32 element is
        /// a number, an IP or an enum member.
        /// </summary>
        internal string? ElementUiType { get; }

        /// <summary>
        /// For a LIST whose element is a <c>type:'union'</c> or a <c>type:'tuple'</c>, the element's parts in
        /// <c>.jg</c> order. <c>null</c> for every other field — including a list of plain scalars, which
        /// <see cref="ElementUiType"/> already describes.
        /// </summary>
        /// <remarks>
        /// <para><see cref="ElementUiType"/> answers "union" or "tuple", which says nothing about how an
        /// element reads. Both shapes are live on 7.23.2 and both were reaching the caller as raw numbers:
        /// <c>/snmp/community</c>'s <c>Addresses</c> is a <c>multi</c> of <c>union{network u8/u9,
        /// network6 a16/u17}</c> (the generic nested-message fallback returned <c>::</c> where the API prints
        /// <c>::/0</c>), and <c>/certificate</c>'s <c>Subject Alt. Name</c> is a <c>multi</c> of
        /// <c>tuple{enm u7f, union{ip6addr a7e, ipaddr u7d, string}}</c> joined by <c>':'</c> — which read as
        /// the bare u32 <c>3959728320</c> where the API prints <c>IP:192.168.4.236</c>.</para>
        /// <para>The two are one shape because <c>types.tuple.tostr</c> renders each child through its own
        /// type and joins them with <see cref="ElementSeparator"/>, dropping the parts that render empty —
        /// so a union is simply a tuple of one part with alternatives.</para>
        /// </remarks>
        internal IReadOnlyList<WinboxJgElementPart>? ElementParts { get; }

        /// <summary>
        /// The <c>.jg</c> <c>sep</c> of a <c>tuple</c> element — what joins its parts (<c>':'</c> on a
        /// certificate's subject-alt-name). webfig's default is <c>'/'</c>; <c>null</c> when the element is
        /// not a tuple.
        /// </summary>
        internal string? ElementSeparator { get; }

        /// <summary>
        /// For a message-array list whose element is wrapped in a <c>type:'not'</c>, the M2 key of the
        /// per-element negation flag inside the element's submessage; 0 when it is not wrapped.
        /// </summary>
        /// <remarks>
        /// <c>types.not.tostr</c> renders the element as <c>(flag ? '!' : '') + inner.tostr(…)</c>, so the
        /// flag is a PREFIX on one element rather than on the whole field (contrast <see cref="NotKey"/>,
        /// which negates a scalar, and <see cref="OffKey"/>, where the negated members ride in a second
        /// array). <c>/tool/sniffer</c>'s filters are the live shape:
        /// <c>filter-ip-address="!192.168.251.0/24,10.0.0.1/32"</c> is one message array whose two elements
        /// carry <c>b1=true</c> and <c>b1=false</c>.
        /// </remarks>
        internal int ElementNotKey { get; }

        /// <summary>
        /// The <c>range:1</c> of a LIST's element — whether the second half of each pair is the range END
        /// rather than a netmask. The field-level analogue is <see cref="IsRange"/>; a list declares it on
        /// the element (<c>{type:'network',range:1}</c>), because that is whose <c>tostr</c> reads it.
        /// </summary>
        internal bool ElementIsRange { get; }

        /// <summary>
        /// The API name taken from this declaration's own <c>.jg</c> <c>title</c>, when it has one that
        /// differs from its <c>name</c>; <c>null</c> otherwise.
        /// </summary>
        /// <remarks>
        /// On a FIELD the two are not two spellings of one label: the <c>name</c> is what WinBox paints
        /// beside the box and the <c>title</c> is what RouterOS calls the field —
        /// <c>{name:'Type',title:'Target'}</c> on <c>/system/logging/action</c> is <c>target</c> to the API,
        /// and <c>type</c> is not a field of that table at all. Kept so the harvest can settle the commonest
        /// shape of it: two declarations on ONE key, one conditional and prefixed 'Old'
        /// (<c>{name:'Old Cache Path',title:'Cache Path',on:'oldfileman'}</c> beside
        /// <c>{name:'Cache Path',on:'newfileman'}</c>), where the title says which of the two is the API's.
        /// </remarks>
        internal string? TitleApiName { get; }

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
        /// disagreement worth surfacing rather than swallowing, and webfig itself says <c>unknown</c>
        /// instead of nothing.
        /// <para>Nor to a field whose members come from a dynamic REFERENCE
        /// (<see cref="RefHandler"/>). There the static map is not the domain — it is a <c>defenum</c>
        /// sentinel sitting in front of the referenced list — so a value it has no member for is a
        /// reference id, not "not set". <c>/caps-man/manager</c>'s <c>Certificate</c> is
        /// <c>{def:4294967295,opt:1,values:{defenum defid:0 defname:'auto',values:{dynamic [19,1]}}}</c>:
        /// with a certificate assigned the router sends its id and the API prints the certificate's NAME,
        /// while this rule read the id as unset and dropped the field.</para>
        /// </remarks>
        internal bool IsUnmappedOptionalEnum(long value)
            => IsOptional && EnumMap != null && RefHandler == null
               && !EnumMap.ContainsKey(unchecked((int)value));

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
        internal string? PaneKind { get; }

        /// <summary>
        /// The M2 key of the field that SELECTS the pane (the deck's <c>selon</c>, e.g. a logging action's
        /// 'Type' or a queue type's 'Kind'), and the selector values this pane is shown for. A record whose
        /// selector value is not among them is of another kind, and RouterOS omits this field from it
        /// entirely. <c>0</c>/<c>null</c> for an ordinary field.
        /// </summary>
        internal int PaneSelectorKey { get; }

        /// <inheritdoc cref="PaneSelectorKey"/>
        internal int[]? PaneValues { get; }

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
        /// <remarks>
        /// A <c>netmask</c> is exempt, because for one all-ones is not a sentinel but a VALUE:
        /// <c>types.netmask.tostr</c> is <c>netmask2len(val)</c>, so 4294967295 is <c>/32</c>. All five
        /// netmask fields in the 7.24 catalog declare it as their default, and RouterOS prints it — the
        /// three stock pcq queue types answer <c>pcq-src-address-mask=32</c> where this rule dropped the
        /// field entirely.
        /// </remarks>
        internal bool IsUnsetValue(long value)
            => value == UnsetSentinel && Def == UnsetSentinel
               && !string.Equals(UiType, "netmask", StringComparison.OrdinalIgnoreCase)
               && (EnumMap == null || !EnumMap.ContainsKey(unchecked((int)UnsetSentinel)));

        internal WinboxJgField(string apiName, int key, string wireType, bool readOnly,
            IReadOnlyDictionary<int, string>? enumMap = null, string? uiType = null, int maskKey = 0,
            int[]? refHandler = null, int optKey = 0, int notKey = 0, bool isRange = false,
            string? allow = null, long? def = null,
            string? paneKind = null, int paneSelectorKey = 0, int[]? paneValues = null, int offKey = 0,
            bool isOptional = false, string? elementUiType = null, int scale = 1,
            IReadOnlyList<WinboxJgElementPart>? elementParts = null, string? postfix = null,
            string? elementSeparator = null, Tuple<WinboxJgField, WinboxJgField>? pairHalves = null,
            int elementNotKey = 0, bool elementIsRange = false, string? titleApiName = null,
            IReadOnlyList<WinboxJgField>? extraRegistrations = null)
        {
            TitleApiName = titleApiName;
            ExtraRegistrations = extraRegistrations;
            PairHalves = pairHalves;
            ElementNotKey = elementNotKey;
            ElementIsRange = elementIsRange;
            ElementSeparator = elementSeparator;
            Postfix = postfix;
            ElementParts = elementParts;
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

        /// <summary>
        /// Further <c>key → field</c> registrations this ONE declaration makes, for a field whose value does
        /// not live at a single key. <c>null</c> for the ordinary case.
        /// </summary>
        /// <remarks>
        /// Two shapes need it, and both are declarations where the LABEL is on the parent and the ids are on
        /// the children (see the M2 protocol findings, §32.7).
        /// <para>A <c>union</c> with <c>single:1</c> is one field with alternative wire encodings, one per
        /// address family, of which the router sends whichever it has: <c>/snmp</c>'s 'Src. Address' is
        /// <c>{ipaddr u1b, ip6addr a1c}</c> and the row carries <c>0x1C</c>. Only the FIRST family was
        /// registered, so a row that used any other one had no field at that key and the API name went
        /// unreported. Each alternative is registered here with its OWN type and <c>maskid</c> — a
        /// <c>network6</c> renders through its prefix length, a <c>network</c> through its mask.</para>
        /// <para>A <c>tuple</c> is one field the API prints as its children joined by the declaration's
        /// <c>sep</c>: <c>/ip/service</c>'s 'Remote' is <c>{ip6addr ad, number ue}</c> printed
        /// <c>192.168.4.31:65504</c>. Every part key registers the SAME compound field, so the row decodes
        /// to the whole value whichever part the reader reaches first, and the parts do not also surface as
        /// fields of their own.</para>
        /// </remarks>
        internal IReadOnlyList<WinboxJgField>? ExtraRegistrations { get; }

        /// <summary>
        /// The same field under a different API name — for a field a window gives a label another field of
        /// that window already claimed, and which is therefore reachable only under a qualified name.
        /// </summary>
        internal WinboxJgField WithApiName(string apiName)
            => new WinboxJgField(apiName, Key, WireType, ReadOnly, EnumMap, UiType, MaskKey, RefHandler,
                OptKey, NotKey, IsRange, Allow, Def, PaneKind, PaneSelectorKey, PaneValues, OffKey,
                IsOptional, ElementUiType, Scale, ElementParts, Postfix, ElementSeparator, PairHalves,
                ElementNotKey, ElementIsRange, TitleApiName, ExtraRegistrations);
    }
}

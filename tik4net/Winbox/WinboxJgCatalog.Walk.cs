using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using tik4net.Diagnostics;

namespace tik4net.Winbox
{
    internal sealed partial class WinboxJgCatalog
    {
        // ── Tree walk + field extraction (port of jg_analyze.py walk) ──────────

        // What a `type:'deck'` pane tells us about the fields inside it: which KIND of record they belong to,
        // and how a record says it is of that kind. Carried down the walk so a field several levels inside a
        // pane still knows (a pane's children are wrapped in tabs, groups and opt containers).
        private sealed class PaneContext
        {
            internal readonly string? Kind;     // normalized selector label ("memory", "fq-codel"); null when
                                                // the pane covers several selector values and so has no one kind
            internal readonly int SelectorKey;  // M2 key of the deck's `selon` field
            internal readonly int[] Values;     // the pane's `vals`
            internal PaneContext(string? kind, int selectorKey, int[] values)
            { Kind = kind; SelectorKey = selectorKey; Values = values; }
        }

        private void Walk(object? node, string? handlerKey, List<string> crumb, PaneContext? pane = null,
            string? tab = null)
        {
            if (node is Dictionary<string, object> flagOnly && _flagOnlyNodes.Contains(flagOnly))
                return;   // the negation flag of its group's value (InvertSibling), not a field
            if (node is Dictionary<string, object> dict)
            {
                string? owner = handlerKey;
                int[]? handlerInts = null;
                if (dict.TryGetValue("path", out var pv) && pv is List<object> pathList && pathList.Count > 0)
                {
                    var ints = new List<int>();
                    bool ok = true;
                    foreach (var p in pathList)
                    {
                        if (p is int pi) ints.Add(pi);
                        else { ok = false; break; }
                    }
                    if (ok) { handlerInts = ints.ToArray(); owner = HandlerKey(handlerInts); }
                }

                string nodeName = (dict.TryGetValue("name", out var nnv) && nnv is string nns && nns.Length > 0)
                    ? nns
                    : (dict.TryGetValue("title", out var ntv) && ntv is string nts ? nts : "");
                string? groupSeg = (dict.TryGetValue("group", out var gv) && gv is string gs && gs.Length > 0)
                    ? WinboxFieldResolver.NormalizeLabel(gs) : null;
                string? ty = dict.TryGetValue("type", out var tyv) && tyv is string tys ? tys : null;

                // Harvest derived menu-label path → handler for WINDOW nodes (map/query/item/doit/action
                // with a handler path). Dropdown references (type:'enm',values:{type:'dynamic',path:…}) reuse
                // the same handler but are NOT windows — skip them. The key is the full breadcrumb
                // (ancestor menu chain + this node's group + name).
                if (handlerInts != null && ty != null && WindowTypes.Contains(ty))
                {
                    string? apiPath = BuildPath(crumb, groupSeg, nodeName);
                    if (apiPath != null && !_derivedPaths.ContainsKey(apiPath))
                    {
                        _derivedPaths[apiPath] = handlerInts;
                        // Record what THIS window is, not what its handler is — see _singletonPaths.
                        if (ty == "item" || ty == "map" || ty == "query")
                            _singletonPaths[apiPath] = ty == "item";
                        if (ty == "item")
                        {
                            int? getCmd = dict.TryGetValue("getcmd", out var gcv) && gcv is int gc ? gc : (int?)null;
                            int? setCmd = dict.TryGetValue("setcmd", out var scv) && scv is int sc ? sc : (int?)null;
                            if (getCmd != null || setCmd != null)
                                _singletonCommands[apiPath] = Tuple.Create(getCmd, setCmd);
                        }
                    }
                    // Attribute this window's fields to the window as well as to its handler, so a caller
                    // addressing the window is answered by the window's own vocabulary. Windows sharing a
                    // handler number their fields from scratch and collide — see GetWindowFields.
                    if (apiPath != null)
                    {
                        string wk = WindowKey(apiPath);
                        // owner is non-null here: this block requires handlerInts != null, and owner is set
                        // (unconditionally, non-null) to HandlerKey(handlerInts) whenever handlerInts != null.
                        _windowHandlerKey[wk] = owner!;
                        _handlerBackedWindows.Add(wk);
                        owner = wk;
                    }
                    // The row's display name, as the window declares it (see _nameValByHandler).
                    if (dict.TryGetValue("nameval", out var nvv) && nvv is string nvs && nvs.Length > 0
                        && !_nameValByHandler.ContainsKey(HandlerKey(handlerInts)))
                        _nameValByHandler[HandlerKey(handlerInts)] = WinboxFieldResolver.NormalizeLabel(nvs);
                    if (ty == "item") _singletonHandlers.Add(HandlerKey(handlerInts));
                    // Track record-listing vs action-only windows separately so a handler that has BOTH (e.g.
                    // [20,125] = 'SMS Message' map + 'Send SMS' doit) is never mistaken for action-only.
                    if (ty == "doit" || ty == "action") _actionWindowHandlers.Add(HandlerKey(handlerInts));
                    else _recordWindowHandlers.Add(HandlerKey(handlerInts));
                    if (dict.ContainsKey("autorefresh")) _dynamicHandlers.Add(HandlerKey(handlerInts));
                    int readFlags = (dict.TryGetValue("refreshfilter", out var rfv) && rfv is int rf ? rf : 0)
                                  | (dict.TryGetValue("refetchonopen", out var rov) && rov is int ro ? ro : 0);
                    if (readFlags != 0)
                    {
                        _readFlagsByHandler.TryGetValue(HandlerKey(handlerInts), out int known);
                        _readFlagsByHandler[HandlerKey(handlerInts)] = known | readFlags;
                    }
                    HarvestMonitor(handlerInts, ty, dict);

                    // The base interface window (generic:'iface') anchors the subtype filtering: remember its
                    // handler and the key of the `type` discriminator field (named by `typeon`, default 'type').
                    if (dict.TryGetValue("generic", out var genv) && genv is string gen && gen.Length > 0)
                    {
                        string typeon = dict.TryGetValue("typeon", out var tov) && tov is string tos ? tos : "type";
                        int tk = FindChildFieldKey(dict, typeon);
                        if (gen == "iface")
                        {
                            _genericIfaceHandler = handlerInts;
                            if (tk != 0) _ifaceTypeKey = tk;
                        }
                        // A base that carries BOTH a handler and a resolvable discriminator can filter its own
                        // children; one that carries neither (the ppp/wlan mid-tree bases) is handled by the
                        // interface-family chain below and must not overwrite an entry with a real key.
                        if (tk != 0 && !_genericBases.ContainsKey(gen))
                            _genericBases[gen] = Tuple.Create(handlerInts, tk);
                    }
                }

                // A window declaring `generic:'X'` is a BASE others extend with `inherit:'X'`. Remember what
                // discriminator it hands its children (`typeon`, default 'type') and whether it belongs to the
                // interface family, so a chain (iface → ppp → 'L2TP Client') resolves. Recorded outside the
                // window block above because these bases often have no `path:` of their own.
                if (dict.TryGetValue("generic", out var genv2) && genv2 is string genName && genName.Length > 0
                    && !_generics.ContainsKey(genName))
                {
                    string typeonChild = dict.TryGetValue("typeon", out var tov2) && tov2 is string tos2 ? tos2 : "type";
                    bool ifaceFamily = genName == "iface"
                        || (dict.TryGetValue("inherit", out var ginh) && ginh is string ginhs
                            && _generics.TryGetValue(ginhs, out var gbase) && gbase.Item2);
                    _generics[genName] = Tuple.Create(typeonChild, ifaceFamily);
                }

                // Interface subtype window: inherit:'<base>' (reuse the generic interface handler, has no path:
                // of its own) + typevalue:N (the discriminator value). Register a derived path → the generic
                // handler, plus a subtype filter (typeKey, N). The leaf is the window TITLE — `name` is the
                // generic 'Interface' shared by every subtype, so it cannot discriminate; the title carries the
                // subtype ('Bridge', 'VLAN', 'L2TP Client', …).
                //
                // Two constraints keep this honest. The base must be in the interface family, so an unrelated
                // inherit tree cannot be filtered against the interface `type` field. And the base must hand its
                // children the 'type' discriminator: the wireless tree switches to `typeon:'hwtype'` below the
                // 'Wireless' window, and those hardware variants (Atheros AR5212, …) are NOT addressable by an
                // interface type filter — registering them would answer with a filter on the wrong field.
                // A wildcard `typevalue:4294967295` (a base standing for "any of my subtypes") is not an int
                // after parsing and is skipped here for the same reason: it is a set, not a value.
                if (handlerInts == null && _genericIfaceHandler != null && _ifaceTypeKey != 0
                    && dict.TryGetValue("inherit", out var inhv) && inhv is string inh
                    && dict.TryGetValue("typevalue", out var tvv) && tvv is int tval
                    && (inh == "iface"
                        || (_generics.TryGetValue(inh, out var baseGen) && baseGen.Item2 && baseGen.Item1 == "type")))
                {
                    string title = dict.TryGetValue("title", out var ttv) && ttv is string tts && tts.Length > 0
                        ? tts : nodeName;
                    string? apiPath = BuildPath(crumb, groupSeg, title);
                    if (apiPath != null)
                    {
                        if (!_derivedPaths.ContainsKey(apiPath))
                        {
                            _derivedPaths[apiPath] = _genericIfaceHandler;
                            _subtypeFilters[apiPath] = Tuple.Create(_ifaceTypeKey, tval);
                            _singletonPaths[apiPath] = false;   // a subtype window is always a record list
                        }
                        // Attribute this window's fields to the WINDOW, not to the shared [20,0] handler.
                        // The subtypes disagree about keys under identical labels — 'Remote Address' is
                        // 0x7E1 on EoIP, 0x7E5 on GRE and 0x7E3 on IPIP, 'Local Address' is a u32 on EoIP
                        // and an addr on VXLAN — so merging them into one per-handler map would hand every
                        // subtype but the first-parsed one a wrong key: a write that lands on another field.
                        // (Before this, these fields were attributed to the enclosing menu node, i.e. to no
                        // handler at all, and were simply lost — every subtype `add` failed with "cannot
                        // resolve API field 'remote-address'".)
                        owner = WindowKey(apiPath);
                        // Known to read the generic interface handler, but deliberately NOT handler-backed:
                        // see _handlerBackedWindows.
                        _windowHandlerKey[owner] = HandlerKey(_genericIfaceHandler);
                    }
                }

                // The same registration for a NON-interface family whose base owns its handler and its
                // discriminator (_genericBases). The interface branch above is left exactly as it is: its
                // guards were written against a tree that is several levels deep and switches discriminator
                // half-way down, and nothing here needs to disturb them.
                //
                // /ip/route is the case. WinBox has one routes table ([44,21], 'All Routes', typeon:'rtype')
                // and two subtype windows over it — 'Routes' (typevalue 2, IPv4) and ipv6.jg's 'Routes6'
                // (typevalue 3). Addressing the BASE returns both families: on the lab CHR a /ip/route read
                // came back with six rows where the API returns two, the extras being ::1/128 and fe80::/64.
                // roteros.jg is parsed first by design, so the base is registered before ipv6.jg inherits it.
                if (handlerInts == null
                    && dict.TryGetValue("inherit", out var ginhv) && ginhv is string ginh2
                    && dict.TryGetValue("typevalue", out var gtvv) && gtvv is int gtval
                    && !(ginh2 == "iface" || (_generics.TryGetValue(ginh2, out var gfam) && gfam.Item2))
                    && _genericBases.TryGetValue(ginh2, out var gbase2))
                {
                    string gtitle = dict.TryGetValue("title", out var gttv) && gttv is string gtts && gtts.Length > 0
                        ? gtts : nodeName;
                    string? gApiPath = BuildPath(crumb, groupSeg, gtitle);
                    if (gApiPath != null)
                    {
                        if (!_derivedPaths.ContainsKey(gApiPath))
                        {
                            _derivedPaths[gApiPath] = gbase2.Item1;
                            _subtypeFilters[gApiPath] = Tuple.Create(gbase2.Item2, gtval);
                            _singletonPaths[gApiPath] = false;   // a subtype window is always a record list
                        }
                        // Its fields belong to the WINDOW: the subtype declares the whole General/Status tab
                        // the base only sketches (Distance, Scope, Target Scope, VRF Interface, Routing
                        // Table, Immediate Gateway, Pref. Source), and the base's bare numeric 'active' is
                        // the subtype's 'Contribution' enum.
                        owner = WindowKey(gApiPath);
                        _windowHandlerKey[owner] = HandlerKey(gbase2.Item1);
                    }
                }

                // A named opt/not wrapper (type:'opt'/'not' with children) holds its real value in an inner leaf;
                // the wrapper's own id is just a flag bool (opt=present, not=negated). Map the API name to the
                // inner value leaf, carrying the flag keys — e.g. firewall 'Connection State' (opt→not→set) maps
                // to the inner bitmask, not the outer bool. (A childless opt is a plain bool — handled below.)
                //
                // A `group` that carries an ID is the same shape under another word: 97 of them in the 7.24
                // catalog, every one a firewall or bridge-filter MATCH — 'MAC Protocol' is
                // {group,id:'bcc',c:[{not,id:'bd',c:[{name:'MAC Protocol-Num',number,id:'u5',…}]}]}. Without
                // this the group registered as a plain bool and took the name first, so `mac-protocol` meant
                // the present FLAG: the read answered 'true' and a write of `arp` sent bool:False. A group
                // with no id (64 more) is pure layout and its children are fields in their own right.
                if (owner != null && ty == "cond" && !string.IsNullOrEmpty(nodeName))
                {
                    if (!_condNodes.TryGetValue(owner, out var ownConds))
                        _condNodes[owner] = ownConds = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
                    if (!ownConds.ContainsKey(nodeName)) ownConds[nodeName] = dict;
                }
                // Whatever the chain below registers for this node is governed by the node's `on:`.
                _currentOn = owner != null && dict.TryGetValue("on", out var onv) && onv is string onName ? onName : null;
                if (owner != null && (ty == "opt" || ty == "not" || (ty == "group" && dict.ContainsKey("id")))
                    && dict.ContainsKey("c") && !string.IsNullOrEmpty(nodeName))
                {
                    AddOptionField(owner, nodeName, dict, pane);
                }
                // A named `union` holds one value that can be of several families, each child carrying its
                // OWN key — IPsec 'Address' is {union, c:[network u1, network6 a17, string s2f]}. The union
                // node itself has no id, and its children have no name, so without this the field is not in
                // the catalog at all: every dual-stack address field (IPsec peer/policy/identity, graphing
                // 'Allow Address', traffic-flow 'Src./Dst. Address', …) answered "cannot resolve API field".
                // The first child is registered, i.e. the IPv4 family — an IPv6 value then fails to encode
                // with the codec's own error rather than being sent on the wrong key.
                else if (owner != null && ty == "union" && !dict.ContainsKey("id")
                         && !string.IsNullOrEmpty(nodeName))
                {
                    AddUnionField(owner, nodeName, dict, pane);
                }
                // A named `tuple` is the same problem with a different answer: the label is on the node, the
                // ids are on the children, and the API prints the children joined by the node's `sep`.
                else if (owner != null && ty == "tuple" && !dict.ContainsKey("id")
                         && !string.IsNullOrEmpty(nodeName))
                {
                    AddTupleField(owner, nodeName, dict, pane);
                }
                // A numflag names a SET of row flags on one numeric key and carries no name itself, so it is
                // not a field — kept apart, and decoded into the flag the row's value names (see GetNumFlags).
                else if (owner != null && ty == "numflag"
                         && dict.TryGetValue("id", out var nfIdv) && nfIdv is string nfId)
                {
                    AddNumFlag(owner, nfId, dict);
                }
                else if (owner != null && dict.TryGetValue("id", out var idv) && idv is string idStr)
                {
                    var dec = DecodeId(idStr);
                    if (dec != null)
                    {
                        bool ro = dict.TryGetValue("ro", out var rov) && rov is int rin && rin != 0;
                        int maskKey = (dict.TryGetValue("maskid", out var mkv) && mkv is string mks
                            && DecodeId(mks) is var md && md != null) ? md.Value.key
                            : DecodedKeyOf(dict, "portid") is var pk && pk != 0 ? pk : HighKeyOf(dict);
                        int[]? refHandler = ExtractRefHandler(dict);
                        bool isRange = dict.TryGetValue("range", out var rgv) && rgv is int rgi && rgi != 0;
                        // A LIST's allow-mask lives on the element, not on the list node: wireguard's
                        // 'Allowed Address' is {multi,id:'M3f0',c:[{addr,allow:'46/'}]}, and reading only the
                        // node left the encoder with the default mask — which has no '/', so every write
                        // dropped the prefix length and 10.99.2.0/24 landed as /32.
                        string? allow = (dict.TryGetValue("allow", out var alv) ? alv as string : null)
                                        ?? ElementAllowOf(dict);
                        // A list field carries its own present-flag on the node (`optid`) rather than in an
                        // enclosing opt wrapper, and webfig writes it from the list's LENGTH
                        // (types.multi.put: obj[optid] = val.length>0). Same meaning as OptKey, same
                        // encoder — without it the 21 firewall/bridge match lists (dst-port, protocol,
                        // vlan-id, …) reach the router with the option down and are ignored.
                        int optKey = DecodedKeyOf(dict, "optid");
                        // The negated half of a multitristatearray (see WinboxJgField.OffKey).
                        int offKey = DecodedKeyOf(dict, "oid");
                        AddField(owner, nodeName, dec.Value.key, dec.Value.type, ro, ExtractEnumMap(dict),
                            ty, maskKey, refHandler, optKey: optKey, isRange: isRange, allow: allow,
                            def: ExtractDef(dict), pane: pane, offKey: offKey,
                            isOptional: IsOptionalAttr(dict), elementUiType: ElementUiTypeOf(dict),
                            scale: ScaleOf(dict), elementParts: ElementPartsOf(dict),
                            postfix: PostfixOf(dict), elementSeparator: ElementSeparatorOf(dict),
                            elementNotKey: ElementNotKeyOf(dict), elementIsRange: ElementIsRangeOf(dict),
                            tab: tab, title: TitleOf(dict),
                            nonPublic: dict.TryGetValue("nonpublic", out var npv) && npv is int npi && npi != 0,
                            min: dict.TryGetValue("min", out var mnv) && mnv is int mni ? mni : 0,
                            radix: RadixOf(dict), elementScale: ElementScaleOf(dict),
                            relative: RelativeOf(dict), refHandlers: ExtractRefHandlers(dict),
                            elementStatusParts: ElementStatusPartsOf(dict));
                    }
                }

                _currentOn = null;

                // Per-record action verb: a named doit/action carrying a cmd (no own field id) invokes that
                // SYS_CMD on the owning handler against a record .id — e.g. the scripts window's
                // {name:'Run Script',type:'doit',cmd:1} on [48,101]. Register it for /system/script/run dispatch.
                if (owner != null && (ty == "doit" || ty == "action") && !dict.ContainsKey("id")
                    && dict.TryGetValue("cmd", out var cmdv) && cmdv is int cmdN && !string.IsNullOrEmpty(nodeName))
                {
                    // Actions are looked up BY HANDLER (GetActionFields/GetHandlerActions), so an action
                    // declared inside a window is still keyed by the handler that window reads.
                    // owner is non-null here (checked above), so HandlerOfOwner always returns non-null too.
                    string actionOwner = HandlerOfOwner(owner)!;
                    string? actionLabel = AddAction(actionOwner, nodeName, cmdN);
                    // Descend into the action's ARGUMENTS under a key of their own. They keep going into the
                    // handler's map as well (AddField writes both, so nothing that resolved before stops
                    // resolving), but the action's own map is what the caller invoking it resolves against —
                    // otherwise an argument sharing a label with a record column loses to it. See GetActionFields.
                    if (actionLabel != null) owner = ActionKey(actionOwner, actionLabel);
                }

                // Descend. A structural menu node (has children, no field id, has a name) extends the
                // breadcrumb so its descendant windows derive a nested path.
                List<string> childCrumb = crumb;
                if (dict.ContainsKey("c") && !dict.ContainsKey("id") && !string.IsNullOrEmpty(nodeName))
                {
                    childCrumb = new List<string>(crumb);
                    if (groupSeg != null) childCrumb.Add(groupSeg);
                    string leaf = WinboxFieldResolver.NormalizeLabel(nodeName);
                    if (leaf.Length > 0) childCrumb.Add(leaf);
                }

                // A deck's panes are walked here, each with its own pane context, so their fields carry the
                // kind they belong to. A window of its own inside a pane starts fresh (handlerInts != null).
                if (ty == "deck" && handlerInts == null)
                {
                    WalkDeck(dict, owner, childCrumb);
                    return;
                }

                // A `separate:1` tuple's parts are fields of their own, walked below — and the unit is declared
                // once, on the tuple: /queue/simple 'Burst Time' is {tuple,postfix:'s',c:[{Upload Burst Time,
                // number},{Download Burst Time,number}]}, so without it the halves read 7 and 9 where the API
                // prints burst-time=7s/9s (7.24.2). A part that declares its own postfix keeps it.
                if (ty == "tuple" && dict.TryGetValue("postfix", out var tpf) && tpf is string tupPostfix
                    && dict.TryGetValue("c", out var tcv) && tcv is List<object> tupParts)
                    foreach (var part in tupParts)
                        if (part is Dictionary<string, object> pd && !pd.ContainsKey("postfix"))
                            pd["postfix"] = tupPostfix;

                foreach (var kv in dict)
                {
                    if (kv.Key == "id") continue;
                    Walk(kv.Value, owner, childCrumb, handlerInts != null ? null : pane,
                        handlerInts != null ? null : tab);
                }
            }
            else if (node is List<object> list)
            {
                // A tab is a SIBLING of the fields it heads, not their parent, so the current tab is carried
                // along the list rather than down a subtree. It is what tells apart two fields a window gives
                // the same label - and what the API prefixes the second one with (see AddField).
                string? currentTab = tab;
                foreach (var it in list)
                {
                    if (it is Dictionary<string, object> td
                        && td.TryGetValue("type", out var ttv) && (ttv as string) == "tab"
                        && td.TryGetValue("name", out var tnv) && tnv is string tn)
                        currentTab = WinboxFieldResolver.NormalizeLabel(tn);
                    Walk(it, handlerKey, crumb, pane, currentTab);
                }
            }
        }

        /// <summary>
        /// Walks a <c>type:'deck'</c> node: resolves the selector field named by <c>selon</c> (its M2 key and
        /// its value labels) among the fields already harvested for this handler, then walks each pane with a
        /// <see cref="PaneContext"/> naming the kind it belongs to.
        /// </summary>
        /// <remarks>
        /// The selector is looked up in the handler's own field map rather than by re-reading the .jg, so it
        /// works wherever the deck sits relative to its selector — in every window seen on 7.23.2 the selector
        /// ('Type', 'Kind') is declared before the deck. A deck whose selector or labels cannot be resolved is
        /// walked as an ordinary node: its fields keep the plain names they have always had.
        /// </remarks>
        private void WalkDeck(Dictionary<string, object> deck, string? owner, List<string> crumb)
        {
            int selectorKey = 0;
            IReadOnlyDictionary<int, string>? kinds = null;
            if (deck.TryGetValue("selon", out var sv) && sv is string selon
                && owner != null && _byHandler.TryGetValue(owner, out var known)
                && known.TryGetValue(WinboxFieldResolver.NormalizeLabel(selon), out var selField))
            {
                selectorKey = selField.Key;
                kinds = selField.EnumMap;
            }

            // A field one pane declares and others merely list as {name:'Passthrough',type:'alias'} is shown for every
            // one of those panes: mangle's passthrough is declared in the mark-packet pane and aliased by
            // mark-connection, mark-routing and the rest. Without the aliases the field would count as mark-packet's
            // alone, and the decoder would drop it from every other kind's row, where the router does send it.
            var outerAliases = _deckAliasValues;
            _deckAliasValues = CollectDeckAliases(deck);
            try
            {
            if (deck.TryGetValue("panes", out var pv2) && pv2 is List<object> panes)
                foreach (var p in panes)
                {
                    if (!(p is Dictionary<string, object> pd)) continue;
                    var vals = new List<int>();
                    if (pd.TryGetValue("vals", out var vv) && vv is List<object> vl)
                        foreach (var v in vl) if (v is int vi) vals.Add(vi);

                    // One kind per pane — but only a pane shown for a SINGLE selector value has one to be
                    // named after. A pane covering several (IPsec identity's 'My ID' is shown for fqdn, user
                    // fqdn AND key id) is walked without a kind: its fields keep their plain names, and only
                    // the "which kind is this record" filter applies to them. That filter needs the selector
                    // and the values, not the label, so it is deliberately not conditioned on the kind.
                    string? kind = null;
                    if (kinds != null && vals.Count == 1 && kinds.TryGetValue(vals[0], out var label))
                        kind = label;

                    var ctx = (selectorKey != 0 && vals.Count > 0)
                        ? new PaneContext(kind, selectorKey, vals.ToArray())
                        : null;
                    if (pd.TryGetValue("c", out var pc)) Walk(pc, owner, crumb, ctx);
                }
            }
            finally { _deckAliasValues = outerAliases; }
        }

        // The raw .jg label each registered field was declared with ('New Packet Mark'), by field instance — for
        // audits and the entity generator (TikPropertyAttribute.WinboxLabel). Not used to resolve anything.
        private readonly Dictionary<WinboxJgField, string> _labels = new Dictionary<WinboxJgField, string>();

        /// <summary>The <c>.jg</c> label a field was declared with, as WinBox shows it; <c>null</c> when unknown.</summary>
        internal string? LabelOf(WinboxJgField field) => _labels.TryGetValue(field, out string? label) ? label : null;

        // Normalized label -> the selector values of every pane of the deck being walked that lists it as an alias.
        private Dictionary<string, List<int>>? _deckAliasValues;

        private static Dictionary<string, List<int>>? CollectDeckAliases(Dictionary<string, object> deck)
        {
            Dictionary<string, List<int>>? result = null;
            if (!(deck.TryGetValue("panes", out var pv) && pv is List<object> panes)) return null;
            foreach (var p in panes)
            {
                if (!(p is Dictionary<string, object> pd)) continue;
                var vals = new List<int>();
                if (pd.TryGetValue("vals", out var vv) && vv is List<object> vl)
                    foreach (var v in vl) if (v is int vi) vals.Add(vi);
                if (vals.Count == 0 || !pd.TryGetValue("c", out var pc)) continue;
                foreach (string name in AliasNames(pc))
                {
                    result ??= new Dictionary<string, List<int>>(StringComparer.Ordinal);
                    string label = WinboxFieldResolver.NormalizeLabel(name);
                    if (!result.TryGetValue(label, out var list)) result[label] = list = new List<int>();
                    list.AddRange(vals);
                }
            }
            return result;
        }

        // The names of the type:'alias' leaves under a pane's children (they may sit inside tabs or groups).
        private static IEnumerable<string> AliasNames(object? node)
        {
            if (node is List<object> list)
            {
                foreach (var item in list)
                    foreach (var name in AliasNames(item)) yield return name;
            }
            else if (node is Dictionary<string, object> dict)
            {
                if (dict.TryGetValue("type", out var tv) && tv as string == "alias"
                    && dict.TryGetValue("name", out var nv) && nv is string name)
                    yield return name;
                else if (dict.TryGetValue("c", out var c) && !(dict.TryGetValue("type", out var t2) && t2 as string == "deck"))
                    foreach (var n in AliasNames(c)) yield return n;
            }
        }

        // A pane field's selector values: its own pane's, plus those of every pane of the same deck that aliases it.
        private int[]? PaneValuesOf(string label, PaneContext? pane)
        {
            if (pane == null) return null;
            if (_deckAliasValues == null
                || !_deckAliasValues.TryGetValue(WinboxFieldResolver.NormalizeLabel(label), out var extra))
                return pane.Values;
            return pane.Values.Concat(extra).Distinct().ToArray();
        }

        // Build a derived menu-label path "/ip/firewall/connection" from the breadcrumb + this window's
        // group segment + node name, each normalized (lower, spaces→'-'). Returns null for an empty leaf.
        private static string? BuildPath(List<string> crumb, string? groupSeg, string nodeName)
        {
            string leaf = WinboxFieldResolver.NormalizeLabel(nodeName);
            if (string.IsNullOrEmpty(leaf)) return null;
            var parts = new List<string>(crumb);
            if (groupSeg != null) parts.Add(groupSeg);
            parts.Add(leaf);
            return "/" + string.Join("/", parts);
        }

        private void AddField(string handlerKey, string label, int key, M2Type wireType, bool ro,
            IReadOnlyDictionary<int, string>? enumMap, string? uiType, int maskKey, int[]? refHandler,
            int optKey = 0, int notKey = 0, bool isRange = false, string? allow = null, long? def = null,
            PaneContext? pane = null, int offKey = 0, bool isOptional = false, string? elementUiType = null,
            int scale = 1, IReadOnlyList<WinboxJgElementPart>? elementParts = null, string? postfix = null,
            string? elementSeparator = null, int elementNotKey = 0, bool elementIsRange = false,
            string? tab = null, string? title = null,
            IReadOnlyList<WinboxJgField>? extraRegistrations = null, bool nonPublic = false,
            long min = 0, int radix = 0, string? prefix = null, int elementScale = 1,
            bool relative = false, IReadOnlyList<int[]>? refHandlers = null,
            IReadOnlyList<WinboxJgElementPart>? elementStatusParts = null)
        {
            string apiName = WinboxFieldResolver.NormalizeLabel(label);
            if (string.IsNullOrEmpty(apiName)) return;
            // A tab whose fields RouterOS names with the tab as a prefix (loop-protect-disable-time,
            // mlag-heartbeat). Unconditional, unlike the collision-driven qualification below: these fields
            // are prefixed whether or not anything else on the window claims the bare name. The prefix is
            // idempotent, so the tab's own same-named field (Loop Protect on the Loop Protect tab) keeps
            // reading `loop-protect`.
            if (WinboxFieldResolver.TabPrefixed(tab))
                apiName = WinboxFieldResolver.PrefixWithKind(tab, apiName);
            // A declaration's own `title` is carried on the field (see WinboxJgField.TitleApiName) and acted
            // on in SettleTitleNames, once the whole handler is known: it only decides anything when a
            // SECOND declaration on the same key is named by that title, and at this point that one may not
            // have been read yet. Never for a deck-pane leaf — there the API name comes from the pane KIND
            // (bfifo-limit, pcq-rate) and four panes whose boxes all read 'Queue Size' would collapse.
            string? titleName = pane == null && title != null
                ? WinboxFieldResolver.NormalizeLabel(title) : null;
            if (string.IsNullOrEmpty(titleName) || titleName == apiName) titleName = null;
            // A member whose .jg label is a caption rather than the router's own token is renamed here, once,
            // so every reader and the write side see RouterOS's vocabulary (see AliasEnumMembers).
            enumMap = WinboxFieldResolver.AliasEnumMembers(apiName, enumMap);
            var field = new WinboxJgField(apiName, key, wireType, ro, enumMap, uiType, maskKey,
                refHandler, optKey, notKey, isRange, allow, def,
                pane?.Kind, pane?.SelectorKey ?? 0, PaneValuesOf(label, pane), offKey, isOptional, elementUiType, scale,
                elementParts, postfix, elementSeparator, elementNotKey: elementNotKey,
                elementIsRange: elementIsRange, titleApiName: titleName,
                extraRegistrations: extraRegistrations, nonPublic: nonPublic, min: min, radix: radix,
                prefix: prefix, elementScale: elementScale, relative: relative, refHandlers: refHandlers)
                { ElementStatusParts = elementStatusParts };
            _labels[field] = label;
            if (!_fieldsByLabel.TryGetValue(handlerKey, out var ownLabels))
                _fieldsByLabel[handlerKey] = ownLabels = new Dictionary<string, WinboxJgField>(StringComparer.Ordinal);
            if (!ownLabels.ContainsKey(label)) ownLabels[label] = field;
            // Two fields of one window may carry the same label - the packet sniffer's streaming 'Port' (a
            // number) and its filter 'Port' (a list of port matches) - and first-wins kept only the first,
            // leaving the second reachable under no name at all. The TAB it sits under is what tells them
            // apart, and is what RouterOS prefixes the second with: 'filter-port'. Registered only for the
            // loser of a collision, so every name that resolved before keeps resolving to the same key.
            // An incumbent WinBox never paints is not a collision — the visible field takes the plain name
            // off it in Put below, so qualifying it here would rename the wrong one of the two.
            if (tab != null && _byHandler.TryGetValue(handlerKey, out var taken)
                && taken.TryGetValue(apiName, out var incumbent)
                && !(incumbent.NonPublic && !nonPublic))
            {
                string qualified = WinboxFieldResolver.PrefixWithKind(tab, apiName);
                if (qualified != apiName) { field = field.WithApiName(qualified); _labels[field] = label; }
                apiName = qualified;
            }
            Put(handlerKey, apiName, field);
            if (_currentOn != null)
            {
                _pendingConditions.Add(Tuple.Create(field, handlerKey, _currentOn));
                if (extraRegistrations != null)
                    foreach (var extra in extraRegistrations) _pendingConditions.Add(Tuple.Create(extra, handlerKey, _currentOn));
            }
            // A pane field is ALSO filed under the kind-prefixed name the API uses for it (memory-lines,
            // pcq-rate). Both registrations are needed: the plain one keeps every name that resolved before
            // resolving, and the prefixed one is the only way to reach a field whose label another pane
            // already claimed — 'Stop on Full' is memory's b4 AND disk's b6, and first-wins kept only b4, so
            // disk-stop-on-full could not be written at all.
            if (pane != null)
            {
                string prefixed = WinboxFieldResolver.PrefixWithKind(pane.Kind, apiName);
                if (prefixed != apiName) Put(handlerKey, prefixed, field);
            }
            // An action's arguments belong to the action AND, as before, to the handler that owns it — a
            // handler whose only window is an action (Wake on LAN) has no other map to be found in. Written
            // second, so the handler map keeps exactly the first-wins content it had before actions were
            // scoped: the record window's column still wins there.
            if (IsActionKey(handlerKey)) Put(HandlerOfActionKey(handlerKey), apiName, field);
            // The same rule one level out: an ordinary window's field is also its handler's, written second so
            // the per-handler map keeps byte-for-byte the first-wins content it had before windows were scoped.
            // (Interface subtype windows are excluded — they are not in _handlerBackedWindows.)
            else if (IsWindowKey(handlerKey) && _handlerBackedWindows.Contains(handlerKey)
                     && _windowHandlerKey.TryGetValue(handlerKey, out var ownerHandler))
            {
                Put(ownerHandler, apiName, field);
                if (pane != null)
                {
                    string prefixedH = WinboxFieldResolver.PrefixWithKind(pane.Kind, apiName);
                    if (prefixedH != apiName) Put(ownerHandler, prefixedH, field);
                }
            }
        }

        // The .jg 'opt:1' ATTRIBUTE — the field may legitimately have no value. Not to be confused with a
        // type:'opt' WRAPPER, which is a node of its own carrying a present-flag bool (see OptKey).
        private static bool IsOptionalAttr(Dictionary<string, object> dict)
            => dict.TryGetValue("opt", out var ov) && ov is int oi && oi != 0;

        // The .jg `relative:1` flag: a dateandtime measured on the uptime clock rather than on the epoch.
        // See WinboxJgField.Relative.
        private static bool RelativeOf(IDictionary<string, object> dict)
            => dict.TryGetValue("relative", out var rv) && rv is int ri && ri != 0;

        // The .jg `scale` of an interval field: how many wire units make one second.
        private static int ScaleOf(Dictionary<string, object> dict)
            => dict.TryGetValue("scale", out var sv) && sv is int si && si > 0 ? si : 1;

        // The .jg `postfix` — the unit a numeric field's value is expressed in (see WinboxJgField.Postfix).
        private static string? PostfixOf(Dictionary<string, object> dict)
            => dict.TryGetValue("postfix", out var pv) ? pv as string : null;

        // The `type` of a list field's unnamed element child — what webfig renders each element through.
        private static string? ElementUiTypeOf(Dictionary<string, object> dict)
        {
            var child = ElementChild(dict, out _);
            return child != null && child.TryGetValue("type", out var tv) ? tv as string : null;
        }

        /// <summary>
        /// The element child of a list field, with a <c>not</c> WRAPPER unwrapped: an element declared
        /// <c>{type:'not',id:'b1',c:[…]}</c> carries a negation flag at its own id and the real value inside,
        /// exactly as <c>types.not</c> does for a scalar. <paramref name="notKey"/> receives the flag's M2
        /// key, or 0 when the element is not wrapped.
        /// </summary>
        /// <remarks>
        /// The wrapper is NOT the value, which is why it is unwrapped here rather than treated as a leaf:
        /// its own id is a bool, and writing a caller's number into it would set the negation flag and drop
        /// the value (see <see cref="SingleLeafElementTypes"/>).
        /// </remarks>
        private static Dictionary<string, object>? ElementChild(Dictionary<string, object> dict, out int notKey)
        {
            notKey = 0;
            var child = FirstChildDict(dict);
            for (int guard = 0; guard < 4 && child != null; guard++)
            {
                if (!(child.TryGetValue("type", out var tv) && (tv as string) == "not")) return child;
                int k = DecodedKeyOf(child, "id");
                var inner = FirstChildDict(child);
                if (inner == null) return child;   // a childless 'not' is a plain flag, not a wrapper
                notKey = k;
                child = inner;
            }
            return child;
        }

        // The negation-flag key of a `not`-wrapped list element (see ElementChild), or 0.
        private static int ElementNotKeyOf(Dictionary<string, object> dict)
        {
            ElementChild(dict, out int notKey);
            return notKey;
        }

        // The element's own `range:1` — for a network element it says the SIBLING is the range END rather
        // than a netmask, exactly as it does on a scalar (types.network.tostr reads attrs.range).
        private static bool ElementIsRangeOf(Dictionary<string, object> dict)
        {
            var child = ElementChild(dict, out _);
            return child != null && child.TryGetValue("range", out var rv) && rv is int ri && ri != 0;
        }

        /// <summary>
        /// The parts of a list element that is a <c>type:'union'</c> or a <c>type:'tuple'</c>, in <c>.jg</c>
        /// order. <c>null</c> when the element is neither (a list of plain scalars is described by
        /// <see cref="WinboxJgField.ElementUiType"/> alone).
        /// </summary>
        /// <remarks>
        /// A NAMED union field takes its FIRST usable member (<c>AddUnionField</c>): tik4net has one key per
        /// API field name, and the API answers with the IPv4 family. Inside a LIST that shortcut is not
        /// available — every element chooses its own member, and <c>/snmp/community</c>'s single row is
        /// IPv6 — so every alternative is carried and the element picks among them at decode time.
        /// </remarks>
        private static IReadOnlyList<WinboxJgElementPart>? ElementPartsOf(Dictionary<string, object> dict)
        {
            var child = ElementChild(dict, out _);
            string? childType = child != null && child.TryGetValue("type", out var tv) ? tv as string : null;
            if (childType == "tuple")
            {
                // child is non-null here: childType can only be "tuple" via child's own TryGetValue above.
                if (!(child!.TryGetValue("c", out var cv) && cv is List<object> parts)) return null;
                var result = new List<WinboxJgElementPart>();
                foreach (var p in parts)
                    if (p is Dictionary<string, object> pd && PartOf(pd) is WinboxJgElementPart part)
                        result.Add(part);
                return result.Count > 0 ? result : null;
            }
            if (childType == "union")
            {
                // child is non-null here, for the same reason as the "tuple" branch above.
                var part = PartOf(child!);
                return part != null ? new List<WinboxJgElementPart> { part } : null;
            }
            // A list whose element is ONE addressable leaf — {type:'multi',id:'M18',c:[{type:'enm',id:'u19',
            // values:{…}}]}. The element is still a submessage, with the value at the leaf's own key, so it
            // is described by exactly the same one-part list a single-member tuple would be. Without this it
            // had no parts at all and the whole element fell back to a generic dump of the nested message:
            // /snmp's trap-interfaces read as "2" where the API prints "ether1".
            //
            // 'addr' is deliberately excluded: it is a compound of its own with its own formatter and its
            // own allow-mask rules (see FormatAddr / EncodeAddr).
            if (child != null && childType != null && SingleLeafElementTypes.Contains(childType))
            {
                var leaf = PartOf(child);
                return leaf != null ? new List<WinboxJgElementPart> { leaf } : null;
            }
            return null;
        }

        // The read-only status half of a list element (see WinboxJgField.ElementStatusParts): the unnamed
        // {type:'tuple',ro:1} member of the element tuple, flattened to its leaves in order, a 'prefix' wrapper's
        // text carried on the leaf it wraps. A list leaf whose dropdown sits on its element ({multinumber,
        // c:[{enm,values:{dynamic [20,0]}}]}) takes the element's reference table.
        private static IReadOnlyList<WinboxJgElementPart>? ElementStatusPartsOf(Dictionary<string, object> dict)
        {
            var child = ElementChild(dict, out _);
            if (child == null || !(child.TryGetValue("type", out var tv) && tv as string == "tuple")) return null;
            if (!(child.TryGetValue("c", out var cv) && cv is List<object> members)) return null;
            var result = new List<WinboxJgElementPart>();
            foreach (var m in members)
                if (m is Dictionary<string, object> md && md.TryGetValue("type", out var mt) && mt as string == "tuple"
                    && md.TryGetValue("ro", out var rov) && rov is int ri && ri != 0
                    && md.TryGetValue("c", out var lc) && lc is List<object> leaves)
                    foreach (var l in leaves)
                        if (l is Dictionary<string, object> ld) AddStatusLeaf(ld, null, result);
            return result.Count > 0 ? result : null;
        }

        private static void AddStatusLeaf(Dictionary<string, object> node, string? prefix, List<WinboxJgElementPart> into)
        {
            if (node.TryGetValue("type", out var tv) && tv as string == "prefix")
            {
                string? text = node.TryGetValue("name", out var nv) ? nv as string : null;
                if (node.TryGetValue("c", out var pc) && pc is List<object> wrapped)
                    foreach (var w in wrapped)
                        if (w is Dictionary<string, object> wd) AddStatusLeaf(wd, text, into);
                return;
            }
            if (!(node.TryGetValue("id", out var idv) && idv is string ids)) return;
            var dec = DecodeId(ids);
            if (dec == null) return;
            int[]? refHandler = ExtractRefHandler(node);
            if (refHandler == null && node.TryGetValue("c", out var ec) && ec is List<object> el
                && el.Count > 0 && el[0] is Dictionary<string, object> e0)
                refHandler = ExtractRefHandler(e0);
            string ty = node.TryGetValue("type", out var t2) && t2 is string s2 ? s2 : "";
            bool continues = prefix == null && node.TryGetValue("name", out var nm) && nm is string ns && ns.Length == 0;
            into.Add(new WinboxJgElementPart(dec.Value.key, ty, 0, enumMap: ExtractEnumMap(node),
                refHandler: refHandler, prefix: prefix, continues: continues));
        }

        // The element types that ARE one value at one key, so a list of them is described by a single part.
        // A whitelist, not an exclusion list: a wrapper such as {type:'not',id:'b6',c:[…]} also carries an
        // id, and treating it as the value would write the caller's number into the negation FLAG and drop
        // the value entirely. 'addr' is excluded for the opposite reason — it is a compound with its own
        // encoder and its own allow-mask rules.
        private static readonly HashSet<string> SingleLeafElementTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "ipaddr", "ip6addr", "macaddr", "macnetwork", "network", "network6",
            "number", "enm", "interval", "string", "secret", "bool",
        };

        // One part of a tuple/union element: a union node becomes a part carrying its alternatives, anything
        // with an id becomes a value leaf, anything else is not addressable and is dropped.
        private static WinboxJgElementPart? PartOf(Dictionary<string, object> node)
        {
            string? ty = node.TryGetValue("type", out var tv) ? tv as string : null;
            if (ty == "union")
            {
                if (!(node.TryGetValue("c", out var cv) && cv is List<object> members)) return null;
                var alts = new List<WinboxJgElementPart>();
                foreach (var m in members)
                    if (m is Dictionary<string, object> md && PartOf(md) is WinboxJgElementPart leaf)
                        alts.Add(leaf);
                return alts.Count > 0 ? new WinboxJgElementPart(0, "union", 0, alts) : null;
            }
            if (!(node.TryGetValue("id", out var idv) && idv is string ids)) return null;
            var dec = DecodeId(ids);
            if (dec == null) return null;
            // ty! : WinboxJgElementPart.UiType is declared non-nullable; a genuinely missing 'type' here
            // was already passed through as null pre-nullable (unchanged runtime behaviour).
            return new WinboxJgElementPart(dec.Value.key, ty!, DecodedKeyOf(node, "maskid"),
                enumMap: ExtractEnumMap(node), refHandler: ExtractRefHandler(node),
                radix: RadixOf(node));
        }

        // The `allow` mask of a LIST's element — see the call site.
        private static string? ElementAllowOf(Dictionary<string, object> dict)
        {
            var child = ElementChild(dict, out _);
            return child != null && child.TryGetValue("allow", out var av) ? av as string : null;
        }

        // The `scale` of a LIST's element, which is not the field's own — see WinboxJgField.ElementScale.
        private static int ElementScaleOf(Dictionary<string, object> dict)
        {
            var child = ElementChild(dict, out _);
            return child != null && child.TryGetValue("scale", out var sv) && sv is int si && si > 0 ? si : 1;
        }

        // webfig's types.number.tostr is `val.toString(attrs.radix||10)`: a declaration saying 16 is saying
        // the number is written in hexadecimal. 0 here means "declares none", i.e. decimal.
        private static int RadixOf(Dictionary<string, object> dict)
            => dict.TryGetValue("radix", out var rv) && rv is int ri ? ri : 0;

        // The `sep` of a tuple element (webfig's types.tuple.tostr default is '/'), or null when the element
        // is not a tuple.
        private static string? ElementSeparatorOf(Dictionary<string, object> dict)
        {
            var child = ElementChild(dict, out _);
            if (child == null || !(child.TryGetValue("type", out var tv) && (tv as string) == "tuple"))
                return null;
            return child.TryGetValue("sep", out var sv) && sv is string s ? s : "/";
        }

        // The M2 key held by a sibling-key attribute of a field node ('maskid', 'optid', 'oid'), or 0.
        private static int DecodedKeyOf(Dictionary<string, object> node, string attribute)
            => node.TryGetValue(attribute, out var v) && v is string s && DecodeId(s) is var d && d != null
                ? d.Value.key : 0;

        private void Put(string mapKey, string apiName, WinboxJgField field)
        {
            if (!_byHandler.TryGetValue(mapKey, out var map))
            {
                map = new Dictionary<string, WinboxJgField>(StringComparer.OrdinalIgnoreCase);
                _byHandler[mapKey] = map;
            }
            // first label wins for a given apiName; do not let later, less-specific windows clobber it.
            if (!map.ContainsKey(apiName)) { map[apiName] = field; return; }
            // …with one exception: a field WinBox actually paints displaces one it never does. A
            // `nonpublic:1` declaration is an internal slot (see WinboxJgField.NonPublic), and where it is
            // spelled like a visible field, the visible one is what the API is naming. /file declares
            // {name:'type',id:'u3',nonpublic:1} BEFORE {name:'Type',id:'s7'}, so first-wins alone kept the
            // numeric file kind and dropped the text: the path reported type=5 for type=directory, and the
            // string the router sends on every row reached no name at all.
            if (map[apiName].NonPublic && !field.NonPublic) map[apiName] = field;
        }

        // Resolves a named opt/not wrapper (e.g. firewall 'Connection State': opt→not→set) to its inner value
        // leaf and registers the API name against that leaf, carrying the opt/not flag-bool keys. Drills first
        // dict child through nested opt/not layers; the first non-wrapper node is the value leaf (its own id is
        // the value key, uiType the control type — 'set' for a bitmask). If no inner leaf is found, the wrapper
        // itself is the value (a bare bool option) — register its own key as a bool.
        private void AddOptionField(string handlerKey, string label, Dictionary<string, object> wrapper,
            PaneContext? pane = null)
        {
            int optKey = 0, notKey = 0, netmaskKey = 0;
            var cur = wrapper;
            for (int guard = 0; guard < 8 && cur != null; guard++)
            {
                string? ty = cur.TryGetValue("type", out var tv) && tv is string ts ? ts : null;
                var dec = (cur.TryGetValue("id", out var iv) && iv is string ids) ? DecodeId(ids) : null;

                if (ty == "opt" || ty == "not" || ty == "group")
                {
                    // A `group` with an id is an opt under another word — its bool says the match is in
                    // force, exactly as an opt's does.
                    if (dec != null) { if (ty == "not") notKey = dec.Value.key; else optKey = dec.Value.key; }
                    // RouterOS 6 spells the `not` of a group as a SIBLING of the value: 6.49.13's 'Src. Address
                    // Type' is {group b1aa, c:[{name:'Address Type',multibits u49}, {name:'Invert',bool bcc}]},
                    // where 7.x nests the same keys as opt b1aa → not bcc → multibits. Without this the '!' read
                    // as its own field ('invert', 'extra-invert') and src-address-type=!local as 'local'.
                    if (ty == "group" && notKey == 0 && InvertSibling(cur) is { } invert)
                    {
                        notKey = invert.key;
                        _flagOnlyNodes.Add(invert.node);
                    }
                    // 'Connection Limit' is {group b19f, c:[{name:'Limit', not b2a → number u2b}, {name:'Netmask',
                    // netmask u2c}]}, which the API prints as ONE field: connection-limit=!10,24. The netmask is the
                    // value's second half (WinboxFieldResolver.NumberNetmaskUiType), not a field of its own.
                    if (ty == "group" && netmaskKey == 0 && NetmaskSibling(cur) is { } netmask)
                    {
                        netmaskKey = netmask.key;
                        _flagOnlyNodes.Add(netmask.node);
                    }
                    // A wrapper drilled through here is part of THIS field, even when it carries a name of its own:
                    // 'Connection Limit' is {group, c:[{name:'Limit', not b2a, …}]}, and walking that inner 'Limit' as
                    // a field too registered the connection limit's key under the label 'Limit' — first, since the
                    // window declares it before the real 'Limit' group — so FirewallFilter.Limit read and wrote
                    // connection-limit's key and connection-limit read Absent over WinBox native.
                    if (!ReferenceEquals(cur, wrapper)) _flagOnlyNodes.Add(cur);
                    var child = FirstChildDict(cur);
                    if (child == null)
                    {
                        // wrapper with no inner control: it is a plain bool option (e.g. opt around a checkbox).
                        if (dec != null)
                            AddField(handlerKey, label, dec.Value.key, M2Type.Bool, false, null, "bool", 0, null,
                                pane: pane);
                        return;
                    }
                    cur = child;
                    continue;
                }

                // A tuple under the option: the option's label names the joined value, and its flag says
                // whether the API prints it at all. The EoIP/GRE/IPIP 'Keepalive' is
                // {opt b7d4,c:[{tuple,sep:',',separate:1,c:[{interval u7d5},{number u7d9}]}]} and reads
                // keepalive=10s,10 (7.24.2); the tuple has no id, so the leaf below registered nothing.
                // …and 7.24.4 declares the same 'Connection Limit' as not b2a → {tuple sep:'/', separate:1,
                // c:[number u2b, netmask u2c]}. The API still prints limit,prefix (10,24), so it is the same
                // number,netmask field, not a tuple joined by its '/'.
                if (ty == "tuple" && dec == null && NumberAndNetmask(cur) is { } parts)
                {
                    var number = parts.number;
                    var nd = DecodeId((string)number["id"])!.Value;
                    _flagOnlyNodes.Add(parts.netmask);
                    AddField(handlerKey, label, nd.key, nd.type, false, null, WinboxFieldResolver.NumberNetmaskUiType,
                        parts.netmaskKey, null, optKey, notKey, false, null, ExtractDef(number), pane);
                    return;
                }
                if (ty == "tuple" && dec == null)
                {
                    AddTupleField(handlerKey, label, cur, pane, optKey, underOption: true);
                    return;
                }

                // value leaf
                if (dec != null)
                {
                    bool ro = cur.TryGetValue("ro", out var rov) && rov is int rin && rin != 0;
                    // An 'ipaddrandport' names its port at 'portid' (6.x Connections, the sniffer's connections); it is
                    // the same "my value needs a sibling" slot as a netmask.
                    int maskKey = (cur.TryGetValue("maskid", out var mkv) && mkv is string mks
                        && DecodeId(mks) is var md && md != null) ? md.Value.key
                        : DecodedKeyOf(cur, "portid") is var pk && pk != 0 ? pk : HighKeyOf(cur);
                    int[]? refHandler = ExtractRefHandler(cur);
                    // 'range' must be read here as well as on the unwrapped path: EVERY firewall address field
                    // is an opt→not→network with range:1, so dropping it here made the range-END sibling decode
                    // as a netmask (see Docs/winbox-native-m2-protocol.md §24).
                    bool isRange = cur.TryGetValue("range", out var rgv) && rgv is int rgi && rgi != 0;
                    string? allow = cur.TryGetValue("allow", out var alv) ? alv as string : null;
                    // The wrapper's own opt flag wins; a leaf 'optid' fills in when there is no wrapper flag.
                    if (optKey == 0) optKey = DecodedKeyOf(cur, "optid");
                    if (netmaskKey != 0 && ty == "number" && maskKey == 0)
                    {
                        maskKey = netmaskKey;
                        ty = WinboxFieldResolver.NumberNetmaskUiType;
                    }
                    AddField(handlerKey, label, dec.Value.key, dec.Value.type, ro, ExtractEnumMap(cur),
                        ty, maskKey, refHandler, optKey, notKey, isRange, allow, ExtractDef(cur), pane,
                        DecodedKeyOf(cur, "oid"), IsOptionalAttr(cur), ElementUiTypeOf(cur), ScaleOf(cur),
                        ElementPartsOf(cur), PostfixOf(cur), ElementSeparatorOf(cur),
                        ElementNotKeyOf(cur), ElementIsRangeOf(cur),
                        // A wrapped leaf carries its own base and its own minimum, and both decide how the
                        // API spells the value: the bridge's priority is a wrapped {radix:16,postfix:'hex'}
                        // and reads 0x1000 where the raw number is 4096.
                        radix: RadixOf(cur),
                        min: cur.TryGetValue("min", out var omnv) && omnv is int omni ? omni : 0);
                }
                return;
            }
        }

        /// <summary>
        /// Registers a named <c>type:'union'</c> field from its FIRST child that carries a usable id. A union
        /// is one logical value with a per-family key (IPv4 <c>network</c>, IPv6 <c>network6</c>, a free-form
        /// <c>string</c>); WinBox picks the member by what the user typed. tik4net has one key per API field
        /// name, so it takes the first member — the IPv4 family in every union seen on 7.23.2 — which is the
        /// one RouterOS's own API answers with for an IPv4 value.
        /// </summary>
        /// <remarks>
        /// The alternative was to keep dropping the field, which is what happened before: the union node has
        /// no <c>id</c> and its children have no <c>name</c>, so nothing was registered and the field did not
        /// exist as far as the resolver was concerned. An IPv6 value now fails in the codec (a reported
        /// encode error naming the value) instead of being unnameable — never silently written to the IPv4
        /// key, because the codec parses the value against the registered wire type first.
        /// </remarks>
        private void AddUnionField(string handlerKey, string label, Dictionary<string, object> union,
            PaneContext? pane = null)
        {
            var child = FirstChildDict(union);
            if (child == null) return;
            if (!(child.TryGetValue("id", out var idv) && idv is string ids)) return;
            var dec = DecodeId(ids);
            if (dec == null) return;

            // ro / opt live on the union node, the typed attributes on the member.
            bool ro = (union.TryGetValue("ro", out var urov) && urov is int uri && uri != 0)
                   || (child.TryGetValue("ro", out var rov) && rov is int rin && rin != 0);
            int maskKey = (child.TryGetValue("maskid", out var mkv) && mkv is string mks
                && DecodeId(mks) is var md && md != null) ? md.Value.key : 0;
            bool isRange = child.TryGetValue("range", out var rgv) && rgv is int rgi && rgi != 0;
            string? allow = child.TryGetValue("allow", out var alv) ? alv as string : null;
            string? uiType = child.TryGetValue("type", out var tv) && tv is string ts ? ts : null;

            // opt/scale/element type are read from the union node as well as the member: the union node is
            // where 'Watch Address' carries its opt:1, and dropping it left the field looking mandatory, so
            // the "not set" rules could not fire on it.
            AddField(handlerKey, label, dec.Value.key, dec.Value.type, ro, ExtractEnumMap(child),
                uiType, maskKey, ExtractRefHandler(child), isRange: isRange, allow: allow,
                def: ExtractDef(child), pane: pane,
                isOptional: IsOptionalAttr(union) || IsOptionalAttr(child),
                elementUiType: ElementUiTypeOf(child), scale: ScaleOf(child), relative: RelativeOf(child),
                elementParts: ElementPartsOf(child), postfix: PostfixOf(child),
                elementSeparator: ElementSeparatorOf(child),
                // The base and the minimum are the member's too. /interface/bridge's 'Priority' is a union
                // of a {radix:16,postfix:'hex'} number and an enm whose members are spelled '0x1000' — two
                // renderings of one key, and the API uses the first one's.
                radix: RadixOf(child),
                min: child.TryGetValue("min", out var cmnv) && cmnv is int cmni ? cmni : 0,
                // …and the OTHER families answer to the same name at their own keys. The router sends one of
                // them, not the first one: /snmp's 'Src. Address' is {ipaddr u1b, ip6addr a1c} and the row
                // carries 0x1C, so registering only the IPv4 member left the field unreported on every row
                // that had a v6 value. The write side is unchanged — the name still resolves to the first
                // member, so an IPv6 value still fails to encode with the codec's own error.
                extraRegistrations: UnionAlternatives(label, union, ro, pane));
        }

        /// <summary>
        /// Every member of a <c>union</c> after the first, as a field of its own — same API name, its own
        /// key, wire type, <c>maskid</c> and enum/reference metadata.
        /// </summary>
        private IReadOnlyList<WinboxJgField>? UnionAlternatives(string label,
            Dictionary<string, object> union, bool unionReadOnly, PaneContext? pane)
        {
            if (!(union.TryGetValue("c", out var cv) && cv is List<object> members)) return null;
            string apiName = WinboxFieldResolver.NormalizeLabel(label);
            if (string.IsNullOrEmpty(apiName)) return null;

            var result = new List<WinboxJgField>();
            bool first = true;
            foreach (var m in members)
            {
                if (!(m is Dictionary<string, object> md)) continue;
                if (!(md.TryGetValue("id", out var idv) && idv is string ids)) continue;
                var dec = DecodeId(ids);
                if (dec == null) continue;
                if (first) { first = false; continue; }     // the primary, registered by AddUnionField

                bool ro = unionReadOnly
                       || (md.TryGetValue("ro", out var rov) && rov is int rin && rin != 0);
                result.Add(new WinboxJgField(apiName, dec.Value.key, dec.Value.type, ro,
                    ExtractEnumMap(md), md.TryGetValue("type", out var tv) ? tv as string : null,
                    DecodedKeyOf(md, "maskid"), ExtractRefHandler(md),
                    def: ExtractDef(md),
                    paneKind: pane?.Kind, paneSelectorKey: pane?.SelectorKey ?? 0, paneValues: PaneValuesOf(apiName, pane),
                    isOptional: IsOptionalAttr(union) || IsOptionalAttr(md),
                    scale: ScaleOf(md), relative: RelativeOf(md)));
            }
            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// A named scalar <c>tuple</c> — one field the API prints as its children joined by <c>sep</c>.
        /// </summary>
        /// <remarks>
        /// Like a union the node has no <c>id</c> of its own and the children have no names, so without this
        /// the field is not in the catalog at all. <c>/ip/service</c>'s
        /// <c>{name:'Remote',type:'tuple',sep:':',c:[{ip6addr ad},{number ue}]}</c> is one, and the API
        /// prints <c>192.168.4.31:65504</c> where the record carries <c>0xD</c> and <c>0xE</c>.
        /// <para>Every part key registers the same compound field, so the row renders the whole value
        /// whichever part the decode reaches first — and the parts, which have no label of their own, do
        /// not surface as fields the API never reports.</para>
        /// </remarks>
        private void AddTupleField(string handlerKey, string label, Dictionary<string, object> tuple,
            PaneContext? pane = null, int optKey = 0, bool underOption = false)
        {
            if (!(tuple.TryGetValue("c", out var cv) && cv is List<object> children)) return;
            // `separate:1` is webfig saying the parts are shown as boxes of their OWN, and such a tuple's
            // children carry their own names — /queue/simple's 'Max Limit' is
            // {tuple,separate:1,c:[{name:'Upload Max Limit',…},{name:'Download Max Limit',…}]}. Registering
            // the parent there would claim the children's keys first and take two named fields away to put
            // one joined value under a label RouterOS does not use. A child with a name of its own is a
            // field in its own right whatever the tuple says, so both tests are applied.
            // Under an option the label is the option's, and the tunnels' 'Keepalive' is a separate:1 tuple
            // of two unnamed parts the API prints joined — so there only the name test applies.
            if (!underOption && tuple.TryGetValue("separate", out var sepv) && sepv is int sepi && sepi != 0) return;
            foreach (var c0 in children)
                if (c0 is Dictionary<string, object> cd0 && cd0.TryGetValue("name", out var cn)
                    && cn is string cns && cns.Length > 0)
                    return;

            var parts = new List<WinboxJgElementPart>();
            var keys = new List<Tuple<int, M2Type>>();
            foreach (var c in children)
            {
                if (!(c is Dictionary<string, object> cd)) continue;
                if (PartOf(cd) is WinboxJgElementPart part) parts.Add(part);
                if (cd.TryGetValue("id", out var idv) && idv is string ids && DecodeId(ids) is var d && d != null)
                    keys.Add(Tuple.Create(d.Value.key, d.Value.type));
            }
            if (parts.Count == 0 || keys.Count == 0) return;

            // Two unnamed parts under an 'X/Y rest' label are two API fields, not one joined value. The IPv4
            // Connections window's 'Orig./Repl. Bytes' {tuple,c:[{bigbytes q20},{bigbytes q24}]} is the API's
            // orig-bytes and repl-bytes, and a bridge port's 'Tx/Rx BPDU's' is tx-bpdu and rx-bpdu (7.24.2). No
            // API name contains a '/', so the joined spelling is never one RouterOS uses.
            string[]? halves = underOption || keys.Count != 2 ? null : SplitPairLabel(label);
            if (halves != null)
            {
                bool tupleRo = tuple.TryGetValue("ro", out var trv) && trv is int tri && tri != 0;
                var partNodes = children.OfType<Dictionary<string, object>>().Where(c => c.ContainsKey("id")).ToList();
                for (int i = 0; i < 2 && i < partNodes.Count; i++)
                {
                    var node = partNodes[i];
                    string? ui = node.TryGetValue("type", out var ptv) ? ptv as string : null;
                    bool partRo = tupleRo || (node.TryGetValue("ro", out var prv) && prv is int pri && pri != 0);
                    AddField(handlerKey, halves[i], keys[i].Item1, keys[i].Item2, partRo, ExtractEnumMap(node), ui, 0,
                        null, pane: pane, postfix: PostfixOf(node), scale: ScaleOf(node));
                }
                return;
            }

            string apiName = WinboxFieldResolver.NormalizeLabel(label);
            if (string.IsNullOrEmpty(apiName)) return;
            bool ro = tuple.TryGetValue("ro", out var rov) && rov is int rin && rin != 0;
            string sep = TupleSeparatorOf(tuple);
            // Text RouterOS prints in front of the whole joined value — every occurrence in the 7.24
            // catalog is the STP identifiers' '0x', which belongs to the compound and not to its hex part
            // (port-id is 0x80.1, its second part decimal).
            string? prefix = tuple.TryGetValue("prefix", out var pfv) ? pfv as string : null;

            // The compound answers at the first part's key; the rest are registered as the same field.
            var extra = new List<WinboxJgField>();
            for (int i = 1; i < keys.Count; i++)
                extra.Add(new WinboxJgField(apiName, keys[i].Item1, keys[i].Item2, ro,
                    uiType: TupleUiType, optKey: optKey, elementParts: parts, elementSeparator: sep,
                    isOptional: IsOptionalAttr(tuple),
                    paneKind: pane?.Kind, paneSelectorKey: pane?.SelectorKey ?? 0, paneValues: PaneValuesOf(label, pane),
                    prefix: prefix));

            AddField(handlerKey, label, keys[0].Item1, keys[0].Item2, ro, null,
                TupleUiType, 0, null, optKey: optKey, pane: pane, isOptional: IsOptionalAttr(tuple),
                elementParts: parts, elementSeparator: sep,
                extraRegistrations: extra.Count > 0 ? extra : null, prefix: prefix);
        }

        private static readonly Regex PairLabel =
            new Regex(@"^\s*([A-Za-z]+)\.?\s*/\s*([A-Za-z]+)\.?\s+(.+?)\s*$", RegexOptions.CultureInvariant);

        // 'Orig./Repl. Fasttrack Bytes' → 'Orig Fasttrack Bytes' and 'Repl Fasttrack Bytes'; a trailing 's of a
        // plural abbreviation is dropped ('Tx/Rx BPDU's' → 'Tx BPDU'). Null for any other label.
        private static string[]? SplitPairLabel(string label)
        {
            var m = PairLabel.Match(label ?? "");
            if (!m.Success) return null;
            string rest = m.Groups[3].Value;
            if (rest.EndsWith("'s", StringComparison.Ordinal)) rest = rest.Substring(0, rest.Length - 2);
            return new[] { m.Groups[1].Value + " " + rest, m.Groups[2].Value + " " + rest };
        }

        /// <summary>The UI type of a scalar <c>tuple</c> field. webfig's <c>types.tuple.tostr</c> default
        /// separator is <c>'/'</c>.</summary>
        internal const string TupleUiType = "tuple";

        private static string TupleSeparatorOf(Dictionary<string, object> tuple)
            => tuple.TryGetValue("sep", out var sv) && sv is string ss ? ss : "/";

        // Registers a per-record action verb (doit/action label → SYS_CMD) under its owning handler and
        // returns the normalized label its arguments are keyed by (null when the label is unusable).
        private string? AddAction(string handlerKey, string label, int cmd)
        {
            string norm = WinboxFieldResolver.NormalizeLabel(label);
            if (string.IsNullOrEmpty(norm)) return null;
            if (!_actionsByHandler.TryGetValue(handlerKey, out var m))
            {
                m = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                _actionsByHandler[handlerKey] = m;
            }
            if (!m.ContainsKey(norm)) m[norm] = cmd;
            return norm;
        }

        // Harvest a streaming-monitor window. A monitor is a type:'query' window, or a type:'action' window
        // carrying a pollcmd; both run start → poll → cancel (webfig ObjectQuery/ObjectAction). The cmd trio
        // comes straight from the .jg: startcmd, cancelcmd, and the poll command — getallcmd||0xFE0004 for a
        // query (rows under Mfe0002), or pollcmd for an action (a single status record per poll). Windows that
        // lack a startcmd (e.g. a plain doit action verb) are not monitors and are skipped.
        private void HarvestMonitor(int[] handlerInts, string ty, Dictionary<string, object> dict)
        {
            bool isQuery = ty == "query";
            bool isPollAction = ty == "action" && dict.ContainsKey("pollcmd");
            if (!isQuery && !isPollAction) return;
            if (!dict.TryGetValue("startcmd", out var scv) || !(scv is int startCmd)) return;
            if (!dict.TryGetValue("cancelcmd", out var ccv) || !(ccv is int cancelCmd)) return;

            int pollCmd = isQuery
                ? (dict.TryGetValue("getallcmd", out var gav) && gav is int ga ? ga : WinboxM2Protocol.Command.GetAll)
                : (dict.TryGetValue("pollcmd", out var pcv) && pcv is int pc ? pc : WinboxM2Protocol.Command.GetAll);
            int autorefresh = dict.TryGetValue("autorefresh", out var arv) && arv is int ar ? ar : 1000;

            string key = HandlerKey(handlerInts);
            if (!_monitorsByHandler.ContainsKey(key))
                _monitorsByHandler[key] = new WinboxMonitorSpec(handlerInts, startCmd, pollCmd, cancelCmd, autorefresh, isQuery);
        }

        // Key of the direct child field named <paramref name="childName"/> (e.g. the 'type' discriminator the
        // generic interface window references via typeon), or 0 when absent/undecodable.
        private static int FindChildFieldKey(Dictionary<string, object> window, string childName)
        {
            if (window.TryGetValue("c", out var cv) && cv is List<object> list)
                foreach (var it in list)
                    if (it is Dictionary<string, object> d
                        && d.TryGetValue("name", out var nm) && nm is string ns && ns == childName
                        && d.TryGetValue("id", out var idv) && idv is string ids
                        && DecodeId(ids) is var dec && dec != null)
                        return dec.Value.key;
            return 0;
        }

        // First child dict inside a node's 'c' list (skips non-dict entries), or null.
        // A scalar numberrange's high end rides its own key (webfig types.numberrange: obj[id] = low, obj[highid] = high),
        // carried as the field's MaskKey like a network's netmask. packet-size=100-200 read '100' without it. A range with
        // a `unit` (connection-rate, connection-bytes) is the same two plain numbers on the wire, and the API prints them
        // plain (connection-rate=0-100000, 7.24.4) where webfig would write 100k.
        private static int HighKeyOf(Dictionary<string, object> node)
            => node.TryGetValue("type", out var tv) && (tv as string) == "numberrange"
                ? DecodedKeyOf(node, "highid") : 0;

        // A group's second child {type:'netmask', id} beside its value: the value's address-block half (AddOptionField).
        private static (int key, Dictionary<string, object> node)? NetmaskSibling(Dictionary<string, object> group)
        {
            if (!(group.TryGetValue("c", out var cv) && cv is List<object> list)) return null;
            var children = list.OfType<Dictionary<string, object>>().ToList();
            if (children.Count != 2) return null;
            var d = children[1];
            return d.TryGetValue("type", out var tv) && (tv as string) == "netmask"
                   && d.TryGetValue("id", out var iv) && iv is string ids && DecodeId(ids) is { } dec
                ? (dec.key, d) : ((int, Dictionary<string, object>)?)null;
        }

        // A tuple of exactly a number and a netmask (7.24's 'Connection Limit'): the number's node and the netmask's key.
        private static (Dictionary<string, object> number, int netmaskKey, Dictionary<string, object> netmask)? NumberAndNetmask(
            Dictionary<string, object> tuple)
        {
            if (!(tuple.TryGetValue("c", out var cv) && cv is List<object> list)) return null;
            var children = list.OfType<Dictionary<string, object>>().ToList();
            if (children.Count != 2) return null;
            bool IsType(Dictionary<string, object> d, string t) => d.TryGetValue("type", out var tv) && (tv as string) == t
                && d.TryGetValue("id", out var iv) && iv is string ids && DecodeId(ids) != null;
            if (!IsType(children[0], "number") || !IsType(children[1], "netmask")) return null;
            return (children[0], DecodeId((string)children[1]["id"])!.Value.key, children[1]);
        }

        // A group's {name:'Invert', type:'bool', id} child: the RouterOS 6 form of a `not` wrapper (AddOptionField).
        // Only in a group of exactly the value and the Invert: ipv6.jg's 'Headers' group is {Match enm, Invert,
        // Headers set}, whose first child is a selector, not the value an Invert would negate.
        private static (int key, Dictionary<string, object> node)? InvertSibling(Dictionary<string, object> group)
        {
            if (!(group.TryGetValue("c", out var cv) && cv is List<object> list)
                || list.Count(it => it is Dictionary<string, object>) != 2) return null;
            foreach (var it in list)
                if (it is Dictionary<string, object> d
                    && d.TryGetValue("name", out var nv) && (nv as string) == "Invert"
                    && d.TryGetValue("type", out var tv) && (tv as string) == "bool"
                    && d.TryGetValue("id", out var iv) && iv is string ids && DecodeId(ids) is { } dec)
                    return (dec.key, d);
            return null;
        }

        private static Dictionary<string, object>? FirstChildDict(Dictionary<string, object> node)
        {
            if (node.TryGetValue("c", out var cv) && cv is List<object> list)
                foreach (var it in list)
                    if (it is Dictionary<string, object> d) return d;
            return null;
        }

        // Pulls the referenced table handler from an enm dropdown (values:{type:'dynamic',path:[…]}),
        // searching one level of nesting (defenum/pair wrappers carry the dynamic node inside their own
        // values/c). Returns the first dynamic path found, or null for a static/non-reference enum.
        private static int[]? ExtractRefHandler(Dictionary<string, object> node)
        {
            if (node.TryGetValue("values", out var vv))
                return FindDynamicPath(vv, 0);

            // A LIST field declares its element type as an unnamed child instead of carrying 'values' itself
            // — the log's Topics is {name:'Topics',type:'multinumber',id:'U4',c:[{type:'enm',values:{type:
            // 'dynamic',path:[3,3]}}]}. Reading only 'values' left its RefHandler null, so a log row's topics
            // decoded as the raw handle "[9,3]" instead of "script,error". Only reached for a field leaf (the
            // caller requires an 'id'), so this cannot pick up a path from an unrelated menu child.
            if (node.TryGetValue("c", out var cv))
                return FindDynamicPath(cv, 0);
            return null;
        }

        // Every dynamic path under a dropdown's own `values`, in declaration order and without duplicates —
        // the tables webfig's enm.pair tries one after another (see WinboxJgField.RefHandlers). Only
        // `values` is searched: a list field's element child is ExtractRefHandler's business, and a single
        // source needs no list.
        private static IReadOnlyList<int[]>? ExtractRefHandlers(Dictionary<string, object> node)
        {
            if (!node.TryGetValue("values", out var vv)) return null;
            var found = new List<int[]>();
            CollectDynamicPaths(vv, found, 0);
            return found.Count > 1 ? found : null;
        }

        private static void CollectDynamicPaths(object node, List<int[]> found, int depth)
        {
            if (depth > 10) return;
            if (node is Dictionary<string, object> d)
            {
                if (d.TryGetValue("type", out var t) && t is string ts && ts == "dynamic"
                    && d.TryGetValue("path", out var pv) && pv is List<object> pl)
                {
                    var ints = new List<int>();
                    foreach (var p in pl) { if (p is int pi) ints.Add(pi); else return; }
                    if (ints.Count > 0 && !found.Any(f => f.SequenceEqual(ints))) found.Add(ints.ToArray());
                    return;
                }
                foreach (var kv in d) CollectDynamicPaths(kv.Value, found, depth + 1);
            }
            else if (node is List<object> list)
            {
                foreach (var it in list) CollectDynamicPaths(it, found, depth + 1);
            }
        }

        private static int[]? FindDynamicPath(object node, int depth)
        {
            if (depth > 4) return null;
            if (node is Dictionary<string, object> d)
            {
                if (d.TryGetValue("type", out var t) && t is string ts && ts == "dynamic"
                    && d.TryGetValue("path", out var pv) && pv is List<object> pl)
                {
                    var ints = new List<int>();
                    foreach (var p in pl) { if (p is int pi) ints.Add(pi); else return null; }
                    if (ints.Count > 0) return ints.ToArray();
                }
                foreach (var kv in d)
                {
                    var r = FindDynamicPath(kv.Value, depth + 1);
                    if (r != null) return r;
                }
            }
            else if (node is List<object> list)
            {
                foreach (var it in list)
                {
                    var r = FindDynamicPath(it, depth + 1);
                    if (r != null) return r;
                }
            }
            return null;
        }

        /// <summary>
        /// Pulls a static enum value list into <c>{numeric → label}</c>, following the <c>values:</c> chain
        /// wherever the map actually sits.
        /// </summary>
        /// <remarks>
        /// <para>Two map forms: an <b>array</b> (<c>values:{map:['off','on',…]}</c>) whose index is the numeric
        /// value, and an <b>object</b> (<c>values:{map:{0:'invalid',1:'established'}}</c>) with explicit keys —
        /// for <c>type:'set'</c> that key is the BIT INDEX of a bitmask (webfig <c>types.set.tostr</c>:
        /// <c>if(val&amp;(1&lt;&lt;i))</c>), for a sparse enum it is the value itself.</para>
        /// <para>The map is not always the first thing under <c>values</c>. RouterOS wraps it in
        /// <c>enumfilter</c> (which members this board offers), <c>defenum</c> (a sentinel id/name in front of
        /// the real list) and <c>pair</c> (a static sentinel list beside a dynamic table), and nests those —
        /// an IPsec proposal's PFS group is <c>enumfilter → defenum → static</c>. Reading only the top level
        /// left those fields with NO map at all, so <c>pfs-group</c> reached the caller as the bare number
        /// <c>2</c> where the API says <c>modp1024</c>. A list field carries its element's values on its
        /// unnamed child instead (<c>multinumber c:[{type:'enm',values:…}]</c>), same as
        /// <see cref="ExtractRefHandler"/> already handles for references.</para>
        /// <para>The runtime-computed wrappers (<c>queryenum</c>, <c>offsetenum</c>, <c>slotenum</c>,
        /// <c>remapenum</c>) are deliberately NOT followed: their members come from a live query or from
        /// another field's value, so there is no static map to read and inventing one would name values the
        /// router never meant.</para>
        /// </remarks>
        private static IReadOnlyDictionary<int, string>? ExtractEnumMap(Dictionary<string, object> node)
        {
            var map = new Dictionary<int, string>();
            if (node.TryGetValue("values", out var vv)) CollectEnumMap(vv, map, 0);
            // A list field's element type is an unnamed child ({type:'multinumber',c:[{type:'enm',values:…}]}).
            if (map.Count == 0 && node.TryGetValue("c", out var cv)) CollectEnumMap(cv, map, 0);
            return map.Count > 0 ? Normalized(map) : null;
        }

        /// <summary>
        /// Normalizes a map's labels the way every other .jg label is normalized — <b>unless doing so would
        /// merge two members into one</b>, in which case the raw labels are kept.
        /// </summary>
        /// <remarks>
        /// <para>Normalizing is what makes a label match the API's spelling at all: WinBox's
        /// <c>'as username'</c> is the API's <c>as-username</c> and <c>'key 0'</c> is <c>key-0</c>. But it
        /// lowercases and folds separators, and a handful of maps distinguish members by exactly those.
        /// A whole-catalog sweep of 7.24 finds two:</para>
        /// <list type="bullet">
        /// <item><c>/interface/wireless/security-profiles</c> 'MAC Format' — the same seven formats twice,
        /// uppercase then lowercase, and the case SELECTS how the MAC is sent to RADIUS. 14 labels collapse
        /// to 6. The router agrees they are 14 distinct values (tab-completion offers all of them).</item>
        /// <item><c>/ip/hotspot/profile</c> 'MAC Format' — 7 labels, where <c>'XX XX XX XX XX XX'</c> and
        /// <c>'XX-XX-XX-XX-XX-XX'</c> both fold to the same thing.</item>
        /// </list>
        /// <para>In both the raw label is exactly what RouterOS prints and accepts, so keeping it is
        /// not a compromise — normalizing was simply wrong for them. Detected from the map itself rather
        /// than listed per field, so a new such map on a later RouterOS is handled without a code change.</para>
        /// </remarks>
        private static Dictionary<int, string> Normalized(Dictionary<int, string> raw)
        {
            // A vocabulary whose members are already the ROUTER's tokens rather than captions keeps its
            // case: RouterOS prints a DNS record type as `CNAME`, and NormalizeLabel would invent `cname`.
            //
            // Recognised by the vocabulary itself, not by field name or path. 'Type' is one of the
            // commonest labels in the catalog and two of them carry this map — the cache's eighteen members
            // and /ip/dns/static's nine, which add FWD and NXDOMAIN — while other 'Type' enums share single
            // members with it ('null', 'a') and must NOT be touched. Upper case alone is no rule either:
            // 236 distinct ALL-CAPS members exist across the catalog and RouterOS lower-cases most of them
            // (tkip, ccmp, tls, arp). Verified against `/ip/dns/static add type=?` on 7.24.
            bool tokens = IsRouterTokenVocabulary(raw);

            var byNormalized = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in raw)
            {
                string n = NormalizeMember(kv.Value, tokens);
                // Two DIFFERENT raw labels landing on one normalized form is the collision. The same raw
                // label appearing at two keys is not — a defenum names an id the wrapped list also names.
                if (byNormalized.TryGetValue(n, out string? other) && !string.Equals(other, kv.Value, StringComparison.Ordinal))
                    return raw;
                byNormalized[n] = kv.Value;
            }
            var normalized = new Dictionary<int, string>(raw.Count);
            foreach (var kv in raw) normalized[kv.Key] = NormalizeMember(kv.Value, tokens);
            return normalized;
        }

        /// <summary>
        /// One enum member as RouterOS prints it: normalized like a label (<c>'Ethernet'</c> → <c>ethernet</c>),
        /// except where the case is part of the word.
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item>A quantity (<c>1M</c>, <c>512k</c>): the suffix's case is its meaning. <c>/ip/traffic-flow</c>
        /// cache-entries offers <c>1k … 512k, 1M … 32M</c> (7.24.4) and native read <c>1m</c> for <c>1M</c>.</item>
        /// <item>In a router-token vocabulary (<see cref="IsRouterTokenVocabulary"/>), a member with no lower-case
        /// letter is the router's own token and is kept; a caption beside it still folds — the certificate
        /// subject-alt-name completion lists <c>DNS IP email</c> for the catalog's <c>DNS IP Email</c>.</item>
        /// <item>A member LED by a quantity (<c>100M baseT full</c>, <c>2.5G baseX</c>, <c>5.5Mbps</c>): an
        /// ethernet link mode or a rate. RouterOS keeps its case and its point and joins the words with dashes —
        /// completion of <c>/interface ethernet set advertise=</c> lists <c>100M-baseT-full</c> and
        /// <c>2.5G-baseT</c> (7.24.4), and the API prints <c>rate=1Gbps</c>.</item>
        /// <item>Anywhere else, a point between two digits is a version or a decimal, not an abbreviation, and
        /// stays: <c>802.3ad</c> (bonding mode), <c>wireless-802.11</c> (hotspot nas-port-type).</item>
        /// </list>
        /// </remarks>
        private static string NormalizeMember(string member, bool routerTokens)
        {
            if (IsQuantity(member)) return member;
            if (routerTokens && !member.Any(char.IsLower)) return member;
            if (LedByQuantity.IsMatch(member))
                return string.Join("-", member.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            // A member, not a field label: the field-name overrides do not apply (see NormalizeLabel).
            return WinboxFieldResolver.NormalizeLabel(member, applyOverrides: false, keepDecimalPoints: true);
        }

        private static readonly System.Text.RegularExpressions.Regex LedByQuantity =
            new System.Text.RegularExpressions.Regex(@"^\d+(\.\d+)?[kMG](bps)?( |$)",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static bool IsQuantity(string member)
        {
            int i = 0;
            while (i < member.Length && char.IsDigit(member[i])) i++;
            return i > 0 && i == member.Length - 1 && "kMG".IndexOf(member[i]) >= 0;
        }

        // The member sets the catalog spells in the router's own case, each measured against the router's
        // completion on 7.24.4. A fingerprint rather than the whole list, so a shorter or longer map of the
        // same vocabulary matches too: the DNS cache's eighteen record types and /ip/dns/static's nine, SNMP's
        // authentication (MD5 SHA1) and encryption (AES DES) protocols, a certificate's subject-alt-name kinds.
        // Upper case alone is no rule: RouterOS lower-cases most of the catalog's 236 all-caps members.
        private static readonly string[][] RouterTokenFingerprints =
        {
            new[] { "A", "AAAA", "CNAME", "MX", "NS", "SRV", "TXT" },
            new[] { "MD5", "SHA1" },
            new[] { "AES", "DES" },
            new[] { "IP", "DNS" },
        };

        private static bool IsRouterTokenVocabulary(Dictionary<int, string> raw)
        {
            var values = new HashSet<string>(raw.Values, StringComparer.Ordinal);
            return RouterTokenFingerprints.Any(fp => fp.All(values.Contains));
        }

        // The wrappers whose inner `values` still lead to a static map. Anything else (queryenum, slotenum, …)
        // is computed at runtime and has none — see the remarks on ExtractEnumMap.
        //
        // 'tristate' is in the list because it is where a multitristate field keeps its members: the .jg
        // declares TCP Flags as {type:'multitristate',id:'u56',maskid:'u57',c:[{type:'tristate',values:
        // {map:['fin','syn',…]}}]}, and stopping at the child left the field with no map at all — so the
        // bitmask reached the caller as the bare number 2 where the API prints "syn".
        private static readonly HashSet<string> EnumMapWrappers =
            new HashSet<string>(StringComparer.Ordinal) { "enumfilter", "defenum", "pair", "static", "enm", "tristate" };

        private static void CollectEnumMap(object node, Dictionary<int, string> map, int depth)
        {
            // 10, not 5. Each wrapper costs TWO levels (the node, then the list under its `c`), and
            // /ip/traffic-flow 'Interfaces' is enm → pair → pair → static — nine levels down from the field,
            // so its map was cut off entirely and `interfaces=all` read as 4294967295. The guard is only
            // there to stop a cyclic or pathological catalog, so it can afford to be generous.
            if (depth > 10) return;
            if (node is List<object> list)
            {
                foreach (var it in list) CollectEnumMap(it, map, depth + 1);
                return;
            }
            if (!(node is Dictionary<string, object> d)) return;

            string? ty = d.TryGetValue("type", out var tv) && tv is string ts ? ts : null;
            // A defenum names ONE id in front of the list it wraps (defid:0,defname:'none'); the wrapped list
            // then fills in the rest. Put it in first — first-wins below keeps the inner map from renaming it.
            if (ty == "defenum" && d.TryGetValue("defid", out var dv) && TryToLong(dv, out long defId)
                && d.TryGetValue("defname", out var dnv) && dnv is string defName)
                Put(unchecked((int)defId), defName);

            if (d.TryGetValue("map", out var mv))
            {
                if (mv is List<object> arr)
                {
                    for (int i = 0; i < arr.Count; i++)
                        if (arr[i] is string s) Put(i, s);
                }
                else if (mv is Dictionary<string, object> obj)
                {
                    foreach (var kv in obj)
                        if (kv.Value is string s && long.TryParse(kv.Key, out long k))
                            Put(unchecked((int)k), s);
                }
            }

            if (ty != null && !EnumMapWrappers.Contains(ty)) return;
            if (d.TryGetValue("values", out var inner)) CollectEnumMap(inner, map, depth + 1);
            if (d.TryGetValue("c", out var children)) CollectEnumMap(children, map, depth + 1);

            // Stores the RAW label. Normalization happens once, in ExtractEnumMap, because it needs to see
            // the WHOLE map before it can tell whether normalizing would merge two members.
            void Put(int key, string label)
            {
                if (map.ContainsKey(key)) return;
                map[key] = StripValueSuffix(label);
            }
        }

        // WinBox spells the DH GROUP NUMBER out in the label — 'modp1024 (2)' — while the API calls it
        // 'modp1024'. The suffix is display decoration and is always dropped.
        //
        // It used to be dropped only when the number equalled the field's bit KEY, on the theory that a label
        // genuinely ending in a parenthesised number should survive. Two things settled that: the number is the
        // DH group, which merely COINCIDES with the bit key for the classic MODP groups, so 'x25519 (31)' at
        // bit 22 kept its suffix and read as 'x25519-(31)' where the API says 'x25519'; and a sweep of the whole
        // 7.23.2 catalog finds exactly twelve labels of this shape, all of them DH groups. The case the old
        // rule protected does not exist.
        private static string StripValueSuffix(string label)
        {
            if (string.IsNullOrEmpty(label) || label[label.Length - 1] != ')') return label;
            int open = label.LastIndexOf('(');
            if (open <= 0 || label[open - 1] != ' ') return label;
            string inside = label.Substring(open + 1, label.Length - open - 2);
            return long.TryParse(inside, out _) ? label.Substring(0, open - 1) : label;
        }

        // A .jg number too large for int stays a string token after parsing (WinboxJgParser.Scalar), so a `def` or
        // `defid` of 4294967295 arrives as text. Read both forms.
        private static bool TryToLong(object v, out long result)
        {
            if (v is int i) { result = i; return true; }
            if (v is string s && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
                return true;
            result = 0;
            return false;
        }

        /// <summary>
        /// A field declaration's own <c>title</c>, when it has one that differs from its <c>name</c>.
        /// </summary>
        /// <remarks>
        /// On a FIELD (not a window) the two are not two spellings of one label: the <c>name</c> is what
        /// WinBox paints beside the box and the <c>title</c> is what RouterOS calls the field.
        /// <c>{name:'Type',title:'Target'}</c> on <c>/system/logging/action</c> is <c>target</c> to the API
        /// and <c>type</c> is not a field of that table at all. The commonest shape is a pair of
        /// declarations on ONE key, one of them conditional and prefixed 'Old' —
        /// <c>{name:'Old Cache Path',title:'Cache Path',on:'oldfileman'}</c> beside
        /// <c>{name:'Cache Path',on:'newfileman'}</c> — and there the title says which of the two the API
        /// uses. Twenty-five such pairs in the 7.24 catalog.
        /// </remarks>
        private static string? TitleOf(Dictionary<string, object> node)
            => node.TryGetValue("title", out var tv) && tv is string ts && ts.Length > 0
               && !(node.TryGetValue("name", out var nv) && nv is string ns
                    && string.Equals(ns, ts, StringComparison.Ordinal))
                ? ts : null;

        // The .jg `def` (the field's default), kept only so the u32 unset marker can be recognised — see
        // WinboxJgField.Def.
        private static long? ExtractDef(Dictionary<string, object> node)
            => node.TryGetValue("def", out var dv) && TryToLong(dv, out long d) ? (long?)d : null;

        // 's10006' → (key=0x10006, type=String); prefix is exactly one leading letter, rest is hex. A letter outside
        // id2int's fourteen is M2Type.Unknown.
        private static (int key, M2Type type)? DecodeId(string idStr)
        {
            var m = Regex.Match(idStr, "^([a-zA-Z])([0-9a-fA-F]+)$");
            if (!m.Success) return null;
            char pfx = m.Groups[1].Value[0];
            if (!int.TryParse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int key))
                return null;
            M2Types.TryFromJgPrefix(pfx, out M2Type type);
            return (key, type);
        }

        internal static string HandlerKey(int[] handler) => string.Join(",", handler);
    }
}

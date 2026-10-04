# Multi-value fields and negation

How RouterOS spells, stores and rewrites a field that holds several values (`dst-port=22,8291`,
`connection-state=established,related`, `tcp-flags=syn,!ack`), and where a `!` may stand. Measured on
RouterOS 7.24.5 and 6.49.13 over the API, REST, the CLI and WinBox native. The O/R mapper's side of it is
`TikValueList<T>` (`tik4net.objects/TikValueList.cs`) and the `Negatable` / `NegatableMembers` /
`SetKeepsUnnamedHalf` attributes on `TikPropertyAttribute`.

## 1. The text form

Every text transport — API, REST and the CLI — spells a list the same way:

| Form | Meaning | Example |
|---|---|---|
| `a,b` | the items, comma-separated, no spaces | `dst-port=22,8291,1000-2000` |
| `!a,b` | the whole list negated | `dst-port=!22,8291` |
| `a,!b` | one member negated (only where the field negates members, §2) | `tcp-flags=syn,!ack` |
| `!,a,!b` | the whole list negated **and** a negated member: the whole-list `!` is a bare leading element | `tcp-flags=!,syn,!ack` |

On a field that negates members, a leading `!` binds to the first member: `!syn,!ack` is two negated members, not
a negated list. That is why the whole-list `!` there is a separate element. A bare `tcp-flags=!` is refused
(`ambiguous value of flag`), so a negated empty list cannot be written.

## 2. Which `!` a field takes

The `.jg` catalog says it: a whole-list `!` is a `type:'not'` wrapper around the field, a per-member `!` is the
UI type `multitristate` (or `multitristatearray`).

| `!` on | Fields |
|---|---|
| the whole value | `src-address`, `connection-limit`, `connection-rate`, ports, address types, `connection-state`, mangle `connection-nat-state`, `realm`, … |
| each member | `hotspot` and `tcp-flags` (filter, raw, mangle), logging `topics` — the same on 6.49.13 |
| both | `tcp-flags` only |

The catalog can miss a whole-value `!` the router takes: raw's `in-bridge-port`, `out-bridge-port`, the bridge-port
lists, `limit` and `packet-mark` take `!` as the filter's do, though the Raw window has no `not` box for them
(7.24.5).

A member `!` elsewhere is refused: `connection-state=established,!related` → `invalid value for argument state`;
mangle `connection-nat-state=srcnat,!dstnat` the same.

## 3. Order and duplicates

- **Ports keep what they were given:** `8291,22,1000-2000,80` and `22,22,80,20-30` read back unchanged.
- **Bitmask lists come back in the router's order:** plain members in bit order, then the negated ones.
  `ack,!syn,fin` reads `fin,ack,!syn`; `topics` `system,!debug,firewall,!info` reads `system,firewall,!debug,!info`.

So a list is compared without regard to order (or a reload would look like a change), and a write sends the
caller's order.

## 4. `tcp-flags`: a text `set` replaces only the half it names

`tcp-flags` is two bitmasks — plain members and negated members — plus the whole-field `!`. A text `set` (API, REST
or CLI) rewrites a half only when the new value names a member of it; the whole-field `!` is rewritten every time:

| Row | `set tcp-flags=` | Result |
|---|---|---|
| `fin,ack,!syn` | `psh` | `psh,!syn` |
| `!,fin,!psh` | `syn` | `syn,!psh` |
| `!,syn,!ack` | `rst` | `rst,!ack` |
| `!,fin,psh` | `!,fin,!psh` | `!,fin,!psh` (both halves named: exact) |

`unset` hides the field but keeps both masks: `unset` then `set !rst` brings the old plain half back. No text
spelling clears one half (`,!syn` changes nothing). `hotspot` and `topics` do not merge — a `set` replaces them.

So over a text transport a `tcp-flags` write to an existing row can be exact only when the new value names a
member of each half the row holds; tik4net refuses any other before sending (`SetKeepsUnnamedHalf`). An `add` is
always exact. WinBox native writes both masks (§5) and is exact; it declares `StructuredWrites`.

## 5. WinBox native (M2)

| Field | Keys |
|---|---|
| `tcp-flags` | `0x56` plain members, `0x57` negated members (bitmasks over the catalog's map), `0xD3` the whole-field not |
| `hotspot` | `0x5E` plain, `0x5F` negated |
| ports, `vlan-ids`, `http-codes` | one flat `u32[]` of `[lo0,hi0,lo1,hi1,…]` (a single value is `[n,n]`) |

A row can hold the same member in both masks (`syn,!syn` — `0x56=2`, `0x57=2`); both are read.

## 6. Not lists

- `connection-type` and `ipv4-options` refuse a comma list (`input does not match any value of type`) — single
  values.
- The catalog's `multibignumber` on ethernet statistics (one number on the API) and `multilinestring` (file
  contents, notes, log messages — text) are not lists either.
- Some lists are lists only on one version: `/ip/route gateway` on 6.x; `/ip/dns servers` has `addr` elements on
  7.x and `union` elements on 6.x. An item the type cannot hold is kept as the router's word.

# Multi-value fields and negation

How RouterOS spells, stores and rewrites a field that holds several values (`dst-port=22,8291`,
`connection-state=established,related`, `tcp-flags=syn,!ack`), and where a `!` may stand. Measured on
RouterOS 7.24.5 and 6.49.13 over the API, REST, the CLI and WinBox native. The O/R mapper's side of it is
`TikValueList<T>` (`tik4net.objects/TikValueList.cs`), `TikNegatableField<T>` for a whole-value `!`,
`TikNegatableValue<T>` as the item type of a list whose members take their own `!`, and the `SetBehaviour`
(`TikListSetBehaviour`) and `RouterSortsMembers` properties of `TikPropertyAttribute`.

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
- **So do the sets of words** (6.49.21, 7.21.5 and 7.24, over the API, on throwaway rows): PPP `authentication` and
  `allow` (`mschap2,pap,chap` → `pap,chap,mschap2`), IPsec profile `dh-group` (`ecp256,modp1024,modp2048` →
  `ecp256,modp2048,modp1024`), script and scheduler `policy` (`write,read,test` → `read,write,test`), wireless, CAPsMAN
  and wifi `authentication-types` (`wpa2-psk,wpa-psk` → `wpa-psk,wpa2-psk`) and wifi `dh-groups` (`21,19` → `19,21`).
  A `set` replaces each of them, and a member `!` is refused (`invalid value for argument`).
- **So do the cipher, algorithm, method and rate sets** (6.49.21 and 7.21.5, over the API, each written on a throwaway
  row once in another order and once with a duplicate, then read back; the duplicate reads once):

  | Field | Written | Read back |
  |---|---|---|
  | IPsec proposal `auth-algorithms` | `sha512,md5,sha256,sha1` | `sha512,sha256,sha1,md5` |
  | IPsec proposal `enc-algorithms` | `aes-128-cbc,aes-256-cbc,3des` | `aes-256-cbc,aes-128-cbc,3des` |
  | IPsec profile `enc-algorithm` | `3des,aes-256,aes-128` | `aes-256,aes-128,3des` |
  | hotspot profile `login-by` | `https,cookie,mac` | `mac,cookie,https` |
  | `/radius` `service` | `hotspot,ppp,login` | `ppp,login,hotspot` |
  | OSPF instance `redistribute` (7.x) | `static,connected,bgp` | `connected,static,bgp` |
  | BGP peer `address-families` (6.x), BGP template and connection `afi` (7.x) | `ipv6,ip` | `ip,ipv6` |
  | OVPN server `auth` (7.x) | `sha512,md5,sha1` | `sha1,md5,sha512` |
  | OVPN server `cipher` (7.x) | `aes256-cbc,aes128-cbc,blowfish128` | `blowfish128,aes128-cbc,aes256-cbc` |
  | SSTP client `ciphers` (7.x) | `aes256-gcm-sha384,aes256-sha` | `aes256-sha,aes256-gcm-sha384` |
  | wifi security `encryption` (7.x) | `gcmp,ccmp,tkip` | `tkip,ccmp,gcmp` |
  | wifi access-list `days` (7.x) | `wed,mon,sun` | `sun,mon,wed` |
  | wireless security profile `unicast-ciphers`, `group-ciphers` | `aes-ccm,tkip` | `tkip,aes-ccm` |
  | CAPsMAN security `encryption`, configuration `security.encryption` | `tkip,aes-ccm` | `aes-ccm,tkip` |
  | CAPsMAN configuration and rates `rates.basic`, `rates.supported` | `11Mbps,1Mbps,2Mbps` | `1Mbps,2Mbps,11Mbps` |
  | CAPsMAN configuration and rates `rates.ht-basic-mcs`, `rates.ht-supported-mcs` | `mcs-3,mcs-0,mcs-1` | `mcs-0,mcs-1,mcs-3` |
  | certificate `key-usage` | `tls-client,digital-signature,key-cert-sign` | `digital-signature,key-cert-sign,tls-client` |
  | certificate `trust-store` (7.x) | `www,api,ipsec` | `ipsec,www,api` |

  A member `!` is refused on all of them. `/interface/wireless` itself cannot be written on a CHR (no radio); its rate
  and HT MCS lists take the CAPsMAN vocabulary and are typed the same. RouterOS 7 refuses the profile's `blowfish` and
  `camellia-*`, the OVPN server's `sha256`/`sha384`/`sha512` are 7.x only, and 6.x spells the OVPN CBC ciphers
  `aes128`, `aes192`, `aes256`. The OVPN client's `auth` and `cipher` take one value, not a list.
- **Some lists keep the order and the duplicates they are given:** IPsec identity `eap-methods`
  (`eap-ttls,eap-peap,eap-mschapv2` and `eap-mschapv2,eap-mschapv2` read back unchanged; the order is the order the
  client tries them), BGP connection `templates`, OVPN server `push-routes` and `push-routes-ipv6`, and CAPsMAN
  `rates.vht-basic-mcs`/`rates.vht-supported-mcs` (`mcs0-9,mcs0-7`, one range per spatial stream).
- **Some sorted lists are not words:** the radio chain lists (wifi configuration `chains`/`tx-chains`, CAPsMAN
  `rx-chains`/`tx-chains`: `2,0,1` reads `0,1,2`) and the DHCP alert's `valid-server` MAC list
  (`AA:BB:CC:DD:EE:02,AA:BB:CC:DD:EE:01` reads back sorted, a duplicate once, a member `!` refused). They are sets the
  router sorts, but their members are numbers and addresses rather than an enum's words, so the entities declare them
  `RouterSortsMembers` and a list merge compares them by their distinct members.
- **A user group's `policy` holds every policy:** an add of `write,read,!ftp,test` reads
  `read,write,test,!local,!telnet,…`, the ones not granted negated. A text `set` changes only the policies it names:
  `set policy=ftp` grants ftp and keeps the rest, `!write` denies write, and an empty value changes nothing. A write
  that names every policy, as a loaded value does, is exact. WinBox native's write behaves the same (`UserGroupPolicyTest`,
  all 11 transports). `TikListSetBehaviour.KeepsUnnamedMembers` makes an entity's update the whole set: the new value plus every member
  the loaded value had and the new one omits, negated.

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
member of each half the row holds; tik4net refuses any other before sending (`TikListSetBehaviour.KeepsUnnamedHalf`). An `add` is
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

# MACTelnet — Protocol and Implementation Notes

> Local file, not in git. Last updated: 2026-05-26 (added MAC Winbox comparison, Winbox catalog, PoC proposal).
> Result of web research + source code analysis.

---

## Sources

| Source | Description | License |
|---|---|---|
| [haakonnessjoen/MAC-Telnet](https://github.com/haakonnessjoen/MAC-Telnet) | Reference C implementation, 473★ | GPL-2.0 |
| [KCTech-Lab/KC.MacTelnet](https://github.com/KCTech-Lab/KC.MacTelnet) | Pure .NET 10 implementation, February 2026 | Non-commercial |
| [kctech.dk/notes/mactelnet](https://www.kctech.dk/notes/mactelnet) | Author's blog about the KC.MacTelnet implementation | — |
| [salsa.debian.org/debian/mactelnet](https://salsa.debian.org/debian/mactelnet) | Debian `mactelnet` package | GPL-2.0 |
| [TragicWarrior/libvterm](https://github.com/TragicWarrior/libvterm) | VT100 emulator (C) | MIT |

---

## Protocol overview

MACTelnet is a proprietary MikroTik protocol for terminal access over **Layer 2 (MAC address)**,
without requiring IP connectivity. Key use case: recovery and bootstrap of a router that has no
IP address assigned.

- **Transport:** UDP broadcast (255.255.255.255), port **20561**
- **Max packet size:** 1500 bytes
- **Not encrypted** — safe only on isolated networks

---

## Packet header structure (22 bytes)

```
Offset  Length Description
──────────────────────────────────────────────────────────────────
 [0]      1    Protocol version = 1
 [1]      1    Packet type (see enum below)
 [2–7]    6    Source MAC address
 [8–13]   6    Destination MAC address
[14–15]   2    Session key (client) / Client type (server)  ← SWAPPED depending on direction!
[16–17]   2    Client type (client) / Session key (server)
[18–21]   4    Counter (big-endian uint32)
```

**Important:** the position of session key and client type differs by direction:
- client → server: `[14–15] = session_key`, `[16–17] = 0x0015`
- server → client: `[14–15] = 0x0015`, `[16–17] = session_key`

**Packet types:**

| Value | Name | Description |
|---|---|---|
| 0 | `SESSIONSTART` | Starts the session (client sends it first) |
| 1 | `DATA` | Data packet (carries control packets or raw terminal data) |
| 2 | `ACK` | Acknowledgment |
| 4 | `PING` | Keepalive ping |
| 5 | `PONG` | Ping response |
| 255 | `END` | Session termination |

**`END` is not a courtesy — it is the only way to close a session.** UDP has no FIN — if the client
just closes the socket, the router never finds out and **keeps the login alive**. Measured on 7.23.2
(P2.35, 2026-07-28): six WinBox-native-over-MAC connections opened and closed without `END` left six
`winbox` rows in `/user/active`, which were still there after 15 s and only disappeared after roughly
a minute and a half; the TCP sibling (`WinboxNative`) left none, because there the router gets told
via FIN. Every MAC-layer transport therefore must send `END` on close —
`MacTelnetUdpClient.TryCloseSession` and `WinboxMacM2Session.OnDisposing` both do this (for terminal
modes, only after `/quit`).

---

## Control packets (inside DATA packets)

DATA packets can carry a sequence of **control packets** or **raw terminal data**.

**Detection:** a control packet starts with the magic bytes `56 34 12 FF`.

**Structure of a single control packet:**
```
Offset  Length Description
─────────────────────────────
[0–3]    4    Magic: 0x56, 0x34, 0x12, 0xFF
[4]      1    Control packet type (see enum below)
[5–8]    4    Data length (big-endian uint32)
[9+]     n    Data
```

**Control packet types:**

| Value | Name | Data |
|---|---|---|
| 0 | `BEGINAUTH` | empty |
| 1 | `PASSSALT` | see auth flow |
| 2 | `PASSWORD` | response hash (17 or 32 bytes) |
| 3 | `USERNAME` | string |
| 4 | `TERM_TYPE` | string (e.g. `"xterm"`) |
| 5 | `TERM_WIDTH` | uint16 little-endian |
| 6 | `TERM_HEIGHT` | uint16 little-endian |
| 7 | `PACKET_ERROR` | error code |
| 9 | `END_AUTH` | empty (authentication complete) |

Raw terminal data (no magic) is present whenever a DATA packet's content does not start with the
magic bytes.

---

## Auth flow — EC-SRP (RouterOS ≥ 6.43, default)

```
Client → SESSIONSTART
         (type=0, counter=0, no data)

Client → DATA[ BEGINAUTH + PASSSALT ]
         PASSSALT data = username + "\0" + client_pubkey_x[32] + client_parity[1]
         (total: len(username)+1+33 bytes)

Server → DATA[ PASSSALT ]
         PASSSALT data = server_pubkey_x[32] + server_parity[1] + salt[16]
         (total: 49 bytes)

Client  computes the EC-SRP confirmation (see below)

Client → DATA[ PASSWORD(confirm[32]) + USERNAME + TERM_TYPE + TERM_WIDTH + TERM_HEIGHT ]

Server → DATA[ END_AUTH ]
         ↓ from this point on, raw VT100 terminal data flows
```

**EC-SRP math (Curve25519 in Weierstrass form):**

Algorithm (identical to the C implementation `mtwei.*`):

1. Client generates a random 32-byte private key `a`
2. Client computes the public key: `A = a*G` (on Curve25519 in Weierstrass form)
3. Server sends its public key `B` and `salt`
4. `validator = SHA256(salt || SHA256(username + ":" + password))`
5. `validator_point = redp1(gen_pubkey(validator).x, parity=1)`
6. `server_point = lift_x(server_x, server_parity)`
7. `sum = server_point + validator_point`
8. `h = SHA256(client_x || server_x)`
9. `vh = (validator_priv * h + a) mod r`  (where `r` is the group order)
10. `z = vh * sum`
11. `Cc = SHA256(h || z.x_montgomery)` — this is `confirm`

**Curve:** Curve25519 (`p = 2^255 - 19`), but the computations happen in Weierstrass form with a
conversion step.

---

## Auth flow — Legacy MD5 (RouterOS < 6.43)

```
Client → SESSIONSTART

Client → DATA[ BEGINAUTH ]
         (no PASSSALT with a public key)

Server → DATA[ PASSSALT(salt[16]) ]
         (only 16 bytes — this is how it's told apart from EC-SRP: PASSSALT payload length)

Client  computes: hashdata = [0x00] + password + salt
         password_hash = MD5(hashdata)
         response = [0x00] + password_hash  (17 bytes total)

Client → DATA[ PASSWORD(response[17]) + USERNAME + TERM_TYPE + TERM_WIDTH + TERM_HEIGHT ]

Server → DATA[ END_AUTH ]
```

**Detecting the auth version:** by the length of the `PASSSALT` payload:
- 16 bytes → legacy MD5
- 49 bytes → EC-SRP

---

## UDP communication — details

- **Source port:** random, 1024–2047 (chosen by the client at startup)
- **Destination port:** always 20561
- **Destination IP:** broadcast 255.255.255.255 → switches to unicast after the server's first
  reply (latches onto the IP the reply came from)
- **Keepalive:** an ACK packet every ~10s of idle time
- **Retry:** exponential backoff `[15, 20, 30, 50, 90, 170, 330, 660, 1000]` ms

**Counter:** the client tracks `outcounter` (incremented by the number of data bytes sent), the
server sends the `counter` value of the acknowledged packet in the ACK.

**Duplicate detection:** the client ignores packets with `counter ≤ incounter` (except on
wrap-around).

---

## Pure .NET implementation — feasibility

**KC.MacTelnet** (February 2026) proved that **the entire protocol can be implemented in pure .NET
without P/Invoke**.

| Layer | .NET API | Note |
|---|---|---|
| UDP socket | `System.Net.Sockets.UdpClient` | broadcast + unicast latch |
| Local MAC | `System.Net.NetworkInformation.NetworkInterface` | first non-loopback, Up |
| MNDP discovery | `tik4net.Mndp.MndpHelper` | **already exists in tik4net!** |
| EC-SRP math | `System.Numerics.BigInteger` + `SHA256` | ~200 lines |
| Legacy MD5 | `System.Security.Cryptography.MD5` | trivial |
| Random numbers | `RandomNumberGenerator` | private key, session key |

**No Pcap / Npcap dependency needed.** The original assumption in `4x-tiklink-design.md` was
incorrect.

---

## VT100 terminal output

After `END_AUTH` the server sends raw **VT100/xterm escape sequences**. For the tik4net management
library:

| Approach | Complexity | Fit for tik4net |
|---|---|---|
| Raw byte stream | none | For `ITikSession` — the caller parses it themselves |
| ANSI strip | ~50 lines | For simple CLI output scraping |
| Full VT100 emulator | ~1000+ lines | KC.MacTelnet does this via libvterm P/Invoke |

**Recommendation:** `tik4net.mactelnet` exposes `ITikSession` with a raw stream plus an optional
`VtStripper`. Full VT100 emulation is out of scope — MACTelnet is primarily a recovery transport,
not a management one.

---

## MAC Winbox — an alternative transport (analysis)

### How a MAC Winbox connection works

MAC Winbox uses **exactly the same UDP transport as MACTelnet** (port 20561); it differs only in
the client type identifier in the packet header:

| | MACTelnet | MAC Winbox |
|---|---|---|
| **Transport** | UDP 20561, broadcast | UDP 20561, broadcast |
| **Client type ID** | `0x0015` | `0x0f90` |
| **Authentication (ROS ≥ 6.45.1)** | EC-SRP5 | EC-SRP5 (identical) |
| **Payload after authentication** | raw VT100 terminal | Winbox M2 binary protocol |
| **Output** | terminal shell session | GUI management (proprietary binary format) |

**Discovery:** uses MNDP (UDP 5678), same as IP Winbox.
**Firewall note:** blocking UDP/20561 via the IP firewall does **not** block MAC Winbox — the
packets travel as Layer 2 broadcast, so IP rules never apply. A bridge firewall or disabling
`mac-winbox-server` is required instead.

### Structure of the Winbox binary protocol (M2 / nv::message)

After authentication, Winbox carries a proprietary binary protocol internally referred to as
**WinboxMessage** or **nv::message**, whose wire format is called **M2** (starts with the ASCII
bytes `4D 32`).

**Character of the protocol:** a typed key-value message format — **not sentence-based** and
**not similar to the MikroTik API**.

#### Keys

Every key is a **24-bit integer** in one of these namespaces:

| Namespace | Range | Purpose |
|---|---|---|
| SYS | `0xFF0001–0xFF00FF` | routing, session tracking |
| CMD | `0xFE0000–0xFEFFFF` | commands (both builtin and per-binary) |
| User | everything else | application-specific data |

System keys (setter methods from the reverse-engineered `winbox_message.hpp`):

| Key | Method | Description |
|---|---|---|
| `0xFF0001` | `set_to(dst, handler)` | target RouterOS binary (e.g. 17 = undo, 2 = mproxy) |
| `0xFF0003` | `set_from()` | source binary |
| `0xFF0006` | `set_command(cmd)` | operation code |
| `0xFF0007` | `set_session_id()` | session tracking |
| `0xFF0008` | `set_request_id()` | response correlation |
| `0xFF0009` | `set_reply_expected()` | flag for whether the server should reply |

#### Data types

| Type | Note |
|---|---|
| bool | |
| u32 | 32-bit unsigned |
| u64 | 64-bit unsigned |
| IPv6 | 16 bytes |
| string | length-prefixed |
| raw bytes | |
| nested WinboxMessage | recursive nesting |
| array of each above | |

Every type also exists as an array of values. About 14 distinct types in total.

#### Encryption (post-authentication)

```
[2B frame length][HMAC][AES-CBC encrypted body]
```
AES-128-CBC, MAC-then-Encrypt, separate keys for send/receive (derived from the EC-SRP5 shared
secret).

#### Routing across ~90 binaries

RouterOS internally runs about 90 network-reachable binary processes. Every operation (IP
firewall, interface, DHCP...) is routed to the appropriate binary via `set_to()`. There is no
public documentation mapping commands to binary IDs.

### Comparison: Winbox M2 vs. the MikroTik API (tik4net)

| Property | MikroTik API (tik4net) | Winbox M2 |
|---|---|---|
| **Format** | text sentences (length-prefixed words) | binary TLV/KV messages |
| **Commands** | `/ip/firewall/filter/print` (string paths) | numeric binary ID + numeric command code |
| **Parameters** | `=name=value`, `?name=value` (string) | typed key-value (u32, string, bool…) |
| **Responses** | `!re`, `!done`, `!trap`, `!fatal` sentences | reply message with session/request ID |
| **Documentation** | [officially documented](https://help.mikrotik.com/docs/spaces/ROS/pages/47579160/API) | reverse-engineered only |
| **Encryption** | optional TLS (port 8729) | always AES-128-CBC after auth |
| **Similarity to tik4net** | ✅ tik4net implements it | ❌ an entirely different protocol |

**Are they similar?** **No.** The MikroTik API is a text sentence protocol (ADO.NET-style), while
Winbox M2 is a compact binary protocol with numeric routing to internal RouterOS processes. The
concepts are distantly related (both are KV-based request/response schemes), but the wire format,
addressing, and semantics are entirely different.

### Conclusion: MAC Winbox vs. MACTelnet for tik4net

**Implementing MAC Winbox is not worthwhile.** Reasons:

1. **Winbox M2 ≠ the MikroTik API** — an implementation would not reuse any existing tik4net code.
   It would be a brand-new protocol built from scratch.
2. **No public specification** — the protocol exists only through reverse engineering
   (Tenable/MarginResearch). The mapping of commands to binary IDs is undocumented.
3. **Complexity** — ~14 data types, nested messages, routing across 90 binaries, AES-128-CBC with
   separate keys. An order of magnitude more complex than MACTelnet.
4. **tik4net's purpose** — tik4net is a wrapper around the MikroTik API. MAC Winbox would add a
   parallel management stack with no benefit for existing API consumers.
5. **No .NET implementation exists** — unlike MACTelnet, which has one (KC.MacTelnet, February
   2026).

**MACTelnet is the right choice** for Layer 2 / no-IP access:
- The protocol is documented (Omniflux, Wireshark, KC.MacTelnet)
- It provides terminal access = a fully-featured CLI (equivalent to a serial console)
- Screen scraping is only a downside for programmatic parsing — for recovery/bootstrap a VT100
  stream is sufficient
- Estimated scope ~600–800 lines of C# (see below)

### State of the Winbox M2 protocol reverse engineering

The protocol has been reverse-engineered primarily by security researchers. There is no official
documentation from MikroTik.

#### Primary reference: subixonfire/winbox-terminal-protocol

**This is the most complete publicly available documentation of the Winbox M2 protocol.**

The repo contains:
- **[PROTOCOL.md](https://github.com/subixonfire/winbox-terminal-protocol/blob/master/PROTOCOL.md)** — a 15 KB complete specification: authentication (EC-SRP5 and legacy MD5), frame format, M2 TLV structure, system keys, terminal session protocol with byte-level examples
- **[winbox_terminal_client.py](https://github.com/subixonfire/winbox-terminal-protocol/blob/master/winbox_terminal_client.py)** — a 51 KB Python implementation, self-contained single file, a production-usable terminal client over TCP 8291

License: MIT. Actively maintained (master branch).

#### Other references (from most to least complete)

| Project | Language | Scope | Status |
|---|---|---|---|
| [subixonfire/winbox-terminal-protocol](https://github.com/subixonfire/winbox-terminal-protocol) | Python | **Complete spec + implementation** — EC-SRP5+MD5 auth, M2 serialization, terminal session | ★ primary ref |
| [tenable/routeros – common/](https://github.com/tenable/routeros/tree/master/common) | C++ | `WinboxMessage` serialization + `WinboxSession` | Archived 2024 |
| [vulncheck-oss/go-exploit – mikrotik](https://pkg.go.dev/github.com/vulncheck-oss/go-exploit/protocol/mikrotik) | Go | M2Message types, serialization, Winbox + WebFig session | Active |
| [Cisco-Talos/Winbox_Protocol_Dissector](https://github.com/Cisco-Talos/Winbox_Protocol_Dissector) | Lua (Wireshark) | Wireshark decoder for M2 messages, all field types | Active |
| [MarginResearch/mikrotik_authentication](https://github.com/MarginResearch/mikrotik_authentication) | Python | EC-SRP5 auth PoC + Winbox client + MAC Telnet client | Active |
| [Margin Research – Pulling into the Limelight](https://margin.re/2022/06/pulling-mikrotik-into-the-limelight/) | blog | Conceptual description of M2, routing system, binary addresses | 2022 |
| [Make It Rain with MikroTik – Tenable](https://medium.com/tenable-techblog/make-it-rain-with-mikrotik-c90705459bc6) | blog | WinboxMessage routing, SYS keys, exploitation | 2018 |

---

### Winbox M2 protocol — complete specification

*(Source: subixonfire PROTOCOL.md + winbox_terminal_client.py, verified against the source code)*

#### Authentication (EC-SRP5, RouterOS ≥ 6.43)

Happens **before** the M2 layer, as a binary handshake on the TCP socket (not wrapped in M2):

```
Client → [len 1B][0x06][username\0][pubkey_x 32B][parity 1B]
Server → [len 1B][0x06][srv_pubkey_x 32B][srv_parity 1B][salt 16B]
Client → [len 1B][0x06][client_confirmation 32B  (SHA256)]
Server → [len 1B][0x06][server_confirmation 32B  (SHA256)]
```

**Key derivation** (after successful auth, from the shared secret `z`):
```python
magic_send    = b"On the client side, this is the send key; on the server side, it is the receive key."
magic_receive = b"On the client side, this is the receive key; on the server side, it is the send key."

txEnc = SHA1(z + b'\x00'*40 + magic_send    + b'\xf2'*40)[:16]  # send AES key (base)
rxEnc = SHA1(z + b'\x00'*40 + magic_receive + b'\xf2'*40)[:16]  # recv AES key (base)
# Final keys via HKDF (custom, 2 iterations of HMAC-SHA1):
send_aes_key, send_hmac_key     = HKDF(txEnc)[:16], HKDF(txEnc)[16:32]
receive_aes_key, receive_hmac_key = HKDF(rxEnc)[:16], HKDF(rxEnc)[16:32]
```

#### Authentication (Legacy MD5, RouterOS < 6.43)

Happens directly over **unencrypted M2 messages** (tag 0x01). Flow:
1. M2 `SYS_TO=[2,2]`, `SYS_CMD=7`, `key_1="list"` → obtain `SESSION_ID`
2. M2 `SYS_TO=[2,2]`, `SYS_CMD=5`, `SESSION_ID` → setup challenge
3. M2 `SYS_TO=[13,4]`, `SYS_CMD=4` → obtain salt (`key_9`, raw 16B)
4. M2 `SYS_TO=[13,4]`, `SYS_CMD=1`, `key_1=user`, `key_9=salt`, `key_10=\x00+MD5(\x00+password+salt)` → login

---

#### Frame format (chunk-based, same for both encrypted and plain)

```
Chunk: [chunk_len 1B][tag 1B][payload: chunk_len bytes]

First chunk: tag = 0x06 (enc) or 0x01 (plain), chunk_len = payload length
Continuation:  tag = 0xFF, chunk_len = 0xFF (255B, continues) or < 0xFF (last)
```

**Assembled payload (encrypted, tag 0x06):**
```
[enc_length 2B big-endian][IV 16B][ciphertext]
```

**Assembled payload (unencrypted, tag 0x01):**
```
[body_length 2B big-endian][M2 message body]
```

**Encrypt (client→server), MAC-then-Encrypt:**
1. `hmac = HMAC-SHA1(send_hmac_key, plaintext)` → 20 bytes
2. `pad_byte = 0x0F - ((len(plaintext+hmac)) % 0x10)`
3. Append `(pad_byte + 1)` bytes of value `pad_byte` (note: NOT PKCS7 — a custom scheme; the result
   is always block-aligned and no byte exceeds `0x0F`)
4. `ciphertext = AES-128-CBC(send_aes_key, random_IV, plaintext + hmac + padding)`

---

#### M2 message — TLV format

Every M2 message starts with the magic `4D 32` ("M2"), followed by TLV records:

```
TLV record: [key_low 1B][key_high 1B][namespace 1B][type 1B][value...]
full_key = (namespace << 16) | (key_high << 8) | key_low
```

**Namespaces:**

| Namespace | Hex | Purpose |
|---|---|---|
| System | `0xFF` | Routing, commands, request IDs |
| Session | `0xFE` | Session management |
| User | `0x00` | Application data (terminal, parameters) |

**Data types:**

| Type | Hex | Data size | Note |
|---|---|---|---|
| bool_false | `0x00` | 0 B | False value |
| bool_true | `0x01` | 0 B | True value |
| u32 | `0x08` | 4 B LE | |
| u8 | `0x09` | 1 B | |
| u64 | `0x10` | 8 B LE | |
| string_l | `0x20` | 2B LE len + data | length > 255 |
| string_s | `0x21` | 1B len + data | length ≤ 255 |
| raw_l | `0x30` | 2B LE len + data | length > 255 |
| raw_s | `0x31` | 1B len + data | length ≤ 255 |
| msg_l | `0x28` | 2B LE len + "M2"+data | nested M2 |
| msg_s | `0x29` | 1B len + "M2"+data | nested M2, length ≤ 255 |
| u32_array | `0x88` | 2B LE count + count×4B LE | |
| str_array | `0xA0` | 2B LE count + (2B LE len + data)× | |
| msg_array | `0xA8` | 2B LE count + (2B LE len + data)× | |

**System keys:**

| Key | full_key | Type | Description |
|---|---|---|---|
| SYS_TO | `0xFF0001` | u32_array | Target handler path, e.g. `[76]` or `[13,4]` |
| SYS_FROM | `0xFF0002` | u32_array | Source handler path, e.g. `[0, src_id]` |
| SYS_REQUEST | `0xFF0005` | bool | True = a reply is expected |
| SYS_REQID | `0xFF0006` | u8/u32 | Request/response correlation |
| SYS_CMD | `0xFF0007` | u8/u32 | Operation code |
| SYS_STATUS | `0xFF0008` | u8/u32 | Reply status (0 = OK) |
| SESSION_ID | `0xFE0001` | u8 | Terminal session ID |

---

#### Handler path and command mapping (terminal session)

| Handler path | Binary | Description |
|---|---|---|
| `[13, 4]` | system info | Initial capabilities, version, board |
| `[76]` | mepty | Terminal PTY handler |
| `[120]` | session | Login/session handler |
| `[2, 2]` | mproxy | Session management (legacy auth) |
| `[24, 0/1/2]` | management | GUI management data |

| Command | Hex | Description |
|---|---|---|
| cmdGet | `0x07` | Get data/capabilities |
| cmdOpen | `0x05` | Open handler/session |
| meptyLogin | `0x0A0065` (655461) | Open a terminal PTY session |
| meptyData | `0x0A0067` (655463) | Transfer terminal data |

**This is the dictionary for the terminal session only — for other RouterOS operations (IP,
firewall, etc.) there is no public command table.**

---

#### Terminal session flow (mepty)

```
1. M2: SYS_TO=[13,4], SYS_CMD=7          → capabilities, learn src_id
2. M2: SYS_TO=[76], SYS_CMD=0x0A0065     → meptyLogin (cols, rows, password, term="vt102")
   Response: SESSION_ID from [76]
3. M2: SYS_TO=[76], SYS_CMD=0x0A0067     → ready signal (key_3=0, no key_2)
4. Server → M2: SYS_FROM=[76], key_2=<VT102 bytes>   → terminal output
5. Client → M2: SYS_TO=[76], SYS_CMD=0x0A0067, key_2=<keystroke>, key_3=<counter>
6. Flow control: send an ACK after every ~8 KB of data received (meptyData with no key_2, key_3=recv_counter)
```

---

#### Byte-level example: Initial Request

```
4d 32                           -- M2 header
01 00 ff 88 02 00               -- SYS_TO: u32_array[2]
  0d 00 00 00 04 00 00 00       --   = [13, 4]
02 00 ff 88 02 00               -- SYS_FROM: u32_array[2]
  00 00 00 00 01 00 00 00       --   = [0, 1]
05 00 ff 01                     -- SYS_REQUEST: bool true
06 00 ff 09 00                  -- SYS_REQID: u8 = 0
07 00 ff 09 07                  -- SYS_CMD: u8 = 7
```

#### Byte-level example: Keystroke 't' (meptyData)

```
4d 32
01 00 ff 88 01 00 4c 00 00 00  -- SYS_TO = [76]
02 00 ff 88 02 00 00 00 00 00 ad 01 00 00  -- SYS_FROM = [0, 429]
03 00 00 08 98 02 00 00        -- key_3 (user): u32 = 664
01 00 fe 09 1c                 -- SESSION_ID: u8 = 28
07 00 ff 08 67 00 0a 00        -- SYS_CMD: u32 = 0x0A0067
02 00 00 31 01 74              -- key_2 (user): raw_s, len=1, data=0x74='t'
```

---

### Updated conclusion: MAC Winbox vs MACTelnet for tik4net

**Key insight (confirmed):** if MAC Winbox, after the MACTelnet authentication
(client_type=0x0f90), tunnels the exact same M2 protocol as TCP Winbox, then subixonfire's
`winbox_terminal_client.py` is a **direct reference implementation for the M2 layer**.

**What would change compared to TCP Winbox:**

| Layer | TCP Winbox | MAC Winbox |
|---|---|---|
| Transport | TCP socket → port 8291 | UDP 20561, MACTelnet framing |
| Auth handshake | Bare binary over TCP | MACTelnet EC-SRP5 (same math, different wrapping) |
| Auth post-processing | `gen_stream_keys()` → 4 keys | Same — the key math is identical |
| M2 frame wrapping | Chunk-based over the TCP stream | ? — likely M2 frames as the payload of MACTelnet DATA packets |
| M2 messages themselves | Identical | Identical |

**The biggest open question:** exactly how are M2 frames wrapped inside MACTelnet DATA packets?
Two possibilities:
1. **Direct:** the M2 chunk frames (`[chunk_len][0x06][...]`) go straight into the MACTelnet DATA payload
2. **Stripped:** the chunk wrapper is removed, and only `[enc_length][IV][ciphertext]` goes into the MACTelnet DATA payload

This would need to be verified with a packet capture (Wireshark + MACTelnet + Winbox MAC session).

**Updated feasibility assessment:**

| Layer | Effort (original estimate) | Effort (after finding subixonfire) |
|---|---|---|
| M2 serialization/deserialization | ~300–400 lines of C# | ~250 lines — direct translation from Python |
| EC-SRP5 + key derivation | shared with MACTelnet | shared with MACTelnet |
| Frame encrypt/decrypt | ~100 lines | ~80 lines — direct translation |
| MAC transport layer | — | MACTelnet transport (UDP 20561) |
| Terminal session (mepty) | — | ~150 lines — direct translation |
| M2 framing over UDP | 0 | ? — needs verification |
| **Total excluding command mapping** | **complex** | **~600–700 lines of C#** |

---

## Winbox startup catalog — how Winbox discovers available features

### Confirming the hypothesis

Winbox does indeed download a catalog from the router on every connection. The mechanism:

#### 1. The list file — what and how Winbox downloads it

Winbox sends an M2 message to **mproxy** (binary `[2, 2]`):

```
M2
  SYS_TO      = [2, 2]          # mproxy, handler 2
  SYS_FROM    = [0, src_id]
  SYS_REQUEST = true
  SYS_REQID   = N               (u8)
  SYS_CMD     = 7               # open file for reading in /home/web/webfig/
  key_1       = "list"          (string, user namespace) — the file name
```

The reply contains a **session file handle** (SESSION_ID). Winbox then reads the content:

```
M2
  SYS_TO      = [2, 2]
  SYS_FROM    = [0, src_id]
  SYS_REQUEST = true
  SYS_REQID   = N+1
  SESSION_ID  = <file_handle>   (u8, 0xFE namespace)
  key_2       = 32768           (u32) — max bytes to read
  SYS_CMD     = 4               # read from open file handle
```

The file `/home/web/webfig/list` contains the list of plugins Winbox needs:

```
{ crc: 164562873, size: 1149, name: "advtool.jg", unique: "advtool-fc1932f6809e.jg", version: "6.39.3" }
{ crc: ...,      size: ...,  name: "ipv6.jg",     unique: "ipv6-XXXXXXXX.jg",       version: "6.39.3" }
...
```

Every installed RouterOS package contributes its own `.jg` files. Winbox downloads them (via the
same mproxy cmd=7/4 path) and caches them locally under `%AppData%\Roaming\Mikrotik\Winbox\`.

#### 2. What the `.jg` files contain

`.jg` files are Winbox **GUI plugins** (a proprietary binary format, not a standard DLL). Each
plugin:
- defines the UI for a given RouterOS feature (IP/Firewall, Interface, DHCP...)
- contains the **mapping of UI operations → M2 commands** (binary ID + command code)
- is tied to a specific RouterOS version (a unique hash in its name)

This is the direct answer to "where is the command dictionary" — it is embedded in the `.jg`
plugins, not in any publicly accessible table.

#### 3. RouterOS-internal catalog: `/nova/etc/loader/system.x3`

In parallel with the `.jg` mechanism, the router also has a file `/nova/etc/loader/system.x3` (and
a `*.x3` for every package under `/ram/pckg/<name>/nova/etc/loader/`):

- A proprietary pseudo-XML binary format, parsed by `libuxml++.so`
- Contains the mapping: **binary path → SYS_TO ID**
- Example output from [tenable/routeros parse_x3](https://github.com/tenable/routeros/tree/master/msg_re/parse_x3):
  ```
  /nova/bin/log      → ID 3
  /nova/bin/radius   → ID 5
  /nova/bin/moduler  → ID 6
  /nova/bin/user     → ID 13
  ```
- Winbox does not download this — it is an internal RouterOS mechanism for process spawning
  (`/nova/bin/loader`)

#### 4. Confirmation: MAC Winbox = the same M2 content

[BasuCert/WinboxPoC](https://github.com/BasuCert/WinboxPoC) contains **`MACServerExploit.py`** (an
exploit for CVE-2018-14847 over the MAC protocol). The key code:

```python
CLIENT_TYPE = 0x0F90   # ← this is the MAC Winbox client type
```

And critically: **the payload byte arrays `a` and `b` are identical** to the TCP Winbox exploit
(`WinboxExploit.py`). The only difference is the transport (UDP 20561, MACTelnet framing vs. TCP
8291). **The M2 messages themselves are byte-for-byte identical.**

#### 5. CVE-2018-14847 — what it revealed about the mproxy protocol

The exploit relied on the fact that mproxy cmd=4, 5, 7 **required no authentication** (pre-patch).
Path traversal via `key_1`:

```
key_1 = "/////./..///////./..//////./../../flash/rw/store/user.dat"
```

Instead of `/home/web/webfig/user.dat` → it read `/flash/rw/store/user.dat` (the password
database).

After the patch: cmd=7 requires authentication, but the mechanism for downloading the list file
and the plugins works the same way (once logged in).

---

### PoC proposal: Winbox M2 catalog enumerator

**Goal:** after authentication, read the list file and print the available RouterOS features +
binaries. Not needed for tik4net, but an interesting standalone tool.

#### Implementation inputs

| Piece | Source |
|---|---|
| EC-SRP5 auth (TCP 8291) | `subixonfire/winbox-terminal-protocol` — direct translation to C# |
| MACTelnet auth (UDP 20561) | these notes + KC.MacTelnet |
| M2 serialization | `subixonfire/winbox-terminal-protocol` — direct translation to C# |
| Mproxy file read (cmd=4, 7) | CVE-2018-14847 exploit code — trivial M2 messages |
| x3 parser | [tenable/routeros msg_re/parse_x3](https://github.com/tenable/routeros/tree/master/msg_re/parse_x3) (C++) → port to C# |

#### Estimated PoC scope

```
WinboxM2Client.cs      ~300  M2 serialization + TCP/EC-SRP5 transport
MproxyFileReader.cs     ~80  cmd=7 (open) + cmd=4 (read) + reassembly
ListFileParser.cs        ~40  parsing of the { crc, size, name, ... } format
CatalogDumper.cs         ~60  main: connect → read list → enumerate .jg names
```

About 480 lines of C# total for a standalone PoC that, on a given router, prints:
- the versions of installed packages
- the list of GUI plugins (→ a proxy mapping to features)
- optionally: downloads and saves the `.jg` files for offline analysis

**The conclusion doesn't change — MACTelnet remains the better choice for tik4net**, but the
reasons are now more precise:
- The actual blocker for MAC Winbox is not M2 serialization (that part is documented), but the
  **mapping of RouterOS operations to binary IDs** for anything beyond the terminal session
- MAC Winbox = a terminal session over M2 → the same VT100 output as MACTelnet, just with roughly
  3× more implementation on top
- The only benefit MAC Winbox would offer over MACTelnet is an encrypted transport — but for
  recovery/bootstrap on an isolated network, that isn't a requirement

---

## What needs to be implemented (`tik4net.mactelnet`)

Estimated scope ~800–1000 lines of pure C#:

```
MacTelnetPacket.cs          ~100  header + control packet encode/decode
MacTelnetTransport.cs       ~150  UDP duplex (broadcast → unicast latch, retry)
EcsrpAuth.cs                ~200  EC-SRP in pure C# (BigInteger + SHA256)
EcsrpParams.cs               ~60  Curve25519 Weierstrass parameters
Md5Auth.cs                   ~30  legacy MD5 fallback
MacTelnetSession.cs         ~200  state machine (SESSIONSTART → auth → data)
VtStripper.cs                ~80  ANSI escape code remover (optional helper)
MacTelnetDiscovery.cs        ~80  wrapper around MndpHelper for MAC resolution
```

Without VtStripper and reusing tik4net's MNDP: closer to ~600 lines.

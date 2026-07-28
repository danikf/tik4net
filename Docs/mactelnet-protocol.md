# MACTelnet — Protokol a implementační poznámky

> Lokální soubor, není v gitu. Naposledy aktualizováno: 2026-05-26 (doplněno MAC Winbox srovnání, Winbox katalog, PoC návrh).
> Výsledek web-research + analýzy zdrojového kódu.

---

## Zdroje

| Zdroj | Popis | Licence |
|---|---|---|
| [haakonnessjoen/MAC-Telnet](https://github.com/haakonnessjoen/MAC-Telnet) | Referenční C implementace, 473★ | GPL-2.0 |
| [KCTech-Lab/KC.MacTelnet](https://github.com/KCTech-Lab/KC.MacTelnet) | Pure .NET 10 implementace, únor 2026 | Nekomerční |
| [kctech.dk/notes/mactelnet](https://www.kctech.dk/notes/mactelnet) | Autorský blog o implementaci KC.MacTelnet | — |
| [salsa.debian.org/debian/mactelnet](https://salsa.debian.org/debian/mactelnet) | Debian balíček mactelnet | GPL-2.0 |
| [TragicWarrior/libvterm](https://github.com/TragicWarrior/libvterm) | VT100 emulátor (C) | MIT |

---

## Přehled protokolu

MACTelnet je proprietární MikroTik protokol pro terminálový přístup přes **Layer 2 (MAC adresa)**
bez nutnosti IP konektivity. Klíčové použití: recovery a bootstrap routeru, který nemá přidělenu IP.

- **Transport:** UDP broadcast (255.255.255.255), port **20561**
- **Max. velikost paketu:** 1500 bytů
- **Není šifrován** — bezpečný jen v izolovaných sítích

---

## Struktura hlavičky paketu (22 bytů)

```
Offset  Délka  Popis
──────────────────────────────────────────────────────────────────
 [0]      1    Verze protokolu = 1
 [1]      1    Typ paketu (viz enum níže)
 [2–7]    6    Zdrojová MAC adresa
 [8–13]   6    Cílová MAC adresa
[14–15]   2    Session key (klient) / Client type (server)  ← ZÁMĚNA dle směru!
[16–17]   2    Client type (klient) / Session key (server)
[18–21]   4    Counter (big-endian uint32)
```

**Důležité:** pozice session key a client type se liší podle směru:
- klient → server: `[14–15] = session_key`, `[16–17] = 0x0015`
- server → klient: `[14–15] = 0x0015`, `[16–17] = session_key`

**Typy paketů:**

| Hodnota | Název | Popis |
|---|---|---|
| 0 | `SESSIONSTART` | Zahájení session (klient posílá jako první) |
| 1 | `DATA` | Datový paket (obsahuje control packets nebo raw terminal data) |
| 2 | `ACK` | Potvrzení přijetí |
| 4 | `PING` | Keepalive ping |
| 5 | `PONG` | Odpověď na ping |
| 255 | `END` | Ukončení session |

**`END` není zdvořilost, je to jediný způsob, jak session ukončit.** UDP nemá FIN — když klient jen
zavře socket, router se to nedozví a **drží login dál**. Změřeno na 7.23.2 (P2.35, 2026-07-28): šest
WinBox-native-over-MAC spojení otevřených a zavřených bez `END` nechalo šest řádků `winbox`
v `/user/active`, které tam byly i po 15 s a zmizely až po zhruba minutě a půl; TCP sourozenec
(`WinboxNative`) nenechal ani jeden, protože tam to routeru řekne FIN. Každý MAC-layer transport tedy
musí `END` poslat při zavření — `MacTelnetUdpClient.TryCloseSession` i
`WinboxMacM2Session.OnDisposing` to dělají (u terminálových režimů až po `/quit`).

---

## Control packets (uvnitř DATA paketů)

DATA pakety mohou obsahovat sekvenci **control packets** nebo **raw terminálová data**.

**Detekce:** control packet začíná magic byty `56 34 12 FF`.

**Struktura jednoho control packetu:**
```
Offset  Délka  Popis
─────────────────────────────
[0–3]    4    Magic: 0x56, 0x34, 0x12, 0xFF
[4]      1    Typ control packetu (viz enum níže)
[5–8]    4    Délka dat (big-endian uint32)
[9+]     n    Data
```

**Typy control packets:**

| Hodnota | Název | Data |
|---|---|---|
| 0 | `BEGINAUTH` | prázdný |
| 1 | `PASSSALT` | viz auth flow |
| 2 | `PASSWORD` | hash odpovědi (17 nebo 32 bytů) |
| 3 | `USERNAME` | string |
| 4 | `TERM_TYPE` | string (např. `"xterm"`) |
| 5 | `TERM_WIDTH` | uint16 little-endian |
| 6 | `TERM_HEIGHT` | uint16 little-endian |
| 7 | `PACKET_ERROR` | chybový kód |
| 9 | `END_AUTH` | prázdný (autentizace dokončena) |

Raw terminálová data (bez magic) jsou přítomna, pokud obsah DATA paketu nezačíná magic byty.

---

## Auth flow — EC-SRP (RouterOS ≥ 6.43, výchozí)

```
Klient → SESSIONSTART
         (typ=0, counter=0, bez dat)

Klient → DATA[ BEGINAUTH + PASSSALT ]
         PASSSALT data = username + "\0" + client_pubkey_x[32] + client_parity[1]
         (celkem: len(username)+1+33 bytů)

Server → DATA[ PASSSALT ]
         PASSSALT data = server_pubkey_x[32] + server_parity[1] + salt[16]
         (celkem: 49 bytů)

Klient  vypočítá EC-SRP potvrzení (viz níže)

Klient → DATA[ PASSWORD(confirm[32]) + USERNAME + TERM_TYPE + TERM_WIDTH + TERM_HEIGHT ]

Server → DATA[ END_AUTH ]
         ↓ od tohoto bodu proudí raw VT100 terminálová data
```

**EC-SRP matematika (Curve25519 Weierstrass forma):**

Algoritmus (identický s C implementací `mtwei.*`):

1. Klient generuje náhodný 32-bytový privátní klíč `a`
2. Klient spočítá veřejný klíč: `A = a*G` (na Curve25519 v Weierstrass formě)
3. Server pošle svůj veřejný klíč `B` a `salt`
4. `validator = SHA256(salt || SHA256(username + ":" + password))`
5. `validator_point = redp1(gen_pubkey(validator).x, parity=1)`
6. `server_point = lift_x(server_x, server_parity)`
7. `sum = server_point + validator_point`
8. `h = SHA256(client_x || server_x)`
9. `vh = (validator_priv * h + a) mod r`  (kde `r` je řád grupy)
10. `z = vh * sum`
11. `Cc = SHA256(h || z.x_montgomery)` — toto je `confirm`

**Křivka:** Curve25519 (`p = 2^255 - 19`), ale výpočty jsou v Weierstrass formě s převodem.

---

## Auth flow — Legacy MD5 (RouterOS < 6.43)

```
Klient → SESSIONSTART

Klient → DATA[ BEGINAUTH ]
         (bez PASSSALT s veřejným klíčem)

Server → DATA[ PASSSALT(salt[16]) ]
         (jen 16 bytů — detekce vs. EC-SRP: délka PASSSALT payload)

Klient  vypočítá: hashdata = [0x00] + password + salt
         password_hash = MD5(hashdata)
         response = [0x00] + password_hash  (17 bytů celkem)

Klient → DATA[ PASSWORD(response[17]) + USERNAME + TERM_TYPE + TERM_WIDTH + TERM_HEIGHT ]

Server → DATA[ END_AUTH ]
```

**Detekce verze auth:** délka `PASSSALT` payloadu:
- 16 bytů → legacy MD5
- 49 bytů → EC-SRP

---

## UDP komunikace — detaily

- **Zdrojový port:** náhodný, 1024–2047 (klient si vybere při startu)
- **Cílový port:** vždy 20561
- **Cílová IP:** broadcast 255.255.255.255 → po první odpovědi serveru se přepne na unicast (latch na IP odkud přišla odpověď)
- **Keepalive:** ACK paket každých ~10s nečinnosti
- **Retry:** exponenciální backoff `[15, 20, 30, 50, 90, 170, 330, 660, 1000]` ms

**Counter:** klient sleduje `outcounter` (inkrementuje o počet datových bytů poslané), server posílá `counter` hodnotu potvrzeného paketu v ACK.

**Duplicate detection:** klient ignoruje pakety s `counter ≤ incounter` (kromě wrap-around).

---

## Pure .NET implementace — proveditelnost

**KC.MacTelnet** (únor 2026) prokázal, že **celý protokol lze implementovat v čistém .NET bez P/Invoke**.

| Vrstva | .NET API | Poznámka |
|---|---|---|
| UDP socket | `System.Net.Sockets.UdpClient` | broadcast + unicast latch |
| Lokální MAC | `System.Net.NetworkInformation.NetworkInterface` | první non-loopback, Up |
| MNDP discovery | `tik4net.Mndp.MndpHelper` | **už existuje v tik4net!** |
| EC-SRP matematika | `System.Numerics.BigInteger` + `SHA256` | ~200 řádků |
| Legacy MD5 | `System.Security.Cryptography.MD5` | triviální |
| Náhodná čísla | `RandomNumberGenerator` | privátní klíč, session key |

**Závislost na Pcap / Npcap není potřeba.** Původní předpoklad v `4x-tiklink-design.md` byl nesprávný.

---

## VT100 terminálový výstup

Po `END_AUTH` server posílá raw **VT100/xterm escape sekvence**. Pro tik4net management library:

| Přístup | Složitost | Vhodnost pro tik4net |
|---|---|---|
| Raw byte stream | nulová | Pro `ITikSession` — uživatel parsuje sám |
| ANSI strip | ~50 řádků | Pro jednoduché CLI výstup scraping |
| Plný VT100 emulátor | ~1000+ řádků | KC.MacTelnet ho dělá via libvterm P/Invoke |

**Doporučení:** `tik4net.mactelnet` exponuje `ITikSession` s raw stream + volitelný `VtStripper`. Plná VT100 emulace není v scope — MACTelnet je primárně recovery transport, ne management.

---

## MAC Winbox — alternativní transport (analýza)

### Jak funguje MAC Winbox připojení

MAC Winbox používá **identický UDP transport jako MACTelnet** (port 20561), liší se pouze client type identifierem v hlavičce paketu:

| | MACTelnet | MAC Winbox |
|---|---|---|
| **Transport** | UDP 20561, broadcast | UDP 20561, broadcast |
| **Client type ID** | `0x0015` | `0x0f90` |
| **Autentizace (ROS ≥ 6.45.1)** | EC-SRP5 | EC-SRP5 (totožná) |
| **Payload po autentizaci** | raw VT100 terminál | Winbox M2 binární protokol |
| **Výstup** | terminálová shell session | GUI management (vlastní binární formát) |

**Discovery:** stejně jako IP Winbox využívá MNDP (UDP 5678).  
**Firewall poznámka:** blokování UDP/20561 via IP firewall MAC Winbox nezablokuje — pakety jdou jako Layer 2 broadcast, IP pravidla se neuplatní. Nutný bridge firewall nebo vypnutí `mac-winbox-server`.

### Struktura Winbox binárního protokolu (M2 / nv::message)

Winbox po autentizaci přenáší proprietární binární protokol interně označovaný jako **WinboxMessage** nebo **nv::message**, ve wire formátu jako **M2** (začíná ASCII byty `4D 32`).

**Charakter protokolu:** typed key-value message format — **není sentence-based** a **není podobný MikroTik API**.

#### Klíče (Keys)

Každý klíč je **24-bitové celé číslo** v jednom ze jmenných prostorů:

| Namespace | Rozsah | Účel |
|---|---|---|
| SYS | `0xFF0001–0xFF00FF` | routování, session tracking |
| CMD | `0xFE0000–0xFEFFFF` | příkazy (builtin i per-binary) |
| User | ostatní | data specifická pro aplikaci |

Systémové klíče (setter metody z reverzovaného `winbox_message.hpp`):

| Klíč | Metoda | Popis |
|---|---|---|
| `0xFF0001` | `set_to(dst, handler)` | cílový RouterOS binary (např. 17 = undo, 2 = mproxy) |
| `0xFF0003` | `set_from()` | zdrojový binary |
| `0xFF0006` | `set_command(cmd)` | kód operace |
| `0xFF0007` | `set_session_id()` | session tracking |
| `0xFF0008` | `set_request_id()` | korelace odpovědí |
| `0xFF0009` | `set_reply_expected()` | příznak, zda server má odpovědět |

#### Datové typy

| Typ | Poznámka |
|---|---|
| bool | |
| u32 | 32-bit unsigned |
| u64 | 64-bit unsigned |
| IPv6 | 16 bytů |
| string | length-prefixed |
| raw bytes | |
| nested WinboxMessage | rekurzivní zanořování |
| array of each above | |

Každý typ existuje i jako pole hodnot. Celkem ~14 různých typů.

#### Šifrování (po autentizaci)

```
[2B délka frame][HMAC][AES-CBC encrypted body]
```
AES-128-CBC, MAC-then-Encrypt, oddělené klíče pro send/receive (odvozeny z EC-SRP5 shared secret).

#### Routing přes ~90 binaries

RouterOS interně spouští ~90 síťově dostupných binárních procesů. Každá operace (IP firewall, interface, DHCP...) se routuje do příslušného binary přes `set_to()`. Neexistuje veřejná dokumentace mapování příkazů na binary IDs.

### Srovnání: Winbox M2 vs. MikroTik API (tik4net)

| Vlastnost | MikroTik API (tik4net) | Winbox M2 |
|---|---|---|
| **Formát** | textové sentence (length-prefixed words) | binární TLV/KV zprávy |
| **Příkazy** | `/ip/firewall/filter/print` (string cesty) | numeric binary ID + numeric command code |
| **Parametry** | `=name=value`, `?name=value` (string) | typed key-value (u32, string, bool…) |
| **Odpovědi** | `!re`, `!done`, `!trap`, `!fatal` sentences | reply message s session/request ID |
| **Dokumentace** | [oficálně dokumentováno](https://help.mikrotik.com/docs/spaces/ROS/pages/47579160/API) | pouze z reverse-engineeringu |
| **Šifrování** | volitelné TLS (port 8729) | vždy AES-128-CBC po auth |
| **Podobnost s tik4net** | ✅ tik4net ho implementuje | ❌ zcela jiný protokol |

**Jsou si podobné?** **Ne.** MikroTik API je textový sentence protokol (ADO.NET styl), Winbox M2 je kompaktní binární protokol s numerickým routováním na interní RouterOS procesy. Koncepty jsou vzdáleně příbuzné (oba jsou KV-based request/response), ale wire format, adresování i sémantika jsou zcela odlišné.

### Závěr: MAC Winbox vs. MACTelnet pro tik4net

**MAC Winbox implementovat nemá smysl.** Důvody:

1. **Winbox M2 ≠ MikroTik API** — implementace by nevyužila žádný existující kód tik4net. Šlo by o nový protokol od nuly.
2. **Žádná veřejná specifikace** — protokol existuje pouze z reverse-engineeringu (Tenable/MarginResearch). Mapping příkazů na binary IDs není dokumentován.
3. **Složitost** — ~14 datových typů, nested messages, routing přes 90 binaries, AES-128-CBC s oddělenými klíči. Řádově složitější než MACTelnet.
4. **Cíl tik4net** — tik4net je wrapper nad MikroTik API. MAC Winbox by přidal parallel management stack bez přínosu pro stávající API uživatele.
5. **Žádná .NET implementace neexistuje** — na rozdíl od MACTelnet (KC.MacTelnet, únor 2026).

**MACTelnet je správná volba** pro Layer 2 / no-IP přístup:
- Protokol je zdokumentován (Omniflux, Wireshark, KC.MacTelnet)
- Dává terminálový přístup = plnohodnotný CLI (stejný jako sériová konzole)
- Screen scraping je nevýhoda jen pro programatické parsování — pro recovery/bootstrap je VT100 stream dostačující
- Odhadovaný scope ~600–800 řádků C# (viz níže)

### Stav reverse engineeringu Winbox M2 protokolu

Protokol byl reverse-engineerován primárně bezpečnostními výzkumníky. Neexistuje žádná oficiální dokumentace od MikroTiku.

#### Primární reference: subixonfire/winbox-terminal-protocol

**Toto je nejkompletnější veřejně dostupná dokumentace Winbox M2 protokolu.**

Repo obsahuje:
- **[PROTOCOL.md](https://github.com/subixonfire/winbox-terminal-protocol/blob/master/PROTOCOL.md)** — 15 KB kompletní specifikace: autentizace (EC-SRP5 i legacy MD5), frame format, M2 TLV struktura, system keys, terminal session protocol s byte-level příklady
- **[winbox_terminal_client.py](https://github.com/subixonfire/winbox-terminal-protocol/blob/master/winbox_terminal_client.py)** — 51 KB Python implementace, self-contained single file, produkčně použitelný terminal client přes TCP 8291

Licence: MIT. Aktivně udržováno (master branch).

#### Ostatní reference (od nejkompletnějších)

| Projekt | Jazyk | Scope | Stav |
|---|---|---|---|
| [subixonfire/winbox-terminal-protocol](https://github.com/subixonfire/winbox-terminal-protocol) | Python | **Kompletní spec + implementace** — EC-SRP5+MD5 auth, M2 serializace, terminal session | ★ primární ref |
| [tenable/routeros – common/](https://github.com/tenable/routeros/tree/master/common) | C++ | `WinboxMessage` serializace + `WinboxSession` | Archivováno 2024 |
| [vulncheck-oss/go-exploit – mikrotik](https://pkg.go.dev/github.com/vulncheck-oss/go-exploit/protocol/mikrotik) | Go | M2Message typy, serializace, Winbox + WebFig session | Aktivní |
| [Cisco-Talos/Winbox_Protocol_Dissector](https://github.com/Cisco-Talos/Winbox_Protocol_Dissector) | Lua (Wireshark) | Wireshark decoder M2 zpráv, všechny field typy | Aktivní |
| [MarginResearch/mikrotik_authentication](https://github.com/MarginResearch/mikrotik_authentication) | Python | EC-SRP5 auth PoC + Winbox client + MAC Telnet client | Aktivní |
| [Margin Research – Pulling into the Limelight](https://margin.re/2022/06/pulling-mikrotik-into-the-limelight/) | blog | Konceptuální popis M2, routing systém, binary adresy | 2022 |
| [Make It Rain with MikroTik – Tenable](https://medium.com/tenable-techblog/make-it-rain-with-mikrotik-c90705459bc6) | blog | WinboxMessage routing, SYS keys, exploitace | 2018 |

---

### Winbox M2 protokol — kompletní specifikace

*(Zdroj: subixonfire PROTOCOL.md + winbox_terminal_client.py, ověřeno ze zdrojového kódu)*

#### Autentizace (EC-SRP5, RouterOS ≥ 6.43)

Probíhá **před** M2 vrstvou jako binární handshake na TCP socketu (není zabalená do M2):

```
Klient → [len 1B][0x06][username\0][pubkey_x 32B][parity 1B]
Server → [len 1B][0x06][srv_pubkey_x 32B][srv_parity 1B][salt 16B]
Klient → [len 1B][0x06][client_confirmation 32B  (SHA256)]
Server → [len 1B][0x06][server_confirmation 32B  (SHA256)]
```

**Odvození klíčů** (po úspěšné auth, ze shared secret `z`):
```python
magic_send    = b"On the client side, this is the send key; on the server side, it is the receive key."
magic_receive = b"On the client side, this is the receive key; on the server side, it is the send key."

txEnc = SHA1(z + b'\x00'*40 + magic_send    + b'\xf2'*40)[:16]  # send AES key (base)
rxEnc = SHA1(z + b'\x00'*40 + magic_receive + b'\xf2'*40)[:16]  # recv AES key (base)
# Finální klíče přes HKDF (custom, 2 iterace HMAC-SHA1):
send_aes_key, send_hmac_key     = HKDF(txEnc)[:16], HKDF(txEnc)[16:32]
receive_aes_key, receive_hmac_key = HKDF(rxEnc)[:16], HKDF(rxEnc)[16:32]
```

#### Autentizace (Legacy MD5, RouterOS < 6.43)

Probíhá přímo přes **nešifrované M2 zprávy** (tag 0x01). Flow:
1. M2 `SYS_TO=[2,2]`, `SYS_CMD=7`, `key_1="list"` → získat `SESSION_ID`
2. M2 `SYS_TO=[2,2]`, `SYS_CMD=5`, `SESSION_ID` → setup challenge
3. M2 `SYS_TO=[13,4]`, `SYS_CMD=4` → získat salt (`key_9`, raw 16B)
4. M2 `SYS_TO=[13,4]`, `SYS_CMD=1`, `key_1=user`, `key_9=salt`, `key_10=\x00+MD5(\x00+password+salt)` → login

---

#### Frame formát (chunk-based, shodný pro enc i plain)

```
Chunk: [chunk_len 1B][tag 1B][payload: chunk_len bytů]

První chunk: tag = 0x06 (enc) nebo 0x01 (plain), chunk_len = délka payloadu
Continuation:  tag = 0xFF, chunk_len = 0xFF (255B, pokračuje) nebo < 0xFF (poslední)
```

**Assembled payload (šifrovaný, tag 0x06):**
```
[enc_length 2B big-endian][IV 16B][ciphertext]
```

**Assembled payload (nešifrovaný, tag 0x01):**
```
[body_length 2B big-endian][M2 message body]
```

**Encrypt (client→server), MAC-then-Encrypt:**
1. `hmac = HMAC-SHA1(send_hmac_key, plaintext)` → 20 bytů
2. `pad_byte = 0x0F - ((len(plaintext+hmac)) % 0x10)`
3. Append `(pad_byte + 1)` bytů hodnoty `pad_byte` (pozn.: NOT PKCS7, vlastní schéma, výsledek vždy block-aligned, žádný byte > 0x0F)
4. `ciphertext = AES-128-CBC(send_aes_key, random_IV, plaintext + hmac + padding)`

---

#### M2 zpráva — TLV formát

Každá M2 zpráva začíná magic `4D 32` ("M2"), následují TLV záznamy:

```
TLV záznam: [key_low 1B][key_high 1B][namespace 1B][type 1B][value...]
full_key = (namespace << 16) | (key_high << 8) | key_low
```

**Namespaces:**

| Namespace | Hex | Účel |
|---|---|---|
| System | `0xFF` | Routování, příkazy, request IDs |
| Session | `0xFE` | Session management |
| User | `0x00` | Data aplikace (terminál, parametry) |

**Datové typy:**

| Typ | Hex | Velikost dat | Poznámka |
|---|---|---|---|
| bool_false | `0x00` | 0 B | Hodnota false |
| bool_true | `0x01` | 0 B | Hodnota true |
| u32 | `0x08` | 4 B LE | |
| u8 | `0x09` | 1 B | |
| u64 | `0x10` | 8 B LE | |
| string_l | `0x20` | 2B LE len + data | délka > 255 |
| string_s | `0x21` | 1B len + data | délka ≤ 255 |
| raw_l | `0x30` | 2B LE len + data | délka > 255 |
| raw_s | `0x31` | 1B len + data | délka ≤ 255 |
| msg_l | `0x28` | 2B LE len + "M2"+data | nested M2 |
| msg_s | `0x29` | 1B len + "M2"+data | nested M2, délka ≤ 255 |
| u32_array | `0x88` | 2B LE count + count×4B LE | |
| str_array | `0xA0` | 2B LE count + (2B LE len + data)× | |
| msg_array | `0xA8` | 2B LE count + (2B LE len + data)× | |

**Systémové klíče:**

| Klíč | full_key | Typ | Popis |
|---|---|---|---|
| SYS_TO | `0xFF0001` | u32_array | Cílový handler path, např. `[76]` nebo `[13,4]` |
| SYS_FROM | `0xFF0002` | u32_array | Zdrojový handler path, např. `[0, src_id]` |
| SYS_REQUEST | `0xFF0005` | bool | True = čeká se odpověď |
| SYS_REQID | `0xFF0006` | u8/u32 | Korelace request/response |
| SYS_CMD | `0xFF0007` | u8/u32 | Kód operace |
| SYS_STATUS | `0xFF0008` | u8/u32 | Status odpovědi (0 = OK) |
| SESSION_ID | `0xFE0001` | u8 | Terminal session ID |

---

#### Mapování handler paths a příkazů (terminálová session)

| Handler path | Binary | Popis |
|---|---|---|
| `[13, 4]` | system info | Initial capabilities, verze, board |
| `[76]` | mepty | Terminal PTY handler |
| `[120]` | session | Login/session handler |
| `[2, 2]` | mproxy | Session management (legacy auth) |
| `[24, 0/1/2]` | management | GUI management data |

| Command | Hex | Popis |
|---|---|---|
| cmdGet | `0x07` | Get data/capabilities |
| cmdOpen | `0x05` | Open handler/session |
| meptyLogin | `0x0A0065` (655461) | Otevřít terminal PTY session |
| meptyData | `0x0A0067` (655463) | Přenos terminálových dat |

**Toto je slovník pro terminálovou session — pro ostatní RouterOS operace (IP, firewall atd.) neexistuje veřejná tabulka příkazů.**

---

#### Terminal session flow (mepty)

```
1. M2: SYS_TO=[13,4], SYS_CMD=7          → capabilities, zjisti src_id
2. M2: SYS_TO=[76], SYS_CMD=0x0A0065     → meptyLogin (cols, rows, password, term="vt102")
   Response: SESSION_ID z [76]
3. M2: SYS_TO=[76], SYS_CMD=0x0A0067     → ready signal (key_3=0, bez key_2)
4. Server → M2: SYS_FROM=[76], key_2=<VT102 bytes>   → terminálový výstup
5. Klient → M2: SYS_TO=[76], SYS_CMD=0x0A0067, key_2=<keystroke>, key_3=<counter>
6. Flow control: po každých ~8 KB přijatých dat poslat ACK (meptyData bez key_2, key_3=recv_counter)
```

---

#### Byte-level příklad: Initial Request

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

#### Byte-level příklad: Keystroke 't' (meptyData)

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

### Aktualizovaný závěr: MAC Winbox vs MACTelnet pro tik4net

**Klíčový insight (potvrzeno):** Pokud MAC Winbox po MACTelnet autentizaci (client_type=0x0f90) tuneluje identický M2 protokol jako TCP Winbox, pak subixonfire `winbox_terminal_client.py` je **přímá reference implementace pro M2 vrstvu**.

**Co by se změnilo oproti TCP Winboxu:**

| Vrstva | TCP Winbox | MAC Winbox |
|---|---|---|
| Transport | TCP socket → port 8291 | UDP 20561, MACTelnet framing |
| Auth handshake | Bare binary přes TCP | MACTelnet EC-SRP5 (stejná matematika, jiné obalení) |
| Auth post-processing | `gen_stream_keys()` → 4 klíče | Totéž — klíčová matematika je shodná |
| M2 frame obalení | Chunk-based přes TCP stream | ? — pravděpodobně M2 frames jako payload MACTelnet DATA paketů |
| M2 zprávy samotné | Identické | Identické |

**Největší otevřená otázka:** Jak přesně jsou M2 frames zabaleny do MACTelnet DATA paketů? Dvě možnosti:
1. **Přímé:** M2 chunk frames (`[chunk_len][0x06][...]`) jdou přímo do MACTelnet DATA payload
2. **Stripped:** Chunk wrapper je odstraněn, do MACTelnet DATA jde jen `[enc_length][IV][ciphertext]`

Toto by bylo potřeba ověřit packet capture (Wireshark + MACTelnet + Winbox MAC session).

**Aktualizované hodnocení proveditelnosti:**

| Vrstva | Effort (původní odhad) | Effort (po nalezení subixonfire) |
|---|---|---|
| M2 serializace/deserialization | ~300–400 ř. C# | ~250 ř. — přímý překlad z Pythonu |
| EC-SRP5 + key derivation | sdílené s MACTelnet | sdílené s MACTelnet |
| Frame encrypt/decrypt | ~100 ř. | ~80 ř. — přímý překlad |
| MAC transport vrstva | — | MACTelnet transport (UDP 20561) |
| Terminal session (mepty) | — | ~150 ř. — přímý překlad |
| M2 framing v UDP | 0 | ? — nutno ověřit |
| **Celkem bez command mapping** | **složité** | **~600–700 ř. C#** |

---

## Winbox startup katalog — jak Winbox zjišťuje dostupné funkce

### Potvrzení hypotézy

Winbox skutečně stahuje katalog z routeru při každém připojení. Mechanismus:

#### 1. List file — co a jak Winbox stahuje

Winbox posílá M2 zprávu na **mproxy** (binary `[2, 2]`):

```
M2
  SYS_TO      = [2, 2]          # mproxy, handler 2
  SYS_FROM    = [0, src_id]
  SYS_REQUEST = true
  SYS_REQID   = N               (u8)
  SYS_CMD     = 7               # open file for reading in /home/web/webfig/
  key_1       = "list"          (string, user namespace) — název souboru
```

Odpověď obsahuje **session file handle** (SESSION_ID). Winbox pak čte obsah:

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

Soubor `/home/web/webfig/list` obsahuje seznam pluginů, které Winbox potřebuje:

```
{ crc: 164562873, size: 1149, name: "advtool.jg", unique: "advtool-fc1932f6809e.jg", version: "6.39.3" }
{ crc: ...,      size: ...,  name: "ipv6.jg",     unique: "ipv6-XXXXXXXX.jg",       version: "6.39.3" }
...
```

Každý nainstalovaný RouterOS package přispívá svými `.jg` soubory. Winbox je stáhne (stejnou cestou přes mproxy cmd=7/4) a uloží lokálně do `%AppData%\Roaming\Mikrotik\Winbox\`.

#### 2. Co `.jg` soubory obsahují

`.jg` soubory jsou Winbox **GUI pluginy** (proprietární binární formát, ne standard DLL). Každý plugin:
- definuje UI pro danou RouterOS funkci (IP/Firewall, Interface, DHCP...)
- obsahuje **mapování UI operací → M2 příkazů** (binary ID + command code)
- je vázán na konkrétní verzi RouterOS (unique hash v názvu)

Toto je přímá odpověď na otázku "kde je slovník příkazů" — je zalit v `.jg` pluginech, nikoliv v žádné veřejně přístupné tabulce.

#### 3. RouterOS-interní katalog: `/nova/etc/loader/system.x3`

Paralelně s `.jg` mechanismem existuje na routeru soubor `/nova/etc/loader/system.x3` (a `*.x3` pro každý package v `/ram/pckg/<name>/nova/etc/loader/`):

- Proprietární pseudo-XML binární formát, parsovaný `libuxml++.so`
- Obsahuje mapování: **binary path → SYS_TO ID**
- Příklad výstupu z [tenable/routeros parse_x3](https://github.com/tenable/routeros/tree/master/msg_re/parse_x3):
  ```
  /nova/bin/log      → ID 3
  /nova/bin/radius   → ID 5
  /nova/bin/moduler  → ID 6
  /nova/bin/user     → ID 13
  ```
- Winbox toto nestahuje — je to interní RouterOS mechanismus pro process spawning (`/nova/bin/loader`)

#### 4. Potvrzení: MAC Winbox = stejný M2 obsah

[BasuCert/WinboxPoC](https://github.com/BasuCert/WinboxPoC) obsahuje **`MACServerExploit.py`** (exploit CVE-2018-14847 přes MAC protokol). Klíčový kód:

```python
CLIENT_TYPE = 0x0F90   # ← toto je MAC Winbox client type
```

A kriticky: **payload byte array `a` a `b` jsou identické** s TCP Winbox exploitem (`WinboxExploit.py`). Jediný rozdíl je transport (UDP 20561, MACTelnet framing vs. TCP 8291). **M2 zprávy samotné jsou byte-for-byte totožné.**

#### 5. CVE-2018-14847 — jak ukázal mproxy protokol

Exploit využil skutečnosti, že mproxy cmd=4, 5, 7 **nevyžadovaly autentizaci** (pre-patch). Path traversal v `key_1`:

```
key_1 = "/////./..///////./..//////./../../flash/rw/store/user.dat"
```

Místo `/home/web/webfig/user.dat` → četl `/flash/rw/store/user.dat` (databáze hesel).

Po patchi: cmd=7 vyžaduje autentizaci, ale mechanismus stahování list file + pluginů funguje stejně (s přihlášením).

---

### PoC návrh: Winbox M2 katalog enumerátor

**Cíl:** Po autentizaci přečíst list file a vypsat dostupné RouterOS featury + binaries. Není nutné pro tik4net, ale zajímavé jako standalone nástroj.

#### Vstupy pro implementaci

| Složka | Zdroj |
|---|---|
| EC-SRP5 auth (TCP 8291) | `subixonfire/winbox-terminal-protocol` — přímý překlad do C# |
| MACTelnet auth (UDP 20561) | notes + KC.MacTelnet |
| M2 serializace | `subixonfire/winbox-terminal-protocol` — přímý překlad do C# |
| Mproxy file read (cmd=4, 7) | CVE-2018-14847 exploit kód — triviální M2 zprávy |
| x3 parser | [tenable/routeros msg_re/parse_x3](https://github.com/tenable/routeros/tree/master/msg_re/parse_x3) (C++) → přeport do C# |

#### Odhadovaný scope PoC

```
WinboxM2Client.cs      ~300  M2 serializace + TCP/EC-SRP5 transport
MproxyFileReader.cs     ~80  cmd=7 (open) + cmd=4 (read) + reassembly
ListFileParser.cs        ~40  parsování { crc, size, name, ... } formátu
CatalogDumper.cs         ~60  main: connect → read list → enumerate .jg names
```

Celkem ~480 řádků C# pro standalone PoC, který na daném routeru vypíše:
- verze nainstalovaných packages
- seznam GUI pluginů (→ proxy mapování na features)
- volitelně: stáhne a uloží `.jg` soubory pro offline analýzu

**Závěr se nemění — MACTelnet zůstává lepší volba pro tik4net**, ale důvody jsou nyní přesnější:
- Stávající blockerem pro MAC Winbox není M2 serializace (ta je zdokumentována), ale **mapování RouterOS operací na binary IDs** pro cokoliv kromě terminálové session
- MAC Winbox = terminálová session přes M2 → stejný VT100 výstup jako MACTelnet, jen s ~3× větší implementací navíc
- Jediný přínos MAC Winboxu oproti MACTelnet by byl šifrovaný transport — ale pro recovery/bootstrap v izolované síti to není požadavek

---

## Co je potřeba implementovat (`tik4net.mactelnet`)

Odhadovaný scope ~800–1000 řádků čistého C#:

```
MacTelnetPacket.cs          ~100  encode/decode hlavičky + control packets
MacTelnetTransport.cs       ~150  UDP duplex (broadcast → unicast latch, retry)
EcsrpAuth.cs                ~200  EC-SRP v pure C# (BigInteger + SHA256)
EcsrpParams.cs               ~60  Curve25519 Weierstrass parametry
Md5Auth.cs                   ~30  legacy MD5 fallback
MacTelnetSession.cs         ~200  state machine (SESSIONSTART → auth → data)
VtStripper.cs                ~80  ANSI escape code remover (volitelný helper)
MacTelnetDiscovery.cs        ~80  wrapper nad MndpHelper pro MAC resolution
```

Bez VtStripperu a s reusem tik4net MNDP: spíše ~600 řádků.

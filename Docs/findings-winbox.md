# Findings — WinBox CLI connection (kapitola G)

**Datum:** 2026-06-05
**Transport:** `TikConnectionType.WinboxCli` (TCP 8291, mepty terminál)
**Ověřeno živě:** RouterOS 7.21.4 CHR (souřadnice v `tik4net.integrationtests/App.config`)

Sdílené poznatky o protokolu jsou v paměti `project_winbox_m2_poc` a `ref_cli_telnet`. Tady jen to,
co vyšlo najevo nebo se vyřešilo při produkční integraci (kapitola G).

---

## 1. ROOT CAUSE — SESSION_ID > 255 je u32, ne u8 (vyřešeno)

**Symptom:** mepty terminál nešel otevřít — `OpenTerminalSession` házelo
`InvalidOperationException: No SESSION_ID in M2 response`. PoC měl proto oba mepty testy
`[Ignore]` s domněnkou „drain timing between terminal sessions" — **mylná diagnóza**.

**Skutečná příčina (živý dump mepty-open response):**

```
4D 32                                              "M2"
01 00 FF 88 02 00 00000000 00000008                SYS_TO  u32[] = [0, 8]
02 00 FF 88 01 00 0000004C                          SYS_FROM u32[] = [76]   (0x4C = mepty handler)
1C 00 FF A0 0001 0010 6D73...34                     0xFF001C str_array "msg-proxy-7.21.4"
01 00 FE 08 09 01 00 00                              SESSION_ID key=0xFE0001, TYPE=0x08 (u32) = 0x109 = 265
03 00 FF 09 02                                       0xFF0003 u8 = 2
06 00 FF 09 01                                       0xFF0006 u8 = 1
```

Session id = **265**, poslané jako **u32 (typ 0x08)**, protože nemůže být v jednom bajtu.
PoC `M2Message.ParseSessionId` hledal jen `type == 0x09` (u8) → nenašel. A `SessionIdField(int)`
kódoval vždy u8 → poslání 265 zpět = `(byte)265 = 9` → adresování špatné session → mrtvý terminál.

**Fix** (`tik4net/Winbox/M2Message.cs`):
- `ParseSessionId` čte u klíče `0xFE0001` typ 0x09 (u8) i 0x08 (u32).
- `SessionIdField(int id)` kóduje u8 pro `id ≤ 255`, jinak u32.

Predikováno v `project_winbox_m2_poc` §9: „Správná implementace SessionIdField — reálně může být 2B".
Drain-timing hypotéza z PoC ignore komentáře byla scestná.

---

## 2. Login terminal-size hint: 80×25, NE wide

`meptyLogin` (cmd `0x0A0065`) nese `U32User(3)=cols`, `U32User(4)=rows`. Když se sem dá velká
hodnota (zkoušeno 65535), RouterOS vrátí **error response bez SESSION_ID** (stejný symptom jako §1,
jiná příčina!). Drž **80×25** jako v PoC.

Skutečnou šířku terminálu stejně určí až **VT100 cursor-probe** (`ESC[9999C ESC[6n`), na kterou
odpovídá `Vt100State(65535, 25)` — reply se capne ~9999 sloupců, což stačí, aby se nezalamovaly
dlouhé `print as-value` řádky. (Stejný princip jako MAC-Telnet, viz findings-mactelnet.)

---

## 3. Šifrovaný kanál — DataAvailable gating je povinný

Na rozdíl od MAC-Telnet (UDP datagramy) jede WinBox přes **TCP + AES-128-CBC rámce**. Past
(potvrzeno z `project_winbox_m2_poc` §2): nikdy nevolej decrypt s krátkým timeoutem v retry smyčce —
když timeout vyprší **uprostřed rámce**, TCP stream zůstane misaligned a každé další čtení selže
IOException.

Řešení v `WinboxCliClient`: každé čtení je gated `while (!_session.DataAvailable) Thread.Sleep(20)`
a teprve pak `_session.Receive(5000)` s velkorysým per-frame timeoutem (jen ohraničí už přicházející
rámec, nikdy nevyprší mid-frame). Funguje to spolehlivě i pro vícepaketové odpovědi.

⚠️ Ten velkorysý timeout je bezpečný **jen dokud `DataAvailable` nelže**. Nad MAC/UDP lhal a stál
5 s na každý příkaz — viz kap. 15.

---

## 4. Persistentní mepty session funguje

PoC otvíral **novou** mepty session pro každý příkaz (`RunTerminalCommand`). Produkce otevře mepty
**jednou** v `LoginAsync` a všechny příkazy jdou stejnou session (counter `U32User(3)` se inkrementuje
napříč příkazy). Žádný problém — PTY je terminál, drží stav. Tím odpadá i domnělá „drain timing between
terminal sessions" potíž.

---

## 5. Architektura — sdílené vs CLI-specifické

- `tik4net/Winbox/` = **jen sdílené**: `M2Message` (TLV), `WinboxTcpTransport` (chunked framing),
  `WinboxM2Session` (EC-SRP5/legacy MD5 auth + AES frame I/O + generické `Send`/`Receive`/`SendReceive`
  /`NextReqIdField`). **Bez mepty/VT100** — aby to převzal budoucí `WinboxNative*`.
- `tik4net/WinboxCli/` = terminálový mód: `WinboxCliClient` (mepty [76] + VT100), `WinboxCliConnection`.

Krypto (`EcSrp5`, `WinboxStreamCrypto`) je už v `tik4net/Crypto/` z kapitoly E (sdílí MAC-Telnet) →
WinBox NEpotřebuje separátní NuGet, žije in-core jako Telnet/MAC-Telnet.

Pro kapitolu H (`WinboxCliMac*`) bude potřeba `WinboxM2Session` zobecnit nad transport
(`WinboxTcpTransport` → `MacLayerTransport`); generické Send/Receive jsou na to připravené.

---

## 6. Legacy MD5 + terminál = nepodporováno

mepty session jede jen přes šifrovaný EC-SRP5 kanál. Pokud server spadne na legacy MD5 auth
(pre-6.43 RouterOS), `WinboxCliClient.OpenTerminalSession` hodí `NotSupportedException`.
Auth fallback v `WinboxM2Session` zůstává (pro budoucí native legacy operace), ale terminál ne.

---

## 7. Výsledky (WinboxCli / TCP)

- `WinboxCliProtocolTest` — **2/2** (login+list interfaces, set+verify ether1 comment)
- `InterfaceTest` přes `winboxcli.runsettings` — **9 pass / 6 skip / 0 fail** (skip = CLI limitace:
  async/listen/monitor-traffic; přesná parita s Telnet/MAC-Telnet)
- Login ~0.6 s, set+verify ~2 s.

---

# WinBox CLI over MAC (`WinboxCliMac`, kapitola H)

**Transport:** UDP 20561, `client_type=0x0f90`, mepty terminál. Sdílí celý CLI engine s G přes
abstrakci `IWinboxM2Channel`; liší se jen kanál (`WinboxMacM2Session : MacLayerTransport`).

## 8. ROOT CAUSE — MAC-WinBox = WinBox protokol tunelovaný přes MAC (PoC měl obojí špatně)

PoC `WinboxMacClient` byl `[Ignore]` „EXPERIMENTAL, M2 framing unverified". Dvě mylné hypotézy,
živě vyvráceny (RouterOS 7.21.4):

1. **Auth NENÍ MAC-Telnet control-packet auth.** PoC volal base `MacLayerTransport.Authenticate`
   (CTRL_BEGINAUTH/PASSSALT) → **timeout** (router neodpoví). Správně: **identický WinBox EC-SRP5
   handshake jako TCP** — length-prefixed `[len][0x06][payload]` rámce poslané jako DATA payload.
   Challenge dorazila jako `31-06-0E-22-…` = `[len=49][tag=0x06][32B xWB][1B parity][16B salt]`.
   ⇒ V `WinboxMacM2Session.MacAuthEcSrp5` se po `BaseConnect(0x0f90)` posílá WinBox hello a dělá se
   stejná EC-SRP5 matematika jako `WinboxM2Session.EcSrp5Auth`.

2. **Šifrované M2 NENÍ holý `Encrypt(m2)` v DATA.** PoC posílal `Send(PKT_DATA, Encrypt(m2))` a
   dekódoval holý payload → dekrypt z špatného offsetu → „Not a valid M2 response". Správně:
   **stejné chunk framing jako TCP** (`[chunkLen 1B][tag][data]…`, 0xFF = continuation) uvnitř DATA
   paketů. ⇒ `Send` chunk-wrapuje (`ChunkWrap`), `Receive` reassembluje přes `_rxBuf` buffer
   (chunk může přesáhnout hranici DATA paketu) a teprve pak `WinboxStreamCrypto.Decrypt`.

**Závěr:** MAC-WinBox = celý WinBox protokol (EC-SRP5 handshake + chunked AES rámce) tunelovaný přes
MAC reliable stream. Jediný rozdíl oproti TCP je transport (DATA/ACK pakety místo TCP streamu).
To je důvod, proč šel CLI engine (`WinboxCliClient`) sdílet beze změny — stačila kanálová abstrakce.

## 9. Další MAC poznatky

- **ACK = counter+payloadLen** — produkční `MacLayerTransport.AckData` (z kap. D/E), ne holý
  `SendAck(counter)` z PoC. Dedup retransmisí přes `_inCounter`.
- **mac-winbox server** je samostatný od mac-telnet: `/tool/mac-server/mac-winbox set
  allowed-interface-list=all` (test ho nastaví v `[ClassInitialize]`). Oba byly živě enabled.
- **SESSION_ID u8/u32 fix z G** platí i tady (sdílený `M2Message`).
- **Rychlost:** login ~16 s, set+verify ~32 s (MNDP ~5 s + per-frame AES + UDP polling se sleepy).
  Mnohem pomalejší než TCP (~1–2 s). Pro produkci nastavit `RouterMac` (bypass MNDP).

## 10. Výsledky (WinboxCliMac / MAC)

- `WinboxCliMacProtocolTest` — **2/2** (login+list, set+verify ether1 comment).
- Regrese: WinboxCli 2/2 + MacTelnet 2/2 + WinboxCliMac 2/2 = **6/6** dohromady.

---

## 11. Konstanty protokolu centralizovány (2026-06-11)

Všechna M2 čísla zmíněná výše inline (`0x0A0065` meptyLogin, `0x0A0067` meptyData, mepty
`U32User(3)=cols`/`U32User(4)=rows`, `0xFF0005/06/07`, mproxy cmd 7/3/4/5, SESSION_ID `0xFE0001`)
žijí teď v **`tik4net/Winbox/WinboxM2Protocol.cs`** (`internal static`, sdíleno produkcí i testem).
Sekce: `SysKey` / `RecordKey` / `Command` / `Error` / `Mproxy` / `SysInfo` / `LegacyAuth` / `Mepty` / `Tlv`.
Pozn.: mepty `Key.Cols`(3 na Login) a `Key.Counter`(3 na Data) = stejné číslo, jiný význam (zdokumentováno).
Plný soupis + kolize viz `winbox-native-m2-plan.md` §12.

---

## 12. M2 request/response korelace — podklad pro multiplexing (2026-07-21)

Ověřeno živě (RouterOS 7.21.4, testovací CHR) při přípravě `winbox-m2-multiplexing-design.md`.
Do té doby M2 vrstva jela **lockstep** — `SendRecvRaw` čte „další rámec", ne „můj rámec" — takže korelaci
nikdo nepotřeboval a `M2Message` na ni dodnes nemá parser.

### 12.1 `0xFF0006` (RequestId) se v odpovědi vrací ⇒ je to korelační klíč

`/ip/address/print` přes WinboxNative = tři M2 výměny v jedné session (reference resolution — adresa
odkazuje `ether1`, takže následuje getall interface a VRF):

| výměna | handler (`0xFF0001` To) | request `0xFF0006` | response `0xFF0006` | response `0xFF0003` |
|---|---|---|---|---|
| getall address | `[20,1]` | 2 | **2** | 2 |
| getall vrf | `[20,101]` | 3 | **3** | 2 |
| getall interface | `[20,0]` | 4 | **4** | 2 |

`0xFF0006` sleduje request přesně. Multiplexing (víc requestů v letu, dispatch odpovědí podle id) je
tedy proveditelný.

### 12.2 `0xFF0003` korelační pole NENÍ — past na jednovýměnový trace

`0xFF0003` není v `WinboxM2Protocol` definované a napříč session zůstává konstantní (2), zatímco req id
roste. Vypadá to na session / reply-channel id.

**Past:** v trace o jedné výměně (`/system/identity/print`, req id = 2) má `0xFF0003` *náhodou stejnou
hodnotu* jako req id → na jednom vzorku by se vybralo špatné pole. Rozliší je až víc round-tripů.

Nezávislé potvrzení je ostatně **už v §1 tohoto dokumentu**: mepty-open dump z 2026-06-05 má
`0xFF0003 u8 = 2` a `0xFF0006 u8 = 1` — tam se ta dvě pole liší. Ten důkaz ležel v repu 6 týdnů,
než ho někdo potřeboval.

### 12.3 Krypto je bezstavové per rámec ⇒ multiplexing je krypticky bezpečný

Klíčové zjištění, protože tohle byl jediný reálný blocker. Přes název `WinboxStreamCrypto` **to není
běžící stream cipher**: `Encrypt` emituje `[enc_len 2B BE][IV 16B][ciphertext]` s **novým náhodným IV
pro každý rámec** a `Decrypt` si vystačí s tím rámcem + fixními klíči z handshake. Žádný cross-frame
stav, žádný čítač, žádné replay okno.

⇒ Rámce jde dešifrovat nezávisle a **dokončovat mimo pořadí**. Kdyby šlo o stavový stream cipher,
multiplexing by bez redesignu krypto vrstvy nešel vůbec.

Jediné pořadové omezení zůstává **framing**: `RecvChunked` skládá sekvenci chunků, takže čtení musí být
serializované (jeden reader) a zápis taky (sekvence chunků se nesmí proplést). To je přesně reader-loop +
write-lock, nic navíc.

### 12.4 `0xFF0001`/`0xFF0002` (To/From) se v odpovědi prohazují

Request `To=[20,1] From=[0,8]` → response `To=[0,8] From=[20,1]`. Handler je tedy sekundární signál,
ale **není unikátní** — dva souběžné requesty na stejný handler se podle něj nerozliší. Dispatchovat
výhradně podle `0xFF0006`.

### 12.5 Dnes neexistují nevyžádané příchozí rámce

Monitory jsou **polling smyčky**, ne subscription: `MonitorLoop` dělá `StartMonitor` →
opakovaně `PollMonitor` → `CancelMonitor`, každý krok normální request/response. Proto lockstep vůbec
funguje. Multiplexovaná implementace ale musí umět zahodit nespárovaný rámec (opožděná odpověď po
timeoutu) — robustnost, ne běžná cesta.

### 12.6 Req id je jeden bajt

`NextReqIdField()` = `U8Sys(RequestId, (byte)(++_reqId))` → **wrapuje na 256**, a `++_reqId` nad prostým
`int` polem přestane být bezpečné, jakmile budou souběžní odesílatelé (`Interlocked` + maska na 8 bitů).
Id `0` se dnes nikdy nepoužije (counter je pre-inkrementovaný) → nechat rezervované jako „žádné id".

### 12.7 `0xFE0019` = objCount, nikoli „následují další rámce" (uzavřeno 2026-07-21)

Podezření z předchozí verze této sekce (že `0xFE0019=u8:1` značí pokračování) se **nepotvrdilo**.

Zdroj pravdy — webfig `master-d53cd8ec58cb.js`, obě jediná dvě použití pole v celém souboru:

```js
// ObjectMap.prototype.getall  → onreply
if (rep.ufe0019 != null) me.objCount = rep.ufe0019;
// ObjectMap.prototype.listen  → notifyLstn
if (msg.ufe0019 != null) me.objCount = msg.ufe0019;
```

Uloží se do `objCount` a **nikde se nečte** v řízení toku — žádná podmínka smyčky, žádné ukončení,
žádná registrace. Je to informativní celkový počet objektů (proto `1` u výměn s jedním záznamem a
nepřítomnost tam, kde ho handler neposlal). V `WinboxM2Protocol.RecordKey.Count` už ostatně takto
zdokumentovaný **byl** — konstanta ležela v repu s komentářem „total object count" a stačilo se na ni
podívat, než jsem to vedl jako otevřenou otázku.

**Dopad na multiplexing: žádný.** Pravidlo dokončení zůstává „jeden request → právě jeden rámec
odpovědi", registrace se uzavírá prvním rámcem s odpovídajícím `0xFF0006`.

#### 12.7.1 Stránkování multi-frame není

Ověřeno tamtéž — pokračování je **nový request**, ne další nevyžádaný rámec:

```js
else if ((rep.ufe0003 != null || rep.mfe0015) && !me.block) {
    if (rep.ufe0003 != null) req.ufe0003 = rep.ufe0003;
    post(req, onreply);            // ← nový request, nové id
}
```

Přesně to dělá i náš klient: smyčka volá `NextReqIdField()` uvnitř každé iterace
([WinboxNativeM2Operations.cs:129](tik4net/Winbox/WinboxNativeM2Operations.cs:129)) a token přikládá
jako `RecordKey.Continuation` ([:134](tik4net/Winbox/WinboxNativeM2Operations.cs:134)). V modelu
registrací se tedy nic nemění: **každá stránka je samostatná registrace s vlastním id.**

Vedlejší nález (mimo rozsah multiplexingu): webfig pokračuje i na `rep.mfe0015`, náš klient sleduje
jen `ufe0003` ([:151](tik4net/Winbox/WinboxNativeM2Operations.cs:151)). U handleru, který stránkuje
přes `mfe0015`, bychom tiše vrátili jen první stránku. Živě jsme na to nenarazili; stojí za samostatné
ověření.

#### 12.7.2 Pozn. k `post()` — webfig koreluje HTTP, ne `0xFF0006`

`uff0006` se ve webfig JS **nevyskytuje vůbec**: jde o jsproxy nad HTTP, kde pár request/response drží
samo HTTP. Webfig proto **není** zdroj pravdy pro sémantiku req-id — ta stojí na živém trace v §12.1.
Pro `0xFE0019` zdrojem pravdy je, protože význam pole je na transportu nezávislý.

Nevyžádané zprávy webfig zná jen přes `subscribe` (cmd `0xFE0012`) a dispatchuje je podle `Uff0002`
(`From`/path) na odděleném long-pollu (`post_notification_request`). Nad nativním TCP by takové pushe
chodily in-band — dnes je nepoužíváme (§12.5), ale je to druhý důvod, proč reader loop potřebuje větev
pro nespárovaný rámec (§4.4 návrhu), a naznačuje, čím by se dispatchovala, kdyby subscribe přibylo.

### 12.8 Paralelní spojení z jednoho stroje se v M2 neznačí

Hypotéza, že by některé pole muselo identifikovat spojení (kvůli víc session z jednoho stroje, typicky
u MAC variant), **na M2 vrstvě neplatí** — rozlišuje se pod ní:

| transport | co odděluje paralelní session |
|---|---|
| WinBox TCP / TCP-MAC | TCP socket (4-tuple), každá session má vlastní spojení |
| WinBox nad MAC vrstvou | náhodný `_sessionKey` v hlavičce paketu ([MacLayerTransport.cs:98](tik4net/MacTelnet/MacLayerTransport.cs:98)) |

Kandidátem na „reply-channel id" je z §12.2 `0xFF0003` (konstantní 2 napříč session) — ve webfig JS se
ale nevyskytuje vůbec, takže jeho význam zůstává neurčený. Pro dispatch je to jedno: **je konstantní,
takže by dva souběžné requesty stejně nerozlišil.** Korelace zůstává výhradně na `0xFF0006`.

---

## 13. Router odmítne správné přihlášení asi jednou ze sta (2026-07-30, P2.41)

**Ověřeno živě na RouterOS 7.23.2.** Zhruba **0,5–1 % WinBox loginů** skončí tím, že router pošle
tam, kde patří 32bajtový potvrzovací digest, **33 bajtů ASCII**:

```
69 6E 76 61 6C 69 64 20 75 73 65 72 20 6E 61 6D 65 20 6F 72 20 70 61 73 73 77 6F 72 64 20 28 36 29
"invalid user name or password (6)"
```

Router si za tím stojí i ve vlastním logu (`system,error,critical login failure for user admin …
via winbox`), takže **naše hláška o špatném hesle vymyšlená nebyla** — jen nikdo nevěděl proč.
Přihlašovací údaje jsou přitom správné a o 50 ms později fungují.

### 13.1 Není to v nás — důkaz přehráním téhož klíče

Rozhodující experiment (`WinboxHandshakeLoopProbeTest.Probe_WinboxHandshake_SameKeyRetry`): po každém
odmítnutí se handshake zopakuje s **týmž** klientským klíčem `privA`. Výsledek **9 z 9 přehrání přijato**
— tedy tytéž bajty, které router právě odmítl, o chvíli později přijme. Jediné, co se mezi pokusy mění,
je routerův vlastní efemérní klíč `xWB`. Vyloučeno tím bylo:

| podezření | jak vyvráceno |
|---|---|
| chyba v naší EC-SRP5 aritmetice | 4000 round-tripů klient↔server offline, **0 divergencí** (`EcSrp5RoundTripTests`) |
| vedoucí nula v `xWA` (1/256 ≈ pozorovaná četnost) | vynuceno záměrně: **4 z 5 uspělo**; náhoda v prvním vzorku |
| rate-limit / frekvence pokusů | 2/40 při 0 ms, 0/40 při 250 ms, 1/40 při 1000 ms — bez trendu |
| desync rámců | rámec je korektní chunk s tagem `0x06`, délka 33 přesně odpovídá délce textu — nic nepřeteklo ani nechybí |
| jiný transport / jiný auth | API: **0 z 400** odmítnutí — jev je specifický pro WinBox handshake |

V logu je i jeden osamocený `via api` záznam, který se nepodařilo připsat žádnému našemu klientovi;
400 čerstvých API loginů bylo čistých, takže se na něm nic nestaví.

### 13.2 Co s tím — bounded retry, protože obsah obě příčiny nerozliší

**Skutečně špatné heslo vypadá úplně stejně** (je to routerova normální cesta pro odmítnutí), takže
podle obsahu odpovědi je odlišit nelze — jedině podle toho, že přechodné odmítnutí zmizí a skutečné ne.
Proto `WinboxLoginRetry`: 3 pokusy, 100 ms mezi nimi, a retryuje se **výhradně**
`WinboxLoginRefusedException`. Každý pokus staví **nový kanál** — odmítnutý handshake nechá ten starý
nepoužitelný.

Cena je vědomá: opravdu špatné heslo selže o ~200 ms později a zanechá v routeru 3 řádky `login failure`
místo jednoho.

**Ověřeno:** 600 produkčních otevření (WinboxCli / WinboxNative / WinboxNativeMac po 200), **0 selhání
a 6 pohlcených odmítnutí**, všechna vyřešená prvním retry. Že retry opravdu koná práci (a ne že router
zrovna mlčel) je vidět z trace note `wbx.login` — bez něj je zelený běh nerozlišitelný od zametení pod
koberec.

### 13.3 Vedlejší nálezy

- **Handshake se do wire trace vůbec nepromítal.** `SendHandshake` zapisuje přímo do `Stream` a čte
  přes `ReadExact`, takže míjel emit pointy v `SendChunked`/`RecvChunked` — právě ta výměna, která se
  nejhůř ladí, byla jediná neviditelná. Doplněno (`wbxtcp.frame`, note `ecsrp5 …`).
- **MAC vrstva traceovala jen odesílání.** `RecvUntil` neemitoval nic, takže z trace nešlo poznat
  „odpověď nedorazila" od „nikdy jsme se neptali". Doplněno.
- **Fallback na legacy MD5 se vybíral podle textu hlášky** (`ex.Message.Contains("EC-SRP5")`) a čekalo
  se na challenge jen 3 s. Pomalý router tak spadl do MD5 auth, ta na moderním RouterOS selhala a
  výsledkem bylo „wrong username or password". Nahrazeno typem `WinboxEcSrp5UnsupportedException`
  a oknem `max(3 s, ConnectTimeout)` = 15 s.
- **WinboxCliMac je tak pomalý, že 9 testů vyprší** — plný běh 1 h 22 m a 313/9, zatímco týž CLI engine
  přes TCP (`winboxcli`) dá 322/322 za 8 minut. Login ~11 s proti ~1,4 s. **Nesouvisí to s P2.41**:
  těch 9 testů dopadlo na buildu s P2.41 i na stashnutém baseline **identicky (6 fail / 3 pass /
  3 m 14 s)**.

  Past, na kterou nenaletět: nabízí se `RecvUntil` a jeho `Thread.Sleep(20)` místo čekání na socketu
  (každý rámec, který dorazí těsně po kontrole `Available`, platí až 20 ms). **Vyzkoušeno** —
  `_udp.Client.Poll(20 ms, SelectRead)` posunul podmnožinu z 6 fail / 3 m 14 s na 5 fail / 2 m 45 s,
  tedy **~15 %, a pořád červeně**. Vráceno zpět; zbylých ~85 % je jinde, nejspíš v tom, že
  `WinboxCliClient` pollje `DataAvailable` vlastními sleepy (viz §3 — to gatování je záměrné a rušit
  se nesmí, jen předělat na event-driven). Rozepsáno jako P2.43.

## 14. Singletony se nezapisovaly vůbec (P2.44, 2026-07-30)

`0xFE000E` (`setcmd(holder)`) je v `winbox-native-m2-protocol.md` zdokumentovaný od začátku, ale
transport ho **nikdy nevolal**. Zápis šel jedinou cestou — `0xFE0003` (`set`) + `ufe0001` = `.id` —
a singleton (`.jg` `type:'item'`) žádné `.id` nemá, takže `ResolveRecordId(required:true)` skončil na

```
no such item: could not resolve record .id '' on '/system/identity/set'
```

Platí to pro **každou** `IsSingleton` entitu (`/system/identity`, `/ip/dns`, `/ip/settings`, `/snmp`,
`/system/note`, … ~35 tříd). Suita to neodhalila, protože singletony jenom **četla**.

Tvar požadavku podle webfig `ObjectHolder.setObject`:

```js
req.Uff0001 = this.attrs.path;
req.uff0007 = this.attrs.setcmd || 0xfe000e;
if ("ufe0001" in obj) req.ufe0001 = obj.ufe0001;   // .id jen když ho objekt sám nese
```

`.id` se tedy posílá **volitelně** — jediný známý případ je skryté okno „Change Password“
(`setcmd:3`), které míří na záznam uživatele. `WinboxNativeConnection.WriteFields` proto pošle `.id`
jen v doslovném tvaru `*HEX`; dohledávání podle jména by znamenalo `getall`, na který singleton
handler nemá co odpovědět.

### 14.1 `/system/identity` navíc vrací pole pod GUI labelem

Handler `[24,1]`, `.jg`:

```js
{title:'Identity',type:'item',path:[ 24,1 ],autostart:1,
 c:[{name:'Identity',type:'string',id:'sc'},{name:'Version',type:'string',id:'sd',nonpublic:1}]}
```

Čtení tedy vracelo `{"version":"7.23.2","identity":"CHR"}`, kdežto API vrací `{"name":"CHR"}` —
`LoadSingle<SystemIdentity>()` padal na `Missing field 'name'`. Řešeno shipped field aliasem
`name ↔ identity` (stabilní text, klíč pořád z `.jg`).

Pole `version` se **nezahazuje**: `nonpublic:1` neznamená „není to API pole“ — nese ho i řada polí,
která API běžně vrací (`MAC Address`, `Interface`, `L2 MTU`). Native záznamy jsou obecně nadmnožinou
API polí a mapper pole navíc ignoruje.

### 14.2 `multilinestring` je řetězec, ne seznam

`EncodeField` odmítal jako neenkódovatelný seznam všechno, čeho `.jg` UI typ začíná na `multi…`.
Webfig ale říká:

```js
types.multilinestring = inherit(types.string);   // liší se jen VIEW (textarea místo inputu)
```

Ze všech `multi*` typů je to jediný skalár — ostatní (`multinumber`, `multinumberrange`,
`multiipaddr`, `multistring`, …) dědí `types.multi`. Kvůli prefixu se nedal zapsat `note`
u `/system/note`.

### 14.3 Element-typ seznamu nese `c`, ne `values`

`ExtractRefHandler` četl jenom `node["values"]`, takže u seznamu referencí zůstal `RefHandler` prázdný:

```js
{name:'Topics',type:'multinumber',id:'U4',c:[{type:'enm',values:{type:'dynamic',path:[ 3,3 ]}}]}
```

`topics` u `/log` se proto dekódovaly jako surové `"[9,3]"` místo `"script,error"`.

---

## 15. `DataAvailable` nad UDP lhal a stál 5 s na příkaz (P2.43, 2026-08-01)

`WinboxCliMac` byl proti `WinboxCli` řádově pomalejší (plný běh 1 h 22 m vs. 8 min) a bylo to
zapsané jako „latence MAC kanálu". **Není.** Změřeno na 7.23.2 probem
`WinboxCliLatencyProbeTest`, který rozpadá jeden příkaz podle wire-trace kanálu `wbxcli.mepty`
(sdílí ho oba transporty, takže jsou přímo porovnatelné):

| span | WinboxCli | WinboxCliMac (před) | WinboxCliMac (po) |
|---|---|---|---|
| send → první bajt | 25 ms | **25 ms** | 25 ms |
| první bajt → prompt | 25 ms | 1 ms | 0 ms |
| prompt → return | 166 ms | **5012 ms** | 164 ms |
| celkem / příkaz | 216 ms | **5039 ms** | 193 ms |
| open | 1142 ms | **6053 ms** | 1053 ms |

První bajt chodil **stejně rychle jako po TCP**. Celá ztráta seděla za promptem a rovnala se
přesně `WinboxCliClient.FrameTimeoutMs` = 5000 ms.

**Příčina.** Kap. 3 výše říká, že každé čtení terminálu je gated `DataAvailable` a teprve pak
`Receive(5000)` — ten timeout smí ohraničit jen rámec, který **už přichází**. Nad TCP to platí,
protože `NetworkStream.DataAvailable` znamená „jsou tu bajty rámce". Nad UDP `_udp.Available > 0`
znamená jen „přišel nějaký datagram", a naprostá většina provozu na tom socketu jsou ACK, PING a
retransmise routeru. Zachycená časová osa jednoho příkazu:

```
34,8 ms  prompt seen (bytes=254)
34,8 ms  Recv 310B type=0x01 counter=3021   ← duplikát, AckData ho zahodí
34,8 ms  Recv 310B type=0x01 counter=3021   ← druhý duplikát
…        RecvUntil dojede na deadline
5033 ms  settled -> return @5024ms
```

`RecvUntil` má kontrakt „čekej do timeoutu, dokud handler neřekne dost" — pro čekajícího volajícího
správně, pro **pollujícího** katastrofa: každý falešně pozitivní `DataAvailable` stál celý frame
timeout. Padlo to jednou na příkaz a znovu jednou na `DrainSync(250)` po loginu, což je přesně ta
změřená „skipnutý test stojí 6 s" z P2.50.

**Oprava.** `MacLayerTransport.RecvAvailable(handler)` = polling protějšek `RecvUntil`: zpracuje
všechno, co už v socketu leží, a hned se vrátí (společné tělo `ReceiveOne`, takže ACK/PING/duplikáty
se řeší identicky v obou cestách). `WinboxMacM2Session.DataAvailable` na něm stojí a odpovídá
**„je připravený celý M2 rámec"**, ne „přišel datagram"; hotový rámec si drží v `_pendingFrame` a
následující `RecvFrame` ho vydá. Getter tedy dělá I/O — záměrně: je to poll operace kanálu a čte ji
jen jednovláknová terminálová smyčka `WinboxCliClient` (nativní transport jede reader loop a
`SupportsStaleDrain = false` mu polling zakazuje).

**Poučení:** vlastnost, kterou volající bere jako povolení zablokovat se, musí být pravdivá.
MAC-Telnet stejnou vadu nemá — má background pump s blokujícím socketem, ne `DataAvailable` gating.

## 16. Router tiše zahodí session a my to 30 s nepoznáme (P2.54, 2026-08-01)

Po opravě P2.43 zbyly v `winboxclimac` tři červené: `SearchByName_Interface_WillWork`,
`Create_IpAddress_With_LowLevel_API`, `ListRadiusServersWillNotFail` — vždy
`nothing was received within 30000 ms`, vždy ~30,1 s, beze změny napříč třemi plnými běhy před
opravou i po ní. Je to trojice, kterou P2.32 zapsal jako „wedge signature" tohoto transportu.

**Co ukázal trace.** Mechanismus je u všech tří identický: datagram s příkazem odejde, router ho
**nikdy nepotvrdí** a osm bajtově identických retransmisí ignoruje —
`RETRANSMIT #8 end=15639 highestAck=15475`, kde `highestAck` je přesně startovní offset toho
příkazu. A hlavně: **router celých 30 s neposílá vůbec nic** — ani ACK, ani PING, ani retransmisi.
Takže to není „router odmítá náš vstup", jak tahle rodina symptomů dosud vykládala.

> ⚠️ Opraveno §17: původně tu stálo „router tu session už nemá". To **přestřelilo** — routerův
> vlastní log si v okamžiku wedge nezapíše nic, žádné odhlášení ani chybu. Přesné tvrzení, které
> data unesou, je: *jeho MAC vrstva přestane naše bajty potvrzovat*, zatímco jeho účetnictví o
> žádném ukončení session neví. Co P2.54 dodává, na tom nestojí — zotavení visí na tom nepotvrzení,
> ne na výkladu, proč k němu došlo.

**Co jsme s tím udělali teď.** Ne příčinu — tu ještě neznáme (podezření: v sekundách před každým
wedgem router přeposílá rámce, které jsme už potvrdili, takže naše pakety k němu přestaly chodit
dřív, než příkaz vůbec odešel; a všem třem bezprostředně předchází test, který otevřel a zavřel
druhé spojení). Udělali jsme to, co jde udělat bez znalosti příčiny a co má cenu samo o sobě:

* **`IWinboxM2Channel.SendAbandoned`** vynáší `MacLayerTransport.LastSendAbandoned` do CLI enginu.
  Nad TCP je vždy `false` — TCP nemá co nepotvrdit, mrtvé spojení tam přijde jako FIN/RST.
* **`WinboxCliClient.ReadCommandResponseSync`** ho konzultuje a při „nic nepřišlo **a** router naše
  bajty nevzal" hází `TikConnectionSessionClosedException` místo dojetí na 30 s.
  Podmínka `sb.Length == 0` je podstatná: jakmile konzole cokoli vydala, příkaz **prokazatelně**
  dorazil a mohl proběhnout — tvrdit tam „neproběhl" by byla lež, kterou volající nemá jak ověřit.
* **`WinboxCliMacConnection`** na to navěsil reopen + retry, přesně podle `MacTelnetConnection`
  (tentýž nosič, tentýž problém): nové spojení + nový EC-SRP5 login přes `WinboxLoginRetry`,
  s dvěma zákazy — ne v Safe Mode (zahození session je právě to, před čím Safe Mode chrání) a ne
  poté, co už nějaký řádek odešel volajícímu (znovuspuštění by tytéž řádky doručilo dvakrát).

Rozlišení „mrtvá session" vs. „pomalý příkaz" je celá hodnota toho signálu — proto ta rychlá cesta
visí na nepotvrzení, ne na tichu. Kdyby visela na tichu, každý legitimně dlouhý příkaz by padal.

**Poučení (stejné jako P2.39, jen o vrstvu jinde):** hláška „nothing was received within N ms"
popisuje naše čtení, ne to, co udělal protějšek. Když nosič umí říct víc, musí se ho někdo zeptat.

## 17. Proč router zahodí MAC session — šest vyloučených hypotéz (P2.55, 2026-08-01)

P2.54 wedge přežívá, ale nevysvětluje. Tři traceované plné běhy ho ohraničily ostře: **na 27 otevřených
session připadají přesně tři zahození, pokaždé ve stejných třech testech** (`Create_IpAddress_With_-
LowLevel_API`, `ListRadiusServersWillNotFail`, `SearchByName_Interface_WillWork`). Je to deterministické,
ne režie na pozadí, kterou by retry jen schoval.

Nástroje, které to umožnily: `MacLayerTransport` teď loguje `SESSION OPEN key= local= srcMac=` a **každý
traceovaný řádek nese `key=`**. Kanál `wbxmac.udp` je totiž společný pro všechny MAC session, takže trace
pořízený, když jich žije víc, je prokládá — a otázku „co dělala *tahle* session" nešlo položit vůbec.
Bez toho vyšlo měření odstupu od poslední přijaté zprávy o dva řády vedle.

| hypotéza | verdikt |
|---|---|
| kolize 16bitového session key nebo lokálního portu | **ne** — 27 session, žádný klíč ani port se neopakoval. Klíč se navíc losuje při každém otevření, takže by kolize stěhovala chybu mezi běhy; ta se nestěhuje. |
| náš vlastní flood | **následek, ne příčina** — před wedgem se za nepotvrzenou hlavou nakupí ~24 paketů / 2,4 kB, protože pull loop střílí 8×/s bez ohledu na cokoli. Začíná to ale **až po** příkazu, který zůstal bez odpovědi. |
| zavření sourozenecké session | **ne** — `Probe_SiblingSessionTeardown`, 20 cyklů (WinBox-MAC, MAC-Telnet, API sourozenec), nula wedge. Byl to hlavní podezřelý. |
| objem provozu / hranice v bajtovém streamu | **ne** — `Probe_LongLivedSession` ušel na jedné session 400 příkazů a 101 099 odchozích bajtů bez zadrhnutí, tedy za dvěma ze tří offsetů, kde v suitě umírá. |
| idle logout (jako u MAC-Telnetu) | **ne** — per-session trace ukazuje, že session přijímá pakety až do okamžiku startu testu. Žije a umírá až na **prvním příkazu** toho testu. |
| echo logu routeru do terminálu (rodina P2.47) | **ne** — v celém běhu je jediné takové echo, pokrylo by nanejvýš jeden ze tří. |
| ten konkrétní příkaz | **ne** — všechny tři v izolaci projdou za 3 s bez zahození. |

| bezprostředně předchozí test | **ne** — všechny tři dvojice (předchůdce + oběť) projdou za 2–5 s bez zahození |
| limit počtu session / eviction na routeru | **ne** — wedge padá po 2, 14 a 22 otevřených session a živé jsou vždy jen 1–2 |

**Pohled z routeru (nově doplněný).** `/log` přes všechna tři okna: **v okamžiku wedge si router
nezapíše nic** — žádné `logged out`, žádnou chybu. Nejbližší odhlášení je 4 s *po* jednom wedge a 4 s
*před* jiným, obojí cizí spojení. Naše session zůstává v jeho účetnictví přihlášená, zatímco jeho MAC
vrstva přestala potvrzovat naše bajty. To je nesoulad mezi dvěma vrstvami routeru, ne ukončení session
— a proto byla formulace „router tu session už nemá" v §16 opravena.

Vedlejší pozorování ze stejného logu, zatím nevysvětlené: na 27 session, které náš trace otevřel,
připadá 47 loginů `via winbox` z naší MAC — část v párech ve stejné sekundě, část samostatně. Nerovnoměrné,
takže to není systematické zdvojení; stojí za to zjistit, co ty páry zakládá.

**Příčina jednoho ze tří: Safe Mode rollback zabije souběžnou WinBox-over-MAC session.** Reprodukce
5/5 přes `Probe_SafeModeRollbackOnASibling`:

| držená session | nosič | horní vrstva | odpověď | |
|---|---|---|---|---|
| `WinboxCliMac` | MAC / UDP 20561 | WinBox M2 | ~4,3 s | **wedge 5/5** |
| `MacTelnet` | MAC / UDP 20561 | plain telnet | ~0,15 s | v pořádku 0/2 |
| `WinboxCli` | TCP 8291 | WinBox M2 | ~0,37 s | v pořádku 0/2 |

Rollback dopadá pokaždé po ~2,15 s, nezávisle na tom, kdo drží druhou session.

> ⚠️ Zde stálo „je to vlastnost MAC nosiče, ne CLI enginu". **Špatně** — a bylo to publikované, než jsem
> to doměřil. `MacTelnet` jede po tomtéž portu s týmž 22bajtovým rámcováním a nic se mu nestane; po TCP je
> taky klid. Není to tedy ani nosič, ani WinBox vrstva, ale **výhradně jejich kombinace**, tj. služba
> `mac-winbox` na routeru. Poučení stejné jako u toho async rollbacku: obě poloviny hypotézy je potřeba
> změřit, ne jednu odvodit z druhé.

> ⚠️ **Rollback je asynchronní**, a to je celý ten trik. RouterOS drží vlastníka Safe Mode i po zániku
> spojení, až do connection-tracking timeoutu, takže rollback dopadne **~2 s poté** — proto ostatně
> `SafeModeTest` na něj čeká pollingem až 30 s. Když se držené session zeptáš hned po zavření sourozence,
> ptáš se ve špatný okamžik a dostaneš zdravých 223/224 ms. Přesně tak se do těchhle poznámek dvakrát
> dostalo tvrzení „Safe Mode příčina není". **Byla.** A vysvětluje to i to, proč je obětí vždy první test
> **následující** třídy: rollback dopadne až po skončení té třídy, která ho způsobila.

Cesta k tomu vedla přes pozorování, že `ConcurrentCommandsTest` a `SafeModeTest` jsou jediné dvě třídy
s `ReuseConnectionAcrossTests => false`. Jedou po vlastním spojení, takže se v per-session analýze mezi
„uživateli" sdílené session vůbec neobjeví, i když jsou to ony, kdo ji rozbije — proto jsem je nejdřív
jako předchůdce vyškrtl. Všechny tři wedge sedí na hranici testovací třídy.

**Pro uživatele knihovny to znamená:** kdo drží WinBox-CLI-MAC spojení a zároveň na jiném spojení pustí
Safe Mode, který skončí rollbackem, přijde o to první. P2.54 se z toho zotaví, ale stojí to ~4,5 s.

**Oprava v suitě:** `SafeModeTest.OnCleanup() => DisposeSharedConnection()`. Delší čekání ani sleep
nepomůžou — ten test už poluje, dokud rollback nedopadne, takže než skončí, je po všem; sdílená session
mezitím umře a nikdo se jí do konce testu nedotkne. Musí se prohlásit za mrtvou, ne se na ni čekat.
Není to zametení chyby knihovny (transport se zotaví sám), jen odstranění tichého spoléhání suity na to
zotavení. Měřený dopad: plný běh 3 → 2 zahozené session, reprodukce z 1 zahození / 10 s na 0 / 7 s.

**Kde to stojí:** zbývají dva wedge (`Create_IpAddress_With_LowLevel_API`, `ListRadiusServersWillNotFail`),
oba bez Safe Mode, router o nich neví. Nová je ale třída mechanismu, kterou jde zkoušet: co dalšího dělá
RouterOS asynchronně a co dosáhne až na `mac-winbox` session.

**Vedlejší nález, který stojí za opravu bez ohledu na wedge:** nemáme žádné **odesílací okno**. Když
hlava fronty není potvrzená, pull loop na ni dál přisypává 8 paketů/s, takže do díry ve streamu
napumpujeme 2,4 kB, které router nemá jak přijmout. Retransmise chodí po 400 ms a resílá správně jen
hlavu — ale nic nebrání zbytku růst.

**Poučení:** vyloučená hypotéza je taky výsledek, když je zapsaná i s tím, čím byla vyvrácena. Pět z těch
šesti znělo věrohodně a čtyři z nich už jednou v poznámkách figurovaly jako pravděpodobná příčina.

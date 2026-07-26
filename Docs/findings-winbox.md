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

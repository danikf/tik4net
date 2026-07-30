# findings-mactelnet.md — MAC-Telnet (UDP 20561) produkční implementace

**Kapitola E** v [implementation-plan.md](implementation-plan.md). Doplňuje protokolovou
referenci [mactelnet-protocol.md](../mactelnet-protocol.md) a sdílené CLI poznatky
[findings-cli.md](findings-cli.md) o věci ověřené přímo při produkční integraci do `tik4net`
(2026-06-04, RouterOS 7.21.4, Hyper-V CHR VM).

> **Princip:** MAC-Telnet je **CLI transport** (jako Telnet/SSH), **ne** API protokol. Po EC-SRP5
> autentizaci proudí přes UDP **raw VT100 terminál** (nešifrovaný). CRUD jede přes `CliConnectionBase`
> → `:put [/path print … as-value]` a parsování textu, **identicky jako Telnet**. Žádné binární `!re`
> sentence; `.id` se získává z `as-value` výstupu (CLI), ne z API.

---

## Architektura (kde co je)

```
tik4net/MacTelnet/
├── MacLayerTransport.cs    public abstract — UDP 22-byte framing, EC-SRP5 auth, ACK/dedup, MNDP
├── MacTelnetUdpClient.cs   internal sealed — VT100 terminál: login + příkazy (plně synchronní)
└── MacTelnetConnection.cs  public sealed  — CliConnectionBase wrapper (ITikConnection)
```

- `MacTelnetConnection : CliConnectionBase` — `ExecuteCliCommandCoreAsync` deleguje na
  `MacTelnetUdpClient.SendCommandAndReadAsync`. `RouterMac` property = bypass MNDP (jinak ~5 s discovery).
- Krypto v `tik4net/Crypto/` (`EcSrp5`, `WinboxStreamCrypto`), VT100/strip v `tik4net/Cli/`
  (`Vt100State`, `VtStripper`, `RouterOsCliLogin`, `CliOutputHelper`, `CliOutputParser`).
- `MacLayerTransport` je **public** záměrně — dědí z něj i `WinboxMacClient` (kap. H PoC) v jiném assembly.

---

## 1. ⚠️ ACK counter sémantika = KRITICKÝ root-cause (2026-06-04)

**Symptom:** po úspěšné autentizaci (`END_AUTH`, router loguje *"logged in"*) klient 30 s čeká na
prompt a spadne `TimeoutException: timed out waiting for shell prompt`. V diagnostice se tentýž DATA
paket (např. `ctr=859`, 100 B) opakuje **tisíckrát** a terminálový výstup je rozsekaný — router
vkládá `\r\n` přibližně po každých **3 znacích** (`you\r\nr p\r\nass\r\nwor\r\nd …`), takže
`IsChangePasswordNag`/`IsShellPrompt` nikdy nematchnou.

**Příčina:** MAC-Telnet counter je **kumulativní byte-offset streamu**. Po přijetí DATA paketu se ACKuje
offset **za** paketem, tj. `ACK.counter = pkt.counter + payload.Length` — **ne** `pkt.counter`.
Původní port ACKoval holý `counter`, takže RouterOS považoval paket za nedoručený a donekonečna ho
retransmitoval **a** slučoval (coalescing) následné terminálové update do jednoho paketu. To rozhodilo
**cursor-probe negociaci šířky** (viz §2) → router změřil šířku ~3 → zalomený výstup → rozbitá detekce.

**Důkaz counteru** (živý sniff): `ctr=0(len58) → 58 → 58(len9) → 67 → 67(len24) → 91 → 91(len39) →
130 → …` Každý další `counter == předchozí counter + předchozí len`. ACK tedy musí potvrzovat
`counter + len`.

**Oprava** (`MacLayerTransport.AckData`):
```csharp
protected bool AckData(uint counter, int payloadLen)
{
    SendAck(counter + (uint)payloadLen);        // potvrď offset ZA paketem
    if (counter < _inCounter) return false;     // retransmise → znovu NEzpracovávat
    _inCounter = counter + (uint)payloadLen;
    return true;
}
```
- `_inCounter` (kumulativní přijaté byty) se resetuje v `BaseConnect`.
- **Dedup je povinný**: bez něj se retransmitovaný paket znovu nakrmí do `Vt100State` (rozhodí kurzor)
  a znovu připojí do output bufferu (duplicitní/poškozené záznamy). Shoda s protokolem:
  *"klient ignoruje pakety s counter ≤ incounter"* ([mactelnet-protocol.md](../mactelnet-protocol.md)).
- `AckData` se používá ve **všech** příjmových smyčkách: `Authenticate`/`FinishAuth` (auth handlery),
  `WaitForPromptSync`, `ReadCommandResponseSync`, `DrainSync`.

**Ověřeno:** s opravou login spolehlivě uspěje (3/3 běhy), router projde celou cursor-probe negociaci
a pošle nag/prompt **čistě** (`Change your password (Ctrl-C to skip) … new password>`).

> Pozn.: PoC z kap. D „fungoval" i s holým `SendAck(counter)`, protože `/interface print` (sloupcový,
> krátké řádky) přežil i s retransmisemi — ale delší/časově citlivá terminálová negociace ne.
> Šlo o latentní bug zděděný z PoC, ne o regresi portace.

---

## 2. VT100 cursor-probe negociace šířky — POVINNÁ + „velmi velká šířka"

RouterOS po authu **změří terminál** posloupností cursor-move + `ESC[6n` (DSR). Klient musí odpovědět
skutečnou pozicí kurzoru `ESC[row;colR` (řeší `Vt100State`). Bez odpovědí → router předpokládá 1×1 a
nevykreslí výstup. Pozorované sondy (živě):

| Sekvence | Význam | Naše odpověď |
|---|---|---|
| `ESC Z` | DECID | `ESC[?1;0c` |
| `ESC[6n` | DSR (dotaz na pozici) | `ESC[{Row};{Col}R` |
| `ESC[H` … `ESC[9999C` … `ESC[6n` | **měření šířky** (jeď max doprava, kde jsi?) | `ESC[1;{min(Width,10000)}R` |
| `ESC[9999B`/`ESC[9999A`/`ESC D`/`ESC[r` | měření výšky / scroll region | sledování Row |
| `ESC[H ě H ESC[6n` | UTF-8 test (vícebajtový znak = 1 sloupec) | `ESC[1;3R` |

**Klíč:** měřicí sonda je `ESC[9999C` → reportovaný sloupec = `min(Vt100State.Width, ~1+9999)`.
RouterOS se ptá max ~9999, takže `Width` **musí být ≥ 10000**, jinak si `Vt100State` sám usekne odpověď
a router změří úzký terminál → dlouhé `as-value` řádky se zalomí a do dat se vloží `\r\n` → rozbije
parsing. Produkce používá `Vt100State(65535, 25)`. (Telnet má 4096, což stačí jen pro kratší řádky;
pro MAC-Telnet i obecně je bezpečnější ≥ 10000.) Viz [findings-cli.md](findings-cli.md) §10.5.

**`CTRL_TERM_WIDTH` v authu** (aktuálně `(ushort)80`, little-endian) RouterOS po cursor-probe
**ignoruje** — řídí se naměřenou šířkou. Hodnota tedy login neblokuje (ověřeno: i s 80 se po správném
ACK vykreslí široký banner). Ponecháno na 80.

---

## 3. Timing příjmu — nedávat drahou práci do smyčky

Cursor-probe odpovědi jsou **časově citlivé**. Per-paketové logování (`Console.Write` přes
`TransportDiagnostic` hook s hex + `StripAnsi` rostoucího bufferu + substring) v `WaitForPromptSync`
zpomalovalo odpovědi a bylo zprvu podezřelé jako příčina. **Skutečná příčina byla ACK (§1)**, ale
zásada platí: příjmová smyčka musí zůstat bez drahé práce. Verbose diagnostika z hot-loopu odstraněna.

**Ladění bez zásahu do produkce:** místo `Console.Write` v produkčním kódu použít neinvazivní
**session hook** — debug subclass veřejné `MacLayerTransport` v test projektu
(`tik4net.tests/Protocols/Tests/MacTelnetDebugTest.cs`) s in-memory hex dumpem. Test assembly vidí
`internal` typy přes `InternalsVisibleTo("tik4net.tests")`.

---

## 4. Login sekvence (`MacTelnetUdpClient.LoginAsync`, vše synchronní v `Task.Run`)

```
BaseConnect(host, CLIENT_TYPE=0x0015)   // MNDP/override MAC, SESSIONSTART na subnet broadcast
Authenticate(user, pass)                // EC-SRP5 (sync), končí END_AUTH
WaitForPromptSync()                      // odpovídá cursor-probe; Ctrl-C na nag; čeká na "] >"
DrainSync(250)                           // dojede zbytkový redraw, aby neprosákl do 1. příkazu
```

- **Plně synchronní** (`UdpClient.ReceiveTimeout` + blokující `Receive`, poll 500 ms). Míchání
  sync/async `Receive` na .NET Framework 4.8 rozbíjelo `SO_RCVTIMEO` → async varianty
  (`AuthenticateAsync`, `RecvUntilAsync`, `TryReceivePacketAsync`) jsou **nepoužité dead-code** a lze
  je smazat.
- **Change-password nag**: router s prázdným/def. heslem zobrazí `new password>` → odpovědět
  **Ctrl-C (0x03)**, `sb.Clear()`, pokračovat. Detekce `RouterOsCliLogin.IsChangePasswordNag`
  (substring `password>`). Viz [findings-cli.md](findings-cli.md) §10.6.
- **Prompt**: `RouterOsCliLogin.IsShellPrompt` = `TrimEnd().EndsWith("] >")`.

## 5. Vykonání příkazu (`SendCommandAndReadAsync` → `ReadCommandResponseSync`)

```
cmd = CliOutputHelper.InjectWithoutPaging(command)   // "without-paging" za "print"
Send(PKT_DATA, cmd + "\r")
raw = ReadCommandResponseSync()                       // prompt + 150 ms ticho (settle), jako Telnet
return CliOutputHelper.CleanOutput(VtStripper.StripAnsi(raw), cmd)  // ořež echo + koncový prompt
```
Settle logika je shodná s `TelnetClient.ReadCommandResponseAsync` (prompt na konci + `SettleMs` ticha;
redraw promptu před výstupem resetuje settle window). Viz [findings-cli.md](findings-cli.md) §10.8.

---

## 6. ⚠️ Dvojité echo příkazu → `CleanOutput` (root-cause „Missing '.id'")

**Symptom:** po opraveném loginu `LoadAll<Interface>()` spadne na `TikSentenceException: Missing field
'.id'` v `CliReSentence`/mapperu. **Pozor — vzniká to ve sdílené CLI output vrstvě, ne v transportu
ani v mapperu** (mapper i `.id` jsou v CLI legitimní a sdílené s funkčním Telnetem).

**Raw výstup je čistý a `.id` OBSAHUJE** (ověřeno dumpem, šířka 65535 funguje):
```
:put [/interface print … as-value]\r[admin@MikroTik] > :put [/interface print … as-value]\r\n
.id=*2;…;name=ether1;…;.id=*1;…;name=lo;…\r\n
\r\r\r[admin@MikroTik] >
```
MAC-Telnet (raw VT100, řádek zakončen jen `\r`) echuje příkaz **dvakrát**: (1) znakové echo zadaného
příkazu na vlastním řádku, (2) překreslení line-editoru jako `<prompt> <příkaz>`. Telnet (`\r\n`)
produkuje jen jedno echo. Původní `CleanOutput` odstraňoval **jen první** echo řádek → zbylá
`[admin@…] > :put […]` se v `ParseAsValue` (které normalizuje `\r\n` → `;`) slila s prvním `.id=*2`
záznamem do jednoho „klíče" → první záznam neměl pole `.id` → výjimka.

**Oprava** (`CliOutputHelper.CleanOutput`): smyčkou odstranit **všechny** úvodní prázdné i echo řádky.
Řádek je echo/šum, pokud (a) obsahuje prompt `] >` (prompt-prefixed překreslení / zbytkový prompt),
nebo (b) je fragmentem odeslaného příkazu (`cmdCore.Contains(line)` / `cmdCore.StartsWith(line)`).
Bod (b) řeší i **víceřádkové příkazy** — `/system/script/add` s `source` obsahujícím `\n` se echuje
přes víc řádků; bez toho zbylý fragment splynul s 1. záznamem / byl mylně vrácen jako id přidaného
záznamu (`RunAdd`). Datový řádek (`.id=…`, holé `*N`, error) žádné z kritérií nesplňuje → smyčka se
na něm zastaví. Bezpečné napříč transporty (Telnet beze změny).

## 7. Stav (2026-06-04) — ✅ HOTOVO

| Položka | Stav |
|---|---|
| Transport / framing (22 B, big-endian session_key/client_type) | ✅ (kap. D) |
| EC-SRP5 auth (`END_AUTH`) | ✅ |
| **ACK counter + dedup (§1)** | ✅ opraveno + ověřeno |
| Cursor-probe šířka ≥ 10000 (§2) | ✅ `Vt100State(65535,25)` |
| Detekce nag/promptu, Ctrl-C | ✅ |
| **Dvojité echo → `CleanOutput` (§6)** | ✅ opraveno (sdílené s Telnetem, bezpečné) |
| `MacTelnet_Login_ListInterfaces_ReturnsAtLeastOne` | ✅ **PASS** |
| `MacTelnet_SetAndVerify_InterfaceEther1Comment` | ✅ **PASS** |

**Úklid:** odstraněn verbose `TransportDiagnostic` z hot-loopu, nepoužité pole `_diagnostic` i nepoužité
async varianty (`AuthenticateAsync`, `RecvUntilAsync`, `TryReceivePacketAsync`) — MacTelnetUdpClient je
plně synchronní. Ladění probíhalo neinvazivně přes dočasný debug subclass `MacLayerTransport` v test
projektu (smazán po dokončení).

---

## 8. Login timeout (`ConnectTimeout`) + chování pod zátěží

Login (`WaitForPromptSync`) má vlastní timeout `MacTelnetConnection.ConnectTimeout` (default **15 000 ms**),
**oddělený** od per-command `ReceiveTimeout` (30 000 ms). Důvod: pod zátěží (stovky rychle po sobě
jdoucích MAC-Telnet sezení v plné test-sadě) občas jedno přihlášení nedoběhne k promptu. Když by login
blokoval celých 30 s (`ReceiveTimeout`), connect-retry smyčka volajícího (TestBase: 1 s × 20 s okno) by
nestihla druhý pokus. Kratší login timeout (15 s) → retry reálně proběhne a flaky sezení se zotaví.

`TikConnectionSetup.ConnectTimeout` (TimeSpan, default 15 s) se propisuje do
`MacTelnetConnection.ConnectTimeout` v `CreateMacTelnetConnection[Async]`. Lze nastavit i přímo:
`new MacTelnetConnection { ConnectTimeout = 10000 }`.

## 9. Předpoklady na routeru

- `/tool mac-server set allowed-interface-list=all` (nebo povolený interface) — jinak MAC-Telnet neodpoví.
- MNDP (UDP 5678) zapnuté, pokud se nepoužije `RouterMac` override.
- Hyper-V: SESSIONSTART na **subnet broadcast** (`192.168.x.255`), ne `255.255.255.255`; DATA/ACK na
  **unicast** IP routeru; preferovat NIC na stejné podsíti. (kap. D)

## 10. Retransmise: jeden slot nestačí, jakmile je víc requestů v letu (2026-07-30, P2.42)

**Kontext:** P2.19 přidala odesílací spolehlivost — držíme poslední odeslaný DATA paket a když ho router
nepotvrdí, pošleme ho znovu byte-identicky. To bylo správně, dokud byl každý volající lockstep: v letu byl
vždy nejvýš jeden paket, takže jeden slot (`_lastDataPacket`) pokryl všechno.

**Co se rozbije při multiplexování** (`WinboxNativeMac`, viz [winbox-m2-multiplexing-design.md](winbox-m2-multiplexing-design.md) §4.5):
counter je **kumulativní byte-offset** (§1), takže potvrzení je kumulativní taky. Pošleme A (offset 0–99)
a B (100–199), A se ztratí:

- router dostane B, ale nemůže potvrdit **nic** — ve streamu je díra, jeho ACK zůstane na 0,
- paket, který musí jít znovu, je **A**,
- jenže jediný slot už mezitím přepsalo B.

Výsledek není pomalý round trip, ale **trvalé zaseknutí session**: my čekáme na odpověď, router čeká na
bajty, které nikdy nedorazí, a retransmit posílá pořád B, které už dávno má.

**Oprava:** fronta nepotvrzených paketů místo slotu.

- `SendCore` **přidává** na konec (nikdy nepřepisuje cizí nepotvrzený paket),
- `NoteAck(counter)` zahodí všechno s `End <= counter` (jeden ACK může retirovat víc paketů) a resetuje
  rozpočet retransmisí — ten patří paketu na hlavě fronty, a ta se právě změnila,
- `RetransmitIfUnacked` posílá **nejstarší** nepotvrzený, protože při kumulativním ACK je díra vždy na
  začátku fronty; při jednom requestu v letu je to tentýž paket jako dřív, takže chování beze změny,
- `NoteAck` běží pod `SendGate` — volá se z příjmové strany, což je u multiplexovaného kanálu jiné vlákno
  než to odesílající. Předtím se překrýt nemohly a zámek tam nebyl potřeba.
- Fronta je omezená (`MaxUnackedTracked = 256`), aby volající píšící do mrtvé session nerostl bez limitu.

**Pozn. k write-side zámku:** design §4.5 čekal, že hlavní překážkou multiplexování bude posílání ACK/PONG
z příjmové cesty. Nebyla — všechny zápisy (`Send`, `SendAck`, `SendPong`, `RetransmitIfUnacked`) šly přes
`SendGate` už kvůli MAC-Telnet pumpě. Skutečná překážka byla o patro níž, právě ta retransmitní fronta.

**Testy:** `tik4net.unittests/MacTelnet/MacLayerRetransmitTests.cs` — loopback UDP, bez routeru. Živě se
tyhle případy nedají vyrobit (laboratorní router nezahazuje pakety na povel), takže díra ve streamu,
částečný ACK, vyčerpaný rozpočet a souběžný send/ACK jsou pokryté jen tady.

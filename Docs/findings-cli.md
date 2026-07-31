# Findings — MikroTik RouterOS CLI (Command Line Interface)

**Zdroj:** https://help.mikrotik.com/docs/spaces/ROS/pages/328134/Command+Line+Interface
**Datum:** 2026-05-31
**Retrieval status:** Oficiální dokumentaci zpracoval research agent (Sonnet). Označení 📄 = z dokumentace.
**✅ ŽIVĚ OVĚŘENO 2026-05-31** při implementaci kapitoly C (Telnet) proti testovacímu CHR routeru (ROS 7.x).
Část původních předpokladů se ukázala jako NEPŘESNÁ — viz **sekce 10 (živě ověřené poznatky)**, která má
přednost před staršími 📄 tvrzeními. Probe nástroj: [`telnet-cli-probe.ps1`](telnet-cli-probe.ps1).

> Účel: podklad pro CLI-based transporty (Telnet/SSH/MACTelnet) — překlad `ITikCommand` → CLI string
> a parsing výstupu. **Doplňuje** existující návrhový dokument
> [terminal-cli-parsing.md](../terminal-cli-parsing.md) (ten obsahuje plnou implementační architekturu
> `CliConnectionBase`/`CliOutputParser`/`VtStripper`); zde jsou jen **nové/ověřující poznatky z oficiálních docs**.

---

## 1. Nejlepší formát pro parsování: `print as-value`

📄 `print as-value` je **strojově čitelný** výstup, jeden řádek = jeden záznam, pole jako `key=value`
oddělená `;` **bez mezer**. Názvy polí i bool hodnoty (`yes`/`no`) jsou **byte-for-byte shodné s API
protokolem** → `tik4net.entities` mapper funguje beze změny (viz [terminal-cli-parsing.md](../terminal-cli-parsing.md)).

```
/ip/address/print as-value
→ .id=*1;address=192.168.1.1/24;interface=ether1;comment=;dynamic=no;disabled=no
```

Doplňkové modifikátory `print`:
| Modifikátor | Účel |
|---|---|
| `as-value` | strojový `key=value;…` výstup (**primární pro parsing**) |
| `without-paging` | vypne stránkování (`-- [Q quit...]`) — **nutné na PTY transportech** (Telnet/MACTelnet) |
| `detail` | human-readable detail; komentář jako `;;;` prefix (NE pro parsing) |
| `terse` | řádkový strojově-čitelnější výstup (alternativa) |
| `count-only` | jen počet záznamů |
| `where <cond>` | filtr (ekvivalent API `?name=value`) |

---

## 2. ⚠️ Kritický caveat — středník `;` uvnitř list-type polí

📄 Některá pole používají `;` jako **interní** oddělovač seznamu → v `print as-value` vypadají jako
další pole a **naivní split-on-`;` parser je rozbije**. Konkrétně hlášeno u:
- `route-count` (a podobné statistické agregáty),
- wireless `ranges=` (seznam frekvenčních rozsahů),
- BGP statistiky.

**Workaround (RouterOS 7.x):** `:serialize to=dsv delimiter="#"` — přeserializuje s jiným oddělovačem:
```
:put [:serialize to=dsv delimiter="#" [/ip route print as-value]]
```
→ záznamy/pole oddělené `#` místo `;`, takže vnořené `;` nevadí.

**Dopad na plán:** `CliOutputParser` (kapitola B) by měl umět volitelně použít `:serialize`/`delimiter`
pro entity s rizikovými poli, nebo escape-aware parser. Pro běžné entity (interface, address, firewall)
naivní split-on-`;` stačí. → Aktualizovat „Otevřené otázky" v [terminal-cli-parsing.md](../terminal-cli-parsing.md) bod 1.

---

## 3. Transport: SSH exec vs PTY (Telnet/MACTelnet)

📄 **SSH exec (bez PTY) je nejčistší transport pro parsing:**
- žádný banner, žádný prompt, žádné ANSI escape kódy,
- oddělený stderr a dostupný **exit code** → robustní detekce chyb,
- **ALE: MikroTik SSH server nepodporuje PTY pro exec kanál** → každý `RunCommand()` musí být
  **jeden kompletní příkaz** (žádná interaktivní session, žádné víceřádkové stavové sekvence).

📄 **PTY transporty (Telnet, MACTelnet):**
- výstup obsahuje ANSI escape sekvence → nutný `VtStripper` (už navržen v terminal-cli-parsing.md),
- nutné `print without-paging` na **každém** `print` (jinak stránkování blokuje),
- výstup = echo příkazu + data + nový prompt → odstranit echo a prompt (řešeno v `VtStripper.RemovePromptAndEcho`).

---

## 4. Telnet/PTY: login a prompt sekvence

📄 Expect-patterny pro Telnet auth (pozor na velikost písmen a mezeru):
- `"Login: "` (velké L) → poslat username
- `"Password: "` (velké P) → poslat password
- `"] > "` → shell prompt (konec promptu; detekovat **`EndsWith("] > ")`**, ne celý prompt — identity může mít libovolné znaky)

📄 **Login modifikátor `admin+ct80w`** (přidat za username): vypne ANSI barvy (`c`), nastaví fixní
šířku `80` (`t80`), `w` = bez wrap → **výrazně zjednoduší `VtStripper`** (méně escape sekvencí, stabilní šířka).
Obecný tvar: `<user>+<flags>`. Doporučeno pro Telnet/MACTelnet PTY session.

📄 RouterOS může po loginu zobrazit „change password" nag → poslat Ctrl-C (`0x03`) pro přeskočení
(shodné s WinBox terminál findings).

---

## 5. Komentáře a další parsovací drobnosti

📄 Komentář:
- `print as-value` → inline `comment=<text>` (parsovatelné jako běžné pole),
- `print detail` (human) → `;;;` prefix na samostatném řádku (NE pro parsing — to je past WinBox terminálu).

📄 Quoting/escaping: hodnoty s mezerami/speciálními znaky se v CLI uzavírají do `"..."`;
`;` odděluje příkazy na řádku; `#` uvozuje komentář; `\` line continuation; `[ ... ]` = command substitution.

---

## 6. Add / scalar přes CLI (potvrzuje terminal-cli-parsing.md)

📄 `:put [/ip/address/add address=10.0.0.1/24 interface=ether1]` → vrátí **`.id`** nového záznamu
(např. `*3`), ekvivalent API `=ret=*3`. Bez `:put [...]` wrapperu `add` vrací prázdno/index, ne `*N`.
→ Mapper `Save` (čtení nového `.id`) musí používat `:put [...]` formu.

📄 Scalar: `:put [/system/identity/get name]` nebo `/path get .id=*N value-name=x` → jedna hodnota.

---

## 7. Monitor / streaming přes CLI

📄 Kontinuální příkazy (`/interface/monitor`, `/tool/torch`, `/tool/ping`) v PTY produkují průběžný
výstup → pro one-shot použít `once` (`/interface ethernet monitor ether1 once`).
Streaming/`/listen` ekvivalent přes CLI **není** spolehlivý (viz capability gaps v terminal-cli-parsing.md).

---

## 8. Open questions / k ověření při implementaci

1. `:serialize to=dsv delimiter="#"` — přesné chování a dostupnost napříč ROS 7.x; jak parsovat výstup
   (oddělovač záznamů vs polí). Ověřit na entitách s `;`-poli (route, wireless, BGP).
2. `admin+ct80w` — ověřit, že modifikátory fungují u Telnet i MACTelnet a skutečně potlačí barvy/wrap.
3. SSH no-PTY: ověřit, že `print as-value` přes `RunCommand` vrací čistý výstup bez nutnosti `without-paging`.
4. Přesné texty CLI chyb pro mapování na `Tik*Exception` (částečně v terminal-cli-parsing.md §„Detekce chyb").
5. `once` přesný tvar pro různé monitor příkazy.

---

## 9. Dopady na plán (kapitoly B/C/F)

- Potvrzuje volbu `print as-value` jako parsovací formát a `CliConnectionBase` se `SemaphoreSlim` (terminal-cli-parsing.md).
- **Nové:** přidat do `CliOutputParser` ošetření `;`-v-hodnotách přes `:serialize`/escape (rizikové entity).
- **Nové:** Telnet/MACTelnet PTY session použít `<user>+ct80w` + `print without-paging` + Ctrl-C na password nag.
- **Nové:** SSH `RunCommand` = vždy jeden kompletní příkaz (no-PTY) — pasuje na `ExecuteCliCommandCoreAsync` model.
- Tyto poznatky promítnout do kapitol B (CLI vrstva), C (Telnet), F (SSH) až na ně dojde řada.

---

## 10. ✅ Živě ověřené poznatky (kapitola C, Telnet, 2026-05-31)

Při prvním živém nasazení CLI vrstvy přes Telnet se odhalilo několik věcí, které dokumentace
neuváděla nebo uváděla nepřesně. **Tato sekce má přednost** před staršími 📄 tvrzeními.
Detailní kontext: [`C-telnet-implementation-plan.md`](C-telnet-implementation-plan.md) (sekce „Výsledky implementace").

### 10.1 `print as-value` v interaktivním terminálu NIC nevypíše ⚠️ KRITICKÉ
Bare `/interface print as-value` zadané do PTY (telnet) vrátí **prázdno** (jen echo + prompt).
`as-value` se materializuje jen ve **skriptovém kontextu**. Nutné obalit do `:put [ … ]`:
```
:put [/interface print detail as-value where name=ether1]
```
Výstup `:put` je **jeden řádek**, záznamy zřetězené `;`, **hranice nového záznamu = `.id=`**
(singleton bez `.id` = jeden záznam). Tj. NE jeden-řádek-na-záznam, jak uváděla sekce 1!
→ `CliOutputParser` proto splituje záznamy na `.id`.

### 10.2 `detail` je nutný pro plnou sadu polí
`:put [/path print as-value]` vrací jen **souhrnné sloupce** (např. `/interface` vynechá
`default-name`, `mtu`, `rx-byte`…). Pro plnou sadu (paritní s API) je nutné `print detail as-value`.
O/R mapper to řídí přes `IncludeDetails` → builder přeloží na `print detail`.

### 10.3 `print stats` — live countery (✅ VYŘEŠENO `IncludeCliStats`, 2026-06-01)
`:put [/path print detail as-value]` **NEobsahuje** runtime countery (`bytes`/`packets`, `rx-byte`…).
Countery jsou jen v `print stats as-value`, což je **jiný sloupcový režim** — vrací countery + `.id`
+ pár identity polí, ale **ne config pole**. `detail stats` dohromady = ≈ jako `stats` (config-only pole
zmizí). **Žádný jediný modifikátor nedá config I countery** → nutné **dva dotazy + merge podle `.id`**.

**Řešení (commit `a72431c`):** CLI-only metadata flag **`IncludeCliStats`** (na `FirewallFilter/Mangle/Nat`,
`Interface`, `QueueSimple/Tree`). Mapper přidá marker `.cli-stats` (`TikSpecialProperties.CliStats`);
`CliConnectionBase.RunPrint` při markeru udělá `print detail` (config) + `print stats` (countery) a
mergne záznamy podle `.id`. API/REST marker **ignorují** (countery mají z `detail`; marker se nedostane
na drát) — viz `IsSpecialParam` v ApiCommand/RestRequestBuilder. Detaily: [`cli-print-stats-design.md`](cli-print-stats-design.md).

### 10.4 `where` hodnoty se speciálními znaky MUSÍ být v uvozovkách
`where address=192.168.1.1/24` (bez uvozovek) **nematchuje nic** — v `where` expression kontextu se
`/` (a `:`) interpretují jako operátory. Nutné `where address="192.168.1.1/24"`. Bezpečná sada bez
uvozovek: `[A-Za-z0-9._-]`. Hodnoty `*N` (id) se NEuvozovkují (`where .id=*1` funguje). → `QuoteForWhere`.
Pozn.: `name=value` pro `add`/`set` uvozovkování `/` NEpotřebuje (není to expression kontext).

### 10.4b Uvnitř uvozovek jsou `$` a `\` speciální — zapsaná hodnota se tiše přepíše (P2.38)
RouterOS console nemá jednoduché uvozovky vůbec (`:put 'a$b'` → `syntax error` na `'`), takže jediná
forma je dvojitá — a v ní platí **substituce proměnných** a **escape sekvence**. Změřeno na 7.23.2
(probe `Tools/probes/telnet-cli-probe.ps1`), zápis do `/system script`:

| Odesláno | Router uloží | Pozn. |
|---|---|---|
| `source="x\$y z"` | `x$y z` | `\$` = literál `$` |
| `source="x$y z"` | `x z` | **tichá substituce** — `$y` nedefinováno → prázdno |
| `source="x\\y z"` | `x\y z` | `\\` = literál `\` |
| `source="x\"b"` | `x"b` | |
| `source="C:\temp\new w"` | `C:<TAB>emp<LF>ew w` | **tichá** — `\t`, `\n` jsou známé escapy |
| `source="x\y z"` | `syntax error` | neznámý escape |
| `source=x$y` | `syntax error` | `$` mimo uvozovky není nikdy legální |
| `source=x\y` | `syntax error` | `\` mimo uvozovky taky ne |

Plná sada escapů (MikroTik docs, ověřeno pro `\$ \\ \" \t \n \_ \41`): `\"` `\\` `\n` `\r` `\t` `\$`
`\_` `\a` `\b` `\f` `\v` `\<hex>`.

Důsledky pro `QuoteIfNeeded`: hodnota s `$` nebo `\` **musí** být uvozovkovaná (nekvotovaná = tvrdá
chyba) a uvnitř uvozovek **musí** být obojí escapované (neescapované = tichá koruptce). Escapovat v
pořadí `\` → `"` → `$`, jinak si backslash pass zdvojí to, co přidal dollar pass. Reálný newline se
NEpřepisuje na `\n` (router bere zalomení uvnitř otevřených uvozovek; `\n` by navíc nešlo odlišit od
hodnoty, která opravdu nese `\`+`n`) — CR/LF jsou v trigger setu jen proto, že nekvotované by ukončily
příkazový řádek.

Tohle je **write**-side zrcadlo P2.17: tam se hodnota rozbíjela při čtení, tady ji router přepíše
dřív, než ji uloží — add projde, `.id` se vrátí, nic netrapne, a poškozená je routerova vlastní kopie,
takže se na špatné hodnotě shodnou i všechny ostatní transporty.

### 10.5 VT100 cursor-probe negociace je POVINNÁ
Bez odpovědí na RouterOS cursor-probe (`ESC[6n` → cursor report `ESC[row;colR`) považuje router
terminál za 1×1 a **nevykreslí výstup příkazu** (typicky `…\r\n\r\r\r\r] > ` bez dat). Nutné sledovat
pozici kurzoru a odpovídat (`Vt100State` v `tik4net/Cli`). Inzerovat **velkou šířku** (ne 80), jinak
RouterOS zalamuje dlouhé as-value řádky a vkládá do dat `\r\n` → rozbije parsing.
Pozn.: RouterOS měří šířku sondou `ESC[9999C ESC[6n`, takže reportovaný sloupec je `min(Vt100State.Width, ~10000)`
— `Width` musí být **≥ 10000** (jinak si odpověď usekne sám klient). Pro MAC-Telnet viz
[findings-mactelnet.md](findings-mactelnet.md) §1–2 (kritická i **ACK = counter + payloadLen** sémantika).

### 10.6 Change-password nag = `new password>` (NE „change password")
Router s default/prázdným heslem zobrazí po loginu `new password>` (a `repeat new password>`).
Odmítnout **Ctrl-C (0x03)**. Detekce na substring `password>`. (`RouterOsCliLogin`.)

### 10.7 `.NET Framework NetworkStream.ReadAsync` nectí timeout ani CancellationToken
U čekajícího readu bez dat blokuje **navždy** (ReadTimeout platí jen pro sync `Read`; CT se kontroluje
jen před začátkem). Nutné číst přes polling `stream.DataAvailable` + `Task.Delay` a hlídat deadline ručně.

### 10.8 Prompt detekce: redraw a „settle"
RouterOS prompt překresluje (`\r\r\r\r] > `) i PŘED výstupem příkazu → naivní „ends with `] >`" matchne
předčasně. Řešení: po loginu **drainnout** zbytkový redraw; výstup příkazu číst do „prompt + ustálení"
(prompt na konci a pak ~120 ms ticho). Prompt suffix porovnávat jako `TrimEnd().EndsWith("] >")` (bez koncové mezery).

### 10.9 Texty CLI chyb → výjimky (ověřené)
| CLI text | Mapování |
|---|---|
| `no such item`, `expected item id (line N column M)` | `TikNoSuchItemException` |
| `no such command`, `bad command name …`, `expected end of command`, `syntax error (line …)` | `TikNoSuchCommandException` |
| `already have such …`, `item with such name already …` | `TikAlreadyHaveSuchItemException` |
| `failure:` / `error:` / jiný error stream | `TikCommandTrapException` |

Pozn.: `remove`/`set` s neexistujícím/nevalidním `.id` (`[find .id=…]` prázdné) → `expected item id`.

### 10.10 Scalar: `get value-name=.id` je nevalidní
`:put [/path get .id=*N value-name=.id]` → `get .id=` je syntax error a `value-name=.id` →
„input does not match any value of value-name". **Scalar se čte přes `print`** a hodnota se vybere
z řádku (funguje i pro `.id`). `get value-name=…` nepoužívat pro `.id`.

### 10.11 Akční příkazy bez per-řádkového výstupu (`script run`) — ✅ podporováno
`/system/script/run` přes terminál **skript spustí** (`/system script run [find .id=*N]`), ale nevrací
per-řádkové `!re` jako binární API (je to fire-and-forget akce). `CliConnectionBase.RunPrint` proto verb
`run` routuje jako akci a vrací **prázdný** result set. Test `RunScript_Issue53` je transport-aware
(`TestBase.IsCliTransport()`): na CLI ověří jen že běh neselhal, na API/REST drží počet `!re` řádků.
(commit `eb5e687`)

### 10.12 Monitor příkazy: `numbers=` + `once`
`/interface/ethernet/monitor` vyžaduje `numbers=<iface> once` před `as-value`:
`:put [/interface ethernet monitor numbers=ether1 once as-value]`. Builder předává NameValue paramy
`numbers` a flag `once` (kontinuální monitor/torch jinak v PTY blokuje — viz sekce 7).

---

## 11. ✅ Živě ověřené poznatky (kapitola F, SSH, 2026-06-15)

SSH **NENÍ** „exec bez PTY" (jak naznačovala sekce 3), ale **interaktivní PTY ShellStream** — RouterOS přes
exec kanál `as-value` nevypíše stejně jako Telnet, takže se používá tentýž PTY/CLI stack jako u Telnetu.
Sdílené `RouterOsCliLogin`/`Vt100State`/`CliOutputHelper` fungují beze změny; SSH dodává jen ~280 LOC
transportu (`tik4net.ssh`, balíček kvůli `Renci.SshNet`).

### 11.1 Auth dělá SSH.NET — žádné Login:/Password: prompty
Po `SshClient.Connect()` se jen dotáhne prompt přes **`RouterOsCliLogin.ResolveToPromptAsync`** (nag→prompt,
extrahováno z `LoginAsync`) + drain post-login redraw. Username flag `+c` přes SSH přijat; fallback na
čisté jméno při `SshAuthenticationException`.

### 11.2 Raw PTY módy
`CreateShellStream(..., terminalModes)` s `ECHO/ICANON/ISIG/IEXTEN/IXON/IXOFF/ICRNL/INLCR/OPOST = 0` —
RouterOS chce raw keystrokes (vlastní VT100 editor). RouterOS SSH server ale módy z velké části ignoruje.

### 11.3 ⚠️ Ctrl+D = SSH EOF → zavře kanál (Safe Mode unroll)
Discard klávesa **Ctrl+D (0x04)** je v SSH konvence EOF; RouterOS SSH server na ni **zavře kanál**
(`ShellStream` disposed) bez ohledu na raw módy. Telnet to nemá (holý byte → konzole → undo). **Řešení:**
SSH unroll jede přes **scriptable `/safe-mode/unroll`** (RouterOS 7.18+) — normální příkaz, kanál žije,
in-place. Fallback (starší verze, `TikNoSuchCommandException`): Ctrl+D = rollback-by-disconnect (+ `Close`).
Take/Release (**Ctrl+X**) přes SSH fungují in-place bez problému.

### 11.4 `ShellStream.DataAvailable` polling
Stejný vzor jako Telnet `NetworkStream` (poll + `Task.Delay`, deadline z `ReceiveTimeout`) — žádný hang,
echo/prompt trim (`CliOutputHelper.CleanOutput`) sedí i pod raw módy. **Výsledek: SSH suite 172/1→0/77,
SafeMode 3/3.**

## 12. ✅ WinboxCli/MacCli: mepty je PULL protokol — velký výstup se zasekne (P2.13)

**Stav: OPRAVENO 2026-07-23** (`WinboxCliClient.SendPull`). Vysoce riziková RE oblast; ověřeno živě
raw-byte instrumentací (dočasná, odstraněna — viz P2.15 pro promotion do MCP).

### Symptom
Plný sólo `winboxcli`: **35 deterministických selhání** (ne latence, ne kontaminace paralelními běhy,
ne counter-semantika — vše vyvráceno živě). Selhávající = timeouty (násobky 30s), ne pomalé odpovědi.
Izolovaně projdou; selhává až po dost příkazech na **sdíleném** spojení.

### Root cause (raw-byte trace)
mepty `Data` command (`0x0A0067`) dělá DVĚ věci: **posílá klávesy I tahá výstup**. RouterOS odpoví na
jeden `Data` **jednou dávkou** čekajícího výstupu. Odpověď větší než dávka (řádově pár set bajtů — např.
`print detail as-value` přes víc záznamů) se doručí, jen když klient **dál pulluje**. Náš klient poslal
jeden `Data` na příkaz a pak jen pasivně četl → velký výstup se nedotáhne.

Klíč: po velké odpovědi RouterOS pošle **echo** dalšího příkazu, ale výstup už NE, a od dalšího příkazu
**přestane echovat úplně** (`DataAvailable=False` 30 s, `bufLen=0`) — terminál je zaseknutý do konce
session. Ta downstream prázdnota je přesně to, co skill vedl jako „gotcha A" (add prázdno) a „gotcha B"
(druhý print prázdno). **Jeden bug, ne dva.** (Moje průběžná diagnóza „posun o jednu" byla TAKÉ špatná —
opraveno až raw-byte tracem.)

```
SEND :put [… datapath print as-value]     → RETURN len=526 ✓ (velká odpověď)
SEND :put [… datapath print detail …]     → echo (228 B) → DataAvailable=False 30 s → timeout
SEND :put [… datapath print as-value]     → bufLen=0 (ani echo) → timeout
…                                          všechny další příkazy prázdno
```

### Fix
`WinboxCliClient.SendPull()` — prázdný `Data` frame (bez `Input` klíče, monotonic counter; stejný tvar
jako `SendTerminalReady`). V `ReadCommandResponseSync` se pulluje pokaždé, když nic není v bufferu A
completion prompt ještě nedorazil (`prompted==false`). Po promptu je výstup kompletní → jen settle,
žádný další pull (jinak zbytečný churn). Ověřeno: `print detail` z 30s záseku → 620 B plný výsledek.
Sdílený `WinboxCliClient` → platí i pro `winboxclimac`.

### Otevřené / k doladění
- `ReadUntilQuietSync` (Tab-completion) pulluje NErozšířeno — completion výstup je malý, ale pokud by
  někdy překročil dávku, zasekne se stejně. Kandidát na stejný pull, až/pokud se projeví.
- Cadence: pull každých ~`PollSleepMs` (20 ms) během čekání. Funguje; případná optimalizace (pull jen po
  N ms ticha) je kosmetika, ne korektnost.
- Přesná velikost „dávky" neproměřena (nepotřeba pro fix). Pokud by šlo o počet záznamů/bajtů, dá se
  dohledat ve webfig mepty JS — ale pull-until-prompt je robustní bez znalosti prahu.

### Dopad na už zapracované
Odblokovává `TestBase.SaveTracked` orphan-sweep na tomto transportu (dřív četl přes totéž rozbité
spojení). Po fixu čtení funguje → sweep dohledá i id-less orphany.

## 13. ✅ Prázdná hodnota není prázdný token (P2.44, 2026-07-30)

Živě ověřeno na RouterOS 7.23.2 přes telnet. Týká se **všech CLI transportů** (sdílený
`CliCommandBuilder`).

### 13.1 `name=` uprostřed řádku je syntaktická chyba

```
/system note set note= show-at-login=yes
  → expected end of command (line 1 column 37)     ← sloupec 37 = začátek `show-at-login`
/system script add name=X source=":put 1" comment=
  → *1                                             ← na KONCI řádku projde
```

Bare `name=` tedy nepředává prázdný řetězec — parser jím nic nespotřebuje a zakopne o následující
token. Správný zápis je dvouznakový literál `name=""`, který funguje v obou pozicích.

Proč to tak dlouho nikoho netrklo: suita nikdy neukládala prázdný řetězec. Vyplavalo to až na
round-trip testu `/system/note`, který na konci obnovuje původní hodnotu — a ta byla prázdná. Pád
navíc nechal na routeru reziduum (obnova neproběhla), takže **další běhy testu prošly** — obnovovaly
už neprázdný text. Přesně ten druh chyby, který se sám zahladí.

### 13.2 `where name` a `where name=""` jsou opačné dotazy

Dvě `/system/script` položky, jedna s komentářem `hello`, druhá bez:

```
:put [/system script print as-value where comment]      → řádek S komentářem
:put [/system script print as-value where comment=""]   → nic
API  ?comment=                                          → nic
```

`where <field>` je test „je nastaveno" (truthiness), kdežto API `?field=` znamená „rovná se prázdné".
Builder do té doby posílal bare `name` pro obojí, takže filtr na prázdnou hodnotu vracel přesně
doplňkovou množinu. Rozlišuje se podle toho, zda je hodnota parametru `null` (→ bare `name`, API
`?name`) nebo prázdný řetězec (→ `name=""`, API `?name=`).

## 14. ✅ Router píše do živého terminálu sám od sebe (P2.47, 2026-07-31)

RouterOS má v základní konfiguraci pravidlo `topics=critical action=echo` (`/system/logging`), a
`echo` neznamená „na lokální konzoli" — znamená **do otevřených terminálových session**. Do relace
tedy může kdykoli přiletět řádek, o který nikdo nežádal:

```
21:18:05.412 telnet.sock RECV | <CR>23:17:46 echo: system,error,critical login failure for user
                                admin from 192.168.4.31 via api<ESC>[K<CR><LF><CR><ESC>[9999B[admin@CHR] >
```

Změřeno na wire-trace celého (zeleného) telnet běhu suity. Vlastnosti, které je potřeba znát:

- **Není to login banner.** Přiletělo to na dávno ustavené session, mezi dvěma testy, bez IAC
  negociace v okolí. (Banner recentní logy taky tiskne — při hledání v trace se to snadno splete,
  filtruj podle toho, jestli je poblíž `<FF><FD>` negociace.)
- **Router to bufferuje.** Časové razítko v řádku bylo ~19 s starší než okamžik doručení, takže
  příčina a projev spolu časově nesouvisí.
- **Přiletí jen do session, která zrovna nic nedělá.** Pokus vynutit si to během 20s čtení
  (`/system script run` s `:delay 20s`) nedoručil nic; v ostrém případě byla relace idle mezi příkazy.
- **Za frontou následuje překreslený prompt** (`<ESC>[9999B[admin@CHR] > `), takže čtení, které
  zrovna běží, na konci prompt zase uvidí.
- **Vyrobit se to dá** neúspěšným loginem z druhé session — `login failure` je `critical`. Log
  vznikne u telnetu i u API (`/log/print ?message=login failure...` to potvrdí); doručení do cizí
  relace je ale řízené tím, jestli je relace idle, takže to není spolehlivý injektor.

Pokud řádek dorazí **mezi příkazy**, sežere ho `DrainAsync` a nic se nestane — to je i případ výše.
Nebezpečné je, když dorazí až **za** drainem, tedy na začátek odpovědi na další příkaz: v
`CliOutputHelper.CleanOutput` pak přeskakovací smyčka na hlavičce zastavila (log řádek není prázdný,
není prompt a není fragment příkazu) a **echo příkazu za ním propadlo do dat**. U čtení se echo
nalepí před první záznam, u tiše-úspěšného zápisu vznikne neprázdný „výstup", který poziční pravidlo
z P2.12 čte jako odmítnutí routerem. Opraveno tím, že se log řádek přeskakuje i v hlavičce — spojovací
smyčka ho zahazovala už předtím, takže se tím nic nového neztrácí.

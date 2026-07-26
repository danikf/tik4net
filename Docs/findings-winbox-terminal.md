# Winbox M2 terminál — poznatky z PoC (mepty, comment, MAC vrstva)

> Lokální soubor, není v gitu. Naposledy aktualizováno: 2026-05-27.
> Navazuje na [`project_winbox_m2_poc.md`](../memory/project_winbox_m2_poc.md) a
> [`mactelnet-protocol.md`](mactelnet-protocol.md).
>
> Zdrojové soubory:
> - `tik4net.tests/WinboxM2CatalogTest.cs` — Winbox TCP PoC (7/7 testů prochází)
> - `tik4net.tests/MacLayerTest.cs` — MAC-layer PoC (0/5 testů prochází, viz sekce 5)

---

## 1. Formát komentáře v RouterOS CLI

RouterOS **nezobrazuje** comment jako `comment=text` ve výstupu `/interface print detail`.
Místo toho ho zobrazí jako **triple-semicolon notaci** na samostatném řádku před entitou:

```
Flags: R - running
 0  R   ;;; tik4net-winbox-test
         name="ether1" default-name="ether1" type="ether" mtu=1500 ...
```

Regex pro extrakci:

```csharp
var m = Regex.Match(output, @";;;\s+(.+?)(?:\r|\n|$)", RegexOptions.Multiline);
if (m.Success) return m.Groups[1].Value.Trim();
return "";
```

**Pasti:**
- `comment=...` se v detailním výpisu nevyskytuje — regex na `comment=(\S+)` vždy selže.
- Řádek s `;;;` je nad entitou, ne za ní.
- Pokud comment není nastaven, řádek `;;;` v outputu chybí úplně (vrátí `""`).

---

## 2. Nastavení komentáře přes CLI (`SetInterfaceComment`)

Příkaz: `/interface set <ifName> comment=<value>`

**Hodnoty:**
- Prázdný string: `comment=""` (prázdné uvozovky — RouterOS vymaže komentář)
- Jednoduchý řetězec bez mezer/uvozovek/backslashů: **bez uvozovek** (`comment=tik4net-test`)
- Řetězec s mezerami nebo speciálními znaky: s uvozovkami a escapingem

```csharp
public void SetInterfaceComment(string password, string ifName, string comment)
{
    string safe = comment ?? "";
    string valueExpr = (safe.Length == 0)
        ? "\"\""                            // empty → comment=""
        : (safe.IndexOfAny(new[] { ' ', '"', '\\' }) >= 0)
            ? "\"" + safe.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
            : safe;                         // simple word — no quotes needed
    string cmd = $"/interface set {ifName} comment={valueExpr}";
    string setOut = RunTerminalCommand(password, cmd);
}
```

**Past:** Nikdy nepřidávej uvozovky ke všem hodnotám automaticky. RouterOS CLI
`comment="tik4net-test"` sice funguje, ale pro hodnoty získané parserem (např. prázdný string)
je kritické rozlišit `""` (smaž) vs. chybějící parametr (neměň).

---

## 3. Životní cyklus mepty terminálové session

### Architektura

Každé volání `RunTerminalCommand` otevírá **novou mepty session** na existujícím TCP spojení:

```
TCP spojení (port 8291, AES-128-CBC zašifrované)
  └── mepty session A  [76] cmd=0x0A0065  → ListInterfaces
  └── mepty session B  [76] cmd=0x0A0065  → SetInterfaceComment
  └── mepty session C  [76] cmd=0x0A0065  → GetInterfaceComment
  └── ...
```

RouterOS přiřadí každé session nové `sessionId` (byte, vrácen serverem v SYS_SESSION_ID).

### Drain před otevřením session

Po dokončení příkazu routuje router ještě push framy (VT100 bytes, čistící sekvence)
přes stávající TCP stream. Pokud je nezpracujeme, `RecvAndDecrypt` v dalším volání
`OpenTerminalSession` vrátí stará data místo čerstvé meptyLogin odpovědi
→ špatné `sessionId` → všechny příkazy do špatné session → žádný výstup.

```csharp
DrainEncryptedFrames(600);  // 600 ms — musí být dost na doběhnutí posledních push framů
int sessionId = OpenTerminalSession(password);
```

**Proč 600 ms?** Při 300 ms byl drain příliš krátký a občas doběhl push frame o zlomek
sekundy pozdě. 600 ms se ukázalo jako spolehlivé.

### Phase 1: VT100 negociace

RouterOS před zobrazením CLI promptu provede **multi-round cursor probe sekvenci**:

```
Server → ESC[H + ESC[9999B + ESC Z + ESC[6n   (detekuj spodní okraj + terminal ID)
Client → ESC[25;1R + ESC[?1;0c
Server → ESC[H + ESC[9999B + ESC D + ESC[9999A + ESC[6n  (ověř výšku po IND scroll)
Client → ESC[25;1R
Server → ESC[H + ESC[9999C + ESC[6n             (šířka)
Client → ESC[1;80R
Server → [admin@MikroTik] >                      (prompt)
```

Třída `Vt100State(width=80, height=25)` sleduje pozici kurzoru a generuje správné odpovědi.

**Kritická past:** Pokud vždy odpovídáš `ESC[1;1R`, RouterOS usoudí terminál 1×1 a opakuje
proby donekonečna — Phase 1 nikdy neskonf.

Phase 1 končí, když `StripAnsi(initSb).Contains("] >")` vrátí `true`.

### Phase 2: odeslání příkazu a čekání na výstup

```csharp
SendTerminalInput(sessionId, Encoding.UTF8.GetBytes(command + "\r"), ref counter);
// ... polling loop ...
string stripped = StripAnsi(cmdSb.ToString()).TrimEnd();
if (stripped.EndsWith("] >"))
    break;
```

**Klíčový detail — `EndsWith` místo `Contains`:**

RouterOS nejprve **echuje** odeslaný příkaz zpět:
```
[admin@MikroTik] > /interface set ether1 comment=test
```
Tento echo obsahuje `"] >"`, ale jde o echo příkazu, ne o nový prompt.
Skutečný nový prompt přijde **až po výstupu příkazu** na konci odpovědi.

Proto `Contains("] >")` by přerušilo čtení příliš brzy (na echu),
zatímco `TrimEnd().EndsWith("] >")` čeká správně na trailing prompt.

### RouterOS "Change your password" nag

RouterOS může před CLI promptem zobrazit výzvu ke změně hesla:
```
new password>
```
Řešení: detekovat v Phase 1 a odeslat `\x03` (Ctrl-C) pro přeskočení.

```csharp
if (!sentCtrlC && (stripped.Contains("new password>") || stripped.Contains("password>")))
{
    SendTerminalInput(sessionId, new byte[] { 0x03 }, ref counter);
    sentCtrlC = true;
    initSb.Clear();
}
```

---

## 4. Shoda MikroTik M2 protokolu pro TCP i MAC přípojení

Winbox M2 protokol je **identický** bez ohledu na transport:

| Aspekt | Winbox TCP (port 8291) | Winbox MAC (UDP 20561, ct=0x0f90) |
|---|---|---|
| EC-SRP5 autentizace | ✅ stejná matematika | ✅ stejná matematika |
| Curve25519 (Weierstrass forma) | ✅ | ✅ |
| AES-128-CBC šifrování framů | ✅ | ✅ (očekáváno, neověřeno) |
| M2 TLV formát | ✅ | ✅ (očekáváno) |
| Handler [76] mepty | ✅ | ✅ (očekáváno) |
| MAC Telnet (UDP 20561, ct=0x0015) | ❌ jiný protokol | — |

**MAC Telnet** (ct=0x0015) je **odlišný protokol** — raw VT100 terminál bez šifrování,
bez M2 TLV formátu. EC-SRP5 matematika je sdílená, ale framing je jiný.

**MAC Winbox** (ct=0x0f90) je Winbox M2 přes UDP MAC-layer transport místo TCP.
Lze tedy přímo přenést `WinboxM2Client` logiku — jen vyměnit TCP `NetworkStream`
za UDP+MAC transport vrstvu.

---

## 5. Stav MAC-layer testů (`MacLayerTest.cs`)

### Aktuální stav: 0 / 5 testů prochází

Všechny testy selžou na `RecvUntil` timeoutu — router na UDP pakety vůbec neodpovídá.

### Co bylo ověřeno

| Test | Výsledek |
|---|---|
| API přístup na router (port 8728) | ✅ funguje |
| MNDP discovery (UDP broadcast 5678) | ✅ funguje |
| Winbox TCP PoC (port 8291) | ✅ funguje, 7/7 testů |
| MAC Telnet unicast (unicast na IP routeru:20561) | ❌ NO packets received |
| MAC Telnet broadcast (<subnet-broadcast>:20561, srcPort=20561) | ❌ jen vlastní pakety viděny zpět |
| MAC Telnet broadcast (<subnet-broadcast>:20561, srcPort=random 52774) | ❌ NO packets received |

### Konfigurační stav routeru (ověřeno přes API)

```
/tool mac-server:         allowed-interface-list=all
/tool mac-server mac-winbox: allowed-interface-list=all
```

RouterOS 7.x **nemá property `disabled`** na `/tool mac-server` — ovládání pouze přes
`allowed-interface-list`. Původní kód s `disabled=no` selhal s `unknown parameter disabled`.

### Winbox aplikace na testovacím PC

Při diagnostice jsme zachytili, že Winbox.exe (běžící na testovacím PC, port 61126,
ct=0x900F) **aktivně vysílá** na router MAC (<router-MAC>) přes UDP 20561.
To potvrzuje, že síťová vrstva pro MAC protokoly **není globálně blokovaná**.

### Možné příčiny selhání

1. **CHR omezení**: CloudHostedRouter (Hyper-V VM) nemusí odpovídat na MAC Telnet.
   Hyper-V virtual switch možná neforwarduje UDP-broadcast na správný interface,
   nebo CHR RouterOS image nemá MAC Telnet plně podporovaný.

2. **Zdrojový port**: MAC Telnet specifikace říká, že router může ignorovat pakety
   z portů jiných než 20561. Testováno: srcPort=20561 i srcPort=random — obě varianty selhaly.

3. **Broadcast vs. unicast**: Router reaguje na MNDP broadcast (5678), ale ne na
   MAC Telnet broadcast (20561). Možné, že RouterOS CHR má rozdílné chování pro 20561.

4. **Windows Firewall**: Blokuje příchozí UDP z routeru na testovací PC.
   Winbox aplikace je exemptována z firewallu; náš UdpClient test nemusí být.

### RouterOS 7.x — known issue s `mac-server`

V RouterOS 7.x neexistuje `disabled` property na `/tool/mac-server`:

```
# RouterOS 6.x (funguje):
/tool mac-server set disabled=no

# RouterOS 7.x (hodí: "unknown parameter disabled"):
/tool mac-server set allowed-interface-list=all  ← správný způsob
```

Stejné platí pro `/tool/mac-server/mac-winbox/set`.

### Doporučený další postup

1. Otestovat na fyzickém RouterBoard (ne CHR) — ověřit, zda je problém v CHR specifický.
2. Zachytit síťový provoz Wiresharkem na testovacím PC — ověřit, zda router vůbec
   odesílá UDP odpověď (Windows Firewall diagnóza).
3. Zkusit `UdpClient` bound na `0.0.0.0:20561` s `SO_REUSEADDR` — některé
   MAC Telnet implementace to vyžadují.
4. Zkontrolovat, zda CHR image má MAC server funkční vůbec (`/tool/mac-server/sessions/print`
   by ukázalo aktivní session pokud by router přijal SESSIONSTART paket).

---

## 6. API pattern pro `/tool mac-server` (RouterOS 7.x)

```csharp
// Správný pattern pro povolení MAC serveru v RouterOS 7.x
// (platí pro ClassInitialize v MacLayerTest.cs)

using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
{
    // Čtení stavu
    var print = conn.CreateCommand("/tool/mac-server/print");
    foreach (var row in print.ExecuteList())
        Console.WriteLine("allowed-interface-list=" +
            row.GetResponseFieldOrDefault("allowed-interface-list", "?"));

    // Nastavení — pouze allowed-interface-list, nikoli disabled
    var cmd = conn.CreateCommand("/tool/mac-server/set");
    cmd.AddParameterAndValues("allowed-interface-list", "all");
    cmd.ExecuteNonQuery();

    // Stejný pattern pro mac-winbox:
    var cmd2 = conn.CreateCommand("/tool/mac-server/mac-winbox/set");
    cmd2.AddParameterAndValues("allowed-interface-list", "all");
    cmd2.ExecuteNonQuery();
}
```

**Chybný pattern (RouterOS 7.x hodí `unknown parameter disabled`):**

```csharp
// ŠPATNĚ — RouterOS 7.x:
cmd.AddParameterAndValues("disabled", "no");   // ← hodí výjimku

// SPRÁVNĚ:
cmd.AddParameterAndValues("allowed-interface-list", "all");
```

---

## 7. Přehled stavu PoC testů

| Testovací třída | Testy | Stav | Poznámka |
|---|---|---|---|
| `WinboxM2CatalogTest` | 7 | ✅ 7/7 | Winbox TCP, mepty, set/get comment |
| `MacLayerTest` | 5 | ❌ 0/5 | Router neodpovídá na UDP 20561 |

Winbox TCP testy (`WinboxM2CatalogTest`):

| Test | Popis |
|---|---|
| `WinboxM2_IpLayer_TcpPort8291_*` | TCP handshake smoke test |
| `WinboxM2_ReadListCatalog_*` | čtení plugin katalogu přes mproxy [2,2] |
| `WinboxM2_ParseCatalog_*` | parsování `list` souboru |
| `WinboxM2_GetSystemInfo_*` | system info přes handler [13,4] |
| `WinboxM2_ListInterfaces_*` | `/interface print` přes mepty [76] |
| `WinboxM2_SetAndVerify_InterfaceEther1Comment` | set+verify+restore comment na ether1 |

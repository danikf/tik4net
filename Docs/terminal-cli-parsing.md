# Terminálový výstup → tik4net entities: design

> Lokální soubor, není v gitu. Naposledy aktualizov\xE1no: 2026-05-26 (přid\xE1n Telnet, async/semaphore strategie).
> Navazuje na [`4x-package-architecture.md`](4x-package-architecture.md).

---

## Základní myšlenka

RouterOS CLI nabízí formát `print as-value`, který je **strojově čitelný** a **přímočaře mapovatelný** na strukturu `ITikReSentence`. To umožňuje implementovat `ITikConnection` nad SSH nebo MACTelnet bez potřeby full VT100 emulace — stačí textové zpracování.

```
API protokol:         CLI (print as-value):
─────────────         ─────────────────────
!re                   .id=*1;address=192.168.1.1/24;interface=ether1;dynamic=no
=.id=*1
=address=192.168.1.1/24
=interface=ether1             ←── přímá 1:1 shoda klíčů a hodnot
=dynamic=no
!re
=.id=*2;...
!done
```

Hodnoty jsou identické s API (`yes`/`no` pro bool, formáty adres atd.) — `tik4net.entities` entitní mapper **nemusí vědět**, že komunikuje přes CLI místo API.

---

## Formát `print as-value` — dokumentace

### List entities

```
/ip address print as-value
```
Výstup (jeden řádek = jedna entita, `;` separátor klíčů):
```
.id=*1;address=192.168.1.1/24;network=192.168.1.0;interface=ether1;comment=;dynamic=no;disabled=no;invalid=no;actual-interface=ether1
.id=*2;address=10.0.0.1/8;network=10.0.0.0;interface=bridge;comment=;dynamic=no;disabled=no;invalid=no;actual-interface=bridge
```

### Singleton entity

```
/system resource print as-value
```
Výstup (jeden řádek, bez `.id`):
```
uptime=1h30m;version=7.16;build-time=2024-01-01 00:00:00;free-memory=128.0MiB;total-memory=256.0MiB;cpu=ARM;cpu-count=4;cpu-load=3;free-hdd-space=10.0MiB
```

### Filtrovaný print

```
/ip address print as-value where interface=ether1
```
Vrátí jen matching záznamy — stejné chování jako API `?interface=ether1`.

### Add — získání .id přes `:put`

```
:put [/ip address add address=10.0.0.1/24 interface=bridge]
```
Výstup: `*3` (vrací .id nové entity — ekvivalent API `=ret=*3`).

Bez `:put [...]` wrapper vrátí `add` prázdný výstup nebo index (číslo), ne `*N` formát.

### Set

```
/ip address set [find .id=*1] comment=updated-comment
```
Výstup: prázdný (úspěch).

### Remove

```
/ip address remove [find .id=*1]
```
Výstup: prázdný (úspěch).

### Enable / Disable

```
/ip firewall filter enable [find .id=*1]
/ip firewall filter disable [find .id=*1]
```

### Chybový výstup (příklady)

```
no such item
expected end of command (line 1 column 15)
failure: already have such entry
```

---

## Překladová logika: API příkaz → CLI string

### 1. Překlad cesty (`CommandText`)

API formát: `/ip/address/print`
CLI formát: `/ip address print`

Algoritmus: rozdělení po `/`, spojení mezerami s vedoucím `/`.

```csharp
// /ip/address/print  →  /ip address print
string ApiPathToCli(string apiPath)
{
    var parts = apiPath.TrimStart('/').Split('/');
    return "/" + string.Join(" ", parts);
}
```

Příklady:
| API CommandText | CLI string (základ) |
|---|---|
| `/ip/address/print` | `/ip address print` |
| `/ip/address/add` | `/ip address add` |
| `/ip/address/set` | `/ip address set` |
| `/ip/address/remove` | `/ip address remove` |
| `/ip/firewall/filter/print` | `/ip firewall filter print` |
| `/system/identity/get` | `/system identity get` |
| `/system/reboot` | `/system reboot` |

### 2. Překlad parametrů

`ITikCommandParameter` má dva typy (`ParameterFormat`):

**Filter parametry** (`Format = Filter`, API: `?name=value`):
- Přidávají se jako `where name=value` za `print`
- Negace: `?name=!value` → `where name!=value`
- Porovnání: `?>count=5` → `where count>5`
- Regex: `?~comment=eth` → `where comment~eth`
- Multiple filters: `where a=x && b=y`

**NameValue parametry** (`Format = NameValue`, API: `=name=value`):
- Pro `add`: `name=value name2=value2 ...`
- Pro `set`: extrahovat `.id` → `[find .id=*N]`, zbytek `name=value`
- Pro ostatní (nonquery): `name=value ...`
- Speciální: `.proplist` je ignorováno (as-value vrátí vždy všechna pole)

### 3. Sestavení CLI příkazu dle operace

Detekce operace z poslední části cesty:

```
print   → /path print as-value [where filter1=val && filter2=val]
add     → :put [/path add name=val name2=val2]
set     → /path set [find .id=*N] name=val name2=val2
remove  → /path remove [find .id=*N]
enable  → /path enable [find .id=*N]
disable → /path disable [find .id=*N]
move    → /path move [find .id=*N] destination=*M
get     → /path get .id=*N value-name=name  (alias pro scalar)
```

**Příklady sestavení:**

```
API: /ip/address/print + ?interface=ether1
CLI: /ip address print as-value where interface=ether1

API: /ip/address/add + =address=10.0.0.1/24 + =interface=ether1
CLI: :put [/ip address add address=10.0.0.1/24 interface=ether1]

API: /ip/address/set + =.id=*1 + =comment=test
CLI: /ip address set [find .id=*1] comment=test

API: /ip/address/remove + =.id=*1
CLI: /ip address remove [find .id=*1]

API: /system/reboot (nonquery)
CLI: /system reboot
```

---

## Parsování výstupu `print as-value`

### Algoritmus

```csharp
IEnumerable<ITikReSentence> ParseAsValue(string output)
{
    foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed)) continue;

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        int pos = 0;
        while (pos < trimmed.Length)
        {
            // najdi '=' jako konec klíče
            int eq = trimmed.IndexOf('=', pos);
            if (eq < 0) break;
            string key = trimmed.Substring(pos, eq - pos);

            // najdi ';' jako konec hodnoty (nebo konec řádku)
            int semi = trimmed.IndexOf(';', eq + 1);
            string value = semi < 0
                ? trimmed.Substring(eq + 1)
                : trimmed.Substring(eq + 1, semi - eq - 1);

            fields[key] = value;
            pos = semi < 0 ? trimmed.Length : semi + 1;
        }

        if (fields.Count > 0)
            yield return new CliReSentence(fields);
    }
}
```

### Mock implementace `ITikReSentence`

```csharp
internal sealed class CliReSentence : ITikReSentence
{
    private readonly IReadOnlyDictionary<string, string> _fields;

    public CliReSentence(Dictionary<string, string> fields)
        => _fields = fields;

    public string GetId()
        => GetResponseField(".id");

    public string GetResponseField(string fieldName)
        => _fields.TryGetValue(fieldName, out var v) ? v
           : throw new TikSentenceException($"Field '{fieldName}' not found in CLI response");

    public bool TryGetResponseField(string fieldName, out string fieldValue)
        => _fields.TryGetValue(fieldName, out fieldValue);

    public string GetResponseFieldOrDefault(string fieldName, string defaultValue)
        => _fields.TryGetValue(fieldName, out var v) ? v : defaultValue;
}
```

### Detekce a překlad chyb

Chybový výstup nemá strukturu — detekce na základě prefixů:

```csharp
void ThrowIfError(string output, int? exitCode = null)
{
    if (exitCode.HasValue && exitCode != 0) { /* SSH: parse output jako error */ }

    var line = output.Trim();
    if (line.StartsWith("no such item")) throw new TikNoSuchItemException(line);
    if (line.StartsWith("already have such")) throw new TikAlreadyHaveSuchItemException(line);
    if (line.StartsWith("no such command") || line.StartsWith("expected end"))
        throw new TikNoSuchCommandException(line);
    if (line.StartsWith("failure:") || line.StartsWith("error:"))
        throw new TikCommandTrapException(line);
    // Ostatní neprázdný výstup z error streamu → TikCommandTrapException
}
```

**SSH výhoda:** `SshClient.RunCommand()` vrací oddělené stdout/stderr a exit code — error detekce je robustnější.

**MACTelnet nevýhoda:** vše je jeden stream, nutné heuristické parsování.

---

## Architektura — sdílená vrstva `tik4net.cli`

Protože logika překladu a parsování je identická pro SSH, MACTelnet i Telnet a nemá žádné externí závislosti, **žije přímo v `tik4net` core** (namespace `Tik4Net.Cli`). Samostatný NuGet `tik4net.cli` nevzniká — není důvod.

```
tik4net/Cli/
├── CliCommandBuilder.cs                ~ 120 LOC  (ITikCommand → CLI string)
├── CliOutputParser.cs                  ~ 80 LOC   (as-value text → IEnumerable<ITikReSentence>)
├── CliErrorParser.cs                   ~ 40 LOC   (chybový text → výjimky tik4net)
├── CliReSentence.cs                    ~ 40 LOC   (mock ITikReSentence)
├── VtStripper.cs                       ~ 50 LOC   (ANSI escape remover pro MACTelnet + Telnet)
└── CliConnectionBase.cs                ~ 220 LOC  (společná logika, abstract; SemaphoreSlim pro async)
```

Transportní balíčky (`tik4net.ssh`, `tik4net.mactelnet`, `tik4net.telnet`) závisí pouze na `tik4net` — CLI vrstva přichází zadarmo.

Abstraktní základ (včetně async strategie):

```csharp
// tik4net.cli
public abstract class CliConnectionBase : ITikConnection
{
    // Terminál je inherentně sekvenční — jeden příkaz najednou.
    // SemaphoreSlim serializuje operace; volající vidí čistý async/await.
    private readonly SemaphoreSlim _cmdLock = new(1, 1);

    // Podtřídy implementují async transport (TCP, UDP PTY…):
    protected abstract Task<string> ExecuteCliCommandCoreAsync(string cliText, CancellationToken ct);

    // Vstupní bod pro všechny operace — serializovaný přes semafor:
    protected async Task<string> ExecuteCliCommandAsync(string cliText, CancellationToken ct)
    {
        await _cmdLock.WaitAsync(ct).ConfigureAwait(false);
        try { return await ExecuteCliCommandCoreAsync(cliText, ct).ConfigureAwait(false); }
        finally { _cmdLock.Release(); }
    }

    // Sync wrapper pro zpětnou kompatibilitu s ITikCommand.Execute*:
    protected string ExecuteCliCommand(string cliText)
        => ExecuteCliCommandAsync(cliText, CancellationToken.None).GetAwaiter().GetResult();

    public ITikCommand CreateCommand(string commandText, params ITikCommandParameter[] parameters)
        => new CliCommand(this, commandText, parameters);

    internal IEnumerable<ITikReSentence> RunPrint(CliCommand cmd)
    {
        var cli = CliCommandBuilder.BuildPrint(cmd);
        var output = ExecuteCliCommand(cli);
        CliErrorParser.ThrowIfError(output);
        return CliOutputParser.ParseAsValue(output);
    }

    internal string RunAdd(CliCommand cmd)
    {
        var cli = CliCommandBuilder.BuildAdd(cmd);    // :put [/path add ...]
        var output = ExecuteCliCommand(cli);
        CliErrorParser.ThrowIfError(output);
        return output.Trim();                         // vrátí *N (nové .id)
    }

    internal void RunNonQuery(CliCommand cmd)
    {
        var cli = CliCommandBuilder.BuildNonQuery(cmd);
        ExecuteCliCommand(cli);
    }
}
```

**Async strategie pro terminálová spojení:** MACTelnet, Telnet i SSH-terminal jsou sekvenční protokoly — není možné odeslat druhý příkaz dříve, než dorazí odpověď na první (žádná tag-based korelace jako v API protokolu). Správné řešení je `SemaphoreSlim(1,1)` v `CliConnectionBase` — ne pool spojení (limit sessions na routeru, overhead) a ne background task s korelací (komplexní, výstup se může preplést). Operace jsou interně seřazeny, ale z pohledu TikLink jsou stále `await`itelné.

`tik4net.ssh`, `tik4net.mactelnet` a `tik4net.telnet` pouze implementují transport vrstvu:

```csharp
// tik4net.ssh
public class SshConnection : CliConnectionBase
{
    private SshClient _ssh;

    protected override async Task<string> ExecuteCliCommandCoreAsync(string cliText, CancellationToken ct)
    {
        // SSH.NET RunCommand je synchronní — offloadujeme na thread pool
        var cmd = await Task.Run(() => _ssh.RunCommand(cliText), ct);
        if (!string.IsNullOrWhiteSpace(cmd.Error))
            CliErrorParser.ThrowIfError(cmd.Error, cmd.ExitStatus);
        return cmd.Result;
    }
}

// tik4net.mactelnet
public class MacTelnetConnection : CliConnectionBase
{
    private MacTelnetSession _session;

    protected override async Task<string> ExecuteCliCommandCoreAsync(string cliText, CancellationToken ct)
    {
        var raw = await _session.RunCommandAndWaitAsync(cliText, ct);
        return VtStripper.Strip(raw);     // odstranit ANSI escape kódy + echo + prompt
    }
}

// tik4net.telnet — identická struktura jako MacTelnetConnection, liší se jen transport
public class TelnetConnection : CliConnectionBase
{
    private TelnetSession _session;

    protected override async Task<string> ExecuteCliCommandCoreAsync(string cliText, CancellationToken ct)
    {
        var raw = await _session.RunCommandAndWaitAsync(cliText, ct);
        return VtStripper.Strip(raw);     // stejný VT100 výstup jako MACTelnet
    }
}
```

---

## VT stripping (pro MACTelnet a Telnet)

SSH `RunCommand` vrací čistý stdout — **VT stripping není potřeba**.

MACTelnet i Telnet používají PTY → výstup obsahuje ANSI escape sekvence. `VtStripper` je sdílený v `tik4net.cli` a reusuje se pro oba transporty. Jednoduchý regex stripper:

```csharp
public static class VtStripper
{
    // Pokrývá: CSI sekvence, OSC sekvence, ostatní ESC sekvence
    private static readonly Regex AnsiRegex = new(
        @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~]|\][^\x07]*\x07)",
        RegexOptions.Compiled);

    public static string Strip(string input)
    {
        var stripped = AnsiRegex.Replace(input, "");
        // Odstranit echoed prompt a vstupní příkaz (MACTelnet echo):
        return RemovePromptAndEcho(stripped);
    }

    private static string RemovePromptAndEcho(string text)
    {
        // RouterOS prompt: "[user@identity] > "
        // Výstup = echo_of_command + newline + actual_output + newline + prompt
        var lines = text.Split('\n');
        // První řádek je echo příkazu, poslední je nový prompt → odříznout
        return string.Join("\n", lines.Skip(1).SkipLast(1));
    }
}
```

---

## Mapovací matice: `ITikCommand` metody

| Metoda | Implementace přes CLI | Poznámka |
|---|---|---|
| `ExecuteNonQuery()` | `/path verb [find .id=*N] params` | empty výstup = úspěch |
| `ExecuteList()` | `/path print as-value [where ...]` | parsování řádků |
| `ExecuteSingleRow()` | `/path print as-value [where ...]` | assert 1 řádek |
| `ExecuteScalar()` | `:put [/path get .id=*N value-name=x]` | skalární výstup |
| `ExecuteScalar(target)` | `:put [/path get .id=*N value-name=target]` | |
| `ExecuteAsync(cb,...)` | ⚠️ emulace přes thread + sync | bez true push |
| `ExecuteListWithDuration` | ❌ nelze | potřebuje streaming |
| `ExecuteListUntilDone` | ⚠️ jen pro self-terminating commands | `/ping count=N` |
| `CallCommandSync` | ❌ low-level, nelze mapovat | raw API sentences |
| `CallCommandAsync` | ❌ nelze | |
| `Cancel()` | ❌ nelze | no in-flight command |

**Capabilities pro CLI-based connection:**
- `Read = ✅`, `Write = ✅`, `Listen = ❌`, `Streaming = ❌`, `Async = ⚠️`

---

## Co `tik4net.entities` potřebuje a dostane

Entitní mapper (`LoadAll<T>()`, `Save<T>()`, `Delete<T>()`, …) volá:

| Entities operace | Volá na ITikConnection/Command | CLI výsledek |
|---|---|---|
| `LoadAll<IpAddress>()` | `ExecuteList()` na `/ip/address/print` | `as-value` parsing → `CliReSentence` list |
| `LoadById<IpAddress>("*1")` | `ExecuteSingleRow()` na `/ip/address/print ?=.id=*1` | 1 `as-value` řádek |
| `Save<IpAddress>(newEntity)` | `ExecuteNonQuery()` na `/ip/address/add` s params | `:put [add ...]` → nové `.id` |
| `Save<IpAddress>(existing)` | `ExecuteNonQuery()` na `/ip/address/set` s `.id` + změny | `set [find .id=*N] ...` |
| `Delete<IpAddress>(entity)` | `ExecuteNonQuery()` na `/ip/address/remove` s `.id` | `remove [find .id=*N]` |
| `LoadSingle<SystemResource>()` | `ExecuteSingleRow()` na `/system/resource/print` | 1 `as-value` řádek (bez `.id`) |

**Entities mapper neví** o CLI — vidí jen `ITikReSentence` objekty, které jsou naplněny z `CliReSentence`.

---

## Hranice a omezení

### Spolehlivé (pro produkční použití)

- Read operace přes `LoadAll`, `LoadList`, `LoadById`, `LoadSingle`
- Write operace: `Save` (add i set), `Delete`, `Enable`, `Disable`, `Move`
- Filtry přes `?name=value` parametry → `where name=value`
- Singleton entity (bez `.id`)
- Boolean hodnoty: RouterOS CLI i API používají `yes`/`no`

### Podmíněně spolehlivé (s caveaty)

- **Values se středníkem (`;`):** komentáře nebo hodnoty obsahující `;` rozbijí parsování.
  Workaround: omezit na entity kde to nehrozí, nebo implementovat escape-aware parser.
- **SSH vs MACTelnet:** SSH je robustnější (exit code, oddělený stderr). MACTelnet je fragile (heuristiky).
- **RouterOS verze:** `as-value` formát je konzistentní od ROS 6.x. Pro ROS < 6 může být odlišný.

### Nelze nebo jen s velkým effort

- **Listen (`/listen`)** — real-time push notifikace nejsou možné přes terminál.
- **Streaming commands** — `/tool/torch`, `/tool/ping` průběžné výsledky: SSH by to zvládl přes streaming RunCommand, ale integrace s `ITikCommand.ExecuteListWithDuration` je složitá.
- **Async cancel** — `Cancel()` nemá ekvivalent pro synchronní SSH RunCommand.
- **Batch commands** — pipelining není.
- **Proplist optimalizace** — `print as-value` vždy vrátí všechna pole (nelze omezit jako v API přes `.proplist`).

---

## Implementační přístup — doporučené pořadí

1. **CLI vrstva v `tik4net`** — implementovat `CliCommandBuilder` + `CliOutputParser` + `CliReSentence` + `CliConnectionBase` se semaforem (jsou na sobě nezávislé, testovatelné bez sítě). Součástí téhož milníku: `TikConnectionSetup` s `CreateApiConnection()` / `CreateApiSslConnectionAsync()` — nahradí `ConnectionFactory`.
2. **`tik4net.ssh` — `SshConnection`** — nejjednodušší CLI transport (čistý stdout, exit code); ověřit funkčnost entitního mapperu. Extension metoda `TikConnectionSetup.CreateSshConnection(privateKeyPath?)`.
3. **Unit testy parseru** — `CliOutputParser` testovat se zaznamenanými výstupy z RouterOS (bez živého routeru).
4. **`tik4net.mactelnet` — `MacTelnetConnection`** — přidat VtStripper a prompt-detection; fragile, potřebuje integrační testy na živém routeru. Extension metoda `TikConnectionSetup.CreateMacTelnetConnection()`.
5. **`tik4net.telnet` — `TelnetConnection`** — přidat `TelnetNegotiator` (~30 LOC), detekci login/password promptu; reuse `VtStripper` beze změny. Implementační effort cca 20 % oproti MACTelnet. Extension metoda `TikConnectionSetup.CreateTelnetConnection()`.

---

## Otevřené otázky

1. **Escape-aware `;` parser**: Je třeba pro robustní produkční nasazení? Komentáře se středníky jsou reálné.  
   Možnost: parsovat zleva, sledovat `=` a přeskakovat hodnoty s uvozovkami (pokud RouterOS quote-uje).

2. **`add` a nové `.id`**: `:put [/path add ...]` vrátí `.id` jen pokud `add` vrací handle.  
   Ne všechny entity v RouterOS vrací `.id` z `add` — nutno otestovat na různých entitách.

3. **MACTelnet prompt-detection spolehlivost**: RouterOS prompt může obsahovat custom identity s libovolnými znaky.  
   Robustnější pattern: detekovat `] > ` (konec promptu) místo celého promptu.

4. **Sdílení `tik4net.cli`**: Jako samostatný NuGet, nebo interní dependency (InternalsVisibleTo)?  
   Doporučení: samostatný NuGet od začátku — umožňuje jiným projektům reusovat parsing.

5. **`ExecuteAsync` emulace**: `CliConnectionBase.ExecuteCliCommandAsync` + semafor je správný základ. `ITikCommand.ExecuteAsync(callback, done, trap)` lze emulovat jako `Task.Run(() => { /* sync exec */; callback(each_re); done(); })` — vrátí se okamžitě, výsledky dorazí přes callback. Cancel není možný (žádný in-flight command). Doporučení: implementovat, ale dokumentovat omezení.

6. **Telnet vs. MACTelnet prompt-detection**: RouterOS Telnet prompt je identický s MACTelnet promptem (`[user@identity] > `). `VtStripper.RemovePromptAndEcho` lze reusovat beze změny — ověřit na živém routeru.

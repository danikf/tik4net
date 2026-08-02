# Winbox M2 terminal — PoC findings (mepty, comment, MAC layer)

> Local file, not in git. Last updated: 2026-05-27.
> Builds on [`project_winbox_m2_poc.md`](../memory/project_winbox_m2_poc.md) and
> [`mactelnet-protocol.md`](mactelnet-protocol.md).
>
> Source files:
> - `tik4net.tests/WinboxM2CatalogTest.cs` — Winbox TCP PoC (7/7 tests pass)
> - `tik4net.tests/MacLayerTest.cs` — MAC-layer PoC (0/5 tests pass, see section 5)

---

## 1. Comment format in the RouterOS CLI

RouterOS **does not display** a comment as `comment=text` in `/interface print detail`
output. Instead it renders it as a **triple-semicolon notation** on its own line above
the entity:

```
Flags: R - running
 0  R   ;;; tik4net-winbox-test
         name="ether1" default-name="ether1" type="ether" mtu=1500 ...
```

Regex for extraction:

```csharp
var m = Regex.Match(output, @";;;\s+(.+?)(?:\r|\n|$)", RegexOptions.Multiline);
if (m.Success) return m.Groups[1].Value.Trim();
return "";
```

**Pitfalls:**
- `comment=...` never appears in the detail listing — a regex matching `comment=(\S+)`
  always fails.
- The `;;;` line sits above the entity, not after it.
- If no comment is set, the `;;;` line is absent from the output entirely (returns `""`).

---

## 2. Setting the comment via CLI (`SetInterfaceComment`)

Command: `/interface set <ifName> comment=<value>`

**Values:**
- Empty string: `comment=""` (empty quotes — RouterOS clears the comment)
- A plain string with no spaces/quotes/backslashes: **no quoting needed**
  (`comment=tik4net-test`)
- A string containing spaces or special characters: quoted, with escaping

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

**Pitfall:** Never quote every value unconditionally. `comment="tik4net-test"` does
work on the RouterOS CLI, but for values coming out of a parser (e.g. an empty string)
it's critical to distinguish `""` (clear it) from a missing parameter (leave it
unchanged).

---

## 3. Lifecycle of a mepty terminal session

### Architecture

Every call to `RunTerminalCommand` opens a **new mepty session** on the existing TCP
connection:

```
TCP connection (port 8291, AES-128-CBC encrypted)
  └── mepty session A  [76] cmd=0x0A0065  → ListInterfaces
  └── mepty session B  [76] cmd=0x0A0065  → SetInterfaceComment
  └── mepty session C  [76] cmd=0x0A0065  → GetInterfaceComment
  └── ...
```

RouterOS assigns each session a fresh `sessionId` (a byte, returned by the server in
SYS_SESSION_ID).

### Draining before opening a session

After a command completes, the router still routes a few more push frames (VT100
bytes, clear sequences) over the existing TCP stream. If these aren't consumed,
`RecvAndDecrypt` on the next `OpenTerminalSession` call returns stale data instead of
the fresh meptyLogin response → wrong `sessionId` → all commands go to the wrong
session → no output.

```csharp
DrainEncryptedFrames(600);  // 600 ms — must be enough for the last push frames to land
int sessionId = OpenTerminalSession(password);
```

**Why 600 ms?** At 300 ms the drain was too short and occasionally a push frame
arrived a fraction of a second late. 600 ms turned out to be reliable.

### Phase 1: VT100 negotiation

Before showing the CLI prompt, RouterOS runs a **multi-round cursor probe sequence**:

```
Server → ESC[H + ESC[9999B + ESC Z + ESC[6n   (detect bottom edge + terminal ID)
Client → ESC[25;1R + ESC[?1;0c
Server → ESC[H + ESC[9999B + ESC D + ESC[9999A + ESC[6n  (verify height after IND scroll)
Client → ESC[25;1R
Server → ESC[H + ESC[9999C + ESC[6n             (width)
Client → ESC[1;80R
Server → [admin@MikroTik] >                      (prompt)
```

The `Vt100State(width=80, height=25)` class tracks the cursor position and generates
the correct responses.

**Critical pitfall:** if you always reply `ESC[1;1R`, RouterOS concludes the terminal
is 1×1 and repeats the probes forever — Phase 1 never finishes.

Phase 1 ends when `StripAnsi(initSb).Contains("] >")` returns `true`.

### Phase 2: sending the command and waiting for output

```csharp
SendTerminalInput(sessionId, Encoding.UTF8.GetBytes(command + "\r"), ref counter);
// ... polling loop ...
string stripped = StripAnsi(cmdSb.ToString()).TrimEnd();
if (stripped.EndsWith("] >"))
    break;
```

**Key detail — `EndsWith` instead of `Contains`:**

RouterOS first **echoes** the submitted command back:
```
[admin@MikroTik] > /interface set ether1 comment=test
```
This echo contains `"] >"` too, but it's the command echo, not a new prompt.
The real new prompt arrives **only after the command's output**, at the end of the
response.

So `Contains("] >")` would stop reading too early (on the echo), while
`TrimEnd().EndsWith("] >")` correctly waits for the trailing prompt.

### The RouterOS "Change your password" nag

RouterOS may show a password-change prompt before the CLI prompt:
```
new password>
```
Fix: detect it during Phase 1 and send `\x03` (Ctrl-C) to skip it.

```csharp
if (!sentCtrlC && (stripped.Contains("new password>") || stripped.Contains("password>")))
{
    SendTerminalInput(sessionId, new byte[] { 0x03 }, ref counter);
    sentCtrlC = true;
    initSb.Clear();
}
```

---

## 4. Parity of the MikroTik M2 protocol across TCP and MAC transports

The Winbox M2 protocol is **identical** regardless of transport:

| Aspect | Winbox TCP (port 8291) | Winbox MAC (UDP 20561, ct=0x0f90) |
|---|---|---|
| EC-SRP5 authentication | ✅ same math | ✅ same math |
| Curve25519 (Weierstrass form) | ✅ | ✅ |
| AES-128-CBC frame encryption | ✅ | ✅ (expected, unverified) |
| M2 TLV format | ✅ | ✅ (expected) |
| Handler [76] mepty | ✅ | ✅ (expected) |
| MAC Telnet (UDP 20561, ct=0x0015) | ❌ different protocol | — |

**MAC Telnet** (ct=0x0015) is a **different protocol** — a raw VT100 terminal, no
encryption, no M2 TLV format. The EC-SRP5 math is shared, but the framing differs.

**MAC Winbox** (ct=0x0f90) is Winbox M2 carried over the UDP MAC-layer transport
instead of TCP. This means the `WinboxM2Client` logic can be carried over directly —
just swap the TCP `NetworkStream` for a UDP+MAC transport layer.

---

## 5. Status of the MAC-layer tests (`MacLayerTest.cs`)

### Current status: 0 / 5 tests passing

All tests fail on a `RecvUntil` timeout — the router doesn't respond to the UDP
packets at all.

### What has been verified

| Test | Result |
|---|---|
| API access to the router (port 8728) | ✅ works |
| MNDP discovery (UDP broadcast 5678) | ✅ works |
| Winbox TCP PoC (port 8291) | ✅ works, 7/7 tests |
| MAC Telnet unicast (unicast to the router's IP:20561) | ❌ NO packets received |
| MAC Telnet broadcast (<subnet-broadcast>:20561, srcPort=20561) | ❌ only our own packets seen looping back |
| MAC Telnet broadcast (<subnet-broadcast>:20561, srcPort=random 52774) | ❌ NO packets received |

### Router configuration state (verified via API)

```
/tool mac-server:         allowed-interface-list=all
/tool mac-server mac-winbox: allowed-interface-list=all
```

RouterOS 7.x **has no `disabled` property** on `/tool mac-server` — it's controlled
solely via `allowed-interface-list`. The original code using `disabled=no` failed
with `unknown parameter disabled`.

### Winbox application on the test PC

While diagnosing this, we captured Winbox.exe (running on the test PC, port 61126,
ct=0x900F) **actively transmitting** to the router's MAC (<router-MAC>) over UDP
20561. This confirms the network layer for MAC protocols is **not globally blocked**.

### Possible causes of the failure

1. **CHR limitation**: the CloudHostedRouter (a Hyper-V VM) may not respond to MAC
   Telnet at all. The Hyper-V virtual switch might not forward UDP broadcast to the
   correct interface, or the CHR RouterOS image may not fully support MAC Telnet.

2. **Source port**: the MAC Telnet spec says the router may ignore packets from ports
   other than 20561. Tested both srcPort=20561 and srcPort=random — both failed.

3. **Broadcast vs. unicast**: the router responds to MNDP broadcast (5678) but not to
   MAC Telnet broadcast (20561). RouterOS CHR may behave differently for port 20561.

4. **Windows Firewall**: may be blocking inbound UDP from the router to the test PC.
   The Winbox application is exempted from the firewall; our `UdpClient` test may not
   be.

### RouterOS 7.x — known `mac-server` issue

RouterOS 7.x has no `disabled` property on `/tool/mac-server`:

```
# RouterOS 6.x (works):
/tool mac-server set disabled=no

# RouterOS 7.x (throws "unknown parameter disabled"):
/tool mac-server set allowed-interface-list=all  ← correct way
```

The same applies to `/tool/mac-server/mac-winbox/set`.

### Recommended next steps

1. Test against a physical RouterBoard (not CHR) — determine whether the problem is
   CHR-specific.
2. Capture network traffic with Wireshark on the test PC — verify whether the router
   sends any UDP response at all (to diagnose the Windows Firewall theory).
3. Try a `UdpClient` bound to `0.0.0.0:20561` with `SO_REUSEADDR` — some MAC Telnet
   implementations require this.
4. Check whether the CHR image's MAC server is functional at all
   (`/tool/mac-server/sessions/print` would show an active session if the router had
   accepted a SESSIONSTART packet).

---

## 6. API pattern for `/tool mac-server` (RouterOS 7.x)

```csharp
// Correct pattern for enabling the MAC server on RouterOS 7.x
// (used in ClassInitialize in MacLayerTest.cs)

using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
{
    // Read current state
    var print = conn.CreateCommand("/tool/mac-server/print");
    foreach (var row in print.ExecuteList())
        Console.WriteLine("allowed-interface-list=" +
            row.GetResponseFieldOrDefault("allowed-interface-list", "?"));

    // Set — allowed-interface-list only, not disabled
    var cmd = conn.CreateCommand("/tool/mac-server/set");
    cmd.AddParameterAndValues("allowed-interface-list", "all");
    cmd.ExecuteNonQuery();

    // Same pattern for mac-winbox:
    var cmd2 = conn.CreateCommand("/tool/mac-server/mac-winbox/set");
    cmd2.AddParameterAndValues("allowed-interface-list", "all");
    cmd2.ExecuteNonQuery();
}
```

**Incorrect pattern (throws `unknown parameter disabled` on RouterOS 7.x):**

```csharp
// WRONG — RouterOS 7.x:
cmd.AddParameterAndValues("disabled", "no");   // ← throws

// CORRECT:
cmd.AddParameterAndValues("allowed-interface-list", "all");
```

---

## 7. PoC test status overview

| Test class | Tests | Status | Notes |
|---|---|---|---|
| `WinboxM2CatalogTest` | 7 | ✅ 7/7 | Winbox TCP, mepty, set/get comment |
| `MacLayerTest` | 5 | ❌ 0/5 | Router does not respond on UDP 20561 |

Winbox TCP tests (`WinboxM2CatalogTest`):

| Test | Description |
|---|---|
| `WinboxM2_IpLayer_TcpPort8291_*` | TCP handshake smoke test |
| `WinboxM2_ReadListCatalog_*` | reads the plugin catalog via mproxy [2,2] |
| `WinboxM2_ParseCatalog_*` | parses the `list` file |
| `WinboxM2_GetSystemInfo_*` | system info via handler [13,4] |
| `WinboxM2_ListInterfaces_*` | `/interface print` via mepty [76] |
| `WinboxM2_SetAndVerify_InterfaceEther1Comment` | set+verify+restore comment on ether1 |

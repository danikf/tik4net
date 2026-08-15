# WinBox M2 terminal — mepty session behaviour

How the WinBox terminal channel behaves: the mepty session lifecycle, VT100 negotiation, and how
RouterOS renders and accepts a comment over the CLI.

> Section numbers are cited from the C# source — do not renumber a heading without checking who cites
> it. Builds on [`mactelnet-protocol.md`](mactelnet-protocol.md).
>
> The protocol proof-of-concept tests these findings came from now live in
> [`tik4net.integrationtests/Protocols/`](../tik4net.integrationtests/README.md) — `Clients/` for the
> low-level WinBox and MAC-Telnet clients, `Tests/` for the test classes.

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

## 5. The MAC layer works; its early failure was host-side

The MAC-layer transports (`MacTelnet`, `WinboxCliMac`, `WinboxNativeMac`) are shipped and exercised by
the integration suite. During the initial proof of concept they could not reach the router at all, and
the hypotheses recorded at the time — a CHR limitation, a source-port rule, CHR treating port 20561
differently from MNDP's 5678, Windows Firewall — were **all wrong**.

**The actual cause is host-side NIC selection:** the SESSIONSTART broadcast leaves via the wrong
adapter. MNDP keeps working throughout, which is what makes this misleading — it is also broadcast, but
is answered over a different path. `MacLayerTransport.BaseConnect` now selects the NIC explicitly.

See [findings-mactelnet.md](findings-mactelnet.md) for the transport's actual behaviour, including the
ACK rule, the reliability queue and the login-refusal handling.

### RouterOS 7.x has no `disabled` on `/tool/mac-server`

Access is controlled solely by `allowed-interface-list`:

```
/tool mac-server set allowed-interface-list=all
/tool mac-server mac-winbox set allowed-interface-list=all
```

`disabled=no` fails with `unknown parameter disabled` on RouterOS 7.x.

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

## 7. Settled questions — do not re-investigate

- **Does CHR support the MAC-layer protocols?** Yes. All three MAC transports run against the CHR test
  router. The early "CHR may not respond to MAC-Telnet at all" theory is disproved.
- **Does the client's UDP source port have to be 20561?** No. 20561 is the *router's* port; the client
  binds an ephemeral local port. Both fixed and random source ports were tried during the PoC and
  neither was the problem.
- **Is Windows Firewall blocking the router's replies?** No. This was tested by observing WinBox itself
  transmitting to the router over UDP 20561 from the same host, which proved the path was not blocked.
- **Do TCP and MAC need different M2 handling above the transport?** No — see §4. The M2 layer is
  identical; only the carrier below it differs.

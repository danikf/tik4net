# RoMON — findings

What RouterOS does with RoMON, as measured on a 7.24.4 agent (the lab CHR) relaying to a 7.17rc3 target. The
relay (§4) is also measured to a 6.49.13 and a 7.21.5 target: reads and Tab completion over Telnet, SSH and
MAC-Telnet to the agent answer from the target alike. The WinBox relay (§5) is measured on a 7.24.5 agent and a
7.21.5 target.
Ids below are placeholders: `AA:BB:CC:00:00:01` is the agent, `AA:BB:CC:DD:EE:FF` the target.

## 1. The model

RoMON (Router Management Overlay Network) is a MikroTik-only Layer-2 overlay: EtherType `0x88bf`, destination
MAC `01:80:c2:00:88:bf`, MD5 message authentication by shared secrets, **no encryption**. Every RoMON-enabled
router forwards for its neighbours, so a node is reachable over several hops as long as a path of RoMON-enabled
MikroTiks exists — independently of IP configuration.

No PC speaks RoMON. A client reaches a RoMON node by logging in to a RoMON-enabled router — the **agent** —
and letting it relay. The node is addressed by its **RoMON id**: its `/tool romon` `current-id`, which is the
`id` setting or, when that is zero, a MAC the router picked. A RoMON id has a MAC's shape but is not necessarily
any interface's MAC (the lab CHR's is its ether2 MAC, not ether1's).

## 2. Agent-side tools

| Verb | Parameters | Behaviour |
|---|---|---|
| `/tool/romon/discover` | `duration`, `freeze-frame-interval`, `proplist` | The neighbour set: `address`, `cost`, `hops`, `path`, `l2mtu`, `identity`, `version`, `board`, `uptime` (absent for a RouterOS 6 neighbour — the API row has no such word); the CLI also prints flag `active`. Runs until `duration` ends |
| `/tool/romon/ping` | `id`, `count`, `size`, `interval` | One row per echo with running totals; a lost echo has `status=timeout` and no `time` |
| `/tool/romon/ssh` | `address`, `user`, `command`, `output-to-file` | See §4 |

With RoMON disabled every tool answers `failure: RoMON not running`.

**discover per transport.** The binary API and REST report the whole neighbour set once per one-second refresh
(`.section` 0, 1, 2, …), so a 3 s scan returns each neighbour three times. The CLI's `print as-value`-style
answer lists each neighbour once. discover refuses `once` (`bad parameter once`); over the CLI `duration=1`
prints nothing and `duration=2` prints the neighbours — so 2 is the shortest bound that works everywhere
(`TikMonitorVerbs`).

**WinBox native.** Handler `[127,4]` is "RoMON Discovery" (start `0xFE000F`, cancel `0xFE0011`, autorefresh
1000 ms, empty request — no `duration` input: the window runs until cancelled). Handler `[127,2]` hosts both the
settings singleton and "RoMON Ping" (start 7, cancel 8). The ping reply record carries keys the `.jg` does not
name for it: `0x1` host (raw MAC — contested with the settings' `Enabled` bool at the same key), `0x64` time as
a scale-1000 fixed-point **without** a postfix (wire unit: milliseconds), `0x69`/`0x6A` sent/received, `0x6B`
packet loss, `0x6C` a status-bar string (`"2 of 2 packets received"`), `0x6D` seq.

## 3. The agent on the M2 layer

Binary 127 answers as **`msg-proxy`** (`0xFF001C = "msg-proxy-<version>"` on every reply). Sub-handlers:
`[127,1]` port list, `[127,2]` settings + ping, `[127,4]` discover; `[127,0]` and `[127,5..8]` answer
`0xFE0002` (not implemented) from `[127]`; **`[127,3]`** is in no `.jg` window — generic requests get no
reply at all (the session survives), commands 1 and 7 answer `0xFE0009` (not permitted).

`[127,2]` with `SYS_CMD = 9` and an empty body answers status 0: it is the "RoMON enabled on the agent" check
WinBox makes first.

WinBox's system-key table names **`SYS_ROMON` = `0xFF0018`** (raw) and `SYS_RMACADDR` = `0xFF0017` (raw).
A client that sets `SYS_ROMON` on a request gets the agent's own answer — the key is not a client-side route
selector.

## 4. The SSH relay — `/tool romon ssh`

On the agent's CLI, `/tool romon ssh address=<target id> user=<user>`:

- asks `password:` straight away — no host-key prompt;
- **unless the target user's password is empty:** then there is no `password:` at all. The login succeeds at
  once and the first thing on screen is the target's banner and its change-password nag,
  `Change your password (Ctrl-C to skip)` / `new password>` (7.24.4 target). Ctrl-C declines it and the prompt
  follows. A line typed there is taken as the account's **new password** (`repeat new password>` follows), so a
  client waiting for `password:` must recognise the nag instead, never answer it;
- after the password, the target sets up its terminal **through** the agent (`ESC Z`, cursor-position queries,
  scroll region), prints its banner, then its **recent critical log lines** — which include entries like
  `login failure for user test from AA:BB:CC:00:00:01 by romon AA:BB:CC:00:00:01 via ssh` on a login that
  succeeds — and then its prompt. The `+c` login flag goes through (no colour);
- the session behaves like a direct one; `:put [/tool romon get current-id]` answers the **target's** id;
- `/quit` prints `interrupted`, `Welcome back!` and the agent's prompt; the agent's session is intact.

**The end of the relay hands the terminal back to the agent.** Whatever ends the relay — `/quit` on the target,
the target logging the session out or rebooting, an unreachable id — the agent prints `Welcome back!` and its own
prompt, and the session carries on **on the agent**. A prompt cannot tell the two routers apart (on factory
defaults both are `[admin@MikroTik] >`), so a client that kept typing would run its commands on the agent. Typed
as one line, `/tool romon ssh address=<id> user=<user>; /quit`, the `/quit` runs as soon as the relay ends and
the agent's session ends with it: the screen stops at `Welcome back!` / `interrupted`, no prompt follows.
Measured on Telnet, SSH and MAC-Telnet to the agent, for a `/quit` on the target and for an unknown id.

**Failures.** An unknown RoMON id and RoMON disabled on the agent look identical: a pause (~6 s), then
`Welcome back!` and the agent's prompt, **no text**. `:put [/tool romon get enabled]` on the agent tells them
apart — asked before the relay, since with the trailing `/quit` there is no agent shell left afterwards. A refused login — wrong password, or a user whose group lacks the **`ssh` policy** — is answered with
another `password:` and nothing else; every further line typed into it is one more failed login in the target's
log.

**What the target requires and ignores.** The target's IP `ssh` service is not used: with it disabled, the
login works. The user's `ssh` policy is required. A user's IP `address` restriction does **not** limit RoMON
logins — the target records the source as the agent's RoMON id, not an IP (`/user active`: flag `M`
by-romon, `address=` and `by-romon=` the agent's id, `via=ssh`).

**Security shape.** The agent is the SSH client, so it sees the session in clear; the overlay hop is
SSH-encrypted; the leg from the client to the agent is only as private as the transport used for it — over
Telnet or MAC-Telnet the target's password crosses the network in cleartext.

**MAC-Telnet to the agent.** The relay runs the same over a MAC-Telnet session, so a PC with no IP route to
either router reaches the target: MAC layer to the agent, RoMON beyond it. Being inside `/tool romon ssh` does
not keep an idle MAC-Telnet console alive: a read after 35 s of idle took 4.8 s — a new MAC-Telnet session and
a fresh relay (about 3 s of it), not an answer on the old one.

**Idle.** Over Telnet and SSH to the agent the relayed session survives idle: after 35 s, 2, 5 and 10 minutes the
first read answered from the target in 150–200 ms, on the same session. Over MAC-Telnet every one of those idle
periods ended in a reconnect and a fresh relay (~4.5 s), and the read still answered from the target.

**Safe Mode.** Ctrl+X typed into the agent's terminal while it runs `/tool romon ssh` reaches the target: the
target's `/safe-mode print` shows `enabled=true`, the agent's stays `false` (a Ctrl+X on the agent's own console
does show there). Release (a second Ctrl+X) and `/safe-mode unroll` act on the target too, and the relay stays on
the target afterwards. Ending the relayed session with Safe Mode held rolls the change back on the target.
Measured with a 7.24.4 agent and a 7.19.6 target over Telnet, SSH and MAC-Telnet (`RomonRelayTest`).

**Listen and monitors.** On a CLI transport both are polled — one-shot commands reissued through the same
terminal — so the relay carries them unchanged: a listen reports rows changed on the target and never on the
agent, a callback ping and a bounded synchronous ping return their rows, and the relay is still on the target
when they stop. None of them sends Ctrl-C. A listen reads its baseline before the call returns, which through the
relay takes a second or more.

**Opening, and cancelling part-way.** An open through the relay is the agent's login, two queries on its console,
`/tool romon ssh` and the target's login: about 1.2 s over MAC-Telnet, 1.5 s over SSH, 1.9 s over Telnet on the
lab pair (a first open to a cold agent takes twice that). A cancel anywhere in it ends the open with
`OperationCanceledException` within about 0.1 s — except inside the MAC-layer login to the agent, which is synchronous
and finishes first (~0.7 s) — and leaves nothing behind: `/user active` on both routers is back to what it was
within seconds, and the next open works. An open relay is one row on each: the agent's by its transport, the
target's `via=ssh` by RoMON.

**A cancel during the target's login, through an agent before 7.20.** The terminal's `/quit` on close is what
ends the target's session, and it only works once the target's shell is reading: a cancel that lands between the
target's login and its first prompt leaves a session on the target that the agent has to end. A 7.24.4 agent does,
with the console; a 7.19.6 agent does not, and the row stays on the target's `/user active` indefinitely (measured
at 25 minutes, `by-romon` the agent's id, while the agent's own `/user active` is empty). Of seven cancel points
spread over one open, three reach the target at all and exactly one survives. Nothing on the client side reaches
it: a second `/quit` sent after a pause lands on the *agent's* login prompt instead, because the first one has
already ended that session — it is submitted as a user name, and the failed logins that follow are worse than the
orphan. Sending a password there is how a blind write once changed an account's password (`Docs/HISTORY.md`).

**Large reads.** 450 rows, each wider than the target's 80-column terminal, read through the relay filtered and
unfiltered, windowed at 100 rows and in one command: every read returns exactly what the target's own API does,
over Telnet, SSH and MAC-Telnet — including the single command over MAC-Telnet (0.7 s filtered, 1.7 s for the
whole table; windowed, 2 s).

What is not reliable is the **unwindowed** whole table over MAC-Telnet: about one run in ten the router stops
part way and never resumes. Measured with a byte trace — 445 of the 450 rows delivered, then nothing but its
10 s keepalives for the whole 30 s deadline, every byte already acknowledged and no gap in the counters, so
neither a loss nor a backlog of ours. It has the shape of the single-large-read stall in
`findings-router-throughput-ceiling.md`, but not its cause: both lab CHRs run on two vCPUs, which is what
removed that one. It lands either as a refusal (the router left a prompt) or as a receive timeout (it did not). Both are accepted at page size 0 in `RomonRelayTest`; paging is the MAC layer's default
for this reason. A pre-7.20 target doubles the exposure, because the flags read repeats the same rows and
`proplist=` does not drop their comments (`findings-cli.md`).

**A status line spliced into the target's output.** With the 6.49.13 target, the relayed byte stream now and then
carries `waiting for head` and a bare LF that the target's output does not contain: about one 450-row read
in fifteen, over Telnet and SSH to the 7.24.4 agent alike. It lands at any byte — inside a value
(`address=1waiting for head\n98.18.1.29`) or a field name (`addrwaiting for head\ness=…`), not at a line
boundary — and the target's own bytes continue unbroken on either side, so it is a pure insertion. It arrives on the agent's
session 200 ms after the client last sent anything. Neither router logs anything matching it, and it is in
no tik4net assembly. It has not been seen on a direct session to the target, nor in the same reads through a 7.x
target; which of the two routers writes it is not established. Unhandled, the as-value parser reads the LF as a
field separator: inside a value it returns a wrong value (`1waiting for head,98.18.1.29`) with no error, and inside
a name the row is missing that field. The relaying transports remove the exact text from a relayed session's raw
terminal text before anything parses it (`RouterOsCliLogin.WithoutRomonRelayNoise`). A different status line
from the same source would still get through.

**Tab-completion.** The Tab and the Ctrl-C that clears the line afterwards reach the target's line editor, not
the agent's `/tool romon ssh` client: the listing is the target's menus, and the relay is still on the target
after each call (findings-cli §14).

**Either version can be the agent.** With the lab pair's roles swapped — the 7.19.6 router relaying to the 7.24.4
one — the whole of `RomonRelayTest` behaves as it does the other way round: the reads, the writes that land only
on the target, the session that ends when the target ends it, Safe Mode, listen, both monitors, large reads and
idle. The agent's console queries (`:put [/tool romon get current-id]`, `get enabled`) and `/tool romon ssh
address= user=` are spelled the same on both. The one difference is the cancelled open above. Tab-completion is
not measurable in that direction: it is told apart by a menu the target has and the agent does not, and the older
router's menus are a subset of the newer one's.

tik4net's implementation: `RouterOsCliLogin.RomonSshLoginAsync`, used by Telnet, SSH and MAC-Telnet through
`TikConnectionSetup.RomonAgentSetup`. A `Welcome back!` line in what a relayed session reads raises
`TikRomonRelayEndedException` and closes the connection, and the relay's `waiting for head` line is removed from
what it reads; MAC-Telnet's reconnect after an idle logout relays to the
target again before it resends.

## 5. The WinBox relay — M2 through the agent

Measured on a 7.24.5 agent relaying to a 7.21.5 target, from WinBox 4 through a decrypting relay
(`RomonWinboxProxyProbe`) and reproduced with `RomonAgentProbeTest.Probe_Romon_StreamOpen_Connect`.

**Agent login.** WinBox logs in to the agent as `<user>+r`; the agent lists that session `via=romon`. The router
hashes only the name before `+` into the EC-SRP5 validator, so a client that sends options must hash the bare
account name. The option is not needed: a plain login opens a link the same way.

**Before the connect** WinBox sends `[127,2]` `SYS_CMD=9` (the "RoMON running" check, §3) and, to fill its
neighbour list, starts a discover on `[127,4]` (`0xFE000F`) and polls it; neither is needed for the link.

**Connect.** One request to binary 2 — the agent's `msg-proxy` router, not a `[127,x]` handler:

| Key | Type | Value |
|---|---|---|
| `SYS_TO` | u32[] | `[2]` |
| `SYS_CMD` | u32 | `2001` (`0x7D1`) |
| `0x4` | raw | the target's RoMON id, 6 bytes |
| `0x7` | string | user on the **target** |
| `0x8` | string | password on the target |
| `0x6` | bool | true |

The reply comes after the agent has logged in to the target (~250 ms in the lab) and carries the **link id** in
`STD_ID` (`0xFE0001`, u32) and `0x6 = true`. Without the id at key `0x4` (tried at keys 1–3, or
missing) the answer is `0xFE0006` `bad destination or port`.

**Routing.** Every message for the target puts `[2, <link id>]` in front of the handler path it would use on a
direct session: `SYS_TO=[2,<link>,13,4]` `SYS_CMD=7` is the sysinfo request, `[2,<link>,24,1]` get-singleton
is the identity, `[2,<link>,2,2]` is the target's mproxy (file list, `.jg` downloads). Replies come from
`SYS_FROM=[2,<link>,…]`, and their trace (`0xFF001C`) names both routers, e.g.
`[msg-proxy-7.21.5, tcp-msg(winbox):@::, msg-proxy-7.24.5]`.

**What the agent does.** It opens a stream on the overlay to the target's RoMON **port 4** (TCP-like: SYN,
SYN-ACK, data, ACK frames with 32-bit sequence and acknowledgement numbers) and runs an ordinary WinBox session
over it — EC-SRP5 with the target user and password from the connect, then encrypted frames. It decrypts the
target's stream and re-encrypts for the client, so it sees every message in plain form. The target lists the
session `via=winbox` with `by-romon=<agent id>` and no address.

**Connect failures.** An id the agent cannot reach answers `0xFE0006` `timeout` after about 5 s; RoMON switched
off on the agent looks the same, which is why the agent's own settings (`[127,2]` get-singleton: `0x1` enabled,
`0x65` its current-id, absent when disabled) are read first. A target that refuses the login answers `0xFE0009`.

**When the link ends.** With RoMON switched off on the target under an open link, a request routed to it gets no
reply. About 5.5 s after the target went quiet the agent pushes logouts (`SYS_CMD = 0xFE0014`, no request id),
among them one with `SYS_FROM=[0xFF0003, <link>]` — the only frame that names the link. Nothing answers on the link
after that, not even once the target is back in the overlay; a new connect is needed.
(`RomonAgentProbeTest.Probe_Romon_WinboxRelay_LinkEndFrames`, 7.24.5 agent, 6.49.13 target.)

**Logouts that are not this link's.** The agent pushes a logout to every WinBox session of the user whenever another
session of that user ends on it — a Telnet login closing is enough: `SYS_FROM=[<that session>]`,
`0xFF000B = 0xFFFFFFFF`. The link answers normally afterwards (`Probe_Romon_WinboxRelay_FramesOnAnotherLogout`).
When the agent reaps links that other sessions left behind, it pushes their pairs too — `[0xFF0003, <their link>]`
and `[2, <their link>]` with `0xFF000B` — to every session of the user. Only a link id match says "this link ended".
Those pushes arrive at any time, including between the two requests that open a link and their answers — the
`[127,2]` settings read and the connect — so each answer is the frame carrying its request's id, and a push in front
of it is read past (`WinboxRomonChannel.AgentRequest`, `WinboxRomonChannelPushTests`). Taken as the answer, the
reaped link's logout reads as a connect refused with no error field.

**Idle.** A link with no traffic stays up: 150 s idle, then an ordinary read through it
(`Probe_Romon_WinboxRelay_IdleFrames`). It needs no keepalive.

**Closing.** Closing the session to the agent does not end the agent's session on the target. WinBox does nothing
more: at a disconnect it unsubscribes its open windows through the link and closes the TCP connection (captured
through `RomonWinboxProxyProbe`). The agent ends a dead link's session on the target later, when it reaps dead links
— WinBox's after ~30 s, ours after 2 to 3 minutes, several at once (`Probe_Romon_WatchTargetSessions`) — and the
target then rolls back what the session held, Safe Mode included (`RomonRelayTest`, WinBox native rows: 133 s and
179 s). Nothing the client sends ends it sooner: not a logout routed through the link (`SYS_TO=[2,<link>]`,
`[2,<link>,13,4]`, with or without a reply expected — no answer), nor one to `[0xFF0003,<link>]` or to the
msg-proxy, nor any msg-proxy command 2000–2010 with the link id (all `0xFE0009`), nor a request still in flight at
the close, nor WinBox's `user+r` login (`Probe_Romon_WinboxRelay_CloseVariants`,
`Probe_Romon_WinboxRelay_ProxyCommandScan`). A `+r` session is restricted on the agent: `[127,2]` get-singleton
answers `0xFE0009`, and WinBox checks RoMON with `[127,2]` command 9 instead. The WinBox CLI is not affected: it
ends the target's terminal with `/quit`, which ends the session at once.

**tik4net's implementation.** `WinboxRomonChannel` wraps the carrier session to the agent (TCP or MAC layer):
it logs in, reads the agent's settings, sends the connect, and then rewrites `SYS_TO`/`SYS_FROM` on every
message, so WinBox native and the WinBox CLI's mepty terminal run on it unchanged. The logout naming the link raises
`TikRomonRelayEndedException`, and the connection closes; every other logout is passed up as on a direct session.
Measured with a 7.24.5 agent and a 6.49.13 target on all four WinBox transports (`RomonRelayTest`).

## 6. Capture notes

The CHR's `/tool sniffer` records only frames the CHR **transmits** for EtherType `0x88bf` — received RoMON
frames never appear, whatever `filter-direction` or `filter-interface` say. For both directions run a sniffer on
each end (the agent records what it sends the target, the target what it sends back) or capture on the Hyper-V
host. Frame header as transmitted: byte 0 type (1 = hello to the multicast address, 2/3 =
unicast), a 2-byte total length including the Ethernet header, a 24-byte hash field (zeros with empty secrets),
then the RoMON ids.

# RoMON — findings

What RouterOS does with RoMON, as measured on a 7.24.4 agent (the lab CHR) relaying to a 7.17rc3 target.
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
| `/tool/romon/discover` | `duration`, `freeze-frame-interval`, `proplist` | The neighbour set: `address`, `cost`, `hops`, `path`, `l2mtu`, `identity`, `version`, `board`, `uptime`; the CLI also prints flag `active`. Runs until `duration` ends |
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

tik4net's implementation: `RouterOsCliLogin.RomonSshLoginAsync`, used by Telnet, SSH and MAC-Telnet through
`TikConnectionSetup.RomonAgentSetup`. A `Welcome back!` line in what a relayed session reads raises
`TikRomonRelayEndedException` and closes the connection; MAC-Telnet's reconnect after an idle logout relays to the
target again before it resends.

## 5. Capture notes

The CHR's `/tool sniffer` records only frames the CHR **transmits** for EtherType `0x88bf` — received RoMON
frames never appear, whatever `filter-direction` or `filter-interface` say. A capture of both directions has to
run on the Hyper-V host. Frame header as transmitted: byte 0 type (1 = hello to the multicast address, 2/3 =
unicast), a 2-byte total length including the Ethernet header, a 24-byte hash field (zeros with empty secrets),
then the RoMON ids.

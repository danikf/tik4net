# tik4net developer documentation

Protocol ground truth for the transports tik4net implements — what the router actually does on the
wire, established by live probing and by reading MikroTik's own clients. The source cites these
files from XML docs and comments; when the code and a document disagree, re-verify against a router
rather than assuming either is right.

This is **not** end-user documentation — that lives in the
[wiki](https://github.com/tik4net/tik4net/wiki). Roadmaps, phase plans and work logs are not kept
here either; they are the author's local working notes.

## Transport findings

| Document | Covers |
|---|---|
| [`findings-cli.md`](findings-cli.md) | RouterOS terminal/PTY layer: `print as-value`, `:put` framing, `detail`, `print stats`, VT100 negotiation, where-quoting, and why a settled prompt is not proof the router is answering *your* command (P2.47) |
| [`terminal-cli-parsing.md`](terminal-cli-parsing.md) | Parsing the CLI's as-value output into records |
| [`findings-rest-api.md`](findings-rest-api.md) | REST endpoint shapes, verb mapping, error responses, and why the router's own `listen` cannot be used (§12) |
| [`findings-mactelnet.md`](findings-mactelnet.md) | MAC-Telnet session behaviour, the shared MAC layer's cumulative ACK / retransmission rules, and the login the router refuses *after* reporting the handshake successful (P2.49) |
| [`mactelnet-protocol.md`](mactelnet-protocol.md) | MAC-Telnet wire protocol: framing, the counter/ACK rule, control packets |
| [`findings-mepty-byte-ack.md`](findings-mepty-byte-ack.md) | WinBox `mepty` counter is a cumulative **byte** ACK, not a message counter — a wrong value caps a session at ~8 KB |
| [`findings-winbox.md`](findings-winbox.md) | WinBox transport/session layer: EC-SRP5 login, stream cipher, M2 framing, error codes — incl. §13, the router refusing ~1 % of *valid* logins |
| [`findings-winbox-terminal.md`](findings-winbox-terminal.md) | WinBox CLI (terminal-over-M2) behaviour |

## WinBox native (structured M2)

| Document | Covers |
|---|---|
| [`winbox-native-m2-protocol.md`](winbox-native-m2-protocol.md) | Handler/command model, native CRUD as decoded from webfig `master.js`, streaming monitor protocol |
| [`jg-catalog-format.md`](jg-catalog-format.md) | The `.jg` catalog format (JS object literal): handlers, windows, field keys and wire types |
| [`findings-winbox-catalog.md`](findings-winbox-catalog.md) | Catalog acquisition and cross-version drift |
| [`winbox-m2-multiplexing-design.md`](winbox-m2-multiplexing-design.md) | Request/reply correlation and the channel model the async work is built on |

## Cross-transport

| Document | Covers |
|---|---|
| [`protocol-coverage.md`](protocol-coverage.md) | Which capabilities each transport actually supports, and where the gaps are |
| [`findings-router-throughput-ceiling.md`](findings-router-throughput-ceiling.md) | Sustained load hits an **aggregate** ceiling — ~1 ms round trips clamp to ~20 ms after a shared burst budget, on every transport and across all connections at once (P2.46) |

Related tooling: [`../Tools/probes/`](../Tools/probes/README.md) holds the standalone Telnet probe
and the `.jg` analyzer used to produce much of the above.

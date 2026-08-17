# tik4net (core)

The core library: the transport-neutral connection contract, every in-tree transport, and the
capability model.

| | |
|---|---|
| Target | `netstandard2.0` |
| Ships as | part of the **`tik4net`** NuGet package (with `tik4net.objects`) |
| Packable on its own | **No** — `IsPackable=false`; [`tik4net.package`](../tik4net.package/README.md) assembles the package |
| Runtime dependency | `System.Text.Json` |

## What is in here

- `ITikConnection` / `ITikCommand` — the ADO.NET-shaped contract every transport implements, plus
  `ITikRawSentenceConnection`, `ITikSafeModeConnection`, `ITikTaggedConnection` and the other
  capability-paired interfaces a transport implements only when it has the feature — see
  [ARCHITECTURE.md](../ARCHITECTURE.md#the-contract).
- `TikConnectionCapability` — the fail-closed feature-gating model. A connection that does not
  implement `ITikConnectionCapabilities` supports nothing.
- Transports: `Api/` (binary sentence protocol, the reference implementation), `Rest/`, `Telnet/`,
  `MacTelnet/`, `WinboxCli/`, `WinboxCliMac/`, `WinboxNative/`, `WinboxNativeMac/`. SSH lives in the
  separate [`tik4net.ssh`](../tik4net.ssh/README.md) satellite.
- `Connection/` — `TikCommandConnectionBase` and the shared machinery (path normalization, query
  translation, polling-based `Listen` emulation).
- `Cli/` — the command builder, output/error parsers and VT100 handling shared by every CLI transport.
- `Crypto/`, `Mndp/`, `Winbox/` — EC-SRP5 and the WinBox stream cipher, neighbour discovery, and the
  M2 catalog/codec layer.

## Before changing anything here

Read [ARCHITECTURE.md](../ARCHITECTURE.md). `Crypto/`, `WinboxNative*/`, `MacTelnet/` and
`ApiConnection`'s reader/tag multiplexing are reverse-engineered or subtle, have no deterministic test
coverage, and must only be changed with live-router verification.

Protocol ground truth — what the router actually does on the wire — is in [`Docs/`](../Docs/README.md),
and source XML docs cite those files by name.

Internals are visible to `tik4net.unittests` via `InternalsVisibleTo`, so codec-level types can be
tested without widening the public API.

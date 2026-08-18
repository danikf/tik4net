# tik4net.samples

One runnable demo app, three subcommands. Not shipped (`IsPackable=false`), not run by CI, and it needs a
router you are willing to write to.

```bash
dotnet run --project samples/tik4net.samples -- console --host 192.168.88.1 --user admin
dotnet run --project samples/tik4net.samples -- torch   --host 192.168.88.1 --interface ether1
dotnet run --project samples/tik4net.samples -- crud    --host 192.168.88.1 --transport Rest
```

| Command | Shows |
|---|---|
| `console` | Raw API sentences typed at a prompt, with every word echoed in both directions. The level to drop to when a higher layer surprises you — it answers "what does the router actually reply to this?" |
| `torch` | A streaming read (`/tool/torch`): rows arrive on a callback and the caller ends it by cancelling the loading context. Every streaming feature works this way. |
| `crud` | The O/R mapper end to end, with the outgoing words echoed — so the `/set` visibly carries **only the field that changed**, which is the mapper's most surprising property and is invisible from the result. |

Router coordinates come from `--host` / `--user` / `--pass` (or `TIK4NET_HOST` / `TIK4NET_USER` /
`TIK4NET_PASS`), and `--transport` opens any of the library's transports — the same sample over `Api`,
`Rest`, `Telnet` or `WinboxNative` is a quick way to see that the API surface really does not change.

`crud` **writes to the router**: it adds one `192.0.2.1/24` address (TEST-NET-1, never routed), edits its
comment and deletes it again in a `finally`. Point it at a lab device.

## TFM

`net8.0`, not the library's `netstandard2.0` floor. A demo is not a compatibility surface — what the
library supports is settled by its own TFM, and a sample that mirrored it would just be older code. For
the doc-snippet compile check on `net48`, see [`tik4net.examples`](../../tik4net.examples/README.md).

The three per-demo projects this replaces are recorded in
[`Docs/HISTORY.md`](../../Docs/HISTORY.md#superseded-artifacts).

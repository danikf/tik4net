# tik4net.console

Minimal .NET Framework console harness for manual, ad-hoc calls against a router.

**Non-shipping.** A scratch pad for trying something by hand, not a sample of good usage — for that see
[`tik4net.examples`](../tik4net.examples/README.md).

`ConsoleProgram.cs` opens a binary-API connection, hooks `OnReadRow`/`OnWriteRow` so the raw sentence
traffic is printed, and runs whatever is currently in `Main`. Router coordinates come from
`App.config`.

The row-event hooks are the useful part: they are the cheapest way to see the words actually exchanged
for a command. For the same view over any transport, and for byte-level tracing, use the `mikrotik`
skill's wire-tracing options instead.

For a .NET (Core) equivalent see [`tik4net.coreconsole`](../tik4net.coreconsole/README.md).

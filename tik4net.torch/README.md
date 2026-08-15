# tik4net.torch

Proof-of-concept traffic monitor built on `/tool/torch` — an early sketch of a MikroTik equivalent of
Linux `iftop`.

**Non-shipping**, and superseded: the idea grew into
[**tiktop**](https://github.com/danikf/tiktop), a separate project available on NuGet and GitHub. New
work on a traffic monitor belongs there, not here.

`ProgramTorch.cs` remains useful as a worked example of the **streaming/async command path**: it opens
a torch command, consumes rows as the router produces them, and cancels the command. Streaming
(`ExecuteListWithDuration`) is binary-API only — no other transport holds a command exchange open for a
blocking multi-row read — so this project also demonstrates the one capability that does not generalise
across transports.

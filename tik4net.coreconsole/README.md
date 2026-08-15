# tik4net.coreconsole

Minimal .NET (Core) console harness for manual, ad-hoc calls against a router — and a check that the
library works outside .NET Framework.

**Non-shipping.**

`CoreConsoleProgram.cs` is the counterpart to [`tik4net.console`](../tik4net.console/README.md): it
opens a binary-API connection and hooks `OnReadRow`/`OnWriteRow` to print the raw sentence traffic. The
difference is configuration — it uses `Microsoft.Extensions.Configuration` and a JSON settings file
rather than `App.config`.

Keeping both around is deliberate: the library targets `netstandard2.0` and is used from .NET
Framework, .NET Core/5+, Xamarin and Unity, so having a runnable host on each of the two main runtimes
catches configuration and TFM problems that a unit test would not.

For usage examples rather than a scratch pad, see [`tik4net.examples`](../tik4net.examples/README.md).

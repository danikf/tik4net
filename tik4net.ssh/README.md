# tik4net.ssh

SSH transport satellite — drives the RouterOS CLI over an SSH shell (TCP 22).

| | |
|---|---|
| Target | `netstandard2.0` |
| Ships as | the **`tik4net.ssh`** NuGet package |
| Dependencies | `tik4net` (via [`tik4net.package`](../tik4net.package/README.md)), `Renci.SshNet` |
| Capabilities | `Crud`, `Listen` (polled), `SafeMode`, `RawCommand`, `AsyncCommands` |

```bash
dotnet add package tik4net.ssh
```

## Why it is a separate package

Solely to isolate the `Renci.SshNet` dependency. Everyone who does not use SSH gets a `tik4net` package
whose only runtime dependency is `System.Text.Json`.

## Registering it

The transport plugs into the classic entry point at runtime:

```csharp
Tik4NetSsh.Register();   // then TikConnectionType.Ssh works through ConnectionFactory
```

## Notes

It is a member of the CLI transport family, so it shares the command builder, output parser and VT100
handling in `tik4net/Cli/` — a CLI-layer symptom here almost always affects Telnet, MAC-Telnet and both
WinBox CLI transports too.

`Ssh` is not exposed by the tik4net MCP server; cover it through the integration suite
(`ssh.runsettings`) instead.

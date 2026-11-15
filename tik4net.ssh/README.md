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

None needed: the first time `TikConnectionType.Ssh` is asked for, `tik4net` loads this assembly by name
(`tik4net.Ssh.Tik4NetSsh, tik4net.ssh`, visible to the trimmer) and calls `Tik4NetSsh.Register()`, which stays public
and idempotent for an application that loads assemblies in a way a name cannot find.

## Notes

Logs in with the password or a private key (`TikConnectionSetup.SshPrivateKey`, applied through
`ITikSshKeyConnection`); the key is read before connecting, so a bad one fails as itself, not as a refused login.

**The router's host key is not verified unless `TikConnectionSetup.SshHostKeyFingerprint` is set** (the
`SHA256:<base64>` of `/ip ssh print`'s `host-key-fingerprint`). Without it the session is encrypted but any host key
is accepted, so a man in the middle receives the password. With it, `SshShellClient` checks the key in SSH.NET's
`HostKeyReceived` and a mismatch is a `TikConnectionHostKeyMismatchException` before anything of the login is sent.

It is a member of the CLI transport family, so it shares the command builder, output parser and VT100
handling in `tik4net/Cli/` — a CLI-layer symptom here almost always affects Telnet, MAC-Telnet and both
WinBox CLI transports too.

`Ssh` is not exposed by the tik4net MCP server; cover it through the integration suite
(`ssh.runsettings`) instead.

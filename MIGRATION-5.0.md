# Migrating from tik4net 4.x to 5.0

5.0 is the release where the breaking changes are spent. There is one theme: **a connection no longer
carries members that only some transports can honour**, and the two defaults that were unsafe or
surprising are flipped. Everything else in 4.x still works the way it did.

Most code needs **no change at all**. The two things that stop compiling are `SendTagWithSyncCommand`
and three properties on the `Interface` entity; the two that change *behaviour* without stopping the
compiler are the certificate default and command tagging. They are first, because they are the ones
that can surprise you at run time.

If your own project builds with nullable reference types enabled, expect a third kind of change:
tik4net now ships nullability annotations, and entity properties are nullable because the router really
does leave fields out. Those are warnings in your build, not errors, and
[the section below](#nullable-reference-types-are-on-and-entity-properties-say-so) says what to do
about them.

---

## Behaviour changes — read these two

### 1. An invalid TLS certificate is now rejected

`AllowInvalidCertificate` defaults to **`false`** (was `true`) on API-SSL and REST-SSL. A RouterOS
device normally presents a **self-signed** certificate, so a connection that worked in 4.x can now fail
to open with a TLS error.

```csharp
// 4.x behaviour, stated explicitly
var setup = new TikConnectionSetup(host, user, pass) { AllowInvalidCertificate = true };
using var conn = setup.Create(TikConnectionType.ApiSsl);
```

Better than trusting everything, if you know which router you are talking to — pin its certificate:

```csharp
var setup = new TikConnectionSetup(host, user, pass)
{
    CertificateValidationCallback = (sender, cert, chain, errors) =>
        cert != null && cert.GetCertHashString() == expectedThumbprint,
};
```

The callback wins outright when set; `AllowInvalidCertificate` is then ignored. Both options reach
API-SSL and REST-SSL alike (in 4.0 the flag reached REST only).

### 2. Synchronous commands are tagged

`SendTagWithSyncCommand` defaults to **`true`** (was `false`) on the binary API. Every command —
including the login sentence — now carries a `.tag` word, and RouterOS echoes it back. This is what
makes one connection usable from several threads: without it, concurrent synchronous commands
cross-deliver rows to the wrong caller, which is a wrong *answer* rather than an error.

Nothing needs changing for it. Turn it off only if you need the 4.x bytes on the wire:

```csharp
var setup = new TikConnectionSetup(host, user, pass) { SendTagWithSyncCommand = false };
```

If you script a fake RouterOS in your own tests, **echo the tag back** — a reply that does not carry
the request's tag is now addressed to nobody and the caller waits for its receive timeout.

---

## Source changes

### `SendTagWithSyncCommand` moved to `ITikTaggedConnection`

It is a binary-API concept (`TikConnectionCapability.Tagging`); the other transports correlate replies
by their own means and implemented it as a property nothing read.

```csharp
// 4.x
connection.SendTagWithSyncCommand = true;

// 5.0 — either state it as an option before opening…
var setup = new TikConnectionSetup(host, user, pass) { SendTagWithSyncCommand = true };

// …or ask the connection whether it tags at all
if (connection is ITikTaggedConnection tagged)
    tagged.SendTagWithSyncCommand = true;
```

### Three `Interface` entity properties are read-only

`Type`, `MacAddress` and `FastPath` on `tik4net.objects.Interface` are read-only in 5.0. `/interface
set` accepts only `comment disabled l2mtu mtu name numbers` (RouterOS 7.23), so assigning them built a
command the router refuses. Assign them on the concrete interface menu instead — `InterfaceEthernet`
has a writable `MacAddress`. `Interface` gained `L2Mtu`, which `/interface set` does accept.

### Nullable reference types are on, and entity properties say so

`tik4net`, `tik4net.objects`, `tik4net.ssh` and `tik4net.testing` all build with `<Nullable>enable</Nullable>`
now. This does not change what the compiler does to *your* project — nullability annotations are only
enforced where you have opted in yourself — but if you do build with nullable enabled, you will see new
warnings wherever code dereferences a tik4net type without checking it first.

The part every consumer will notice: every mapped reference-typed property on every `[TikEntity]` class
is now nullable. This is the truthful annotation, not a formality — a RouterOS record only carries the
fields the router actually sent, a partial `.proplist` load populates fewer still, and the mapper builds
entities through a parameterless constructor, so a property can be unset at the moment you read it.

```csharp
// 4.x
public class Interface
{
    public string Id { get; private set; }
    public string Name { get; set; }
}

// 5.0
public class Interface
{
    public string? Id { get; private set; }
    public string? Name { get; set; }
}
```

If you compile with nullable enabled, this surfaces as a warning everywhere you dereference an entity
field without checking it — that is the point, not a defect. Fix it the way you would fix any nullable
warning: null-check, use `?.`/`??`, or the `!` operator where you know the field is always loaded (for
example a mandatory property right after `LoadAll`). Value-typed properties (`bool?`, `long`, enums) are
unchanged — they were never a source of `NullReferenceException` and carry no new annotation.

A handful of other public members are now annotated to match what their own XML docs always said:
`ITikCommand.ExecuteScalarOrDefault()` (all three overloads) and `ExecuteSingleRowOrDefault()`, and their
async counterparts `ExecuteScalarOrDefaultAsync`/`ExecuteSingleRowOrDefaultAsync` on `ITikCommandAsync`,
now return nullable — they document "returns null when not found" and always did return null.
`ITikConnection.OnReadRow`/`OnWriteRow` are nullable events, and `TikConnectionSetup.RouterMac`,
`TikConnectionSetup.CertificateValidationCallback`, `ITikTlsConnection.CertificateValidationCallback` and
`ITikMacLayerConnection.RouterMac` are nullable — all four were already optional in behaviour.

### `CallCommandAsync` is gone

The `[Obsolete]` low-level entry point that returned a `System.Threading.Thread` is removed; no
`Thread` appears in any public tik4net signature now. Use `ITikCommand.ExecuteAsync` for the callback
form, or the Task-based `Execute*Async` extension methods
(`TikConnectionCapability.AsyncCommands`) to await a command.

### Implementing `ITikConnection` yourself

The interface is now lifecycle, configuration and command factory. If you implement it — a custom
transport, or a hand-written test double — you no longer have to provide raw sentences or safe mode,
and you must add `ConnectTimeout`:

| Member | Where it lives in 5.0 | Capability |
|---|---|---|
| `CallCommandSync` (both overloads) | `ITikRawSentenceConnection` | `RawSentences` |
| `SafeModeTake` / `Release` / `Unroll` / `Get` | `ITikSafeModeConnection` | `SafeMode` |
| `SendTagWithSyncCommand` | `ITikTaggedConnection` | `Tagging` |
| `CallCommandAsync` | removed | — |
| `ConnectTimeout` *(new)* | `ITikConnection` | — |

**Calling** them is unchanged: extension methods keep `connection.CallCommandSync(...)` and
`connection.SafeModeTake()` working on a plain `ITikConnection`, throwing
`TikConnectionCapabilityNotSupportedException` when the transport does not have the feature — the same
exception 4.x threw at run time, from a member that is now simply absent. `SafeModeGet()` is the
exception: it answers `false` rather than throwing, so a `finally` block asking whether it holds safe
mode never fails on the way out.

`connection is ITikSafeModeConnection` and `connection.Supports(TikConnectionCapability.SafeMode)`
answer the same question; use whichever reads better.

### `tik4net.testing`

`TikFakeConnection` implements `ITikRawSentenceConnection` and `ITikSafeModeConnection`, and its
default capability set now includes `SafeMode` (clear it to test the branch where a transport has
none). It no longer has `SendTagWithSyncCommand` — it has no wire to put a tag on. Its
`CallCommandAsync` is internal, so `TikFakeCommand.ExecuteAsync` still works and nothing else calls it.

---

## New in 5.0, nothing to migrate

### The packages now multi-target `net8.0` too

`tik4net`, `tik4net.ssh` and `tik4net.testing` build for `netstandard2.0;net8.0` and every `.nupkg` carries
both `lib/netstandard2.0/` and `lib/net8.0/`. This is **not a breaking change for anyone** — `netstandard2.0`
is not going away, so .NET Framework, Xamarin and Unity consumers keep working exactly as before. A consumer
on .NET 8+ now gets the `net8.0` assembly automatically, which trims dependencies: on that TFM the packages
have **no runtime dependencies at all** (`System.Text.Json` is referenced only on the `netstandard2.0` leg,
where net8.0's shared framework already carries it).

### Streaming reads as `IAsyncEnumerable<ITikReSentence>` — net8.0 only

`ITikStreamingCommand.ExecuteListWithDurationAsync` / `ExecuteListUntilDoneAsync`, and the
`TikCommandStreamingExtensions` that reach them from a plain `ITikCommand`, are the async counterparts of
`ExecuteListWithDuration` / `ExecuteListUntilDone` — same `Streaming` capability (binary API only), same end
conditions, but rows reach the caller **as the router sends them** instead of being collected into a list
that is handed over once the read ends:

```csharp
using tik4net;   // TikCommandStreamingExtensions

var torch = connection.CreateCommand("/tool/torch",
    connection.CreateParameter("interface", "ether1"),
    connection.CreateParameter("duration", "10"));

await foreach (var row in torch.ExecuteListWithDurationAsync(10, cancellationToken))
{
    Console.WriteLine(row.GetResponseField("tx"));
}
```

A router `!trap`/`!fatal` is thrown out of the enumeration as `TikCommandAbortException` after whatever rows
already arrived. This surface only exists on the `net8.0` build — reaching `IAsyncEnumerable<T>` from
`netstandard2.0` would mean every consumer, including ones who will never call it, taking a dependency on
`Microsoft.Bcl.AsyncInterfaces`. Nothing about the existing synchronous `ExecuteListWithDuration` /
`ExecuteListUntilDone` changes.

### The binary API's word reader rents its buffers

`ApiConnection`'s word reader now rents its scratch buffer from `ArrayPool<byte>.Shared` instead of
allocating a new `byte[]` per word. Not observable from outside the library — this is a lower-allocation
implementation of the same read, not a behaviour change.

---

`TikConnectionSetup` is the single entry point and carries every connection option, including ones that
had no home before — `ReceiveTimeout`, `SendTimeout`, `Encoding`, `DebugEnabled` and `RouterMac`:

```csharp
var setup = new TikConnectionSetup(host, user, pass)
{
    ConnectTimeout = TimeSpan.FromSeconds(5),
    RouterMac = "AA:BB:CC:DD:EE:FF",     // MAC-layer transports; skips a 5 s MNDP discovery
};
using var conn = setup.Create(TikConnectionType.MacTelnet);
```

`ConnectionFactory` still works and is still supported; it simply has nowhere to state an option, which
is the reason to prefer `TikConnectionSetup` in new code. Both create connections through the same
registry, so every transport is reachable from either.

`ConnectTimeout` is on `ITikConnection` now, so it applies on every transport rather than on the six
that happened to declare it — SSH in particular used to bound its connect with `SendTimeout` and ignore
everything else the setup said.

Which option reaches which transport is a table in the wiki
([Connection types and capabilities](https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities)),
and a unit-test matrix keeps it true.

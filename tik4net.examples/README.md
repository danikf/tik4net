# tik4net.examples

Runnable usage examples for the library — the code behind the wiki's example pages.

**Non-shipping.** Not packed, not referenced by any shipping assembly.

`ProgramExamples.cs` walks the API surface: connecting, the low-level ADO.NET-like commands, the
high-level O/R mapper, and the asynchronous/monitor calls. It is linked from the project
[README](../README.md) as the example project.

`OneTaskEveryTransportExamples.cs` is the code behind the wiki page *One task on every transport and API
level*: one task — find an interface by its comment, keep its `.id`, write a new comment back — written
once per API level, plus the single method that opens any of the 11 transports.

Router coordinates come from the project's own configuration — point it at a router before running.

## Related

- [Getting started](https://github.com/danikf/tik4net/wiki/Getting-started) and
  [CRUD examples for all APIs](https://github.com/danikf/tik4net/wiki/CRUD-examples-for-all-APIs) in the wiki.
- The long-term intent is to convert these examples into unit tests; see the roadmap in the wiki.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// AssemblyVersion/FileVersion/etc. are generated from the project's <Version> property.
[assembly: ComVisible(false)]
[assembly: Guid("15288f76-5a85-418e-87b2-b22265726601")]

// Allow tik4net.integrationtests to access internal types (EcSrp5, ECPoint, WinboxStreamCrypto,
// etc.) needed by WinBox protocol PoC clients (chapters G/H) that live in the test project.
[assembly: InternalsVisibleTo("tik4net.integrationtests")]

// The router-free unit test project tests internal codecs directly (word-length encoding,
// CliOutputParser, VtStripper, M2Message, EcSrp5, ...).
[assembly: InternalsVisibleTo("tik4net.unittests")]

// Allow the satellite SSH package (separate NuGet because of its Renci.SshNet dependency)
// to reuse the shared CLI/PTY helpers (RouterOsCliLogin, Vt100State, CliOutputHelper) without
// making them public or duplicating them.
[assembly: InternalsVisibleTo("tik4net.ssh")]

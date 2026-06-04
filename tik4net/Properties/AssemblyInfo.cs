using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// AssemblyVersion/FileVersion/etc. are generated from the project's <Version> property.
[assembly: ComVisible(false)]
[assembly: Guid("15288f76-5a85-418e-87b2-b22265726601")]

// Allow tik4net.tests to access internal types (EcSrp5, ECPoint, WinboxStreamCrypto, etc.)
// needed by WinBox protocol PoC clients (chapters G/H) that live in the test project.
[assembly: InternalsVisibleTo("tik4net.tests")]

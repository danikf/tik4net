using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// AssemblyVersion/FileVersion/etc. are generated from the project's <Version> property.
[assembly: ComVisible(false)]

// The mapper's compiled accessors fall back to reflection silently when a platform refuses to bind a
// delegate, so whether the fast path is actually taken is only observable from inside the assembly.
[assembly: InternalsVisibleTo("tik4net.unittests")]
[assembly: Guid("dd32e354-19c0-4992-902b-9cdd8e53e879")]

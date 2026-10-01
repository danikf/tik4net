using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests
{
    /// <summary>Process-wide settings for the router-free tests.</summary>
    [TestClass]
    public static class TestAssemblySetup
    {
        /// <summary>
        /// No persistent CLI grammar cache for the fake routers: a CLI connection would otherwise ask each of them for its
        /// build (one command more than a test scripts) and write under the machine's %TEMP%. The cache's own tests set
        /// a directory of their own on their connection.
        /// </summary>
        [AssemblyInitialize]
        public static void Initialize(TestContext context)
        {
            CliConnectionBase.DefaultCatalogCachePath = null;
        }
    }
}

// MacTelnetProtocolTest.cs — MAC-Telnet pre-test setup
// Enables the MAC server on the router before any MAC-Telnet tests in this run.
// Actual test scenarios are covered by:
//   - ApiProtocolTest.AllTransports_Login_ListInterfaces_SetComment (DataRow MacTelnet)
//   - Full test suite via mactelnet.runsettings (TestBase-based tests)

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Configuration;

namespace tik4net.tests
{
    [TestClass]
    public class MacTelnetProtocolTest
    {
        /// <summary>
        /// Enables MAC Telnet server on the router before any MAC-Telnet tests run.
        /// Prerequisite: router must be reachable via the standard API (port 8728).
        /// </summary>
        [ClassInitialize]
        public static void EnableMacServer(TestContext _)
        {
            var host = ConfigurationManager.AppSettings["host"];
            var user = ConfigurationManager.AppSettings["user"];
            var pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var conn = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
            {
                try
                {
                    var cmd = conn.CreateCommand("/tool/mac-server/set");
                    cmd.AddParameterAndValues("allowed-interface-list", "all");
                    cmd.ExecuteNonQuery();
                    Console.WriteLine("[init] MAC server: set allowed-interface-list=all");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[init] MAC server set failed (continuing): " + ex.Message);
                }
            }
        }
    }
}

using System.Configuration;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.integrationtests
{
    /// <summary>
    /// G10: <c>/system/health</c>'s two states must read on every transport, and write on every transport
    /// that can write at all.
    /// </summary>
    /// <remarks>
    /// The audit recorded these as "API-only fields with no WinBox equivalent". The router sends both: a
    /// getall on [24,14] answers <c>0x8=bool:False 0x9=bool:True</c> against the API's
    /// <c>state=disabled state-after-reboot=enabled</c>. No .jg window names those keys — [24,14]'s windows
    /// are 'Settings' (fan control) and the x86-gated 'System Health' (voltages, temperatures, <c>caps</c>)
    /// — and the decoder drops what nothing names, so native read the path as <c>caps</c> alone.
    /// <para>The board-gated half is genuinely empty on a CHR, which has no hardware sensors. That is the
    /// part of this path that stays a difference with a reason.</para>
    /// </remarks>
    [TestClass]
    public class SystemHealthTest : TestBase
    {
        [TestMethod]
        public void HealthStateAgreesWithTheApi()
        {
            EnsureCommandAvailable("/system/health");
            var viaTransport = Connection.LoadSingle<SystemHealth>();

            string host = ConfigurationManager.AppSettings["host"];
            string user = ConfigurationManager.AppSettings["user"];
            string pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var apiConnection = ConnectionFactory.CreateConnection(TikConnectionType.Api))
            {
                apiConnection.Open(host, user, pass);
                var viaApi = apiConnection.LoadSingle<SystemHealth>();

                Assert.AreEqual(viaApi.State, viaTransport.State, "state");
                Assert.AreEqual(viaApi.StateAfterReboot, viaTransport.StateAfterReboot, "state-after-reboot");
            }
        }

        /// <summary>
        /// The write direction, toggled and verified through the API — a value written at the wrong wire
        /// type is one the router accepts, answers, and ignores (G4).
        /// </summary>
        /// <remarks>
        /// <c>state</c> is read-only on the router itself: <c>/system/health set</c> tab-completes to
        /// <c>state-after-reboot</c> and nothing else, which is also how the two keys were told apart —
        /// setting it moved 0x9 and left 0x8 alone. Only the next reboot acts on this, so toggling it
        /// changes nothing about the running router.
        /// </remarks>
        [TestMethod]
        public void HealthStateAfterRebootCanBeWritten()
        {
            EnsureCommandAvailable("/system/health");

            string host = ConfigurationManager.AppSettings["host"];
            string user = ConfigurationManager.AppSettings["user"];
            string pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var apiConnection = ConnectionFactory.CreateConnection(TikConnectionType.Api))
            {
                apiConnection.Open(host, user, pass);
                string original = apiConnection.LoadSingle<SystemHealth>().StateAfterReboot;
                string flipped = original == "enabled" ? "disabled" : "enabled";

                try
                {
                    var cmd = Connection.CreateCommandAndParameters("/system/health/set",
                        "state-after-reboot", flipped);
                    cmd.ExecuteNonQuery();

                    Assert.AreEqual(flipped, apiConnection.LoadSingle<SystemHealth>().StateAfterReboot,
                        "the transport under test wrote state-after-reboot and the router did not change it");
                }
                finally
                {
                    var restore = apiConnection.CreateCommandAndParameters("/system/health/set",
                        "state-after-reboot", original);
                    restore.ExecuteNonQuery();
                }
            }
        }
    }
}

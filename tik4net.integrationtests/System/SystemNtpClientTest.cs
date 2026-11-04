using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.integrationtests
{
    [TestClass]
    [SafeInParallelLegs]
    public class SystemNtpClientTest : TestBase
    {
        // Singleton — only a LoadSingle test (no Add/Delete).
        [TestMethod]
        public void LoadSystemNtpClientWillNotFail()
        {
            EnsureCommandAvailable("/system/ntp/client");
            var client = Connection.LoadSingle<SystemNtpClient>();
            Assert.IsNotNull(client);
        }

        /// <summary>RouterOS 6 names its servers in fields of its own; each one the menu prints is mapped.</summary>
        [TestMethod]
        [TestCategory(TestCategories.AnyRouter)]
        public void OnRouterOs6_TheServerFieldsRead()
        {
            EnsureCommandAvailable("/system/ntp/client");
            EnsureRouterOsHas<SystemNtpClient>(nameof(SystemNtpClient.PrimaryNtp));
            var client = Connection.LoadSingle<SystemNtpClient>();

            Assert.AreNotEqual(TikFieldState.Absent, client.PrimaryNtp.State, "primary-ntp");
            Assert.AreNotEqual(TikFieldState.Absent, client.SecondaryNtp.State, "secondary-ntp");
            Assert.AreNotEqual(TikFieldState.Absent, client.ServerDnsNames.State, "server-dns-names");
            Assert.AreNotEqual(TikFieldState.Unparsed, client.PollInterval.State, "poll-interval: " + client.PollInterval);
            Assert.AreNotEqual(TikFieldState.Unparsed, client.LastUpdateBefore.State, "last-update-before: " + client.LastUpdateBefore);
            Assert.AreNotEqual(TikFieldState.Unparsed, client.LastAdjustment.State, "last-adjustment: " + client.LastAdjustment);
        }
    }
}

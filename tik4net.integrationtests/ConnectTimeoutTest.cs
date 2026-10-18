using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.integrationtests
{
    /// <summary>
    /// <see cref="ITikConnection.ConnectTimeout"/> ≤ 0 means "no bound of its own" over <b>the transport under test</b>:
    /// the TCP connect is left to the OS and every later step of the open waits for the receive timeout, so the open
    /// succeeds. It used to fail at once on the API, Telnet, SSH and WinBox, whose waits took 0 as "give up now".
    /// </summary>
    [TestClass]
    [SafeInParallelLegs]
    [TestCategory(TestCategories.AnyRouter)]
    public class ConnectTimeoutTest : TestBase
    {
        [TestMethod]
        public void ConnectTimeoutZero_Opens()
        {
            var connType = ResolveConnectionType();
            var setup = LabSetup(connType);
            setup.ConnectTimeout = TimeSpan.Zero;

            using (var connection = setup.Create(connType))
            {
                Assert.AreEqual(TimeSpan.Zero, connection.ConnectTimeout, "the setup applied its ConnectTimeout");
                Assert.IsFalse(string.IsNullOrEmpty(connection.CreateCommand("/system/identity/print").ExecuteScalar("name")));
            }
        }
    }
}

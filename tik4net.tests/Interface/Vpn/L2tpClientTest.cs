using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Vpn;

namespace tik4net.tests
{
    [TestClass]
    public class L2tpClientTest : TestBase
    {
        [TestMethod]
        public void ListL2tpClientsWillNotFail()
        {
            EnsureCommandAvailable("/interface/l2tp-client");
            var list = Connection.LoadAll<L2tpClient>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddL2tpClientWillNotFail()
        {
            EnsureCommandAvailable("/interface/l2tp-client");

            string marker = Guid.NewGuid().ToString();
            string ifName = "t4ntest-l2tp" + marker.Substring(0, 8);

            var client = new L2tpClient
            {
                Name = ifName,
                ConnectTo = "192.0.2.1",   // TEST-NET, safe dummy address
                User = "testuser",
                Password = "testpass",
                Disabled = true,            // keep it inert while the test runs
                Comment = marker,
            };

            Connection.Save(client);
            try
            {
                var loaded = Connection.LoadById<L2tpClient>(client.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(marker, loaded.Comment);
                Assert.AreEqual(ifName, loaded.Name);
            }
            finally
            {
                try { Connection.Delete(client); }
                catch { /* best-effort cleanup */ }
            }
        }
    }
}

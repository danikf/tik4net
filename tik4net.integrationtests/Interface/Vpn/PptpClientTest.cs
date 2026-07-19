using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Vpn;

namespace tik4net.integrationtests
{
    [TestClass]
    public class PptpClientTest : TestBase
    {
        [TestMethod]
        public void ListPptpClientsWillNotFail()
        {
            EnsureCommandAvailable("/interface/pptp-client");
            var list = Connection.LoadAll<PptpClient>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddPptpClientWillNotFail()
        {
            EnsureCommandAvailable("/interface/pptp-client");

            string marker = Guid.NewGuid().ToString();
            string ifName = "t4ntest-pptp" + marker.Substring(0, 8);

            var client = new PptpClient
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
                var loaded = Connection.LoadById<PptpClient>(client.Id);
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

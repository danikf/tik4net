using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Vpn;

namespace tik4net.tests
{
    [TestClass]
    public class SstpClientTest : TestBase
    {
        [TestMethod]
        public void ListSstpClientsWillNotFail()
        {
            EnsureCommandAvailable("/interface/sstp-client");
            var list = Connection.LoadAll<SstpClient>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddSstpClientWillNotFail()
        {
            EnsureCommandAvailable("/interface/sstp-client");

            string marker = Guid.NewGuid().ToString();
            string ifName = "t4ntest-sstp" + marker.Substring(0, 8);

            var client = new SstpClient
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
                var loaded = Connection.LoadById<SstpClient>(client.Id);
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

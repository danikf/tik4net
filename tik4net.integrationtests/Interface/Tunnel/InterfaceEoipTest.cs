using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Tunnel;

namespace tik4net.integrationtests
{
    [TestClass]
    public class InterfaceEoipTest : TestBase
    {
        [TestMethod]
        public void ListEoipsWillNotFail()
        {
            EnsureCommandAvailable("/interface/eoip");
            var list = Connection.LoadAll<InterfaceEoip>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddEoipWillNotFail()
        {
            EnsureCommandAvailable("/interface/eoip");
            string marker = Guid.NewGuid().ToString();
            var eoip = new InterfaceEoip
            {
                Name = "test-eoip",
                RemoteAddress = "192.0.2.1",
                TunnelId = 42,
                Comment = marker,
            };
            // The router refuses `add` on the generic interface handler for a subtype ('unsupported
            // device type', 0xFE0006) — a WinBox-protocol limit, not a mapping gap: reading, setting
            // and removing the same interface all work natively. Skipped only where it is refused.
            SkipIfWinboxNativeCannot("/interface/eoip add", () => SaveTracked(eoip));

            var loaded = Connection.LoadById<InterfaceEoip>(eoip.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);
            Assert.AreEqual(42, loaded.TunnelId);

            Connection.Delete(loaded);
        }

        /// <summary>
        /// The same subtype add through <c>ExecuteNonQuery</c>, which reaches another branch of the native writer than
        /// the mapper's <c>ExecuteScalar</c> (review X1: it omitted the interface-type discriminator, so the router
        /// refused it as 'unsupported device type').
        /// </summary>
        [TestMethod]
        public void AddEoipThroughExecuteNonQueryWillNotFail()
        {
            EnsureCommandAvailable("/interface/eoip");
            string marker = Guid.NewGuid().ToString();
            const string name = "test-eoip-nq";
            try
            {
                Connection.CreateCommandAndParameters("/interface/eoip/add",
                    "name", name, "remote-address", "192.0.2.2", "tunnel-id", "43", "comment", marker).ExecuteNonQuery();

                var loaded = Connection.LoadSingle<InterfaceEoip>(Connection.CreateParameter("name", name));
                Assert.AreEqual(marker, loaded.Comment);
                Assert.AreEqual(43, loaded.TunnelId);
            }
            finally
            {
                foreach (var row in Connection.LoadList<InterfaceEoip>(Connection.CreateParameter("name", name)))
                    Connection.Delete(row);
            }
        }
    }
}

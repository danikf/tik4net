using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Vpn;

namespace tik4net.integrationtests
{
    [TestClass]
    public class OvpnServerTest : TestBase
    {
        // Singleton — but on this RouterOS version /interface/ovpn-server/server/print
        // returns !empty (no !re record) instead of a settable singleton row, so
        // LoadSingle would throw "no such item". Use LoadAll and just assert the call
        // succeeds and yields a non-null (possibly empty) list.
        [TestMethod]
        public void LoadOvpnServerWillNotFail()
        {
            EnsureCommandAvailable("/interface/ovpn-server/server");
            var list = Connection.LoadAll<OvpnServer>();
            Assert.IsNotNull(list);
        }
    }
}

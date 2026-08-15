using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface;

namespace tik4net.integrationtests
{
    [TestClass]
    public class InterfaceVrrpTest : TestBase
    {
        [TestMethod]
        public void ListVrrpsWillNotFail()
        {
            EnsureCommandAvailable("/interface/vrrp");
            var list = Connection.LoadAll<InterfaceVrrp>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddVrrpWillNotFail()
        {
            EnsureCommandAvailable("/interface/vrrp");
            string marker = Guid.NewGuid().ToString();
            var vrrp = new InterfaceVrrp
            {
                Name = "test-vrrp",
                Interface = "ether1",
                Vrid = 99,
                Comment = marker,
            };
            // The router refuses `add` on the generic interface handler for a subtype ('unsupported
            // device type', 0xFE0006) — a WinBox-protocol limit, not a mapping gap: reading, setting
            // and removing the same interface all work natively. Skipped only where it is refused.
            SkipIfWinboxNativeCannot("/interface/vrrp add", () => SaveTracked(vrrp));

            var loaded = Connection.LoadById<InterfaceVrrp>(vrrp.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);
            Assert.AreEqual(99, loaded.Vrid);

            Connection.Delete(loaded);
        }
    }
}

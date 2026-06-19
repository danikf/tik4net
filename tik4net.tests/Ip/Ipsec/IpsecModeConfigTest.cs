using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip.Ipsec;

namespace tik4net.tests
{
    [TestClass]
    public class IpsecModeConfigTest : TestBase
    {
        [TestMethod]
        public void ListIpsecModeConfigsWillNotFail()
        {
            EnsureCommandAvailable("/ip/ipsec/mode-config");
            var list = Connection.LoadAll<IpsecModeConfig>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddIpsecModeConfigWillNotFail()
        {
            EnsureCommandAvailable("/ip/ipsec/mode-config");
            // MikroTik mode-config names must be valid identifiers — strip GUID hyphens.
            string marker = "t4n" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var cfg = new IpsecModeConfig
            {
                Name = marker,
            };
            Connection.Save(cfg);

            var loaded = Connection.LoadById<IpsecModeConfig>(cfg.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Name);

            Connection.Delete(loaded);
        }
    }
}

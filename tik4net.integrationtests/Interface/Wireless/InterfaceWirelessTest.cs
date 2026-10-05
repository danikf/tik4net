using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Objects;
using tik4net.Objects.Interface.Wireless;

namespace tik4net.integrationtests
{

    [TestClass]
    public class InterfaceWirelessTest : TestBase
    {
        [TestMethod]
        public void AsyncLoad_WirelessAccessList_WillNotFail()
        {
            EnsureCapability(TikConnectionCapability.Listen, "async LoadAsync");
            EnsureCommandAvailable("/interface/wireless");
            var tmpAccessList = new WirelessAccessList()
            {
                Interface = "all",
            };
            SaveTracked(tmpAccessList);

            try
            {
                var result = new List<WirelessAccessList>();
                bool failed = false;
                var cmd = Connection.LoadWithCallback<WirelessAccessList>(
                    (item) => { lock (result) result.Add(item); },
                    (ex) => { failed = true; }
                );
                // Returns as soon as a row arrives; RouterOS 6 over a WinBox terminal takes longer than a second.
                WaitUntil(() => { lock (result) { if (result.Count > 0) return true; } return failed; }, TimeSpan.FromSeconds(10));
                cmd.CancelAndJoin();

                Assert.IsFalse(failed);
                Assert.IsTrue(result.Count > 0);
            }
            finally
            {
                Connection.Delete(tmpAccessList);
            }
        }
    }
}

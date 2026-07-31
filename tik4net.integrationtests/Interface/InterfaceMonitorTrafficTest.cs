using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using tik4net.Objects;
using tik4net.Objects.Interface;

namespace tik4net.integrationtests
{
    [TestClass]
    public class InterfaceMonitorTrafficTest : TestBase
    {
        /// <remarks>
        /// A <c>once</c> snapshot is an ordinary synchronous read, not a stream — it used to gate on
        /// <c>Streaming</c>, which only the binary API reports, so it never ran anywhere else and the
        /// CLI/REST defects P2.51 fixed went unseen. The native WinBox transport still has no handler mapping
        /// for this path and says so, which is what the skip below is bound to.
        /// </remarks>
        [TestMethod]
        public void GetTrafficSnapshotForEther1WillNotFail()
        {
            SkipIfWinboxNativeCannot("/interface/monitor-traffic", () =>
            {
                var tmp = Connection.GetInterfaceMonitorTrafficSnapshot(TestConstants.Interface);
                Assert.AreEqual(TestConstants.Interface, tmp.Name);
            });
        }

        [TestMethod]
        public void LoadTrafficSnapshotWillNotFail()
        {
            SkipIfWinboxNativeCannot("/interface/monitor-traffic", () =>
            {
                var tmp = Connection.LoadSingle<InterfaceMonitorTraffic>(
                    Connection.CreateParameter("interface", TestConstants.Interface),
                    Connection.CreateParameter("once", ""));
                Assert.AreEqual(TestConstants.Interface, tmp.Name);
            });
        }

        [TestMethod]
        public void LoadTrafficWithDurationNotFail()
        {
            EnsureCapability(TikConnectionCapability.Streaming, "monitor-traffic streaming");
            var tmp = Connection.LoadWithDuration<InterfaceMonitorTraffic>(3,
                Connection.CreateParameter("interface", TestConstants.Interface));

            Assert.IsNotNull(tmp);
            Assert.IsTrue(tmp.Count() > 0);
        }


    }
}

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
        /// CLI/REST defects P2.51 fixed went unseen. It runs on every transport now, WinBox native included
        /// (P2.52 mapped the path onto the interface window's live rate fields).
        /// </remarks>
        [TestMethod]
        public void GetTrafficSnapshotForEther1WillNotFail()
        {
            var tmp = Connection.GetInterfaceMonitorTrafficSnapshot(TestConstants.Interface);
            Assert.AreEqual(TestConstants.Interface, tmp.Name);
            Assert.IsNotNull(tmp.RxBitsPerSecond, "the reading the command exists for");
        }

        [TestMethod]
        public void LoadTrafficSnapshotWillNotFail()
        {
            var tmp = Connection.LoadSingle<InterfaceMonitorTraffic>(
                Connection.CreateParameter("interface", TestConstants.Interface),
                Connection.CreateParameter("once", ""));
            Assert.AreEqual(TestConstants.Interface, tmp.Name);
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

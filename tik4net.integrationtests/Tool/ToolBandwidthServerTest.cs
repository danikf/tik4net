using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Tool;

namespace tik4net.integrationtests
{
    [TestClass]
    public class ToolBandwidthServerTest : TestBase
    {
        [TestMethod]
        public void LoadBandwidthServerWillNotFail()
        {
            EnsureCommandAvailable("/tool/bandwidth-server");
            var settings = Connection.LoadSingle<ToolBandwidthServer>();
            Assert.IsNotNull(settings);
        }
    }
}

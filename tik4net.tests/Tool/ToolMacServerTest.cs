using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Tool.MacServer;

namespace tik4net.tests
{
    [TestClass]
    public class ToolMacServerTest : TestBase
    {
        [TestMethod]
        public void LoadMacServerWillNotFail()
        {
            EnsureCommandAvailable("/tool/mac-server");
            var settings = Connection.LoadSingle<ToolMacServer>();
            Assert.IsNotNull(settings);
        }

        [TestMethod]
        public void LoadMacServerWinboxWillNotFail()
        {
            EnsureCommandAvailable("/tool/mac-server/mac-winbox");
            var settings = Connection.LoadSingle<ToolMacServerWinbox>();
            Assert.IsNotNull(settings);
        }
    }
}

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Tool.Romon;

namespace tik4net.integrationtests
{
    [TestClass]
    public class ToolRomonTest : TestBase
    {
        [TestMethod]
        public void LoadRomonSettingsWillNotFail()
        {
            EnsureCommandAvailable("/tool/romon");
            var settings = Connection.LoadSingle<ToolRomon>();
            Assert.IsNotNull(settings);
        }

        [TestMethod]
        public void ListRomonPortsWillNotFail()
        {
            EnsureCommandAvailable("/tool/romon/port");
            var list = Connection.LoadAll<ToolRomonPort>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddRomonPortWillNotFail()
        {
            EnsureCommandAvailable("/tool/romon/port");
            string marker = Guid.NewGuid().ToString().Substring(0, 8);
            // The default "all" entry already exists → use a specific interface.
            var entry = new ToolRomonPort
            {
                Interface = "ether1",
                Forbid = false,
                Comment = marker,
            };
            Connection.Save(entry);

            var loaded = Connection.LoadById<ToolRomonPort>(entry.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);

            Connection.Delete(loaded);
        }
    }
}

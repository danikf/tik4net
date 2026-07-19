using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Tool;

namespace tik4net.integrationtests
{
    [TestClass]
    public class ToolNetwatchTest : TestBase
    {
        [TestMethod]
        public void ListNetwatchEntriesWillNotFail()
        {
            EnsureCommandAvailable("/tool/netwatch");
            SkipOnWinboxNativeUnmappedPath("/tool/netwatch");

            var list = Connection.LoadAll<ToolNetwatch>();
            Assert.IsNotNull(list);
        }

        [TestMethod]
        public void AddNetwatchEntryWillNotFail()
        {
            EnsureCommandAvailable("/tool/netwatch");
            SkipOnWinboxNativeUnmappedPath("/tool/netwatch");

            string marker = Guid.NewGuid().ToString();
            var entry = new ToolNetwatch
            {
                Host = "192.0.2.1",
                Comment = marker,
            };
            Connection.Save(entry);

            var loaded = Connection.LoadById<ToolNetwatch>(entry.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual("192.0.2.1", loaded.Host);
            Assert.AreEqual(marker, loaded.Comment);

            Connection.Delete(loaded);
        }
    }
}

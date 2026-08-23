using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Tool;

namespace tik4net.integrationtests
{
    [TestClass]
    public class ToolNetwatchTest : TestBase
    {
        // The guard here is bound to a refusal, not to the transport's name: WinBox does expose netwatch
        // (handler [51,1]), so a name-based skip took the transport out of the test on an assumption and
        // left the path uncovered on the one transport whose field mapping can be wrong.
        [TestMethod]
        public void ListNetwatchEntriesWillNotFail()
        {
            EnsureCommandAvailable("/tool/netwatch");

            SkipIfWinboxNativeCannot("/tool/netwatch", () =>
            {
                var list = Connection.LoadAll<ToolNetwatch>();
                Assert.IsNotNull(list);
            });
        }

        [TestMethod]
        public void AddNetwatchEntryWillNotFail()
        {
            EnsureCommandAvailable("/tool/netwatch");

            SkipIfWinboxNativeCannot("/tool/netwatch", () =>
            {
                string marker = Guid.NewGuid().ToString();
                var entry = new ToolNetwatch
                {
                    Host = "192.0.2.1",
                    Comment = marker,
                };
                SaveTracked(entry);

                var loaded = Connection.LoadById<ToolNetwatch>(entry.Id);
                Assert.IsNotNull(loaded);
                Assert.AreEqual("192.0.2.1", loaded.Host);
                Assert.AreEqual(marker, loaded.Comment);

                Connection.Delete(loaded);
            });
        }
    }
}

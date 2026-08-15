using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.integrationtests
{
    [TestClass]
    public class SystemLoggingTest : TestBase
    {
        // 1) List — LoadAll must not throw and must return a (possibly empty) list.
        [TestMethod]
        public void ListSystemLoggingsWillNotFail()
        {
            EnsureCommandAvailable("/system/logging");
            var list = Connection.LoadAll<SystemLogging>();
            Assert.IsNotNull(list);
        }

        // 2) Add — create, reload by id, assert round-trip fields, then delete.
        [TestMethod]
        public void AddSystemLoggingWillNotFail()
        {
            EnsureCommandAvailable("/system/logging");
            var entity = new SystemLogging
            {
                Topics = "info",
                Action = "memory",
                Prefix = "t4n-test",
            };
            // A list/array field (topics) is not yet encodable over native WinBox M2 writes; the resolver
            // says so explicitly. Reading the same table works — skip only the write, only where refused.
            SkipIfWinboxNativeCannot("/system/logging add", () => SaveTracked(entity));

            var loaded = Connection.LoadById<SystemLogging>(entity.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual("info", loaded.Topics);
            Assert.AreEqual("memory", loaded.Action);
            Assert.AreEqual("t4n-test", loaded.Prefix);

            Connection.Delete(loaded);
        }
    }
}

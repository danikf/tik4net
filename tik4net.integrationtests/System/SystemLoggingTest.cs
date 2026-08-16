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
            // topics is a multitristatearray — one API list ("info", or "info,!debug") riding as TWO u32[]
            // keys, the plain members and the negated ones. It encodes and decodes over native since A4; the
            // guard stays because it is feature-bound, so it costs nothing while the feature works and would
            // name the transport again if it regressed.
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

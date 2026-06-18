using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.tests
{
    [TestClass]
    public class SystemLoggingActionTest : TestBase
    {
        // 1) List — LoadAll must not throw and must return a non-null list.
        [TestMethod]
        public void ListSystemLoggingActionsWillNotFail()
        {
            EnsureCommandAvailable("/system/logging/action");
            var list = Connection.LoadAll<SystemLoggingAction>();
            Assert.IsNotNull(list);
        }

        // 2) Add — create a memory action, reload by id, assert name, delete.
        [TestMethod]
        public void AddSystemLoggingActionWillNotFail()
        {
            EnsureCommandAvailable("/system/logging/action");
            string marker = Guid.NewGuid().ToString();
            var entity = new SystemLoggingAction
            {
                Name = "t4ntest" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Target = SystemLoggingAction.LoggingTarget.Memory,
                MemoryLines = 100,
                Comment = marker,
            };
            Connection.Save(entity);

            var loaded = Connection.LoadById<SystemLoggingAction>(entity.Id);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(marker, loaded.Comment);

            Connection.Delete(loaded);
        }
    }
}

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.tests
{
    [TestClass]
    public class SystemNoteTest : TestBase
    {
        [TestMethod]
        public void LoadSystemNoteWillNotFail()
        {
            EnsureCommandAvailable("/system/note");
            var note = Connection.LoadSingle<SystemNote>();
            Assert.IsNotNull(note);
        }
    }
}

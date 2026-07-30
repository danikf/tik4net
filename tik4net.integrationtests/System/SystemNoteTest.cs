using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.integrationtests
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

        /// <summary>
        /// A second singleton write, on a different handler and with a bool alongside the string, so the
        /// singleton path (P2.44) is covered by more than <c>/system/identity</c> — which is special-cased in
        /// the WinBox field resolver and would otherwise be the only thing proving the path at all.
        /// </summary>
        [TestMethod]
        public void SaveSystemNote_RoundTrip_WillWork()
        {
            EnsureCommandAvailable("/system/note");
            var original = Connection.LoadSingle<SystemNote>();
            string originalText = original.Note;
            bool originalShowAtLogin = original.ShowAtLogin;
            string tempText = "tik4net test note " + Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                original.Note = tempText;
                original.ShowAtLogin = !originalShowAtLogin;
                Connection.Save(original);

                var reloaded = Connection.LoadSingle<SystemNote>();
                Assert.AreEqual(tempText, reloaded.Note);
                Assert.AreEqual(!originalShowAtLogin, reloaded.ShowAtLogin);
            }
            finally
            {
                var restore = Connection.LoadSingle<SystemNote>();
                restore.Note = originalText;
                restore.ShowAtLogin = originalShowAtLogin;
                Connection.Save(restore);
            }
        }
    }
}

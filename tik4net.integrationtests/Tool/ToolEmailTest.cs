using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Tool;

namespace tik4net.integrationtests
{
    [TestClass]
    public class ToolEmailTest : TestBase
    {
        // Singleton — LoadSingle must not throw and must return a non-null result.
        [TestMethod]
        public void LoadToolEmailWillNotFail()
        {
            EnsureCommandAvailable("/tool/e-mail");
            var email = Connection.LoadSingle<ToolEmail>();
            Assert.IsNotNull(email);
        }

        // RouterOS 6 calls the SMTP server `address`, RouterOS 7 `server`, and each refuses the other's name — so
        // the save must go out under the name the settings were read under. A TEST-NET address, not the default,
        // so the read-back cannot be satisfied by the default fill.
        [TestMethod]
        public void TheServerRoundTripsUnderTheRoutersOwnName()
        {
            EnsureCommandAvailable("/tool/e-mail");
            var email = Connection.LoadSingle<ToolEmail>();
            string original = email.Server;
            try
            {
                email.Server = "192.0.2.25";
                Connection.Save(email);

                Assert.AreEqual("192.0.2.25", Connection.LoadSingle<ToolEmail>().Server);
            }
            finally
            {
                var restore = Connection.LoadSingle<ToolEmail>();
                restore.Server = original;
                Connection.Save(restore);
            }
        }
    }
}

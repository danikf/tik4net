using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// The console flags a MAC-Telnet login adds (<see cref="RouterOsCliLogin.TerminalLoginFlags"/>) go into the login
    /// name's one options group: RouterOS reads a single '+' group, and 'admin+t+c' logs in with the console in colour
    /// (measured on 7.25, 7.21.5 and 6.49.21 — WinBox + MAC review, F1).
    /// </summary>
    [TestClass]
    public class TerminalLoginFlagsTests
    {
        [TestMethod]
        public void ABareName_GetsAGroupOfItsOwn() => Assert.AreEqual("admin+c", RouterOsCliLogin.WithTerminalLoginFlags("admin"));

        [TestMethod]
        public void ANameWithOptions_GetsTheFlagsMergedIntoItsGroup()
            => Assert.AreEqual("admin+tc", RouterOsCliLogin.WithTerminalLoginFlags("admin+t"));

        [TestMethod]
        public void AFlagTheCallerAlreadyWrote_IsNotRepeated()
            => Assert.AreEqual("admin+ct", RouterOsCliLogin.WithTerminalLoginFlags("admin+ct"));
    }
}

using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;
using tik4net.Ssh;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// <see cref="ITikConnection.DebugEnabled"/> starts as <see cref="Debugger.IsAttached"/> on every transport, as its
    /// XML docs and the wiki say.
    /// </summary>
    /// <remarks>
    /// Without a debugger this passes on the old code too, where only the API and REST set it: the difference shows only
    /// when the test itself runs under a debugger, and there it was red on the old code for the nine other transports.
    /// </remarks>
    [TestClass]
    public class DebugEnabledDefaultTests
    {
        [TestMethod]
        public void EveryTransportStartsWithDebuggerIsAttached()
        {
            Tik4NetSsh.Register();
            foreach (var type in Enum.GetValues(typeof(TikConnectionType)).Cast<TikConnectionType>())
            {
                using (var conn = ConnectionFactory.CreateConnection(type))
                    Assert.AreEqual(Debugger.IsAttached, conn.DebugEnabled, type.ToString());
            }
        }

        [TestMethod]
        public void AnExplicitSettingWins()
        {
            using (var conn = ConnectionFactory.CreateConnection(TikConnectionType.Telnet))
            {
                conn.DebugEnabled = !Debugger.IsAttached;
                Assert.AreEqual(!Debugger.IsAttached, conn.DebugEnabled);
            }
        }
    }
}

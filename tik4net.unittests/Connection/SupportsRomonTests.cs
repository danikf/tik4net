using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Ssh;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// <see cref="TikConnectionSetup.SupportsRomon"/> answers from the transport table: it builds no connection to ask,
    /// so it answers for SSH whether or not <c>tik4net.ssh</c> has registered its factory.
    /// </summary>
    [TestClass]
    public class SupportsRomonTests
    {
        // Counts the SSH connections built on this thread only: other test classes run in parallel and may build some.
        [ThreadStatic] private static int _built;

        [TestMethod]
        public void SupportsRomon_BuildsNoConnectionToAnswer()
        {
            ConnectionFactory.RegisterConnectionFactory(TikConnectionType.Ssh, () => { _built++; return new SshConnection(); });
            try
            {
                _built = 0;
                Assert.IsTrue(TikConnectionSetup.SupportsRomon(TikConnectionType.Ssh));
                Assert.AreEqual(0, _built, "SupportsRomon built a connection to answer");
            }
            finally
            {
                Tik4NetSsh.Register();
            }
        }

        [TestMethod]
        public void SupportsRomon_OfAnUndeclaredValue_Throws()
            => Assert.ThrowsException<ArgumentOutOfRangeException>(() => TikConnectionSetup.SupportsRomon((TikConnectionType)9999));
    }
}

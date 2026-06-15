using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.tests
{
    /// <summary>
    /// Assembly-wide test setup. Registers satellite transports (SSH lives in tik4net.ssh, which core
    /// cannot reference) so the generic suite can reach them via
    /// <see cref="ConnectionFactory.CreateConnection"/> when <c>tik.connectionType=Ssh</c> is selected
    /// through a runsettings file.
    /// </summary>
    [TestClass]
    public static class TestAssemblyInit
    {
        [AssemblyInitialize]
        public static void Init(TestContext context)
        {
            tik4net.Ssh.Tik4NetSsh.Register();
        }
    }
}

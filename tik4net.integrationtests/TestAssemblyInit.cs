using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.integrationtests
{
    /// <summary>
    /// Assembly-wide test setup. Registers satellite transports (SSH lives in tik4net.ssh, which core
    /// cannot reference) so the generic suite can reach them via
    /// <see cref="ConnectionFactory.CreateConnection"/> when <c>tik.connectionType=Ssh</c> is selected
    /// through a runsettings file, then sweeps any leftover test residue off the router so a prior run's
    /// orphans cannot collide with this one (see <see cref="RouterOrphanCleaner"/>).
    /// </summary>
    [TestClass]
    public static class TestAssemblyInit
    {
        [AssemblyInitialize]
        public static void Init(TestContext context)
        {
            tik4net.Ssh.Tik4NetSsh.Register();

            // Opt-in (TIK4NET_WIRETRACE), no-op otherwise. Some defects only surface in a full run over the
            // long-lived shared connection, where the MCP tool's per-call trace cannot reach them.
            WireTraceCapture.StartIfRequested();

            // Always over the API, once, before any test — clears conflicts left by a killed run or by a CLI
            // add that created a row without yielding its .id (which per-test teardown then cannot delete).
            // It also takes this run's lease, which is what keeps a parallel leg's sweep off this run's rows.
            string leg = context?.Properties?["tik.connectionType"] as string;
            RouterOrphanCleaner.PurgeTestResidue(string.IsNullOrEmpty(leg) ? LabConfig.Get("connectionType") ?? "Api" : leg);
        }

        [AssemblyCleanup]
        public static void Cleanup()
        {
            // Tear down the connection shared across the TestBase suite (see TestBase.ReuseConnectionAcrossTests).
            TestBase.DisposeSharedConnection();
            RouterOrphanCleaner.PurgeOwnResidue();
            WireTraceCapture.Stop();
        }
    }
}

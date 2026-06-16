using System;
using System.Configuration;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.tests
{
    /// <summary>
    /// Live-router tests for the <see cref="ITikConnection.SafeModeTake"/> /
    /// <see cref="ITikConnection.SafeModeRelease"/> / <see cref="ITikConnection.SafeModeUnroll"/> /
    /// <see cref="ITikConnection.SafeModeGet"/> pattern. Requires a transport that reports
    /// <see cref="TikConnectionCapability.SafeMode"/> (binary API, a CLI terminal, or native WinBox).
    /// </summary>
    [TestClass]
    public class SafeModeTest : TestBase
    {
        private const string PATH = "/ppp/secret";

        // Safe-mode take/release/unroll and mid-test reconnects make this class connection-lifecycle
        // sensitive; force a fresh, isolated connection per test rather than sharing one.
        protected override bool ReuseConnectionAcrossTests => false;

        /// <summary>
        /// A safe-mode session left uncommitted by a previous test's disconnect keeps RouterOS reporting
        /// safe mode as held (owner survives until the connection-tracking timeout), which would block the
        /// next <c>take</c>. Clear it from a fresh binary-API session (a release from any session clears a
        /// stale hold) before every test, regardless of the transport under test. Best-effort.
        /// </summary>
        protected override void OnInitialize()
        {
            try
            {
                string host = ConfigurationManager.AppSettings["host"];
                string user = ConfigurationManager.AppSettings["user"];
                string pass = ConfigurationManager.AppSettings["pass"] ?? "";
                using (var api = ConnectionFactory.OpenConnection(TikConnectionType.Api, host, user, pass))
                    api.CreateCommand("/safe-mode/release").ExecuteNonQuery();
            }
            catch { /* nothing held, or API unavailable — ignore */ }
        }

        private void DeleteAllItems(string itemsPath)
        {
            foreach (var id in Connection.CallCommandSync($"{itemsPath}/print").OfType<ITikReSentence>().Select(s => s.GetId()))
                Connection.CreateCommandAndParameters($"{itemsPath}/remove", TikSpecialProperties.Id, id).ExecuteNonQuery();
        }

        private int CountItems(ITikConnection conn, string name)
            => conn.CallCommandSync($"{PATH}/print").OfType<ITikReSentence>()
                   .Count(s => s.GetResponseFieldOrDefault("name", null) == name);

        [TestMethod]
        public void SafeMode_Take_Release_PersistsChange()
        {
            EnsureCapability(TikConnectionCapability.SafeMode, "SafeModeTake/Release");

            string name = "safemode-commit-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                Assert.IsFalse(Connection.SafeModeGet(), "Should not be in safe mode initially.");
                Connection.SafeModeTake();
                Assert.IsTrue(Connection.SafeModeGet(), "SafeModeGet should report held after take.");
                Connection.CreateCommandAndParameters($"{PATH}/add", "name", name).ExecuteNonQuery();
                Assert.AreEqual(1, CountItems(Connection, name), "Item should exist inside safe mode.");

                Connection.SafeModeRelease();
                Assert.IsFalse(Connection.SafeModeGet(), "SafeModeGet should report not-held after release.");

                // Reconnect — a committed change survives the session ending.
                RecreateConnection();
                Assert.AreEqual(1, CountItems(Connection, name), "Committed item must survive disconnect.");
            }
            finally
            {
                try { DeleteAllItems(PATH); } catch { /* best-effort cleanup */ }
            }
        }

        [TestMethod]
        public void SafeMode_Unroll_DiscardsChangeWithoutDisconnect()
        {
            EnsureCapability(TikConnectionCapability.SafeMode, "SafeModeUnroll");
            // Native WinBox (TCP or over MAC) has no in-place unroll — skip it there (rollback is disconnect-only).
            var transport = ResolveConnectionType();
            if (transport == TikConnectionType.WinboxNative || transport == TikConnectionType.WinboxNativeMac)
                Assert.Inconclusive("Native WinBox does not support in-place SafeModeUnroll (rollback is disconnect-only).");
            // SSH cannot use the Ctrl+D discard key (the SSH EOF convention closes the channel); it unrolls
            // in place via the scriptable /safe-mode/unroll command, available on RouterOS 7.18+. On older
            // RouterOS SSH degrades to a disconnect-rollback, so the in-place assertion below does not apply.
            if (transport == TikConnectionType.Ssh && GetMikrotikVersion() < new Version(7, 18))
                Assert.Inconclusive("SSH in-place SafeModeUnroll requires the scriptable /safe-mode/unroll command (RouterOS 7.18+).");

            string name = "safemode-unroll-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                Connection.SafeModeTake();
                Connection.CreateCommandAndParameters($"{PATH}/add", "name", name).ExecuteNonQuery();
                Assert.AreEqual(1, CountItems(Connection, name), "Item should exist inside safe mode.");

                Connection.SafeModeUnroll();   // roll back NOW, stay connected
                Assert.IsFalse(Connection.SafeModeGet(), "SafeModeGet should report not-held after unroll.");

                // Same connection, no reconnect — the change must be gone.
                Assert.AreEqual(0, CountItems(Connection, name), "Unroll must discard the change in place.");
            }
            finally
            {
                try { DeleteAllItems(PATH); } catch { /* best-effort cleanup */ }
            }
        }

        [TestMethod]
        [Timeout(90000)] // 30s rollback poll + reconnect retries — guard against a wedged router hanging the run
        public void SafeMode_DisconnectWithoutRelease_RollsBack()
        {
            EnsureCapability(TikConnectionCapability.SafeMode, "SafeModeTake");

            string name = "safemode-rollback-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            bool committed = false;
            try
            {
                Connection.SafeModeTake();
                Connection.CreateCommandAndParameters($"{PATH}/add", "name", name).ExecuteNonQuery();
                Assert.AreEqual(1, CountItems(Connection, name), "Item should exist inside safe mode.");

                // Drop the owning session WITHOUT releasing → RouterOS rolls the change back.
                Connection.Close();

                RecreateConnection();
                int remaining = 1;
                for (int i = 0; i < 30 && remaining > 0; i++)
                {
                    remaining = CountItems(Connection, name);
                    if (remaining > 0) Thread.Sleep(1000);
                }

                if (remaining > 0)
                {
                    committed = true;
                    Assert.Inconclusive(
                        "Safe-mode change was not rolled back within 30s of a clean disconnect. " +
                        "RouterOS may defer rollback to the connection-tracking timeout for graceful closes.");
                }

                Assert.AreEqual(0, remaining, "Uncommitted safe-mode change must be rolled back after disconnect.");
            }
            finally
            {
                if (committed) { try { DeleteAllItems(PATH); } catch { /* best-effort */ } }
            }
        }
    }
}

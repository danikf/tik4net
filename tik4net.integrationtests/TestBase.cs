using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;   // Save/Delete O/R mapper extensions used by SaveTracked

namespace tik4net.integrationtests
{
    public class TestBase
    {
        private ITikConnection _connection;
        private Version _routerOsVersion;

        // Process-wide connection shared across all tests that opt into reuse (see ReuseConnectionAcrossTests).
        // The suite runs single-threaded (no [Parallelize]), so one cached connection is safe. Disposed at
        // assembly cleanup (TestAssemblyInit), or dropped/re-opened when a test fails or closes it.
        private static ITikConnection _sharedConnection;
        private static TikConnectionType _sharedConnectionType;
        private static readonly object _sharedConnectionLock = new object();

        /// <summary>MSTest injects this for access to runsettings parameters.</summary>
        public TestContext TestContext { get; set; }

        protected ITikConnection Connection
        {
            get { return _connection; }
        }

        /// <summary>
        /// When true (the default), every test in the class reuses one router connection — created lazily
        /// and cached for the whole run — instead of opening and logging in a fresh one per test. This is a
        /// large speed win on transports with expensive setup (SSL handshake, MAC-layer MNDP discovery,
        /// WinBox-native catalog fetch). The shared connection self-heals: it is re-opened on the next test
        /// if a test closes it, calls <see cref="RecreateConnection"/>, or fails (a failed test may have left
        /// an async/listen command running, so its connection is discarded rather than reused).
        /// Connection-lifecycle-sensitive classes (e.g. <c>SafeModeTest</c>) override this to false to get a
        /// guaranteed-isolated connection per test.
        /// <para>
        /// <b>Every transport reuses, including the MAC-layer ones.</b> The native WinBox-over-MAC transport
        /// (<see cref="TikConnectionType.WinboxNativeMac"/>) used to be excluded, on the theory that the lossy
        /// UDP MAC layer's cumulative-byte ACK/retransmit state could not survive an arbitrary sequence of
        /// unrelated exchanges on one channel — a prior multi-frame test leaving the next test's first
        /// <c>getall</c> unanswered. That exclusion was retired in P2.42 (2026-07-30) after the symptom failed
        /// to reproduce: five full <c>winboxnativemac</c> runs with reuse on (two on 2026-07-29 plus three
        /// confirming runs) came back green on every test using the shared connection. The cause it was written
        /// for had already been removed by the MAC-layer cumulative-ACK correction (P2.19) and the broadcast-NIC
        /// binding fix (P2.36); the exclusion outlived it and merely cost one fresh MAC session per test, which
        /// is the session churn P2.35 measured on <c>/user/active</c>.
        /// </para>
        /// </summary>
        protected virtual bool ReuseConnectionAcrossTests => true;

        [TestInitialize]
        public void Init()
        {
            if (WireTraceCapture.IsEnabled)
                WireTraceCapture.MarkTest("TEST", TestContext?.TestName);
            if (ReuseConnectionAcrossTests)
                AcquireSharedConnection();
            else
                RecreateConnection();
            OnInitialize();
        }

        protected virtual void OnInitialize()
        {
            // dummy
        }

        // Entities created by the running test, newest first. Drained in Cleanup so a failed assert cannot
        // leave objects behind on the router.
        private readonly Stack<Action> _createdEntities = new Stack<Action>();

        /// <summary>
        /// Saves <paramref name="entity"/> and registers it for guaranteed deletion when the test ends,
        /// whether it passes, fails or throws.
        /// </summary>
        /// <remarks>
        /// <para>Use this instead of a bare <c>Connection.Save(...)</c> for anything the test creates. The
        /// natural shape — save, assert, delete — silently leaks on the <i>failure</i> path, which is exactly
        /// when someone is looking at the router. Those leftovers are not just clutter: orphaned
        /// <c>/interface/list/member</c> rows whose parent list had been deleted made later runs of
        /// <c>AddInterfaceListMemberWillNotFail</c> fail against unrelated records, which reads as a
        /// transport bug.</para>
        /// <para>Deletion is last-in-first-out so children go before their parents (a member before its
        /// list), and failures are swallowed — the test's own outcome must not be masked by teardown, and a
        /// test that already deleted the entity itself is the normal case, not an error.</para>
        /// </remarks>
        protected T SaveTracked<T>(T entity) where T : new()
        {
            // Registered BEFORE the Save, deliberately. On the CLI transports 'add' can answer without the
            // new .id (mikrotik-tests skill, gotcha A) — Save then THROWS even though the row was created on
            // the router. Registering afterwards leaves exactly those rows behind, and they are the worst
            // kind: the next transport's run fails with 'already have interface with name test-eoip' against
            // a record it never created, which reads as a transport bug. Verified live — one orphaned
            // test-eoip made AddEoipWillNotFail fail on the binary API, where nothing is broken at all.
            _createdEntities.Push(() => DeleteCreated(entity));
            Connection.Save(entity);
            return entity;
        }

        /// <summary>
        /// Deletes an entity registered by <see cref="SaveTracked{T}"/>. Falls back to an identity sweep when
        /// the entity has no <c>.id</c> — see <see cref="SaveTracked{T}"/> for why that case is real.
        /// </summary>
        private void DeleteCreated<T>(T entity) where T : new()
        {
            var metadata = TikEntityMetadataCache.GetMetadata<T>();

            if (metadata.HasIdProperty
                && !string.IsNullOrEmpty(metadata.IdProperty.GetEntityValue(entity)))
            {
                Connection.Delete(entity);
                return;
            }

            // No .id — the Save failed before it learned one. The row may still exist, so find it by the
            // identifying fields this test set (name/comment; the tests stamp a GUID comment precisely so a
            // leftover is attributable) and remove it. Matching only on non-empty values keeps this from
            // touching anything the test did not create.
            foreach (var candidate in FindByIdentity<T>(entity, metadata))
            {
                try { Connection.Delete(candidate); }
                catch { /* best effort — see DeleteTrackedEntities */ }
            }
        }

        private IEnumerable<T> FindByIdentity<T>(T entity, TikEntityMetadata metadata) where T : new()
        {
            var identityFields = metadata.Properties
                .Where(p => p.FieldName == "name" || p.FieldName == "comment")
                .Select(p => new { p.FieldName, Value = p.GetEntityValue(entity) })
                .Where(f => !string.IsNullOrEmpty(f.Value))
                .ToList();

            // Nothing distinguishing to match on — better to leak than to delete by guesswork.
            if (identityFields.Count == 0)
                return Enumerable.Empty<T>();

            return Connection.LoadAll<T>()
                .Where(candidate => identityFields.All(f =>
                    string.Equals(
                        metadata.Properties.First(p => p.FieldName == f.FieldName).GetEntityValue(candidate),
                        f.Value,
                        StringComparison.Ordinal)))
                .ToList();
        }

        /// <summary>
        /// Registers an already-created <paramref name="entity"/> for guaranteed deletion, for the cases
        /// where the entity is not created through <see cref="SaveTracked{T}"/>.
        /// </summary>
        protected T TrackForCleanup<T>(T entity) where T : new()
        {
            _createdEntities.Push(() => Connection.Delete(entity));
            return entity;
        }

        private void DeleteTrackedEntities()
        {
            while (_createdEntities.Count > 0)
            {
                var delete = _createdEntities.Pop();
                try { delete(); }
                catch { /* already deleted by the test, or the connection is gone — never mask the outcome */ }
            }
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (WireTraceCapture.IsEnabled)
                WireTraceCapture.MarkTest("END " + TestContext?.CurrentTestOutcome, TestContext?.TestName);
            try
            {
                DeleteTrackedEntities();
                OnCleanup();
            }
            finally
            {
                if (ReuseConnectionAcrossTests)
                {
                    // A failed test may have left a live async/listen command (assert-before-CancelAndJoin)
                    // or otherwise corrupted the session — discard the shared connection so the next test
                    // starts clean. A passed test keeps the connection cached for reuse.
                    bool passed = TestContext == null || TestContext.CurrentTestOutcome == UnitTestOutcome.Passed;
                    if (!passed)
                        DisposeSharedConnection();
                }
                else
                {
                    _connection?.Dispose();
                }
                _connection = null;
            }
        }

        protected virtual void OnCleanup()
        {
            // dummy
        }

        /// <summary>
        /// Returns the cached shared connection (opening it on first use, or re-opening it if a previous
        /// test closed it or the transport changed) and points <see cref="Connection"/> at it.
        /// </summary>
        private void AcquireSharedConnection()
        {
            TikConnectionType connType = ResolveConnectionType();
            lock (_sharedConnectionLock)
            {
                if (_sharedConnection == null || _sharedConnectionType != connType || !_sharedConnection.IsOpened)
                {
                    _sharedConnection?.Dispose();
                    _sharedConnection = OpenNewConnection();
                    _sharedConnectionType = connType;
                }
                _connection = _sharedConnection;
            }
            _connection.DebugEnabled = true;
            _routerOsVersion = null;
        }

        /// <summary>Disposes the process-wide shared connection. Called from the assembly cleanup hook.</summary>
        internal static void DisposeSharedConnection()
        {
            lock (_sharedConnectionLock)
            {
                _sharedConnection?.Dispose();
                _sharedConnection = null;
            }
        }

        /// <summary>
        /// Resolves the transport type to use. Priority:
        /// 1. runsettings parameter  tik.connectionType
        /// 2. App.config key         connectionType
        /// 3. Default                Api
        /// </summary>
        protected TikConnectionType ResolveConnectionType()
        {
            string raw = null;

            // 1. runsettings / TestContext
            if (TestContext?.Properties != null && TestContext.Properties.Contains("tik.connectionType"))
                raw = TestContext.Properties["tik.connectionType"] as string;

            // 2. App.config
            if (string.IsNullOrEmpty(raw))
                raw = ConfigurationManager.AppSettings["connectionType"];

            // 3. Default
            if (string.IsNullOrEmpty(raw))
                raw = "Api";

            return (TikConnectionType)Enum.Parse(typeof(TikConnectionType), raw, ignoreCase: true);
        }

        protected void RecreateConnection(int retryTimeoutSeconds = 20)
        {
            var conn = OpenNewConnection(retryTimeoutSeconds);
            _connection = conn;

            // In reuse mode a test that asks for a fresh connection (after Close/reboot) makes that fresh
            // connection the new shared one, so subsequent tests inherit a healthy session.
            if (ReuseConnectionAcrossTests)
            {
                lock (_sharedConnectionLock)
                {
                    if (!ReferenceEquals(_sharedConnection, conn))
                        _sharedConnection?.Dispose();
                    _sharedConnection = conn;
                    _sharedConnectionType = ResolveConnectionType();
                }
            }

            _routerOsVersion = null;
        }

        /// <summary>
        /// Opens an <b>additional</b> connection on the transport under test, for a test that must act on
        /// the router while a monitor or listen owns <see cref="Connection"/>. Caller disposes.
        /// </summary>
        /// <remarks>
        /// The CLI family emulates listen/monitor by polling a single request/reply terminal, and that
        /// worker owns the channel for as long as it runs — issuing CRUD on the same connection meanwhile
        /// is explicitly not supported (<c>CliConnectionBase.RunMonitorAsync</c>). The binary API
        /// multiplexes by tag and tolerates it, which is exactly why a one-connection listen test passes
        /// there and is a lottery over Telnet and WinBox CLI (P2.14).
        /// </remarks>
        protected ITikConnection OpenSecondaryConnection() => OpenNewConnection();

        /// <summary>Opens a brand-new connection to the router for the resolved transport, with retry.</summary>
        private ITikConnection OpenNewConnection(int retryTimeoutSeconds = 20)
        {
            string host = ConfigurationManager.AppSettings["host"];
            string user = ConfigurationManager.AppSettings["user"];
            string pass = ConfigurationManager.AppSettings["pass"] ?? "";

            TikConnectionType connType = ResolveConnectionType();

            var deadline = DateTime.UtcNow.AddSeconds(retryTimeoutSeconds);
            Exception lastException;
            do
            {
                try
                {
                    var conn = ConnectionFactory.CreateConnection(connType);
                    ApplyRouterMac(conn);   // MAC-layer transports: bypass MNDP using App.config routerMac
                    conn.Open(host, user, pass);
                    conn.DebugEnabled = true;
                    return conn;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Thread.Sleep(1000);
                }
            } while (DateTime.UtcNow < deadline);

            throw new Exception($"Could not connect to router at {host} via {connType} within {retryTimeoutSeconds}s.", lastException);
        }

        /// <summary>
        /// For the MAC-layer transports (MacTelnet / WinboxCliMac / WinboxNativeMac), sets the
        /// <c>RouterMac</c> from the App.config <c>routerMac</c> key so the generic suite can connect
        /// without MNDP discovery (which is environment-sensitive). No-op for IP transports.
        /// </summary>
        private static void ApplyRouterMac(ITikConnection conn)
        {
            string mac = ConfigurationManager.AppSettings["routerMac"];
            if (string.IsNullOrEmpty(mac))
                return;

            switch (conn)
            {
                case tik4net.MacTelnet.MacTelnetConnection mt: mt.RouterMac = mac; break;
                case tik4net.WinboxCliMac.WinboxCliMacConnection wm: wm.RouterMac = mac; break;
                case tik4net.WinboxNativeMac.WinboxNativeMacConnection nm: nm.RouterMac = mac; break;
            }
        }

        /// <summary>
        /// Marks the test as Inconclusive when the active transport does not support the given capability.
        /// </summary>
        protected void EnsureCapability(TikConnectionCapability cap, string feature = null)
        {
            if (!_connection.Supports(cap))
            {
                string transportName = ResolveConnectionType().ToString();
                string msg = $"Transport '{transportName}' does not support {cap}"
                             + (feature != null ? $" ({feature})" : "")
                             + " — test skipped.";
                Assert.Inconclusive(msg);
            }
        }

        /// <summary>
        /// Blocks until <paramref name="condition"/> holds or the timeout elapses, and reports which.
        /// </summary>
        /// <remarks>
        /// Prefer this over a fixed <see cref="Thread.Sleep(int)"/> whenever a test waits on the router.
        /// A constant budget that is generous over the binary API is not generous over Telnet or a WinBox
        /// terminal, nor over an API connection on a router that has already served a few hundred tests —
        /// and that mismatch is what produces a test which passes alone and fails in the suite (P2.14,
        /// P2.21). This only waits *longer*; the assertion after it is unchanged, so a router that never
        /// satisfies the condition still fails the test.
        /// </remarks>
        protected static bool WaitUntil(Func<bool> condition, int timeoutSeconds = 20, int pollMs = 250)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (true)
            {
                if (condition()) return true;
                if (DateTime.UtcNow >= deadline) return false;
                Thread.Sleep(pollMs);
            }
        }

        /// <summary>
        /// Returns the RouterOS version read from /system/resource.
        /// Result is cached for the lifetime of the test (one connection).
        /// </summary>
        protected Version GetMikrotikVersion()
        {
            if (_routerOsVersion != null)
                return _routerOsVersion;

            var sentence = _connection.CreateCommand("/system/resource/print").ExecuteSingleRow();
            var raw = sentence.GetResponseField("version"); // e.g. "7.21.4 (stable)"
            var versionPart = raw.Split(' ')[0];            // "7.21.4"
            _routerOsVersion = Version.Parse(versionPart);
            return _routerOsVersion;
        }

        /// <summary>
        /// Marks the test as inconclusive when the router version is below <paramref name="minimumMajor"/>.
        /// Use for features introduced in a specific major RouterOS version.
        /// </summary>
        protected void EnsureMinRouterOsVersion(int minimumMajor, string featureDescription = null)
        {
            var v = GetMikrotikVersion();
            if (v.Major < minimumMajor)
                Assert.Inconclusive(
                    $"RouterOS {minimumMajor}.x required{(featureDescription != null ? $" for {featureDescription}" : "")}; " +
                    $"router runs {v}.");
        }

        /// <summary>
        /// Marks the test as inconclusive when the router version is <paramref name="removedInMajor"/> or higher.
        /// Use for features removed in a specific major RouterOS version.
        /// </summary>
        protected void EnsureMaxRouterOsVersion(int removedInMajor, string featureDescription = null)
        {
            var v = GetMikrotikVersion();
            if (v.Major >= removedInMajor)
                Assert.Inconclusive(
                    $"Feature{(featureDescription != null ? $" '{featureDescription}'" : "")} was removed in RouterOS {removedInMajor}.x; " +
                    $"router runs {v}.");
        }

        /// <summary>
        /// True when <paramref name="ex"/> is the native WinBox M2 layer reporting that it cannot carry an
        /// operation: a handler that answers <c>getall</c>/<c>add</c> with an error status (e.g.
        /// <c>/system/health</c>, bridge-vlan <c>add</c>), or a field with no derivable M2 key. Use in a
        /// <c>catch (Exception ex) when (IsWinboxNativeUnsupported(ex))</c> to mark the test inconclusive.
        /// <para>
        /// This is deliberately bound to the ACTUAL failure (the operation is attempted and only the
        /// specific native-M2 error is tolerated) rather than a blanket transport-name skip: it only
        /// triggers on the native transport (those messages are produced nowhere else), it does NOT mask a
        /// genuine bug on that transport, and the test starts passing automatically if a future
        /// RouterOS/WinBox build adds support — no test edit needed.
        /// </para>
        /// </summary>
        protected static bool IsWinboxNativeUnsupported(Exception ex)
        {
            if (ex is tik4net.Winbox.WinboxFieldResolutionException)
                return true;
            return ex is TikCommandTrapException
                && ex.Message.IndexOf("WinBox native", StringComparison.OrdinalIgnoreCase) >= 0
                && ex.Message.IndexOf("returned error", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Runs <paramref name="body"/>, and marks the test Inconclusive when the native WinBox transport
        /// answers that it cannot carry the operation (see <see cref="IsWinboxNativeUnsupported"/>).
        /// </summary>
        /// <remarks>
        /// Prefer this over a capability gate whenever the operation is expected to work on every OTHER
        /// transport. It attempts the command on all of them and skips only on the transport that actually
        /// refused, so a mapping added later turns the test green with no test edit — whereas a capability
        /// gate keyed on <c>Streaming</c> skipped the sync monitor tests on ten of eleven transports and hid
        /// the defect P2.51 fixed. <paramref name="feature"/> names the path in the skip message.
        /// </remarks>
        protected void SkipIfWinboxNativeCannot(string feature, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex) when (IsWinboxNativeUnsupported(ex) || IsWinboxNativeUnmappedPath(ex))
            {
                Assert.Inconclusive(
                    $"The native WinBox transport cannot carry '{feature}' on this router: {ex.Message} " +
                    "Test skipped (it runs on every other transport).");
            }
        }

        // The native transport's "this path has no M2 handler" report — raised as TikNoSuchCommandException
        // with the transport named, so it cannot be confused with the router's own "no such command".
        private static bool IsWinboxNativeUnmappedPath(Exception ex)
            => ex is TikNoSuchCommandException
               && ex.Message.IndexOf("WinBox native", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Marks the test as inconclusive (skipped) only on the native WinBox M2 transport, for an
        /// API path that WinBox itself does not expose as a structured handler. Native CRUD is driven
        /// by the version-matched WinBox <c>.jg</c> catalog (path → handler array); a path absent from
        /// every WinBox window cannot be derived and has no numeric handler to call. Unlike the CLI
        /// family (Telnet/SSH/WinBox-CLI), which runs the textual command and is unaffected, only the
        /// native M2 transport needs the handler mapping.
        /// </summary>
        /// <param name="feature">API path / feature shown in the skip message.</param>
        protected void SkipOnWinboxNativeUnmappedPath(string feature)
        {
            var t = ResolveConnectionType();
            if (t == TikConnectionType.WinboxNative || t == TikConnectionType.WinboxNativeMac)
                Assert.Inconclusive(
                    $"'{feature}' is not exposed by WinBox as an M2 handler (absent from the .jg catalog), " +
                    "so the native WinBox transport cannot reach it — use the API or a CLI transport. Test skipped.");
        }

        /// <summary>
        /// Marks the test as Inconclusive on the CLI-family transports, which drive a single request/reply
        /// terminal and therefore serialize commands by design.
        /// </summary>
        /// <remarks>
        /// The expectation this encodes (P2.42): <b>only</b> Telnet, SSH, WinBox-CLI and WinBox-CLI-MAC have
        /// a real reason to be single-command. Everything else — API, API-SSL, REST and both native WinBox
        /// transports — must be able to run commands concurrently on one connection, because the router
        /// plainly can. A transport moving out of that list is a regression, not a limitation to accept, so
        /// this skips by naming the CLI family rather than by asking whether the transport happens to manage.
        /// </remarks>
        protected void SkipOnSingleCommandTransport()
        {
            var t = ResolveConnectionType();
            if (t == TikConnectionType.Telnet || t == TikConnectionType.Ssh
                || t == TikConnectionType.MacTelnet
                || t == TikConnectionType.WinboxCli || t == TikConnectionType.WinboxCliMac)
                Assert.Inconclusive(
                    $"Transport '{t}' drives a single request/reply terminal and serializes commands by " +
                    "design — concurrent execution is not expected of it. Test skipped.");
        }

        /// <summary>
        /// True when the active transport is NOT the binary API — i.e. a CLI-family transport
        /// (Telnet/MACTelnet/WinBox-CLI/WinBox-CLI-MAC) or native WinBox M2. These transports go
        /// through the structured-command model rather than the binary-API sentence protocol, so they
        /// do not reproduce some binary-API response semantics (e.g. per-line !re rows from
        /// <c>/system/script/run</c>). Use to branch assertions on that difference — it is NOT a
        /// CLI/terminal property (native WinBox M2 is not a terminal), it is "not the binary API".
        /// </summary>
        protected bool IsNonApiTransport()
        {
            var t = ResolveConnectionType();
            return t == TikConnectionType.Telnet || t == TikConnectionType.MacTelnet
                || t == TikConnectionType.WinboxCli || t == TikConnectionType.WinboxCliMac
                || t == TikConnectionType.WinboxNative || t == TikConnectionType.WinboxNativeMac
                || t == TikConnectionType.Ssh;
        }

        /// <summary>
        /// Ensures the given API command path exists on the router.
        /// If not, marks the test as inconclusive with a message suggesting the required package may not be installed.
        /// </summary>
        /// <remarks>
        /// The probe is an <b>unfiltered</b> print, so do not call it on a large table. On <c>/log</c> it
        /// pulls the router's whole memory log (~1000 rows, 139 358 characters measured on 7.23.2) and blows
        /// the 30 s read budget of a MAC transport outright — the same trap that made
        /// <c>RunScript_Issue53_WillNotFail</c> look like a transport defect (P2.43). For a path every
        /// RouterOS has, skip the check.
        /// </remarks>
        protected void EnsureCommandAvailable(string commandPath)
        {
            try
            {
                // ExecuteList (not CallCommandSync) interprets !trap and throws TikNoSuchCommandException.
                _connection.CreateCommand(commandPath + "/print").ExecuteList();
            }
            catch (TikNoSuchCommandException ex)
            {
                Assert.Inconclusive(
                    $"Command '{commandPath}' is not available on this router. " +
                    $"The required RouterOS package may not be installed. Details: {ex.Message}");
            }
        }
    }
}

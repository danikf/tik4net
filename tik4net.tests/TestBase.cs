using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.tests
{
    public class TestBase
    {
        private ITikConnection _connection;
        private Version _routerOsVersion;

        /// <summary>MSTest injects this for access to runsettings parameters.</summary>
        public TestContext TestContext { get; set; }

        protected ITikConnection Connection
        {
            get { return _connection; }
        }

        [TestInitialize]
        public void Init()
        {
            RecreateConnection();
            OnInitialize();
        }

        protected virtual void OnInitialize()
        {
            // dummy
        }

        [TestCleanup]
        public void Cleanup()
        {
            OnCleanup();
            _connection.Dispose();
        }

        protected virtual void OnCleanup()
        {
            // dummy
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
                    _connection = conn;
                    _connection.DebugEnabled = true;
                    _routerOsVersion = null;
                    return;
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
        /// Marks the test as inconclusive (skipped) when running over a non-binary-API transport
        /// (the CLI family — Telnet/SSH/MACTelnet/WinBox-CLI — and native WinBox M2) because the
        /// feature under test relies on binary-API response semantics those transports do not reproduce.
        /// Currently unused — kept as the counterpart to <see cref="IsNonApiTransport"/>.
        /// </summary>
        /// <param name="feature">Short description of the unsupported feature shown in the skip message.</param>
        protected void SkipOnNonApi(string feature)
        {
            if (IsNonApiTransport())
            {
                string msg = $"Transport '{ResolveConnectionType()}' (non-binary-API) does not support '{feature}' — test skipped.";
                Assert.Inconclusive(msg);
            }
        }

        /// <summary>
        /// Marks the test as inconclusive (skipped) only on the native WinBox M2 transport, for an
        /// API path that WinBox itself does not expose as a structured handler. Native CRUD is driven
        /// by the version-matched WinBox <c>.jg</c> catalog (path → handler array); a path absent from
        /// every WinBox window cannot be derived and has no numeric handler to call. This is distinct
        /// from <see cref="SkipOnNonApi"/>: the CLI family (Telnet/SSH/WinBox-CLI) runs the textual
        /// command and is unaffected — only native M2 needs the handler mapping.
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
                || t == TikConnectionType.WinboxNative || t == TikConnectionType.WinboxNativeMac;
        }

        /// <summary>
        /// Ensures the given API command path exists on the router.
        /// If not, marks the test as inconclusive with a message suggesting the required package may not be installed.
        /// </summary>
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

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
                    _connection = ConnectionFactory.OpenConnection(connType, host, user, pass);
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
        /// Marks the test as inconclusive (skipped) when running over a CLI-based transport
        /// (Telnet, and any future CLI transport such as SSH or MACTelnet) because the feature
        /// under test is not supported over an interactive terminal session.
        /// </summary>
        /// <param name="feature">Short description of the unsupported feature shown in the skip message.</param>
        protected void SkipOnCli(string feature)
        {
            if (IsCliTransport())
            {
                string msg = $"Transport '{ResolveConnectionType()}' (CLI-based) does not support '{feature}' — known CLI limitation, test skipped.";
                Assert.Inconclusive(msg);
            }
        }

        /// <summary>
        /// True when the active transport is CLI/terminal-based (currently Telnet; extend for SSH-PTY /
        /// MAC-Telnet). Use to branch assertions that depend on binary-API response semantics the
        /// terminal cannot reproduce (e.g. per-line !re rows from <c>/system/script/run</c>).
        /// </summary>
        protected bool IsCliTransport()
        {
            var t = ResolveConnectionType();
            return t == TikConnectionType.Telnet || t == TikConnectionType.MacTelnet
                || t == TikConnectionType.WinboxCli || t == TikConnectionType.WinboxCliMac;
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

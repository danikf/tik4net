using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.tests
{
    public class TestBase
    {
        private ITikConnection _connection;
        private Version _routerOsVersion;

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

        protected void RecreateConnection()
        {
            _connection = ConnectionFactory.OpenConnection(TikConnectionType.Api, ConfigurationManager.AppSettings["host"], ConfigurationManager.AppSettings["user"], ConfigurationManager.AppSettings["pass"]);
            _connection.DebugEnabled = true;
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

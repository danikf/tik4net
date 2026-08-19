using System.Configuration;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Wireless;

namespace tik4net.integrationtests
{
    /// <summary>
    /// G3.5: <c>/interface/wireless/sniffer</c> must read the sniffer's SETTINGS on every transport.
    /// </summary>
    /// <remarks>
    /// Two WinBox windows share handler [88,9]. The 'Wireless Sniffer' ACTION window is the running
    /// capture's statistics — Processed Packets, Memory Size, File Saved Packets, and a Start button — and
    /// the settings the API prints live behind its Configuration → Settings item ('Wireless Sniffer
    /// Settings', hide:1). Pointed at the action window, WinboxNative answered with the statistics wearing
    /// the settings' field names: <c>file-limit=false</c>, <c>memory-limit=0</c> where the API says 10.
    /// That is the failure the path-map audit exists to catch — a read that looks like an answer.
    /// </remarks>
    [TestClass]
    public class WirelessSnifferTest : TestBase
    {
        [TestMethod]
        public void SnifferSettingsAgreeWithTheApi()
        {
            EnsureCommandAvailable("/interface/wireless/sniffer");
            var viaTransport = Connection.LoadSingle<WirelessSniffer>();
            Assert.IsNotNull(viaTransport);

            string host = ConfigurationManager.AppSettings["host"];
            string user = ConfigurationManager.AppSettings["user"];
            string pass = ConfigurationManager.AppSettings["pass"] ?? "";

            using (var apiConnection = ConnectionFactory.CreateConnection(TikConnectionType.Api))
            {
                apiConnection.Open(host, user, pass);
                var viaApi = apiConnection.LoadSingle<WirelessSniffer>();

                Assert.AreEqual(viaApi.MultipleChannels, viaTransport.MultipleChannels, "multiple-channels");
                Assert.AreEqual(viaApi.StreamingEnabled, viaTransport.StreamingEnabled, "streaming-enabled");
                Assert.AreEqual(viaApi.StreamingServer, viaTransport.StreamingServer, "streaming-server");
                Assert.AreEqual(viaApi.ChannelTime, viaTransport.ChannelTime, "channel-time");
            }
        }
    }
}

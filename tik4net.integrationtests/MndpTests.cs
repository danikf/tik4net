using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using tik4net.Mndp;

namespace tik4net.integrationtests
{
    [TestClass]
    [TestCategory(TestCategories.LegIndependent)]
    public class MndpTests : LockedTestBase
    {
        [TestMethod]
        public void MNDP_WillWork()
        {
            var items = MndpHelper.Discover(true);

            // Once in 326 runs it found nothing in 60 s (2026-10-02, a parallel run) and passed alone at once; the
            // router log had rotated past it. If it happens again the message carries what is needed to tell why.
            if (!items.Any())
                Assert.Fail("MNDP found no router. " + Diagnostics());
        }

        private static string Diagnostics()
        {
            var parts = new List<string>();
            try
            {
                // Another socket bound to :5678 with SO_REUSEADDR takes the unicast replies on Windows.
                var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners()
                    .Where(e => e.Port == 5678).Select(e => e.ToString()).ToList();
                parts.Add("other listeners on :5678: " + (listeners.Count == 0 ? "none" : string.Join(", ", listeners)));
            }
            catch (Exception ex) { parts.Add("listeners: " + ex.Message); }
            try
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Select(n => n.Name + " " + string.Join("/", n.GetIPProperties().UnicastAddresses
                        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address)));
                parts.Add("NICs up: " + string.Join("; ", nics));
            }
            catch (Exception ex) { parts.Add("NICs: " + ex.Message); }
            try
            {
                int again = MndpHelper.Discover(TimeSpan.FromSeconds(10), System.Text.Encoding.UTF8, true).Count();
                parts.Add("a second discovery right after found " + again);
            }
            catch (Exception ex) { parts.Add("second discovery: " + ex.Message); }
            return string.Join(" | ", parts);
        }
    }
}

// WinboxNativeFieldNameTest.cs — a handful of fields RouterOS names differently from the WinBox window.
//
// The path-map audit measures the whole vocabulary at once, but it is [Ignore]d and runs by hand. These are
// the individual pairings that were established by MOVING the value, kept as tests so a change to the alias
// table or to the harvest cannot quietly take one of them away again.
//
// Every assertion compares the transport under test against the BINARY API on the same router, rather than
// against a literal — the API's own text is the reference, and a stock router's values differ per machine.

using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net;

namespace tik4net.integrationtests
{
    [TestClass]
    public class WinboxNativeFieldNameTest : TestBase
    {
        private static ITikConnection OpenSideApi()
            => LabSetup(TikConnectionType.Api).Create(TikConnectionType.Api);

        /// <summary>
        /// One row of <paramref name="path"/>. <paramref name="detail"/> is for TABLE menus read over a CLI
        /// transport: a plain <c>print</c> there returns the COLUMNS the router chose to show rather than
        /// the row — <c>/interface/ethernet</c> omits the whole Loop Protect group — and the test would be
        /// measuring RouterOS's column layout instead of what this client names a field. A SINGLETON menu
        /// has no <c>detail</c> and answers nothing at all when told to use one, so it is passed per call
        /// rather than always.
        /// <para>Built with <c>CreateParameter(..., NameValue)</c>, which says what the parameter is on the
        /// parameter itself rather than on the command.</para>
        /// </summary>
        private static ITikReSentence Single(ITikConnection conn, string path, bool detail = false)
            => (detail
                    ? conn.CreateCommand(path + "/print",
                          conn.CreateParameter("detail", "", TikCommandParameterFormat.NameValue))
                    : conn.CreateCommand(path + "/print"))
               .ExecuteList().First();

        /// <summary>Asserts the transport under test reports <paramref name="field"/> with the API's value.</summary>
        private void AssertAgreesWithApi(string path, string field, bool detail = false)
        {
            string expected;
            using (var api = OpenSideApi())
                expected = Single(api, path, detail).GetResponseFieldOrDefault(field, null);
            Assert.IsNotNull(expected, $"the API itself must report {field} on {path} for this to mean anything");

            string actual = Single(Connection, path, detail).GetResponseFieldOrDefault(field, null);
            Assert.IsNotNull(actual, $"{path} must report '{field}' — the name RouterOS uses, not the window's");
            Assert.AreEqual(expected, actual, $"{path} {field}");
        }

        /// <summary>
        /// Same assertion for a field that is EMPTY on a stock router. A blank cannot establish a pairing —
        /// two blanks agree vacuously — and native does not report an unset field at all, so the value has
        /// to be put there first and taken away again.
        /// </summary>
        private void AssertAgreesWithApiWhileSet(string path, string field, string probeValue)
        {
            using (var api = OpenSideApi())
            {
                string original = Single(api, path).GetResponseFieldOrDefault(field, "");
                api.CreateCommandAndParameters(path + "/set", field, probeValue).ExecuteNonQuery();
                try
                {
                    AssertAgreesWithApi(path, field);
                }
                finally
                {
                    api.CreateCommandAndParameters(path + "/set", field, original).ExecuteNonQuery();
                }
            }
        }

        /// <summary>WinBox calls it 'IP Address'; the API calls it <c>address</c> on both menus.</summary>
        [TestMethod]
        public void AnArpEntryReportsItsAddressUnderTheApiName()
            => AssertAgreesWithApi("/ip/arp", "address");

        [TestMethod]
        public void ADhcpClientReportsItsLeaseAddressUnderTheApiName()
            => AssertAgreesWithApi("/ip/dhcp-client", "address");

        /// <summary>
        /// The Loop Protect tab: RouterOS prefixes the tab's name onto every field under it except the one
        /// that IS the tab's name, so 'Send Interval' is <c>loop-protect-send-interval</c>.
        /// </summary>
        [TestMethod]
        public void TheLoopProtectTabsFieldsCarryTheTabsName()
        {
            // The NAME only. RouterOS's own CLI prints a duration as 00:00:05 where the binary API prints
            // 5s, so comparing the value here would measure that difference instead — and the value is
            // already compared, per transport, by the path-map audit.
            var row = Single(Connection, "/interface/ethernet", detail: true);

            foreach (string field in new[] { "loop-protect-send-interval", "loop-protect-disable-time",
                                             "loop-protect-status" })
                Assert.IsTrue(row.TryGetResponseField(field, out _),
                    $"/interface/ethernet must report '{field}' — the tab's name is part of it");

            foreach (string label in new[] { "send-interval", "disable-time" })
                Assert.IsFalse(row.TryGetResponseField(label, out _),
                    $"and not '{label}', which is the box's caption inside the Loop Protect tab");
        }

        /// <summary>
        /// A <c>kbytes</c> field: the wire value is in kibibytes and the API prints bytes. Both halves of
        /// this test matter — the NAME comes from an alias and the VALUE from the ×1024 conversion, and
        /// either one wrong fails it.
        /// </summary>
        [TestMethod]
        public void TheDiskSizeIsReportedInTheApisUnits()
            => AssertAgreesWithApi("/system/resource", "total-hdd-space");

        /// <summary>
        /// Two declarations of one key, the disused one prefixed 'Old' and carrying the other's name as its
        /// <c>title</c>. Reported under the name RouterOS uses, and both spellings still resolve.
        /// </summary>
        [TestMethod]
        public void TheProxyCachePathIsNotReportedAsTheOldOne()
        {
            var row = Single(Connection, "/ip/proxy");
            Assert.IsTrue(row.TryGetResponseField("cache-path", out _),
                "the API's name for it");
            Assert.IsFalse(row.TryGetResponseField("old-cache-path", out _),
                "and not the disused label as well");
        }

        /// <summary>WinBox paints 'Contact Info' beside the box the API calls <c>contact</c>.</summary>
        [TestMethod]
        public void TheSnmpContactIsReportedUnderTheApiName()
            => AssertAgreesWithApiWhileSet("/snmp", "contact", "tik4net-field-name-test");

        /// <summary>
        /// 'mDNS Repeater Interfaces' is <c>mdns-repeat-ifaces</c> — an abbreviation of the label rather
        /// than a spelling of it, so nothing derives it and it is an alias.
        /// </summary>
        [TestMethod]
        public void TheMdnsRepeaterInterfaceListIsReportedUnderTheApiName()
            => AssertAgreesWithApiWhileSet("/ip/dns", "mdns-repeat-ifaces", "ether2");

        /// <summary>
        /// A dropdown whose members come from a REFERENCED table, behind a <c>defenum</c> sentinel:
        /// <c>/caps-man/manager</c>'s <c>certificate</c>. The static half has one member (<c>auto</c> at 0)
        /// and the certificates themselves come from <c>[19,1]</c>, so an assigned certificate's id is a
        /// value the static map has no member for — which the "unmapped optional enum means not set" rule
        /// read as unset, dropping the field on a row where the API prints the certificate's name.
        /// <para>The certificate is taken from the router rather than named here: which certificates a lab
        /// router carries is provisioning, not protocol.</para>
        /// </summary>
        [TestMethod]
        public void ACertificateDropdownReportsTheCertificatesName()
        {
            string certificate;
            using (var api = OpenSideApi())
            {
                var certs = api.CreateCommand("/certificate/print").ExecuteList();
                if (!certs.Any())
                    Assert.Inconclusive("the router has no certificate to assign");
                certificate = certs.First().GetResponseField("name");
            }

            AssertAgreesWithApiWhileSet("/caps-man/manager", "certificate", certificate);
        }

        // ── a declaration whose label is on the parent and whose ids are on the children ──

        /// <summary>
        /// A <c>union</c> with <c>single:1</c> is one field with alternative wire encodings, one per address
        /// family, and the router sends whichever it has. <c>/ip/ipsec/policy</c>'s 'Src. Address' is
        /// <c>{network u1, network6 a15}</c> and a stock template policy carries the IPv6 one — the family
        /// that was not in the catalog at all, only the first being registered.
        /// <para>Value as well as name: the prefix length lives in a sibling key, so an alternative
        /// registered without its own <c>maskid</c> would read <c>::</c> where the API says <c>::/0</c>.</para>
        /// </summary>
        [TestMethod]
        public void ABothFamiliesUnionIsReportedInWhicheverFamilyTheRouterSent()
        {
            AssertAgreesWithApi("/ip/ipsec/policy", "src-address");
            AssertAgreesWithApi("/ip/ipsec/policy", "dst-address");
        }

        /// <summary>The same shape on a singleton: <c>/snmp</c>'s 'Src. Address' is
        /// <c>{ipaddr u1b, ip6addr a1c}</c> and the row carries the second.</summary>
        [TestMethod]
        public void ASingletonsUnionFieldIsReportedToo()
            => AssertAgreesWithApi("/snmp", "src-address");

        /// <summary>
        /// A third union, on a writable field: <c>/ip/proxy</c>'s 'Parent Proxy' is
        /// <c>{union,opt:1,single:1,c:[{ipaddr u3},{ip6addr a16}]}</c> — note that the port beside it is a
        /// field of its OWN ('Parent Proxy Port', <c>u4</c>), so this is not the tuple shape however much
        /// an address next to a port looks like one.
        /// </summary>
        [TestMethod]
        public void AWritableUnionFieldIsReportedToo()
            => AssertAgreesWithApi("/ip/proxy", "parent-proxy");

        /// <summary>
        /// A tuple whose parts are only on SOME rows: <c>/ip/service</c>'s 'Remote' is
        /// <c>{ip6addr ad, number ue}</c> and only a live connection row carries it. Compared per row
        /// against the API rather than on the first row, because which connections exist depends on which
        /// transport is running the test.
        /// </summary>
        [TestMethod]
        public void ATupleOnlySomeRowsCarryIsReportedOnThoseRows()
        {
            Dictionary<string, string> expected;
            using (var api = OpenSideApi())
                expected = api.CreateCommand("/ip/service/print").ExecuteList()
                    .Where(r => r.GetResponseFieldOrDefault("remote", null) != null)
                    .ToDictionary(r => r.GetId(), r => r.GetResponseField("remote"));

            if (expected.Count == 0)
                Assert.Inconclusive("no /ip/service row carries 'remote' — nothing is connected");

            var rows = Connection.CreateCommand("/ip/service/print").ExecuteList()
                .ToDictionary(r => r.GetId(), r => r.GetResponseFieldOrDefault("remote", null));

            int compared = 0;
            foreach (var e in expected)
            {
                // A connection can come and go between the two reads; only rows both saw mean anything.
                if (!rows.TryGetValue(e.Key, out string actual)) continue;
                Assert.AreEqual(e.Value, actual, $"/ip/service {e.Key} remote");
                compared++;
            }
            Assert.IsTrue(compared > 0,
                "no row was seen by both transports, so the tuple was never actually compared");
        }
    }
}

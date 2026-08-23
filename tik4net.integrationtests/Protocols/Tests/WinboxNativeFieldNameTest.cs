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

        /// <summary>
        /// Every row of <paramref name="path"/> from the transport under test, read with <c>detail</c>.
        /// A CLI transport's bare <c>print</c> returns the COLUMNS RouterOS chose to show, and a route's
        /// <c>immediate-gw</c> is not one of them — without this the test measures the router's column
        /// layout and calls a transport broken for obeying it.
        /// </summary>
        private IEnumerable<ITikReSentence> AllDetailed(string path)
            => Connection.CreateCommand(path + "/print",
                   Connection.CreateParameter("detail", "", TikCommandParameterFormat.NameValue))
               .ExecuteList();

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

        /// <summary>
        /// <c>/file</c>'s window declares 'type' TWICE: <c>{name:'type',id:'u3',nonpublic:1}</c>, the numeric
        /// file kind WinBox never paints, and <c>{name:'Type',id:'s7'}</c>, the text the API prints. The
        /// router sends both on every row, and the internal one used to claim the name — <c>type=5</c> where
        /// the API says <c>type=directory</c>.
        /// </summary>
        /// <remarks>
        /// Paired by NAME, not by <c>.id</c>: <c>/file</c> is keyed by the router's numeric handle on native
        /// and by an opaque <c>**…</c> string on the API, so an <c>.id</c> pairing matches nothing at all —
        /// which is exactly why the path-map audit reports this path as VALUES UNCOMPARED and could not have
        /// caught this.
        /// </remarks>
        [TestMethod]
        public void AFileReportsTheTextualTypeAndNotTheInternalNumberBesideIt()
        {
            Dictionary<string, string> expected;
            using (var api = OpenSideApi())
                expected = api.CreateCommand("/file/print").ExecuteList()
                    .Where(r => r.GetResponseFieldOrDefault("name", null) != null)
                    .GroupBy(r => r.GetResponseField("name"))
                    .ToDictionary(g => g.Key, g => g.First().GetResponseFieldOrDefault("type", ""));

            if (expected.Count == 0)
                Assert.Inconclusive("the router has no files, so there is nothing to compare");

            var rows = Connection.CreateCommand("/file/print").ExecuteList()
                .Where(r => r.GetResponseFieldOrDefault("name", null) != null)
                .GroupBy(r => r.GetResponseField("name"))
                .ToDictionary(g => g.Key, g => g.First().GetResponseFieldOrDefault("type", null));

            int compared = 0;
            foreach (var e in expected)
            {
                // A file can appear or vanish between the two reads; only names both saw mean anything.
                if (!rows.TryGetValue(e.Key, out string actual)) continue;
                Assert.AreEqual(e.Value, actual, $"/file '{e.Key}' type");
                compared++;
            }
            Assert.IsTrue(compared > 0, "no file was seen by both transports, so nothing was compared");
        }

        /// <summary>
        /// The generic Interface window declares no MAC Address — WinBox paints that box in the subtype
        /// dialog beside it — so <c>/interface</c> had no name for <c>0x3E9</c> and dropped a field the
        /// router sends on every row. Both menus are asserted: the subtype inherits the same alias set, and
        /// a synthetic that shadowed the catalogued field would show up here.
        /// </summary>
        [TestMethod]
        public void EveryInterfaceReportsItsMacAddress()
        {
            foreach (string path in new[] { "/interface", "/interface/ethernet" })
            {
                Dictionary<string, string> expected;
                using (var api = OpenSideApi())
                    expected = api.CreateCommand(path + "/print").ExecuteList()
                        .ToDictionary(r => r.GetResponseField("name"),
                                      r => r.GetResponseFieldOrDefault("mac-address", ""));

                var rows = Connection.CreateCommand(path + "/print").ExecuteList()
                    .ToDictionary(r => r.GetResponseField("name"),
                                  r => r.GetResponseFieldOrDefault("mac-address", null));

                int compared = 0;
                foreach (var e in expected)
                {
                    if (!rows.TryGetValue(e.Key, out string actual)) continue;
                    Assert.AreEqual(e.Value, actual, $"{path} '{e.Key}' mac-address");
                    compared++;
                }
                Assert.IsTrue(compared > 0, $"no {path} row was seen by both transports");
            }
        }

        /// <summary>
        /// A route's ORIGIN: WinBox has one enum for it ('Belongs To'), the API a family of bools of which
        /// it prints only the member that is true. Asserted only where the API DID print one — native
        /// answers <c>false</c> on the other rows, which is more than the API says rather than different
        /// from it.
        /// </summary>
        [TestMethod]
        public void ARoutesOriginIsReportedAsTheApisBool()
        {
            var expected = new Dictionary<string, Dictionary<string, string>>();
            using (var api = OpenSideApi())
                foreach (var r in api.CreateCommand("/ip/route/print").ExecuteList())
                {
                    var flags = new Dictionary<string, string>();
                    foreach (string f in new[] { "connect", "dhcp", "static" })
                    {
                        string v = r.GetResponseFieldOrDefault(f, null);
                        if (v != null) flags[f] = v;
                    }
                    if (flags.Count > 0) expected[r.GetId()] = flags;
                }

            if (expected.Count == 0)
                Assert.Inconclusive("no route on this router carries an origin flag the API prints");

            var rows = AllDetailed("/ip/route").ToDictionary(r => r.GetId(), r => r);

            int compared = 0;
            foreach (var e in expected)
            {
                if (!rows.TryGetValue(e.Key, out var row)) continue;
                foreach (var f in e.Value)
                {
                    Assert.AreEqual(f.Value, row.GetResponseFieldOrDefault(f.Key, null),
                        $"/ip/route {e.Key} {f.Key}");
                    compared++;
                }
            }
            Assert.IsTrue(compared > 0, "no route was seen by both transports");
        }

        /// <summary>
        /// <c>immediate-gw</c> is an <c>addr</c> LIST at <c>0x108</c>. The IPv4 route window declares only a
        /// hyperlink beside it, so the key went unnamed and the field was dropped — though the catalog does
        /// name it, on the IPv6 window (<c>M108</c>), and the router sends it to both.
        /// </summary>
        [TestMethod]
        public void ARouteReportsItsImmediateGateway()
        {
            Dictionary<string, string> expected;
            using (var api = OpenSideApi())
                expected = api.CreateCommand("/ip/route/print").ExecuteList()
                    .Where(r => r.GetResponseFieldOrDefault("immediate-gw", null) != null)
                    .ToDictionary(r => r.GetId(), r => r.GetResponseField("immediate-gw"));

            if (expected.Count == 0)
                Assert.Inconclusive("no route on this router has an immediate gateway");

            var rows = AllDetailed("/ip/route")
                .ToDictionary(r => r.GetId(), r => r.GetResponseFieldOrDefault("immediate-gw", null));

            int compared = 0;
            foreach (var e in expected)
            {
                if (!rows.TryGetValue(e.Key, out string actual)) continue;
                Assert.AreEqual(e.Value, actual, $"/ip/route {e.Key} immediate-gw");
                compared++;
            }
            Assert.IsTrue(compared > 0, "no route was seen by both transports");
        }
    }
}

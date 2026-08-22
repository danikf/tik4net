// WinboxListWriteTests.cs — router-free tests for the list/array field writes.
//
// Every array wire type used to be refused outright ("not yet encodable over native WinBox M2 writes"),
// which covered four scalar list shapes and the message array. The shapes come from master*.js's own
// writer, not from guesswork:
//
//   string[]  0xA0  count(2B) + per element len(2B) + UTF-8
//   raw[]     0xB0  the same, with the element's bytes
//   ip6[]     0x98  count(2B) + 16 fixed bytes per element, no per-element length
//   u32[]     0x88  count(2B) + 4 bytes per element        (an 'ipaddr' element is the packed IPv4)
//   msg[]     0xA8  count(2B) + per element len(2B) + a whole submessage
//
// The .jg fragments are real declarations from RouterOS 7.24, cut to the fields under test.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Winbox;

namespace tik4net.unittests.Winbox
{
    [TestClass]
    public class WinboxListWriteTests
    {
        private static readonly int[] ForwarderHandler = { 14, 6 };
        private static readonly int[] DhcpNetworkHandler = { 20, 6 };

        // roteros.jg, DNS Forwarders [14,6]: an address list (message array of `addr` compounds) next to a
        // plain string list.
        private const string ForwarderWindow =
            "[{name:'DNS',title:'DNS',group:'IP',c:[" +
            "{name:'Forwarders',title:'Forwarders',type:'map',path:[ 14,6 ],c:[" +
              "{name:'Name',type:'string',id:'sfe0010'}," +
              "{name:'DNS Servers',type:'multi',id:'M3',c:[{type:'addr',allow:'46'}]}," +
              "{name:'DoH Servers',type:'multistring',id:'S2',c:[{type:'string'}]}]}" +
            "]}]";

        // roteros.jg, DHCP Network [20,6]: lists of IPv4 addresses, each element packed into one u32.
        private const string DhcpNetworkWindow =
            "[{name:'DHCP Server',title:'DHCP Server',group:'IP',c:[" +
            "{name:'DHCP Network',title:'Networks',type:'map',path:[ 20,6 ],c:[" +
              "{name:'DNS Servers',type:'multiipaddr',id:'U4',c:[{type:'ipaddr'}]}," +
              "{name:'Valid Servers',type:'multiraw',id:'R2',c:[{type:'macaddr'}]}," +
              "{name:'CAPsMAN Addresses',type:'multiip6addr',id:'A6',c:[{type:'ip6addr'}]}]}" +
            "]}]";

        private static WinboxJgCatalog Parse(string body)
        {
            var catalog = new WinboxJgCatalog();
            Assert.IsTrue(catalog.TryParseInto(body), "the trimmed window must parse");
            return catalog;
        }

        private static WinboxFieldResolver Resolver(string body, string apiPath, int[] handler)
            => new WinboxFieldResolver(apiPath, handler, Parse(body), new Dictionary<string, int>());

        // The one encoded field, read back through the real M2 parser.
        private static KeyValuePair<int, Tuple<string, object>> Decoded(IList<byte[]> encoded)
        {
            Assert.AreEqual(1, encoded.Count, "expected exactly one encoded field");
            var fields = M2Message.ParseAllFields(M2Message.BuildM2(encoded.ToArray()));
            Assert.AreEqual(1, fields.Count);
            return fields.First();
        }

        // ── scalar arrays ──────────────────────────────────────────────────────

        [TestMethod]
        public void AStringListRidesAsOneStringArray()
        {
            var doh = Decoded(Resolver(ForwarderWindow, "/ip/dns/forwarders", ForwarderHandler)
                .EncodeField("doh-servers", "https://one.example/dns-query, https://two.example/dns-query"));

            Assert.AreEqual(0x2, doh.Key);
            Assert.AreEqual("str[]", doh.Value.Item1, "one array field, not two string fields");
            Assert.AreEqual("[https://one.example/dns-query,https://two.example/dns-query]",
                            doh.Value.Item2.ToString());
        }

        [TestMethod]
        public void AnEmptyListIsTheEmptyArrayRatherThanNoFieldAtAll()
        {
            // A key the router is not told about keeps whatever it already holds, so a clear that sends
            // nothing reports success and leaves the old list in place.
            var doh = Decoded(Resolver(ForwarderWindow, "/ip/dns/forwarders", ForwarderHandler)
                .EncodeField("doh-servers", ""));

            Assert.AreEqual("str[]", doh.Value.Item1);
            Assert.AreEqual("[]", doh.Value.Item2.ToString());
        }

        [TestMethod]
        public void AnIpv4ListPacksEachElementIntoAU32()
        {
            var dns = Decoded(Resolver(DhcpNetworkWindow, "/ip/dhcp-server/network", DhcpNetworkHandler)
                .EncodeField("dns-servers", "8.8.8.8,1.1.1.1"));

            Assert.AreEqual(0x4, dns.Key);
            Assert.AreEqual("u32[]", dns.Value.Item1);
            // Octet-LSB, the same packing a scalar ipaddr uses.
            Assert.AreEqual("[134744072,16843009]", dns.Value.Item2.ToString());
        }

        [TestMethod]
        public void AMacListRidesAsARawArray()
        {
            var macs = Decoded(Resolver(DhcpNetworkWindow, "/ip/dhcp-server/network", DhcpNetworkHandler)
                .EncodeField("valid-servers", "AA:BB:CC:DD:EE:FF,00:11:22:33:44:55"));

            Assert.AreEqual(0x2, macs.Key);
            Assert.AreEqual("raw[]", macs.Value.Item1);
            Assert.AreEqual("[AABBCCDDEEFF,001122334455]", macs.Value.Item2.ToString());
        }

        [TestMethod]
        public void AnIpv6ListRidesAsFixedWidthElements()
        {
            var addrs = Decoded(Resolver(DhcpNetworkWindow, "/ip/dhcp-server/network", DhcpNetworkHandler)
                .EncodeField("capsman-addresses", "2001:db8::1,::1"));

            Assert.AreEqual(0x6, addrs.Key);
            Assert.AreEqual("ip6[]", addrs.Value.Item1);
            Assert.AreEqual("[20010DB8000000000000000000000001,00000000000000000000000000000001]",
                            addrs.Value.Item2.ToString());
        }

        [TestMethod]
        public void AnUnconvertibleElementFailsRatherThanShorteningTheList()
        {
            // A shorter list is a request the router accepts without complaint, so a dropped element would
            // set one server and report success.
            var resolver = Resolver(DhcpNetworkWindow, "/ip/dhcp-server/network", DhcpNetworkHandler);

            Assert.ThrowsException<WinboxFieldValueException>(
                () => resolver.EncodeField("dns-servers", "8.8.8.8,not-an-address"));
        }

        // ── message arrays ─────────────────────────────────────────────────────

        [TestMethod]
        public void AnAddressListRidesAsAMessageArrayOfAddrCompounds()
        {
            var servers = Decoded(Resolver(ForwarderWindow, "/ip/dns/forwarders", ForwarderHandler)
                .EncodeField("dns-servers", "8.8.8.8,2001:db8::1"));

            Assert.AreEqual(0x3, servers.Key);
            Assert.AreEqual("msg[]", servers.Value.Item1);

            var elements = (List<Dictionary<int, Tuple<string, object>>>)servers.Value.Item2;
            Assert.AreEqual(2, elements.Count);
            // Each element is a whole submessage, with the address at the sub-key its FORM calls for.
            Assert.AreEqual(134744072L,
                Convert.ToInt64(elements[0][WinboxFieldResolver.AddrV4SubKey].Item2), "the IPv4 sub-key");
            Assert.IsTrue(elements[1].ContainsKey(WinboxFieldResolver.AddrV6SubKey), "the IPv6 sub-key");
        }

        [TestMethod]
        public void AListWhoseElementIsNotYetEncodableStillFailsLoudly()
        {
            // A `numbertable` is a control of its own — a read-only table of NAMED columns, one row per
            // rate — and this encoder does not build one. Refusing is the point: the alternative is a
            // wrong-shaped field the router accepts and ignores. (Its three declarations are all `ro:1` and
            // all on radio hardware, which is why it is refused rather than guessed at.)
            const string TxPowerWindow =
                "[{name:'Wireless',title:'Wireless',group:'Interfaces',c:[" +
                "{name:'Interface',title:'Interfaces',type:'map',path:[ 20,20 ],c:[" +
                  "{name:'Current Tx Powers',type:'numbertable',id:'Uc4e',c:[" +
                     "{name:'Rate',type:'enm',id:'u0'},{name:'Tx Power',type:'number',id:'u1'}]}]}" +
                "]}]";
            var resolver = Resolver(TxPowerWindow, "/interface/wireless", new[] { 20, 20 });

            Assert.ThrowsException<WinboxFieldResolutionException>(
                () => resolver.EncodeField("current-tx-powers", "1Mbps:17"));
        }
    }
}

#nullable enable
// TikHexNumberTests.cs — the bridge priorities, which the binary API prints in hex on every RouterOS version
// and the CLI's as-value in decimal before 7.24. The values are the ones measured on the lab routers
// (6.49.13, 7.21.5, 7.24.4) on 2026-09-28.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface;
using tik4net.Objects.Interface.Bridge;
using tik4net.Testing;

namespace tik4net.unittests.Connection
{
    [TestClass]
    public class TikHexNumberTests
    {
        [DataTestMethod]
        [DataRow("0x70", "112")]       // bridge port, API vs as-value on 6.49.13 / 7.21.5
        [DataRow("0x7000", "28672")]   // bridge
        [DataRow("0x8000", "32768")]   // the bridge's default
        [DataRow("0X80", "128")]       // either case of the prefix
        [DataRow("0xab", "171")]       // either case of the digits
        public void TheTwoSpellingsTheRouterWritesAreTheSameNumber(string hex, string dec)
        {
            Assert.AreEqual(TikHexNumber.Parse(hex), TikHexNumber.Parse(dec));
            Assert.AreEqual(long.Parse(dec), TikHexNumber.Parse(hex).Value);
        }

        [TestMethod]
        public void ItIsWrittenAsTheApiPrintsIt()
        {
            // RouterOS 7.24 refuses 'priority=112' on a bridge port and accepts 'priority=0x70'.
            Assert.AreEqual("0x70", TikHexNumber.Parse("112").ToString());
            Assert.AreEqual("0x8000", new TikHexNumber(32768).ToString());
            Assert.AreEqual("0xAB", TikHexNumber.Parse("0xab").ToString(), "upper-case digits, as icmp-rate-mask prints");
            Assert.AreEqual("0x0", new TikHexNumber(0).ToString());
        }

        [DataTestMethod]
        [DataRow("0x")]
        [DataRow("0x-1")]
        [DataRow("0xG")]
        [DataRow("auto")]
        [DataRow(" ")]
        public void NotANumberIsRefused(string value)
        {
            Assert.IsFalse(TikHexNumber.TryParse(value, out _));
        }

        [TestMethod]
        public void TheEntitiesReadEitherSpellingAndWriteHex()
        {
            var connection = new TikFakeConnection()
                .WithResponse(cmd => cmd.FirstOrDefault() == "/interface/bridge/port/print",
                    _ => new ITikSentence[]
                    {
                        new TikFakeReSentence(new Dictionary<string, string> { [".id"] = "*1", ["priority"] = "0x80" }),
                        new TikFakeReSentence(new Dictionary<string, string> { [".id"] = "*2", ["priority"] = "112" }),
                        new TikFakeDoneSentence(),
                    })
                .WithNonQuery(cmd => cmd.First() == "/interface/bridge/port/set");

            var ports = connection.LoadAll<BridgePort>().ToList();
            Assert.AreEqual(TikFieldState.Present, ports[0].Priority.State, "0x80 over the API used to read Unparsed");
            Assert.AreEqual(128L, ports[0].Priority.Value!.Value.Value);
            Assert.AreEqual(112L, ports[1].Priority.Value!.Value.Value);

            ports[1].Priority = new TikHexNumber(0x60);
            connection.Save(ports[1]);
            string sent = connection.SentCommands.Where(c => c.First() == "/interface/bridge/port/set")
                .SelectMany(c => c.Skip(1)).Single(w => w.StartsWith("=priority=", StringComparison.Ordinal));
            Assert.AreEqual("=priority=0x60", sent);
        }

        [TestMethod]
        public void ABridgeIsAddedWithItsPriorityInHex()
        {
            var bridge = new InterfaceBridge { Name = "b", Priority = new TikHexNumber(28672) };
            var connection = new TikFakeConnection().WithScalarResponse(cmd => cmd.First() == "/interface/bridge/add", "*9");
            connection.Save(bridge);
            string[] words = connection.SentCommands.Single(c => c.First() == "/interface/bridge/add").ToArray();
            CollectionAssert.Contains(words, "=priority=0x7000", string.Join(" ", words));
        }

        [TestMethod]
        public void TheIcmpRateMaskIsANumber_AndTheBridgeEtherTypeAWord()
        {
            // icmp-rate-mask takes any number (0x181A, 6168); ether-type only its three lower-case words — the router
            // refuses 0x88A8 and 34984 for it (6.49.13, 7.21.5), so it cannot be a TikHexNumber.
            var connection = new TikFakeConnection()
                .WithResponse(cmd => cmd.FirstOrDefault() == "/ip/settings/print",
                    _ => new ITikSentence[] { new TikFakeReSentence(new Dictionary<string, string> { ["icmp-rate-mask"] = "0x1818" }), new TikFakeDoneSentence() })
                .WithResponse(cmd => cmd.FirstOrDefault() == "/interface/bridge/print",
                    _ => new ITikSentence[] { new TikFakeReSentence(new Dictionary<string, string> { [".id"] = "*1", ["ether-type"] = "0x88a8" }), new TikFakeDoneSentence() })
                .WithNonQuery(cmd => cmd.First() == "/ip/settings/set" || cmd.First() == "/interface/bridge/set");

            var settings = connection.LoadSingle<tik4net.Objects.Ip.IpSettings>();
            Assert.AreEqual(0x1818L, settings.IcmpRateMask.Value!.Value.Value);
            settings.IcmpRateMask = new TikHexNumber(0x181A);
            connection.Save(settings);
            CollectionAssert.Contains(connection.SentCommands.Single(c => c.First() == "/ip/settings/set").ToArray(), "=icmp-rate-mask=0x181A");

            var bridge = connection.LoadAll<InterfaceBridge>().Single();
            Assert.IsTrue(bridge.EtherType == InterfaceBridge.EtherTypeMode.Dot1Ad);
            bridge.EtherType = InterfaceBridge.EtherTypeMode.QinQ9100;
            connection.Save(bridge);
            CollectionAssert.Contains(connection.SentCommands.Single(c => c.First() == "/interface/bridge/set").ToArray(), "=ether-type=0x9100");
        }
    }
}

#nullable enable
using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Interface.Bridge;
using tik4net.Objects.Ip;
using tik4net.Objects.Ip.Firewall;
using tik4net.Objects.Queue;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// The router's own text into a typed field: <see cref="TikField{T}.Parse"/> / <c>TryParse</c> by the rule every value
    /// type follows (Parse throws, TryParse does not, the result is <see cref="TikFieldState.Present"/>), and
    /// <see cref="Tik.Wire"/>, which assigns that text to any field — plain or negatable — and parses it the same strict way.
    /// One case per field kind on the wiki's TikField page.
    /// </summary>
    [TestClass]
    public class TikFieldParseTests
    {
        // ── TikField<T>.Parse / TryParse ──────────────────────────────────────

        [TestMethod]
        public void ParseReadsEveryPlainKindAsPresent()
        {
            Assert.AreEqual(TikFieldState.Present, TikField<string?>.Parse("uplink").State);
            Assert.AreEqual("uplink", TikField<string?>.Parse("uplink").Value);
            Assert.AreEqual("!important", TikField<string?>.Parse("!important").Value, "a TikField's '!' is text");
            Assert.AreEqual("", TikField<string?>.Parse("").Value, "a string holds the empty word");
            Assert.AreEqual(true, TikField<bool?>.Parse("yes").Value);
            Assert.AreEqual(false, TikField<bool?>.Parse("false").Value);
            Assert.AreEqual(8080L, TikField<long?>.Parse("8080").Value);
            Assert.AreEqual(22, TikField<int?>.Parse("22").Value);
            Assert.AreEqual(FirewallFilter.ActionType.Accept, TikField<FirewallFilter.ActionType?>.Parse("accept").Value);
            Assert.AreEqual(TimeSpan.FromDays(1), TikField<TikDuration?>.Parse("1d").Value!.Value.Value);
            Assert.AreEqual((TikRatePair)"1M/2M", TikField<TikRatePair?>.Parse("1M/2M").Value);
        }

        [TestMethod]
        public void ParseReadsListsTheWayTheRouterWritesThem()
        {
            var ports = TikField<TikValueList<TikPortRange>?>.Parse("22,8291,1000-2000");
            Assert.AreEqual("22,8291,1000-2000", ports.ToString());
            Assert.AreEqual(3, ports.Value!.Count);

            var vlans = TikField<TikValueList<TikNumberRange>?>.Parse("10,20-30");
            Assert.AreEqual("10,20-30", vlans.ToString());

            var flags = TikField<TikValueList<TikNegatableValue<FirewallTcpFlag>>?>.Parse("syn,!ack");
            Assert.IsTrue(flags.Value!.Contains(TikNegatableValue<FirewallTcpFlag>.Not(FirewallTcpFlag.Ack)), "a member's own '!'");
        }

        [TestMethod]
        public void ParseRefusesWhatTheTypeCannotHold()
        {
            Assert.ThrowsException<ArgumentNullException>(() => TikField<int?>.Parse(null!));
            Assert.ThrowsException<FormatException>(() => TikField<int?>.Parse("abc"));
            Assert.ThrowsException<FormatException>(() => TikField<int?>.Parse("!22"), "a TikField takes no '!': the negation is the type");
            Assert.ThrowsException<FormatException>(() => TikField<long?>.Parse(""), "a number has no empty value");
            Assert.ThrowsException<FormatException>(() => TikField<bool?>.Parse("maybe"));
            Assert.ThrowsException<FormatException>(() => TikField<TikDuration?>.Parse("soon"));
            Assert.ThrowsException<FormatException>(() => TikField<TikValueList<TikPortRange>?>.Parse("22,http"));
            var unknown = Assert.ThrowsException<FormatException>(() => TikField<FirewallFilter.ActionType?>.Parse("some-new-action"));
            StringAssert.Contains(unknown.Message, "FromWire", "an enum's unknown word points at FromWire");
        }

        [TestMethod]
        public void TryParseAnswersFalseWhereParseThrows()
        {
            Assert.IsTrue(TikField<int?>.TryParse("22", out var port));
            Assert.AreEqual(22, port.Value);

            foreach (string? text in new[] { null, "abc", "!22", "" })
            {
                Assert.IsFalse(TikField<int?>.TryParse(text, out var refused), "TryParse(\"" + text + "\")");
                Assert.AreEqual(TikFieldState.Absent, refused.State, "the out value of a refusal is Absent");
            }
            Assert.IsFalse(TikField<FirewallFilter.ActionType?>.TryParse("some-new-action", out _));
        }

        // ── Tik.Wire ───────────────────────────────────────────────────────────

        [TestMethod]
        public void WireAssignsTheConsoleSpellingToEveryFieldKind()
        {
            var rule = new FirewallFilter
            {
                Chain = Tik.Wire("input"),
                Action = Tik.Wire("drop"),
                Disabled = Tik.Wire("yes"),
                Comment = Tik.Wire("!keep"),                  // a TikField<string?>: the '!' is text
                SrcAddress = Tik.Wire("!10.0.0.0/8"),         // a negated matcher
                DstPort = Tik.Wire("!22,8291"),               // a negated list
                ConnectionState = Tik.Wire("established,related"),
                TcpFlags = Tik.Wire("syn,!ack"),              // negated members
            };

            Assert.AreEqual("input", rule.Chain.Value);
            Assert.AreEqual(FirewallFilter.ActionType.Drop, rule.Action.Value);
            Assert.AreEqual(true, rule.Disabled.Value);
            Assert.AreEqual("!keep", rule.Comment.Value);
            Assert.IsTrue(rule.SrcAddress.IsNegated);
            Assert.AreEqual("10.0.0.0/8", rule.SrcAddress.Value);
            Assert.IsTrue(rule.DstPort.IsNegated);
            Assert.AreEqual("!22,8291", rule.DstPort.ToString());
            Assert.AreEqual(2, rule.ConnectionState.Value!.Count);
            Assert.IsFalse(rule.ConnectionState.IsNegated);
            Assert.IsTrue(rule.TcpFlags.Value!.Contains(TikNegatableValue<FirewallTcpFlag>.Not(FirewallTcpFlag.Ack)));

            var server = new IpDhcpServer { LeaseTime = Tik.Wire("1d") };
            Assert.AreEqual(TimeSpan.FromDays(1), server.LeaseTime.Value!.Value.Value);

            var queue = new QueueSimple { MaxLimit = Tik.Wire("1M/2M") };
            Assert.AreEqual((TikRatePair)"1M/2M", queue.MaxLimit.Value);

            var vlan = new BridgeVlan { VlanIds = Tik.Wire("10,20-30") };
            Assert.AreEqual("10,20-30", vlan.VlanIds.ToString());
        }

        [TestMethod]
        public void WireIsStrict_AValueTheFieldRefusesThrowsAtTheAssignment()
        {
            Assert.ThrowsException<FormatException>(() => new FirewallFilter { Action = Tik.Wire("some-new-action") });
            Assert.ThrowsException<FormatException>(() => new FirewallFilter { Disabled = Tik.Wire("maybe") });
            Assert.ThrowsException<FormatException>(() => new FirewallFilter { DstPort = Tik.Wire("22,http") });
            Assert.ThrowsException<FormatException>(() => new FirewallFilter { SrcAddress = Tik.Wire("!") }, "a bare '!' negates nothing");
            Assert.ThrowsException<FormatException>(() => new IpDhcpServer { LeaseTime = Tik.Wire("soon") });
            Assert.ThrowsException<ArgumentNullException>(() => Tik.Wire(null!));
        }

        [TestMethod]
        public void WireGivesTheSameValueAsParse()
        {
            TikField<TikDuration?> viaWire = Tik.Wire("1h30m");
            Assert.AreEqual(TikField<TikDuration?>.Parse("1h30m"), viaWire);

            TikNegatableField<TikValueList<TikPortRange>?> negated = Tik.Wire("!22,8291");
            Assert.AreEqual(TikNegatableField<TikValueList<TikPortRange>?>.Parse("!22,8291"), negated);
            Assert.AreEqual("!22,8291", Tik.Wire("!22,8291").Text);
        }
    }
}

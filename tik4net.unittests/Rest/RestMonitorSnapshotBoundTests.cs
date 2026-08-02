using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Cli;
using tik4net.Connection;
using tik4net.Rest;

namespace tik4net.unittests.Rest
{
    /// <summary>
    /// Covers the bound that turns an open-ended RouterOS monitor into one reading, and REST's application
    /// of it (P2.26).
    /// </summary>
    /// <remarks>
    /// This is the piece of the REST <c>Listen</c> emulation whose failure mode is invisible. RouterOS
    /// buffers a REST response until the command finishes, so an unbounded monitor does not error and does
    /// not return a partial reading — it returns <b>nothing at all</b>, for as long as the caller waits.
    /// Measured on 7.23.2: <c>POST /rest/ping {"address":…}</c> and <c>POST /rest/interface/monitor-traffic
    /// {"interface":…}</c> both received 0 bytes in 8 s, and <c>POST /rest/tool/torch {"interface":…}</c>
    /// the same; adding <c>count</c>/<c>once</c>/<c>duration</c> makes each answer immediately.
    /// </remarks>
    [TestClass]
    public class RestMonitorSnapshotBoundTests
    {
        private static TikCommandDescriptor Descriptor(string commandText,
            params (string Name, string Value)[] items)
            => new TikCommandDescriptor(commandText, items
                .Select(i => (ITikCommandParameter)new TikCommandParameter(
                    i.Name, i.Value, TikCommandParameterFormat.NameValue))
                .ToList());

        private static string Bound(TikCommandDescriptor descriptor, string name)
            => descriptor.Parameters
                .Where(p => p.Name == name)
                .Select(p => p.Value)
                .FirstOrDefault();

        [TestMethod]
        public void EveryMonitorVerbHasABoundThatRouterOsAccepts()
        {
            var expected = new Dictionary<string, (string Name, string Value)>
            {
                ["ping"] = ("count", "1"),
                ["traceroute"] = ("count", "1"),
                ["profile"] = ("duration", "1"),
                // one second yields an empty frame — see the TikMonitorVerbs note
                ["torch"] = ("duration", "2"),
                ["monitor-traffic"] = ("once", ""),
                ["monitor"] = ("once", ""),
            };

            foreach (var kv in expected)
            {
                TikMonitorVerbs.SnapshotBound(kv.Key, out string name, out string value);
                Assert.AreEqual(kv.Value.Name, name, kv.Key);
                Assert.AreEqual(kv.Value.Value, value, kv.Key);
            }
        }

        [TestMethod]
        public void TheCliModifierIsRenderedFromTheSharedBound_NotADuplicateTable()
        {
            // A bare flag stays bare; a valued one becomes name=value. If the two tables ever diverge again,
            // a verb measured on one transport keeps the wrong bound on the other.
            Assert.AreEqual("once", CliMonitorVerbs.SnapshotModifier("monitor-traffic"));
            Assert.AreEqual("count=1", CliMonitorVerbs.SnapshotModifier("ping"));
            Assert.AreEqual("duration=1", CliMonitorVerbs.SnapshotModifier("profile"));
        }

        [TestMethod]
        public void AMonitorWithNoBound_GetsTheOneItsVerbTakes()
        {
            var bounded = RestConnection.ApplySnapshotBound(
                Descriptor("/interface/monitor-traffic", ("interface", "ether1")), "monitor-traffic");

            Assert.AreEqual("", Bound(bounded, "once"), "an unbounded monitor-traffic answers with nothing at all");
            Assert.AreEqual("ether1", Bound(bounded, "interface"), "the caller's own inputs must survive");
        }

        [TestMethod]
        public void ACallerSuppliedBound_IsNotOverridden()
        {
            var bounded = RestConnection.ApplySnapshotBound(
                Descriptor("/ping", ("address", "127.0.0.1"), ("count", "5")), "ping");

            Assert.AreEqual("5", Bound(bounded, "count"), "count=5 means five replies, not one");
            Assert.AreEqual(2, bounded.Parameters.Count, "and nothing was appended alongside it");
        }

        [TestMethod]
        public void ABoundArrivingInFilterFormIsStillRecognised()
        {
            // The read path rewrites Default-format parameters to Filter (TikGenericCommand.ResolveParamsForRead),
            // so the caller's own count can reach us spelled '?count' — appending a second one would send both.
            var descriptor = new TikCommandDescriptor("/ping", new List<ITikCommandParameter>
            {
                new TikCommandParameter("?address", "127.0.0.1", TikCommandParameterFormat.Filter),
                new TikCommandParameter("?count", "3", TikCommandParameterFormat.Filter),
            });

            var bounded = RestConnection.ApplySnapshotBound(descriptor, "ping");

            Assert.AreEqual(2, bounded.Parameters.Count);
            Assert.IsNull(Bound(bounded, "count"), "the caller's ?count is the bound — no second one is added");
        }
    }
}

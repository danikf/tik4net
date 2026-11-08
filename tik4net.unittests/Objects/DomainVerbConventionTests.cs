using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Ip;
using tik4net.Objects.Ip.DhcpServer;
using tik4net.Objects.Ip.Dns;
using tik4net.Objects.Tool;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// ARCHITECTURE <i>Adding an entity</i> rule 12: a domain verb is an extension on <see cref="ITikConnection"/>
    /// in <c>&lt;Entity&gt;ConnectionExtensions</c>, spelled like CRUD — <c>connection.Renew(client)</c> for a row,
    /// <c>connection.FlushDnsCache()</c> for a menu. The entity class carries no verb.
    /// </summary>
    [TestClass]
    public class DomainVerbConventionTests
    {
        // The 3.x-era verbs on entity classes, each an [Obsolete] forwarder to its extension.
        private static readonly string[] KnownForwarders =
        {
            "DhcpServerAlert.ResetAlert",
            "DhcpServerLease.CheckStatus",
            "DhcpServerLease.MakeStatic",
            "DnsCache.Flush",
            "IpDhcpClient.Release",
            "IpDhcpClient.Renew",
            "IpDhcpRelay.ResetCounters",
            "Log.Debug",
            "Log.Info",
            "Log.Warning",
            "Log.WriteErrorMessage",
            "ToolPing.Execute",
            "ToolTraceroute.Execute",
        };

        // An entity type that is itself [Obsolete] (AccountingSnapshot: RouterOS 6 only) already warns every caller of
        // its verbs, and goes as a whole when RouterOS 6 support does.
        private static IEnumerable<MethodInfo> VerbsOnEntityClasses()
            => EntityCatalogMarkdown.EntityTypes()
                .Where(t => t.GetCustomAttribute<ObsoleteAttribute>() == null)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(m => !m.IsSpecialName)
                .Where(m => m.GetParameters().Any(p => typeof(ITikConnection).IsAssignableFrom(p.ParameterType)));

        [TestMethod]
        public void AnEntityClassCarriesNoVerbExceptTheObsoleteForwarders()
        {
            var offenders = VerbsOnEntityClasses()
                .Where(m => m.GetCustomAttribute<ObsoleteAttribute>() == null)
                .Select(m => m.DeclaringType!.Name + "." + m.Name)
                .Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();

            Assert.AreEqual(0, offenders.Count,
                "a verb taking the connection belongs in <Entity>ConnectionExtensions as connection.<Verb>(entity) or "
                + "connection.<Verb><Noun>() (ARCHITECTURE rule 12): " + string.Join(", ", offenders));
        }

        [TestMethod]
        public void TheObsoleteForwardersAreExactlyTheKnownOnesAndNameTheirReplacement()
        {
            var obsolete = VerbsOnEntityClasses()
                .Where(m => m.GetCustomAttribute<ObsoleteAttribute>() != null)
                .ToList();

            CollectionAssert.AreEquivalent(KnownForwarders,
                obsolete.Select(m => m.DeclaringType!.Name + "." + m.Name).Distinct().ToList(),
                "a forwarder kept for 3.x/4.0 callers disappeared, or a new verb was added to an entity class");

            foreach (var m in obsolete)
            {
                var attr = m.GetCustomAttribute<ObsoleteAttribute>()!;
                Assert.IsFalse(attr.IsError, m.Name + " must stay a warning: it shipped in 3.x/4.0");
                StringAssert.StartsWith(attr.Message, "Use connection.", m.DeclaringType!.Name + "." + m.Name);
            }
        }

        // ── each extension sends exactly what the obsolete spelling sent ───────────────────────────

        private static string[] SentBy(Action<TikFakeConnection> verb)
        {
            var fake = new TikFakeConnection().WithNonQuery(_ => true);
            verb(fake);
            Assert.AreEqual(1, fake.SentCommands.Count);
            return fake.SentCommands[0];
        }

        private static void AssertSameCommand(string expectedPath, Action<TikFakeConnection> extension,
            Action<TikFakeConnection> obsolete)
        {
            var sentByExtension = SentBy(extension);
            Assert.AreEqual(expectedPath, sentByExtension[0]);
            CollectionAssert.AreEqual(SentBy(obsolete), sentByExtension);
        }

#pragma warning disable CS0618 // these tests exist to prove the obsolete forwarders still send what the extensions send
        [TestMethod]
        public void RowVerbsSendTheRowsIdAsTheObsoleteInstanceMethodsDid()
        {
            var client = new IpDhcpClient().WithId("*1");
            AssertSameCommand("/ip/dhcp-client/release", c => c.Release(client), c => client.Release(c));
            AssertSameCommand("/ip/dhcp-client/renew", c => c.Renew(client), c => client.Renew(c));

            var lease = new DhcpServerLease().WithId("*2");
            AssertSameCommand("/ip/dhcp-server/lease/check-status", c => c.CheckStatus(lease), c => lease.CheckStatus(c));
            AssertSameCommand("/ip/dhcp-server/lease/make-static", c => c.MakeStatic(lease), c => lease.MakeStatic(c));

            var alert = new DhcpServerAlert().WithId("*3");
            AssertSameCommand("/ip/dhcp-server/alert/reset-alert", c => c.ResetAlert(alert), c => alert.ResetAlert(c));

            var relay = new IpDhcpRelay().WithId("*4");
            AssertSameCommand("/ip/dhcp-relay/reset-counters", c => c.ResetCounters(relay), c => relay.ResetCounters(c));

            CollectionAssert.Contains(SentBy(c => c.Renew(client)), "=.id=*1");
        }

        [TestMethod]
        public void MenuVerbsSendWhatTheObsoleteStaticMethodsDid()
        {
            AssertSameCommand("/ip/dns/cache/flush", c => c.FlushDnsCache(), c => DnsCache.Flush(c));
            AssertSameCommand("/log/debug", c => c.LogDebug("m"), c => Log.Debug(c, "m"));
            AssertSameCommand("/log/info", c => c.LogInfo("m"), c => Log.Info(c, "m"));
            AssertSameCommand("/log/warning", c => c.LogWarning("m"), c => Log.Warning(c, "m"));
            AssertSameCommand("/log/error", c => c.LogError("m"), c => Log.WriteErrorMessage(c, "m"));
        }

        [TestMethod]
        public void PingAndTracerouteSendWhatTheObsoleteExecuteDid()
        {
            AssertSameCommand("/ping", c => c.Ping("192.0.2.1", 2).ToList(), c => ToolPing.Execute(c, "192.0.2.1", 2).ToList());
            AssertSameCommand("/tool/traceroute", c => c.Traceroute("192.0.2.1").ToList(), c => ToolTraceroute.Execute(c, "192.0.2.1").ToList());
        }
#pragma warning restore CS0618

        [TestMethod]
        public void ARowVerbRefusesANullRow()
        {
            var fake = new TikFakeConnection().WithNonQuery(_ => true);
            Assert.ThrowsException<ArgumentNullException>(() => fake.Renew((IpDhcpClient)null!));
            Assert.AreEqual(0, fake.SentCommands.Count);
        }
    }
}

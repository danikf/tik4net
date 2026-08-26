// TypedConnectionTests.cs — what a transport can do, answered by the compiler.
//
// The capability model is a runtime one and stays: Supports()/Require() are what a caller uses when the
// transport comes from config. But a caller who NAMES the transport should not have to ask, cast, or find
// out by exception — so each transport's own factory returns a type carrying exactly that transport's
// facets, and the facets it lacks are absent rather than present and throwing.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.unittests.Connection
{
    [TestClass]
    public class TypedConnectionTests
    {
        // What each transport is expected to implement. This is the matrix the composite interfaces encode,
        // stated once more in a form the interfaces cannot satisfy by construction.
        private static readonly Dictionary<TikConnectionType, Type[]> Expected =
            new Dictionary<TikConnectionType, Type[]>
            {
                [TikConnectionType.Api] = new[] { typeof(ITikApiConnection) },
                [TikConnectionType.ApiSsl] = new[] { typeof(ITikApiConnection) },
                [TikConnectionType.Rest] = new[] { typeof(ITikRestConnection) },
                [TikConnectionType.RestSsl] = new[] { typeof(ITikRestConnection) },
                [TikConnectionType.Telnet] = new[] { typeof(ITikCliConnection) },
                [TikConnectionType.WinboxCli] = new[] { typeof(ITikCliConnection) },
                [TikConnectionType.MacTelnet] = new[] { typeof(ITikCliConnection), typeof(ITikMacCliConnection) },
                [TikConnectionType.WinboxCliMac] = new[] { typeof(ITikCliConnection), typeof(ITikMacCliConnection) },
                [TikConnectionType.WinboxNative] = new[] { typeof(ITikWinboxNativeConnection) },
                [TikConnectionType.WinboxNativeMac] = new[] { typeof(ITikWinboxNativeConnection), typeof(ITikWinboxNativeMacConnection) },
            };

        [TestMethod]
        public void EachTransportCarriesItsTypedInterface()
        {
            var wrong = new List<string>();

            foreach (var kv in Expected)
                using (var conn = ConnectionFactory.CreateConnection(kv.Key))
                    foreach (var iface in kv.Value)
                        if (!iface.IsInstanceOfType(conn))
                            wrong.Add($"{kv.Key} does not implement {iface.Name}");

            Assert.AreEqual(0, wrong.Count, string.Join(Environment.NewLine, wrong));
        }

        [TestMethod]
        public void EveryConnectionTypeIsCovered()
        {
            var missing = Enum.GetValues(typeof(TikConnectionType))
                .Cast<TikConnectionType>()
                .Where(t => !Expected.ContainsKey(t))
                .Where(t => t != TikConnectionType.Ssh)   // satellite package, not referenced here
                .ToList();

            Assert.AreEqual(0, missing.Count,
                "a new transport needs a typed interface too: " + string.Join(", ", missing));
        }

        // ── The facets a transport lacks are absent, not throwing ─────────────

        [TestMethod]
        public void RestCarriesNeitherRawNorSafeMode()
        {
            using (var rest = ConnectionFactory.CreateConnection(TikConnectionType.Rest))
            {
                Assert.IsFalse(rest is ITikRawSentenceConnection, "no command language of its own");
                Assert.IsFalse(rest is ITikSafeModeConnection, "stateless — no session to bind a rollback to");
                Assert.IsFalse(rest is ITikTaggedConnection, "tagging is the binary API's");
            }
        }

        [TestMethod]
        public void OnlyTheBinaryApiCarriesTagging()
        {
            foreach (TikConnectionType type in Enum.GetValues(typeof(TikConnectionType)))
            {
                if (type == TikConnectionType.Ssh) continue;

                using (var conn = ConnectionFactory.CreateConnection(type))
                {
                    bool isApi = type == TikConnectionType.Api || type == TikConnectionType.ApiSsl;
                    Assert.AreEqual(isApi, conn is ITikTaggedConnection, type.ToString());
                }
            }
        }

        [TestMethod]
        public void OnlyTheMacTransportsCarryARouterMac()
        {
            var macTypes = new[] { TikConnectionType.MacTelnet, TikConnectionType.WinboxCliMac,
                                   TikConnectionType.WinboxNativeMac };

            foreach (TikConnectionType type in Enum.GetValues(typeof(TikConnectionType)))
            {
                if (type == TikConnectionType.Ssh) continue;

                using (var conn = ConnectionFactory.CreateConnection(type))
                    Assert.AreEqual(macTypes.Contains(type), conn is ITikMacLayerConnection, type.ToString());
            }
        }

        // ── The type and the flag answer the same question, where both can ────

        [TestMethod]
        public void TheTypedFacetsAgreeWithTheCapabilityFlags()
        {
            // The type says what the transport implements; Supports() also answers what the ROUTER allows,
            // which no type can know. Where the flag is purely a property of the transport, the two must not
            // disagree — a facet present with the flag clear would offer a call the library then refuses.
            foreach (TikConnectionType type in Enum.GetValues(typeof(TikConnectionType)))
            {
                if (type == TikConnectionType.Ssh) continue;

                using (var conn = ConnectionFactory.CreateConnection(type))
                {
                    Assert.AreEqual(conn.Supports(TikConnectionCapability.RawCommand),
                        conn is ITikRawSentenceConnection, $"{type}: RawCommand vs ITikRawSentenceConnection");

                    Assert.AreEqual(conn.Supports(TikConnectionCapability.SafeMode),
                        conn is ITikSafeModeConnection, $"{type}: SafeMode vs ITikSafeModeConnection");

                    Assert.AreEqual(conn.Supports(TikConnectionCapability.Tagging),
                        conn is ITikTaggedConnection, $"{type}: Tagging vs ITikTaggedConnection");
                }
            }
        }

        // ── The shims really are gone ─────────────────────────────────────────

        [TestMethod]
        public void NoConvenienceShimsHangOffITikConnection()
        {
            // TikRawSentenceExtensions.CallCommandSync(this ITikConnection) and the SafeMode* equivalents
            // made a call compile on every transport and throw on most of them — a compile error traded for
            // a runtime one. If something reintroduces that shape, this fails.
            var offenders = typeof(ITikConnection).Assembly
                .GetTypes()
                .Where(t => t.IsPublic && t.IsAbstract && t.IsSealed)   // static classes
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Where(m => m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false))
                .Where(m => m.GetParameters().Length > 0
                            && m.GetParameters()[0].ParameterType == typeof(ITikConnection))
                .Where(m => m.Name.StartsWith("CallCommandSync", StringComparison.Ordinal)
                            || m.Name.StartsWith("SafeMode", StringComparison.Ordinal)
                            || m.Name.StartsWith("As", StringComparison.Ordinal))
                .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
                .Distinct()
                .ToList();

            Assert.AreEqual(0, offenders.Count,
                "these extend ITikConnection with a facet call that only some transports can honour; put "
                + "them on the facet interface instead: " + string.Join(", ", offenders));
        }
    }
}

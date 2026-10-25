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

        // Extensions on ITikConnection that may check a capability, each with the reason the check is not a shim.
        private static readonly Dictionary<string, string> CapabilityCheckAllowed = new Dictionary<string, string>
        {
            // Its facet, ITikMenuSchemaConnection, is internal, and whether a router can describe a menu depends on
            // its version (RouterOS 6 over the API cannot): the case Supports() is for, not one a type can answer.
            ["TikMenuSchemaExtensions.DescribeMenu"] = "internal facet; the answer depends on the router version",
        };

        [TestMethod]
        public void NoConvenienceShimsHangOffITikConnection()
        {
            // An extension on ITikConnection that only some transports can honour makes a call compile on every
            // transport and throw on the others — a compile error traded for a runtime one. Two checks: the names
            // of the facet calls, and the shape any such shim has whatever its name, a capability check in its
            // body (a call to TikConnectionCapabilityExtensions.Require).
            var extensions = typeof(ITikConnection).Assembly
                .GetTypes()
                .Where(t => t.IsPublic && t.IsAbstract && t.IsSealed)   // static classes
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Where(m => m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false))
                .Where(m => m.GetParameters().Length > 0
                            && m.GetParameters()[0].ParameterType == typeof(ITikConnection))
                .ToList();

            var named = extensions
                .Where(m => m.Name.StartsWith("CallCommandSync", StringComparison.Ordinal)
                            || m.Name.StartsWith("SafeMode", StringComparison.Ordinal)
                            || m.Name.StartsWith("As", StringComparison.Ordinal))
                .Select(m => $"{m.DeclaringType!.Name}.{m.Name}");

            var require = typeof(TikConnectionCapabilityExtensions).GetMethod(
                nameof(TikConnectionCapabilityExtensions.Require))!;
            var gated = extensions
                .Where(m => IlCalls(m).Contains(require))
                .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
                .Where(name => !CapabilityCheckAllowed.ContainsKey(name));

            var offenders = named.Concat(gated).Distinct().ToList();

            Assert.AreEqual(0, offenders.Count,
                "these extend ITikConnection with a call that only some transports can honour; put "
                + "them on the facet interface instead: " + string.Join(", ", offenders));
        }

        [TestMethod]
        public void TheCapabilityScanSeesTheAllowedCheck()
        {
            // The allow-list entry is a real capability check, so the IL scan above is not passing because it
            // finds nothing at all.
            var require = typeof(TikConnectionCapabilityExtensions).GetMethod(
                nameof(TikConnectionCapabilityExtensions.Require))!;
            var describe = typeof(TikMenuSchemaExtensions).GetMethod(nameof(TikMenuSchemaExtensions.DescribeMenu),
                new[] { typeof(ITikConnection), typeof(string) })!;

            CollectionAssert.Contains(IlCalls(describe).ToList(), require);
        }

        // The methods a method's body calls (call / callvirt), including the body of an async or iterator state
        // machine it starts.
        private static IEnumerable<MethodBase> IlCalls(MethodInfo method)
        {
            var bodies = new List<MethodBase> { method };
            var stateMachine = method.GetCustomAttributes()
                .OfType<System.Runtime.CompilerServices.StateMachineAttribute>()
                .FirstOrDefault();
            if (stateMachine != null)
                bodies.Add(stateMachine.StateMachineType.GetMethod("MoveNext",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!);

            foreach (var body in bodies)
            {
                var il = body.GetMethodBody()?.GetILAsByteArray();
                if (il == null)
                    continue;
                foreach (int token in CallTokens(il))
                {
                    MethodBase callee;
                    try
                    {
                        callee = body.Module.ResolveMethod(token,
                            body.DeclaringType?.IsGenericType == true ? body.DeclaringType.GetGenericArguments() : null,
                            body.IsGenericMethod ? body.GetGenericArguments() : null);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }
                    if (callee is MethodInfo info && info.IsGenericMethod)
                        callee = info.GetGenericMethodDefinition();
                    if (callee != null)
                        yield return callee;
                }
            }
        }

        private static readonly Dictionary<short, System.Reflection.Emit.OpCode> OpCodesByValue =
            typeof(System.Reflection.Emit.OpCodes)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!)
                .ToDictionary(o => o.Value);

        // Walks the IL by the opcode table, so an operand byte is never read as an opcode.
        private static IEnumerable<int> CallTokens(byte[] il)
        {
            int i = 0;
            while (i < il.Length)
            {
                short value = il[i] == 0xFE ? (short)(0xFE00 | il[i + 1]) : il[i];
                var op = OpCodesByValue[value];
                i += op.Size;
                if (op == System.Reflection.Emit.OpCodes.Call || op == System.Reflection.Emit.OpCodes.Callvirt)
                    yield return BitConverter.ToInt32(il, i);
                switch (op.OperandType)
                {
                    case System.Reflection.Emit.OperandType.InlineNone: break;
                    case System.Reflection.Emit.OperandType.ShortInlineBrTarget:
                    case System.Reflection.Emit.OperandType.ShortInlineI:
                    case System.Reflection.Emit.OperandType.ShortInlineVar: i += 1; break;
                    case System.Reflection.Emit.OperandType.InlineVar: i += 2; break;
                    case System.Reflection.Emit.OperandType.InlineI8:
                    case System.Reflection.Emit.OperandType.InlineR: i += 8; break;
                    case System.Reflection.Emit.OperandType.InlineSwitch:
                        i += 4 + 4 * BitConverter.ToInt32(il, i); break;
                    default: i += 4; break;
                }
            }
        }
    }
}

using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects.Ip.Ipsec;
using tik4net.Testing;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// <c>/ip/ipsec/key/rsa generate-key key-size=</c> takes exactly 2048, 4096 and 8192 (CLI completion, 7.25.1), so the
    /// key size is an <see cref="IpsecKeySize"/>.
    /// </summary>
    [TestClass]
    public class IpsecKeyTests
    {
        private static TikFakeConnection Fake()
            => new TikFakeConnection().WithNonQuery(rows => rows.First() == "/ip/ipsec/key/rsa/generate-key");

        [DataTestMethod]
        [DataRow(IpsecKeySize.Bits2048, "2048")]
        [DataRow(IpsecKeySize.Bits4096, "4096")]
        [DataRow(IpsecKeySize.Bits8192, "8192")]
        public void TheKeySizeIsSentAsTheRoutersNumber(IpsecKeySize size, string word)
        {
            var connection = Fake();

            connection.GenerateIpsecKey("n", size);

            CollectionAssert.Contains(connection.SentCommands.Single(), "=key-size=" + word);
            CollectionAssert.Contains(connection.SentCommands.Single(), "=name=n");
        }

        [TestMethod]
        public void TheShortCallBindsToTheEnumOverloadWith2048()
        {
            var connection = Fake();
            Action<tik4net.ITikConnection> call = c => c.GenerateIpsecKey("n");

            call(connection);

            CollectionAssert.Contains(connection.SentCommands.Single(), "=key-size=2048");
            var target = CalledMethods(call.Method).Single(m => m.Name == nameof(IpsecKeyConnectionExtensions.GenerateIpsecKey));
            Assert.AreEqual(typeof(IpsecKeySize), target.GetParameters()[2].ParameterType,
                "GenerateIpsecKey(c, name) must bind to the IpsecKeySize overload, not the obsolete string one");
        }

        [TestMethod]
        public void TheStringOverloadIsObsoleteAndHasNoDefault()
        {
            var overload = typeof(IpsecKeyConnectionExtensions).GetMethods()
                .Single(m => m.Name == nameof(IpsecKeyConnectionExtensions.GenerateIpsecKey) && m.GetParameters()[2].ParameterType == typeof(string));

            var obsolete = overload.GetCustomAttribute<ObsoleteAttribute>();
            Assert.IsNotNull(obsolete);
            Assert.IsFalse(obsolete!.IsError);
            Assert.IsFalse(overload.GetParameters()[2].HasDefaultValue, "a default on both overloads makes GenerateIpsecKey(c, name) ambiguous");
        }

        // The methods a compiled lambda calls, read from its IL: an expression tree cannot hold a call that leaves an
        // optional argument out, and that is the call under test.
        private static MethodBase[] CalledMethods(MethodInfo method)
        {
            byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
            var called = new System.Collections.Generic.List<MethodBase>();
            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != OpCodes.Call.Value && il[i] != OpCodes.Callvirt.Value)
                    continue;
                int token = BitConverter.ToInt32(il, i + 1);
                try { called.Add(method.Module.ResolveMethod(token)!); i += 4; }
                catch (ArgumentException) { /* not a method token: an operand byte that looked like the opcode */ }
            }
            return called.ToArray();
        }
    }
}

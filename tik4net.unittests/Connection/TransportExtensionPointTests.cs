using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Connection;

namespace tik4net.unittests.Connection
{
    /// <summary>
    /// Pins that a transport can be written from <b>outside</b> this assembly — that
    /// <see cref="TikCommandConnectionBase"/> is public in substance and not only in name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It was public in name only. The class was <c>public abstract</c> while <c>RunPrint</c>,
    /// <c>RunAdd</c> and <c>RunNonQuery</c> were <c>internal abstract</c>, and a public abstract class with
    /// internal abstract members cannot be derived anywhere else — the compiler has no way to let a
    /// subclass satisfy a member it cannot see. <c>tik4net.ssh</c> only worked because it is one of four
    /// <c>InternalsVisibleTo</c> friends. The types said "extend me" and the accessibility said no, and
    /// nothing announced that except a confusing compiler error for whoever tried.
    /// </para>
    /// <para>
    /// This test cannot literally compile a foreign transport — the unit-test project is itself a friend
    /// assembly, so it sees everything either way, and a test that derived a transport here would pass just
    /// as happily with the hooks internal again. It checks the accessibility itself instead, which is the
    /// property that actually decides the question.
    /// </para>
    /// </remarks>
    [TestClass]
    public class TransportExtensionPointTests
    {
        private static readonly string[] RequiredHooks =
            { "RunPrint", "RunAdd", "RunNonQuery", "RunRawText" };

        private static readonly string[] OptionalAsyncHooks =
            { "RunPrintAsync", "RunAddAsync", "RunNonQueryAsync", "RunRawTextAsync" };

        private static MethodInfo Hook(string name)
        {
            var m = typeof(TikCommandConnectionBase).GetMethod(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            Assert.IsNotNull(m, $"{name} is missing from TikCommandConnectionBase");
            return m!;
        }

        [TestMethod]
        public void TheTransportHooksAreReachableBySubclassesOutsideThisAssembly()
        {
            var wrong = new List<string>();

            foreach (string name in RequiredHooks.Concat(OptionalAsyncHooks))
            {
                var m = Hook(name);
                // IsFamily = protected; IsFamilyOrAssembly = protected internal — both are reachable from a
                // foreign subclass. IsAssembly (internal) is not, and is the state being guarded against.
                if (!(m.IsFamily || m.IsFamilyOrAssembly))
                    wrong.Add($"{name} is {(m.IsAssembly ? "internal" : "not protected")} — a transport in "
                              + "another assembly cannot implement it");
            }

            Assert.AreEqual(0, wrong.Count,
                "TikCommandConnectionBase is public, so its hooks must be protected or the class cannot be "
                + "derived outside tik4net:" + Environment.NewLine + string.Join(Environment.NewLine, wrong));
        }

        [TestMethod]
        public void TheTypesThoseHooksSpeakInArePublic()
        {
            // A protected hook whose parameter or return type is internal is still underivable outside, and
            // fails in a much more confusing way. These two are the whole vocabulary of the extension point.
            foreach (var t in new[] { typeof(TikCommandDescriptor), typeof(TikRecordSentence) })
                Assert.IsTrue(t.IsPublic, $"{t.Name} appears in the hook signatures and must be public");

            Assert.IsTrue(typeof(TikCommandDescriptor).GetConstructors().Any(c => c.IsPublic),
                "a transport receives descriptors, but the tests and any consumer also need to construct one");
            Assert.IsTrue(typeof(TikRecordSentence).GetConstructors().Any(c => c.IsPublic),
                "RunPrint returns these, so a foreign transport must be able to construct them");
        }

        [TestMethod]
        public void TheRequiredHooksAreAbstractAndTheAsyncOnesAreNot()
        {
            // The split is the contract: three hooks every transport must answer, four it may. The async
            // defaults throw rather than wrapping the sync hook in Task.Run, so a transport that cannot
            // await its I/O declines AsyncCommands instead of pretending.
            foreach (string name in new[] { "RunPrint", "RunAdd", "RunNonQuery" })
                Assert.IsTrue(Hook(name).IsAbstract, $"{name} should be abstract — every transport needs it");

            foreach (string name in OptionalAsyncHooks.Concat(new[] { "RunRawText" }))
                Assert.IsFalse(Hook(name).IsAbstract,
                    $"{name} should be virtual with a throwing default, not abstract — it is optional");
        }

        [TestMethod]
        public void TheInternalDispatchShimsStayInternal()
        {
            // The shims exist only so TikGenericCommand can reach the protected hooks from another class.
            // They are not part of the extension point and must not drift into the public surface.
            foreach (string name in RequiredHooks.Concat(OptionalAsyncHooks))
            {
                var shim = typeof(TikCommandConnectionBase).GetMethod("Invoke" + name,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);

                Assert.IsNotNull(shim, $"Invoke{name} is missing — every hook needs its dispatch shim");
                Assert.IsTrue(shim!.IsAssembly, $"Invoke{name} must stay internal");
            }
        }
    }
}

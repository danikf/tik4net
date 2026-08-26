using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// Keeps the <c>Async</c> suffix meaning one thing in the O/R mapper's public surface: the method
    /// returns something you can <c>await</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It did not. <c>LoadAsync</c> and <c>LoadListenAsync</c> started a command on a background thread and
    /// handed back a running <see cref="ITikCommand"/> (or nothing at all) — while
    /// <c>TikConnectionAsyncExtensions</c> offered <c>LoadListAsync</c>, <c>LoadAllAsync</c> and
    /// <c>SaveAsync</c>, which return <see cref="Task"/>. Both are extension methods on
    /// <see cref="ITikConnection"/> in the same namespace, so they merge into one completion list and a
    /// caller typing <c>connection.Load</c> got both spellings with nothing to separate them. The wiki had
    /// to carry a "not part of this family despite the name" warning on two pages, which is the tell that
    /// the name was doing the damage.
    /// </para>
    /// <para>
    /// The callback pair is now <c>LoadWithCallback</c> / <c>LoadListenWithCallback</c>. The old names
    /// survive as <see cref="ObsoleteAttribute"/> forwarders, so this test allows those two specifically
    /// and no others — a new non-awaitable <c>…Async</c> fails.
    /// </para>
    /// </remarks>
    [TestClass]
    public class AsyncNamingConventionTests
    {
        private static IEnumerable<MethodInfo> PublicMapperMethods()
            => typeof(tik4net.Objects.TikConnectionExtensions).Assembly
                .GetTypes()
                .Where(t => t.IsPublic && t.IsAbstract && t.IsSealed)   // static classes
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));

        private static bool IsAwaitable(Type t)
            => typeof(Task).IsAssignableFrom(t)
               || t.Name == "ValueTask" || (t.IsGenericType && t.Name.StartsWith("ValueTask", StringComparison.Ordinal))
               || t.Name.StartsWith("IAsyncEnumerable", StringComparison.Ordinal);

        [TestMethod]
        public void AMethodNamedAsyncReturnsSomethingAwaitable()
        {
            var offenders = PublicMapperMethods()
                .Where(m => m.Name.EndsWith("Async", StringComparison.Ordinal))
                .Where(m => !IsAwaitable(m.ReturnType))
                // The two deprecated forwarders, kept so existing code compiles. They carry [Obsolete]
                // naming their replacement, so nobody reaches them without being told.
                .Where(m => m.GetCustomAttribute<ObsoleteAttribute>() == null)
                .Select(m => $"{m.DeclaringType!.Name}.{m.Name} returns {m.ReturnType.Name}")
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            Assert.AreEqual(0, offenders.Count,
                "these are named '…Async' but cannot be awaited, so they sort next to the Task-based methods "
                + "in IntelliSense and read as if 'await' would work. Name a callback/handle-returning method "
                + "for what it does (…WithCallback):" + Environment.NewLine
                + string.Join(Environment.NewLine, offenders));
        }

        [TestMethod]
        public void TheDeprecatedCallbackNamesStillExistAndPointSomewhere()
        {
            // The rename is only safe while the old spelling keeps compiling. If these disappear, that was a
            // breaking change and belongs in a major version with a note in the wiki's History page.
            var deprecated = PublicMapperMethods()
                .Where(m => m.GetCustomAttribute<ObsoleteAttribute>() != null)
                .Where(m => m.Name == "LoadAsync" || m.Name == "LoadListenAsync")
                .ToList();

            Assert.AreEqual(4, deprecated.Count,
                "expected the four forwarders (LoadAsync/LoadListenAsync on ITikConnection and ITikCommand)");

            foreach (var m in deprecated)
                StringAssert.Contains(m.GetCustomAttribute<ObsoleteAttribute>()!.Message!, "WithCallback",
                    $"{m.Name}'s obsolete message should name its replacement");
        }

        [TestMethod]
        public void TheCallbackLoadsAreReachableUnderTheirNewNames()
        {
            foreach (string name in new[] { "LoadWithCallback", "LoadListenWithCallback" })
            {
                var found = PublicMapperMethods().Where(m => m.Name == name).ToList();
                Assert.AreEqual(2, found.Count,
                    $"{name} should exist on both ITikConnection and ITikCommand");
                Assert.IsTrue(found.All(m => m.GetCustomAttribute<ObsoleteAttribute>() == null),
                    $"{name} is the replacement and must not itself be obsolete");
            }
        }
    }
}

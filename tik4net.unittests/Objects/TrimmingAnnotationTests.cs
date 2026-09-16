using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Objects;
using tik4net.Objects.Tool;

namespace tik4net.unittests.Objects
{
    /// <summary>
    /// Pins the trimming/AOT split: <c>tik4net.dll</c> is trim-safe and says so, <c>tik4net.objects.dll</c> is
    /// not and says so at every public door into the mapper.
    /// </summary>
    /// <remarks>
    /// The trim analyzer already fails the build when an annotated member is called from an unannotated one,
    /// so these do not re-check propagation. They cover what the analyzer cannot see: a public member that
    /// reaches the mapper through a delegate, a cached <see cref="Type"/> or <c>dynamic</c> compiles clean and
    /// would tell a trimmed consumer nothing. Both rules held on the code they were written against once the
    /// metadata-argument exemption was in — they pin the contract rather than demonstrate a fix.
    /// </remarks>
    [TestClass]
    public class TrimmingAnnotationTests
    {
        private static readonly Assembly Objects = typeof(TikEntityAttribute).Assembly;

        [TestMethod]
        public void OnlyTheCoreAssemblyDeclaresItselfTrimmable()
        {
#if NET8_0_OR_GREATER
            // IsAotCompatible is set on the net8.0 leg only; netstandard2.0 consumers have no trimmer.
            Assert.AreEqual("True", AssemblyMetadata(typeof(ITikConnection).Assembly, "IsTrimmable"),
                "tik4net.dll lost IsAotCompatible");
#endif
            Assert.IsNull(AssemblyMetadata(Objects, "IsTrimmable"),
                "tik4net.objects.dll reflects over the caller's entity types and must not claim to be trimmable");
        }

        [TestMethod]
        public void EveryPublicMemberThatProducesEntitiesWarnsATrimmedCaller()
        {
            // An instance of a [TikEntity] type — or of a type parameter standing for one — can only be filled
            // from a router row by the mapper, so a public member that hands one back reflects, however it got there.
            var missing = PublicMethods()
                .Where(m => ProducesEntities(m.ReturnType) || m.IsGenericMethodDefinition)
                .Where(m => !TakesMetadata(m))
                .Where(m => !IsCovered(m))
                .Select(Describe)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            Assert.AreEqual(0, missing.Count,
                "public members of tik4net.objects that produce entities (or are generic over one) without "
                + "[RequiresUnreferencedCode] and [RequiresDynamicCode] on themselves or their type:\n  "
                + string.Join("\n  ", missing));
        }

        [TestMethod]
        public void TheRuleRecognisesTheShapesTheMapperReturns()
        {
            // Guards the test above against passing because ProducesEntities stopped matching anything.
            Assert.IsTrue(ProducesEntities(typeof(ToolPing)));
            Assert.IsTrue(ProducesEntities(typeof(IEnumerable<ToolPing>)));
            Assert.IsTrue(ProducesEntities(typeof(System.Threading.Tasks.Task<IList<ToolPing>>)));
            Assert.IsTrue(ProducesEntities(typeof(ToolPing[])));
            Assert.IsFalse(ProducesEntities(typeof(void)));
            Assert.IsFalse(ProducesEntities(typeof(string)));

            var ping = typeof(ToolPing).GetMethod(nameof(ToolPing.Execute))!;
            Assert.IsTrue(ProducesEntities(ping.ReturnType));
            Assert.IsTrue(IsCovered(ping));
            Assert.IsTrue(PublicMethods().Count(m => m.IsGenericMethodDefinition) > 20,
                "the generic mapper surface (LoadAll<T>, Save<T>, …) was not found");
        }

        private static IEnumerable<MethodInfo> PublicMethods()
            => Objects.GetExportedTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(m => !m.IsSpecialName)                  // property accessors: an entity's own getters
                .Where(m => m.DeclaringType != typeof(object))
                .Where(m => !IsObjectOverride(m));

        // A TikEntityMetadata cannot be obtained without passing an annotated member (TikEntityMetadataCache,
        // TikEntityMetadata's constructor), so a member that needs one as an argument has already warned its
        // caller — TikChangeTracker's snapshot and diff methods are the case this exists for.
        private static bool TakesMetadata(MethodInfo m)
            => m.GetParameters().Any(p => p.ParameterType == typeof(TikEntityMetadata));

        private static bool IsObjectOverride(MethodInfo m)
            => m.Name == nameof(ToString) || m.Name == nameof(Equals) || m.Name == nameof(GetHashCode);

        private static bool ProducesEntities(Type type)
        {
            if (type.IsGenericParameter) return true;
            if (type.IsArray) return ProducesEntities(type.GetElementType()!);
            if (type.GetCustomAttributes(typeof(TikEntityAttribute), false).Length > 0) return true;
            return type.IsGenericType && type.GetGenericArguments().Any(ProducesEntities);
        }

        // Matched by name: the netstandard2.0 leg carries internal copies of both attributes (Compat/).
        private static bool IsCovered(MethodInfo m)
            => HasBoth(m.GetCustomAttributes(false)) || HasBoth(m.DeclaringType!.GetCustomAttributes(false));

        private static bool HasBoth(object[] attributes)
        {
            var names = new HashSet<string>(attributes.Select(a => a.GetType().Name));
            return names.Contains("RequiresUnreferencedCodeAttribute") && names.Contains("RequiresDynamicCodeAttribute");
        }

        private static string AssemblyMetadata(Assembly assembly, string key)
            => assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;

        private static string Describe(MethodInfo m) => m.DeclaringType!.FullName + "." + m.Name;
    }
}

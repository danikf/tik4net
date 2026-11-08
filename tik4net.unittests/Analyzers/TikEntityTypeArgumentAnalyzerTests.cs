using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Analyzers;

namespace tik4net.unittests.Analyzers
{
    /// <summary>
    /// TIK004: a mapper generic called with a type argument that has no <c>[TikEntity]</c>, which compiles and throws
    /// <c>ArgumentException</c> at run time.
    /// </summary>
    /// <remarks>
    /// The snippets declare stand-ins for the mapper's surface in the real namespaces, so the analyzer sees the same
    /// names it sees in a consumer's build. <see cref="TikEntityTypeArgumentCoverageTests"/> holds the real surface to
    /// the names the analyzer keys on.
    /// </remarks>
    [TestClass]
    public class TikEntityTypeArgumentAnalyzerTests
    {
        private const string AttributeInherited = @"[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false)]";
        private const string AttributeNotInherited = @"[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]";

        private static string Stubs(string attributeUsage = AttributeInherited) => @"
using System.Collections.Generic;
using System.Threading.Tasks;
namespace tik4net { public interface ITikConnection { } public interface ITikCommand { } }
namespace tik4net.Objects
{
    " + attributeUsage + @"
    public sealed class TikEntityAttribute : System.Attribute { public TikEntityAttribute(string path) { } }
    public class TikEntityMetadata { }
    public static class TikEntityMetadataCache { public static TikEntityMetadata GetMetadata<TEntity>() => null; }
    public static class TikConnectionExtensions
    {
        public static IEnumerable<TEntity> LoadAll<TEntity>(this ITikConnection connection) where TEntity : new() => null;
        public static void Save<TEntity>(this ITikConnection connection, TEntity entity) where TEntity : new() { }
        public static TikListMerge<TEntity> CreateMerge<TEntity>(this ITikConnection connection, IEnumerable<TEntity> expected, IEnumerable<TEntity> original) where TEntity : new() => null;
        public static string DescribeMenu<TEntity>(this ITikConnection connection) => null;
        public static T GetValueOrDefault<T>(this ITikConnection connection, T fallback) => fallback;
    }
    public static class TikConnectionAsyncExtensions
    {
        public static Task<IEnumerable<TEntity>> LoadAllAsync<TEntity>(this ITikConnection connection) where TEntity : new() => null;
    }
    public static class TikCommandExtensions
    {
        public static IEnumerable<TEntity> LoadList<TEntity>(this ITikCommand command) where TEntity : new() => null;
    }
    public static class TikEntityObjectsExtensions
    {
        public static string EntityToString<TEntity>(this TEntity entity) => null;
        public static bool EntityEquals<TEntity>(this TEntity entity, TEntity other) => false;
    }
    public static class TikFieldStrictness { public static void EnsureStrict<TEntity>(this TEntity entity) { } }
    public static class TikRouterOsRange { public static bool Has<TEntity>(System.Version routerOs, string propertyName = null) => false; }
    public class TikListMerge<TEntity> { }
}
namespace tik4net.Objects.Tracking
{
    public static class Snapshots { public static void Take<TEntity>(TEntity entity) { } }
    public class TikChangeTracker { public bool HasChanges<TEntity>(TEntity entity, tik4net.Objects.TikEntityMetadata metadata) => false; }
}
namespace tik4net.Testing
{
    public class TikFakeConnection : tik4net.ITikConnection
    {
        public TikFakeConnection WithEntities<TEntity>(params TEntity[] entities) where TEntity : new() => this;
    }
}
namespace Elsewhere
{
    public static class Repository { public static TEntity Get<TEntity>() where TEntity : new() => new TEntity(); }
}
namespace Consumer
{
    using tik4net;
    using tik4net.Objects;
    using tik4net.Objects.Tracking;
    using tik4net.Testing;

    [TikEntity(""/ip/address"")] public class Address { }
    public class DerivedAddress : Address { }
    public class Dto { }
    public struct Point { }
    public interface IRow { }
";

        private static string Body(string statements, string attributeUsage = AttributeInherited) => Stubs(attributeUsage) + @"
    public class Subject
    {
        public void Run(ITikConnection connection, ITikCommand command, TikFakeConnection fake, Address address, Dto dto,
                        IEnumerable<Address> addresses, IEnumerable<Dto> dtos)
        {
            " + statements + @"
        }

        public void Generic<T>(ITikConnection connection, T entity) where T : new()
        {
            " + "GENERIC" + @"
        }
    }
}";

        [DataTestMethod]
        [DataRow("connection.LoadAll<Dto>();", "LoadAll<Dto>", "Dto", DisplayName = "an explicit type argument")]
        [DataRow("connection.LoadAllAsync<Dto>();", "LoadAllAsync<Dto>", "Dto", DisplayName = "the async variant")]
        [DataRow("command.LoadList<Dto>();", "LoadList<Dto>", "Dto", DisplayName = "the command variant")]
        [DataRow("connection.LoadAll<Point>();", "LoadAll<Point>", "Point", DisplayName = "a struct")]
        [DataRow("connection.LoadAll<List<Address>>();", "LoadAll<List<Address>>", "List<Address>", DisplayName = "a list of entities is not an entity")]
        [DataRow("connection.Save(dto);", "Save", "Dto", DisplayName = "an inferred type argument")]
        [DataRow("connection.CreateMerge(dtos, dtos);", "CreateMerge", "Dto", DisplayName = "CreateMerge")]
        [DataRow("connection.DescribeMenu<Dto>();", "DescribeMenu<Dto>", "Dto", DisplayName = "DescribeMenu")]
        [DataRow("TikEntityMetadataCache.GetMetadata<Dto>();", "GetMetadata<Dto>", "Dto", DisplayName = "the metadata cache")]
        [DataRow("TikRouterOsRange.Has<Dto>(new System.Version(7, 0));", "Has<Dto>", "Dto", DisplayName = "TikRouterOsRange.Has")]
        [DataRow("Snapshots.Take(dto);", "Take", "Dto", DisplayName = "a nested tik4net.Objects namespace")]
        [DataRow("fake.WithEntities(dto);", "WithEntities", "Dto", DisplayName = "tik4net.Testing")]
        [DataRow("TikEntityObjectsExtensions.EntityToString<IRow>(null);", "EntityToString<IRow>", "IRow", DisplayName = "an interface")]
        [DataRow("System.Func<IEnumerable<Dto>> f = connection.LoadAll<Dto>;", "LoadAll<Dto>", "Dto", DisplayName = "a method group")]
        public void ATypeWithoutTikEntityIsReported(string statement, string span, string type)
        {
            var diagnostics = Analyze(Body(statement));

            Assert.AreEqual(1, diagnostics.Length, string.Join("\n", diagnostics.Select(d => d.ToString())));
            Assert.AreEqual("TIK004", diagnostics[0].Id);
            Assert.AreEqual(span, SpanText(diagnostics[0]));
            StringAssert.Contains(diagnostics[0].GetMessage(), "'" + type + "'");
        }

        [DataTestMethod]
        [DataRow("\"abc\".EntityToString();", "EntityToString", "string", DisplayName = "a string receiver")]
        [DataRow("42.EnsureStrict();", "EnsureStrict", "int", DisplayName = "an int receiver")]
        [DataRow("dto.EntityToString();", "EntityToString", "Dto", DisplayName = "a class receiver")]
        [DataRow("dto.EntityEquals(dto);", "EntityEquals", "Dto", DisplayName = "a receiver and an argument")]
        [DataRow("TikEntityObjectsExtensions.EntityToString(dto);", "EntityToString", "Dto", DisplayName = "called as a static method")]
        public void AnExtensionCallOnAReceiverThatIsNotAnEntityIsReported(string statement, string span, string type)
        {
            var diagnostics = Analyze(Body(statement));

            Assert.AreEqual(1, diagnostics.Length, string.Join("\n", diagnostics.Select(d => d.ToString())));
            Assert.AreEqual("TIK004", diagnostics[0].Id);
            Assert.AreEqual(span, SpanText(diagnostics[0]));
            StringAssert.Contains(diagnostics[0].GetMessage(), "'" + type + "'");
        }

        [DataTestMethod]
        [DataRow("connection.LoadAll<Address>();", DisplayName = "an entity")]
        [DataRow("connection.LoadAllAsync<Address>();", DisplayName = "an entity, async")]
        [DataRow("connection.Save(address);", DisplayName = "an inferred entity")]
        [DataRow("connection.CreateMerge(addresses, addresses);", DisplayName = "CreateMerge of entities")]
        [DataRow("address.EntityToString();", DisplayName = "an entity receiver")]
        [DataRow("fake.WithEntities(address);", DisplayName = "tik4net.Testing with an entity")]
        [DataRow("connection.LoadAll<DerivedAddress>();", DisplayName = "an entity attribute inherited from the base class")]
        [DataRow("connection.GetValueOrDefault(dto);", DisplayName = "a tik4net generic whose parameter is not TEntity")]
        [DataRow("Elsewhere.Repository.Get<Dto>();", DisplayName = "a TEntity method outside tik4net")]
        [DataRow("new TikChangeTracker().HasChanges((object)dto, TikEntityMetadataCache.GetMetadata<Address>());", DisplayName = "a method handed the metadata never reads the type argument")]
        public void AnEntityOrAnUnrelatedGenericIsNotReported(string statement)
        {
            var diagnostics = Analyze(Body(statement));

            Assert.AreEqual(0, diagnostics.Length, string.Join("\n", diagnostics.Select(d => d.ToString())));
        }

        [DataTestMethod]
        [DataRow("connection.LoadAll<T>();", DisplayName = "an explicit type parameter")]
        [DataRow("connection.Save(entity);", DisplayName = "an inferred type parameter")]
        [DataRow("entity.EntityToString();", DisplayName = "a receiver typed as the type parameter")]
        [DataRow("connection.LoadAll<List<T>>();", DisplayName = "a constructed type over a type parameter is still not an entity")]
        public void ATypeParameterOfTheCallersOwnGenericIsNotReportedUnlessWrapped(string statement)
        {
            var diagnostics = Analyze(Body("", AttributeInherited).Replace("GENERIC", statement));

            int expected = statement.Contains("List<T>") ? 1 : 0;
            Assert.AreEqual(expected, diagnostics.Length, string.Join("\n", diagnostics.Select(d => d.ToString())));
        }

        [TestMethod]
        public void AnAttributeThatIsNotInheritedDoesNotCoverADerivedClass()
        {
            var diagnostics = Analyze(Body("connection.LoadAll<DerivedAddress>(); connection.LoadAll<Address>();", AttributeNotInherited));

            Assert.AreEqual(1, diagnostics.Length, string.Join("\n", diagnostics.Select(d => d.ToString())));
            StringAssert.Contains(diagnostics[0].GetMessage(), "'DerivedAddress'");
        }

        [TestMethod]
        public void NothingIsReportedWithoutTikEntityAttributeInTheCompilation()
        {
            var diagnostics = Analyze("namespace tik4net.Objects { public static class X { public static void LoadAll<TEntity>() { } } } "
                + "class Dto { } class C { void M() => tik4net.Objects.X.LoadAll<Dto>(); }");

            Assert.AreEqual(0, diagnostics.Length);
        }

        [TestMethod]
        public void TheDiagnosticNamesTheMethodAndLinksTheWiki()
        {
            var diagnostic = Analyze(Body("connection.LoadAll<Dto>();")).Single();

            Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.AreEqual("Usage", diagnostic.Descriptor.Category);
            StringAssert.Contains(diagnostic.GetMessage(), "LoadAll");
            StringAssert.Contains(diagnostic.Descriptor.HelpLinkUri, "github.com/danikf/tik4net/wiki/");
        }

        private static ImmutableArray<Diagnostic> Analyze(string source)
        {
            var compilation = CSharpCompilation.Create("snippet", new[] { CSharpSyntaxTree.ParseText(source.Replace("GENERIC", "")) },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.AreEqual(0, errors.Count, "the snippet itself does not compile:\n" + string.Join("\n", errors));
            return compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TikEntityTypeArgumentAnalyzer()))
                .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
        }

        private static string SpanText(Diagnostic diagnostic)
            => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);
    }
}

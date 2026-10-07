using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using tik4net.Analyzers;

namespace tik4net.unittests.Analyzers
{
    /// <summary>
    /// TIK002: a <c>TikField&lt;T&gt;</c> handed to a null check that takes <c>object</c> is boxed and never null.
    /// </summary>
    /// <remarks>The snippets use the same stand-ins as <see cref="TikFieldObjectEqualityAnalyzerTests"/>.</remarks>
    [TestClass]
    public class TikFieldNullCheckAnalyzerTests
    {
        private const string Stubs = @"
namespace tik4net.Objects
{
    public readonly struct TikField<T>
    {
        public T Value => default(T);
        public static implicit operator TikField<T>(T value) => default(TikField<T>);
        public static bool operator ==(TikField<T> a, T b) => true;
        public static bool operator !=(TikField<T> a, T b) => false;
        public override bool Equals(object obj) => obj is TikField<T>;
        public override int GetHashCode() => 0;
    }
    public readonly struct TikValue<T>
    {
        public T Value => default(T);
        public static implicit operator TikValue<T>(T value) => default(TikValue<T>);
        public static bool operator ==(TikValue<T> a, T b) => true;
        public static bool operator !=(TikValue<T> a, T b) => false;
        public override bool Equals(object obj) => obj is TikValue<T>;
        public override int GetHashCode() => 0;
    }
}
static class Assert
{
    public static void AreEqual(object expected, object actual) { }
    public static void AreEqual<T>(T expected, T actual) { }
    public static void AreNotEqual(object notExpected, object actual) { }
    public static void IsNull(object value) { }
    public static void IsNotNull(object value, string message) { }
    public static void Null(object value) { }
    public static void NotNull(object value) { }
    public static void IsTrue(bool condition) { }
}
static class Check { public static void IsNull<T>(T value) { } }
class Row { public tik4net.Objects.TikField<string> Name; public tik4net.Objects.TikField<int?> Mtu; public object Any;
            public tik4net.Objects.TikValue<string> Item; }
";

        private static string Body(string statements) => Stubs + @"
class Subject
{
    void Run(Row row, Row other, bool flag)
    {
        " + statements + @"
    }
}";

        [DataTestMethod]
        [DataRow("Assert.IsNull(row.Name);", "row.Name", DisplayName = "MSTest IsNull")]
        [DataRow("Assert.IsNotNull(row.Mtu, \"mtu\");", "row.Mtu", DisplayName = "MSTest IsNotNull with a message")]
        [DataRow("Assert.Null(row.Name);", "row.Name", DisplayName = "xUnit Null")]
        [DataRow("Assert.NotNull(row.Item);", "row.Item", DisplayName = "a TikValue<T> in xUnit NotNull")]
        [DataRow("Assert.IsNull(flag ? row.Name : other.Name);", "flag ? row.Name : other.Name", DisplayName = "a conditional of two wrappers")]
        public void AWrapperHandedToANullCheckAsObjectIsReported(string statement, string wrapped)
        {
            var diagnostics = Analyze(Body(statement));

            Assert.AreEqual(1, diagnostics.Length, string.Join("\n", diagnostics.Select(d => d.ToString())));
            Assert.AreEqual("TIK002", diagnostics[0].Id);
            Assert.AreEqual(wrapped, SpanText(diagnostics[0]));
        }

        [DataTestMethod]
        [DataRow("Assert.IsNull(row.Name.Value);", DisplayName = "the value checked")]
        [DataRow("Assert.IsTrue(row.Name == null);", DisplayName = "the == operator against null")]
        [DataRow("Assert.IsNull(row.Any);", DisplayName = "an object-typed value")]
        [DataRow("Check.IsNull(row.Name);", DisplayName = "a generic parameter is not object")]
        [DataRow("Assert.AreEqual(null, row.Name);", DisplayName = "not a null check (TIK001's business)")]
        public void ACheckThatWorksIsNotReported(string statement)
        {
            var diagnostics = Analyze(Body(statement));

            Assert.AreEqual(0, diagnostics.Length, string.Join("\n", diagnostics.Select(d => d.ToString())));
        }

        [TestMethod]
        public void NothingIsReportedWithoutTikFieldInTheCompilation()
        {
            var diagnostics = Analyze("static class Assert { public static void IsNull(object v) { } } "
                + "struct S { } class C { void M(S s) => Assert.IsNull(s); }");

            Assert.AreEqual(0, diagnostics.Length);
        }

        [DataTestMethod]
        [DataRow("Assert.IsNull(row.Name);", "Assert.IsNull(row.Name.Value);")]
        [DataRow("Assert.IsNotNull(row.Mtu, \"mtu\");", "Assert.IsNotNull(row.Mtu.Value, \"mtu\");")]
        [DataRow("Assert.IsNull(flag ? row.Name : other.Name);", "Assert.IsNull((flag ? row.Name : other.Name).Value);")]
        public void TheFixChecksTheValue(string statement, string expected)
        {
            string source = Body(statement);

            Assert.AreEqual(Body(expected), ApplyFix(source));
        }

        private static ImmutableArray<Diagnostic> Analyze(string source)
        {
            var compilation = Compile(source);
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.AreEqual(0, errors.Count, "the snippet itself does not compile:\n" + string.Join("\n", errors));
            return compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TikFieldNullCheckAnalyzer()))
                .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
        }

        private static CSharpCompilation Compile(string source)
            => CSharpCompilation.Create("snippet", new[] { CSharpSyntaxTree.ParseText(source) },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        private static string SpanText(Diagnostic diagnostic)
            => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

        private static string ApplyFix(string source)
        {
            using var workspace = new AdhocWorkspace();
            var project = workspace.AddProject("snippet", LanguageNames.CSharp)
                .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            var document = project.AddDocument("Snippet.cs", SourceText.From(source));

            var diagnostic = Analyze(source).Single();
            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
            new TikFieldObjectEqualityCodeFix().RegisterCodeFixesAsync(context).GetAwaiter().GetResult();

            var operation = actions.Single().GetOperationsAsync(CancellationToken.None).GetAwaiter().GetResult()
                .OfType<ApplyChangesOperation>().Single();
            return operation.ChangedSolution.GetDocument(document.Id)!.GetTextAsync().GetAwaiter().GetResult().ToString();
        }
    }
}

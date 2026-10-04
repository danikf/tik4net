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
    /// TIK001, the analyzer the tik4net package ships: a <c>TikField&lt;T&gt;</c> compared with a plain value through
    /// <c>object</c> is never equal.
    /// </summary>
    /// <remarks>
    /// The snippets declare a stand-in <c>tik4net.Objects.TikField&lt;T&gt;</c> with the real one's conversions, and an
    /// <c>Assert</c> with MSTest's two <c>AreEqual</c> shapes: the analyzer recognises the type by its metadata name, and
    /// corlib is then the only reference, so the tests run the same on net8.0 and net48.
    /// </remarks>
    [TestClass]
    public class TikFieldObjectEqualityAnalyzerTests
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
}
static class Assert
{
    public static void AreEqual(object expected, object actual) { }
    public static void AreEqual<T>(T expected, T actual) { }
    public static void AreNotEqual(object notExpected, object actual) { }
}
class Row { public tik4net.Objects.TikField<string> Name; public tik4net.Objects.TikField<int?> Mtu; public object Any; }
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
        [DataRow("object.Equals(\"x\", row.Name);", "row.Name", DisplayName = "object.Equals(value, wrapper)")]
        [DataRow("object.Equals(row.Name, \"x\");", "row.Name", DisplayName = "object.Equals(wrapper, value)")]
        [DataRow("\"x\".Equals(row.Name);", "row.Name", DisplayName = "a plain value's Equals(object)")]
        [DataRow("Assert.AreEqual(1500L, row.Mtu);", "row.Mtu", DisplayName = "types generic inference cannot unify")]
        [DataRow("Assert.AreNotEqual(\"x\", row.Name);", "row.Name", DisplayName = "AreNotEqual(object, object)")]
        [DataRow("Assert.AreEqual<object>(\"x\", row.Name);", "row.Name", DisplayName = "generic with T = object")]
        public void AnObjectTypedComparisonOfAWrapperAndAValueIsReported(string statement, string wrapped)
        {
            var diagnostics = Analyze(Body(statement));

            Assert.AreEqual(1, diagnostics.Length, string.Join("\n", diagnostics.Select(d => d.ToString())));
            Assert.AreEqual("TIK001", diagnostics[0].Id);
            Assert.AreEqual(wrapped, SpanText(diagnostics[0]));
        }

        [DataTestMethod]
        [DataRow("Assert.AreEqual(\"x\", row.Name);", DisplayName = "generic AreEqual infers the wrapper")]
        [DataRow("Assert.AreEqual(1500, row.Mtu);", DisplayName = "generic AreEqual through int -> int?")]
        [DataRow("object.Equals(\"x\", row.Name.Value);", DisplayName = "the value compared")]
        [DataRow("object.Equals(row.Name, other.Name);", DisplayName = "two wrappers")]
        [DataRow("object.Equals(row.Any, row.Name);", DisplayName = "an object-typed side says nothing")]
        [DataRow("object.Equals(null, row.Name);", DisplayName = "a null literal has no type")]
        [DataRow("bool b = row.Name == \"x\";", DisplayName = "the == operator")]
        [DataRow("string s = string.Format(\"{0}{1}\", \"x\", row.Name);", DisplayName = "not an equality method")]
        public void AComparisonThatWorksIsNotReported(string statement)
        {
            var diagnostics = Analyze(Body(statement));

            Assert.AreEqual(0, diagnostics.Length, string.Join("\n", diagnostics.Select(d => d.ToString())));
        }

        [TestMethod]
        public void NothingIsReportedWithoutTikFieldInTheCompilation()
        {
            var diagnostics = Analyze("class C { bool M(object a) => object.Equals(\"x\", a) || object.Equals(\"x\", 5); }");

            Assert.AreEqual(0, diagnostics.Length);
        }

        [DataTestMethod]
        [DataRow("object.Equals(\"x\", row.Name);", "object.Equals(\"x\", row.Name.Value);")]
        [DataRow("Assert.AreEqual(1500L, row.Mtu);", "Assert.AreEqual(1500L, row.Mtu.Value);")]
        [DataRow("object.Equals(\"x\", flag ? row.Name : other.Name);", "object.Equals(\"x\", (flag ? row.Name : other.Name).Value);")]
        [DataRow("object.Equals(\"x\", /* kept */ row.Name);", "object.Equals(\"x\", /* kept */ row.Name.Value);")]
        public void TheFixComparesTheValue(string statement, string expected)
        {
            string source = Body(statement);

            Assert.AreEqual(Body(expected), ApplyFix(source));
        }

        private static ImmutableArray<Diagnostic> Analyze(string source)
        {
            var compilation = Compile(source);
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.AreEqual(0, errors.Count, "the snippet itself does not compile:\n" + string.Join("\n", errors));
            return compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TikFieldObjectEqualityAnalyzer()))
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

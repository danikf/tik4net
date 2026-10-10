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
    /// TIK003: a string constant containing ',' handed to a <c>TikValueList&lt;T&gt;</c> as one item — the constructor
    /// refuses it at run time; the analyzer says so at compile time.
    /// </summary>
    /// <remarks>The snippets compile against stand-ins with the shape of the real types.</remarks>
    [TestClass]
    public class TikValueListCommaItemAnalyzerTests
    {
        private const string Stubs = @"
namespace tik4net.Objects
{
    public readonly struct TikValue<T>
    {
        public static implicit operator TikValue<T>(T value) => default(TikValue<T>);
        public static TikValue<T> Not(T value) => default(TikValue<T>);
        public static TikValue<T> FromWire(string word) => default(TikValue<T>);
    }
    public sealed class TikValueList<T>
    {
        public TikValueList(params T[] items) { }
        public TikValueList(params TikValue<T>[] items) { }
        public static TikValueList<T> Parse(string text) => null;
        public TikValueList<T> With(params T[] items) => this;
        public TikValueList<T> With(params TikValue<T>[] items) => this;
    }
    public enum Flag { Syn, Ack }
}
";

        private static string Body(string statements) => Stubs + @"
class Subject
{
    const string Both = ""a,b"";
    void Run(string variable, tik4net.Objects.TikValueList<string> list)
    {
        " + statements + @"
    }
}";

        [DataTestMethod]
        [DataRow("var x = new tik4net.Objects.TikValueList<string>(\"a,b\");", "\"a,b\"", DisplayName = "a literal in the constructor")]
        [DataRow("var x = new tik4net.Objects.TikValueList<string>(\"x\", \"a,b\");", "\"a,b\"", DisplayName = "the second item")]
        [DataRow("var x = new tik4net.Objects.TikValueList<string>(Both);", "Both", DisplayName = "a const")]
        [DataRow("var x = list.With(\"c,d\");", "\"c,d\"", DisplayName = "With")]
        [DataRow("var x = new tik4net.Objects.TikValueList<string>(tik4net.Objects.TikValue<string>.Not(\"a,b\"));", "\"a,b\"", DisplayName = "inside Not")]
        [DataRow("var x = new tik4net.Objects.TikValueList<tik4net.Objects.Flag>(tik4net.Objects.TikValue<tik4net.Objects.Flag>.FromWire(\"syn,ack\"));", "\"syn,ack\"", DisplayName = "inside FromWire")]
        public void AConstantItemWithACommaIsReported(string statement, string item)
        {
            var diagnostics = Analyze(Body(statement));

            Assert.AreEqual(1, diagnostics.Length, string.Join("\n", diagnostics.Select(d => d.ToString())));
            Assert.AreEqual("TIK003", diagnostics[0].Id);
            Assert.AreEqual(item, SpanText(diagnostics[0]));
        }

        [DataTestMethod]
        [DataRow("var x = new tik4net.Objects.TikValueList<string>(\"a\", \"b\");", DisplayName = "one argument per item")]
        [DataRow("var x = tik4net.Objects.TikValueList<string>.Parse(\"a,b\");", DisplayName = "Parse is the way for text")]
        [DataRow("var x = new tik4net.Objects.TikValueList<string>(variable);", DisplayName = "a run-time string is the constructor's to refuse")]
        [DataRow("var x = string.Join(\",\", \"a\", \"b\");", DisplayName = "a comma elsewhere")]
        public void ItemsWithoutACommaAreNotReported(string statement)
        {
            var diagnostics = Analyze(Body(statement));

            Assert.AreEqual(0, diagnostics.Length, string.Join("\n", diagnostics.Select(d => d.ToString())));
        }

        [TestMethod]
        public void NothingIsReportedWithoutTikValueListInTheCompilation()
        {
            var diagnostics = Analyze("class L<T> { public L(params T[] items) { } } class C { object M() => new L<string>(\"a,b\"); }");

            Assert.AreEqual(0, diagnostics.Length);
        }

        [DataTestMethod]
        [DataRow("var x = new tik4net.Objects.TikValueList<string>(\"a,b\");", "var x = new tik4net.Objects.TikValueList<string>(\"a\", \"b\");")]
        [DataRow("var x = new tik4net.Objects.TikValueList<string>(\"x\", \"1.1.1.1, 8.8.8.8\");", "var x = new tik4net.Objects.TikValueList<string>(\"x\", \"1.1.1.1\", \"8.8.8.8\");")]
        [DataRow("var x = list.With(\"c,d\");", "var x = list.With(\"c\", \"d\");")]
        public void TheFixPassesOneArgumentPerItem(string statement, string expected)
        {
            Assert.AreEqual(Body(expected), ApplyFix(Body(statement)));
        }

        [DataTestMethod]
        [DataRow("var x = new tik4net.Objects.TikValueList<string>(\"a,!b\");", DisplayName = "a negation is not a plain item")]
        [DataRow("var x = new tik4net.Objects.TikValueList<string>(\"a,,b\");", DisplayName = "an empty part")]
        [DataRow("var x = new tik4net.Objects.TikValueList<string>(Both);", DisplayName = "a const is not rewritten")]
        [DataRow("var x = new tik4net.Objects.TikValueList<string>(tik4net.Objects.TikValue<string>.Not(\"a,b\"));", DisplayName = "inside Not")]
        public void NoFixIsOfferedWhereSplittingWouldGuess(string statement)
        {
            Assert.AreEqual(0, FixActions(Body(statement)).Count);
        }

        private static ImmutableArray<Diagnostic> Analyze(string source)
        {
            var compilation = Compile(source);
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.AreEqual(0, errors.Count, "the snippet itself does not compile:\n" + string.Join("\n", errors));
            return compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TikValueListCommaItemAnalyzer()))
                .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
        }

        private static CSharpCompilation Compile(string source)
            => CSharpCompilation.Create("snippet", new[] { CSharpSyntaxTree.ParseText(source) },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        private static string SpanText(Diagnostic diagnostic)
            => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

        private static (Document Document, List<CodeAction> Actions) FixContext(string source)
        {
            var workspace = new AdhocWorkspace();
            var project = workspace.AddProject("snippet", LanguageNames.CSharp)
                .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            var document = project.AddDocument("Snippet.cs", SourceText.From(source));

            var diagnostic = Analyze(source).Single();
            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
            new TikValueListCommaItemCodeFix().RegisterCodeFixesAsync(context).GetAwaiter().GetResult();
            return (document, actions);
        }

        private static List<CodeAction> FixActions(string source) => FixContext(source).Actions;

        private static string ApplyFix(string source)
        {
            var (document, actions) = FixContext(source);
            var operation = actions.Single().GetOperationsAsync(CancellationToken.None).GetAwaiter().GetResult()
                .OfType<ApplyChangesOperation>().Single();
            return operation.ChangedSolution.GetDocument(document.Id)!.GetTextAsync().GetAwaiter().GetResult().ToString();
        }
    }
}

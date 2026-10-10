using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace tik4net.Analyzers
{
    /// <summary>
    /// The fix for TIK003: one argument per item — <c>new TikValueList&lt;string&gt;("a,b")</c> becomes
    /// <c>new TikValueList&lt;string&gt;("a", "b")</c>.
    /// </summary>
    /// <remarks>
    /// Offered only for a string literal passed straight as an argument, and only when every part is a plain item: an empty
    /// part (<c>"a,,b"</c>) or one starting with <c>!</c> (a negation, which a plain string item would not carry) is left
    /// for the developer, as is an item inside <c>TikValue&lt;T&gt;.Not</c> or <c>FromWire</c>.
    /// </remarks>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TikValueListCommaItemCodeFix)), Shared]
    public sealed class TikValueListCommaItemCodeFix : CodeFixProvider
    {
        private const string Title = "One argument per item";

        /// <inheritdoc/>
        public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(TikValueListCommaItemAnalyzer.DiagnosticId);

        /// <inheritdoc/>
        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        /// <inheritdoc/>
        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null)
                return;
            foreach (var diagnostic in context.Diagnostics)
            {
                if (!diagnostic.Properties.ContainsKey(TikValueListCommaItemAnalyzer.SplittableProperty))
                    continue;
                if (!(root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is LiteralExpressionSyntax literal)
                    || !(literal.Parent is ArgumentSyntax argument) || !(argument.Parent is ArgumentListSyntax list))
                    continue;
                string[] parts = literal.Token.ValueText.Split(',').Select(p => p.Trim()).ToArray();
                if (parts.Any(p => p.Length == 0 || p.StartsWith("!")))
                    continue;

                var replacement = parts.Select(p => SyntaxFactory.Argument(
                    SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(p))));
                var newList = list.WithArguments(list.Arguments.ReplaceRange(argument, replacement));
                context.RegisterCodeFix(
                    CodeAction.Create(Title, _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(list, newList))),
                        equivalenceKey: Title),
                    diagnostic);
            }
        }
    }
}

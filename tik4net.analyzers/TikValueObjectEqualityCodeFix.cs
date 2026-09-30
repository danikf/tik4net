using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace tik4net.Analyzers
{
    /// <summary>
    /// The fix for TIK001: compare the wrapper's <c>.Value</c>, so both sides are the plain type.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TikValueObjectEqualityCodeFix)), Shared]
    public sealed class TikValueObjectEqualityCodeFix : CodeFixProvider
    {
        private const string Title = "Compare '.Value'";

        /// <inheritdoc/>
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create(TikValueObjectEqualityAnalyzer.DiagnosticId);

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
                if (!(root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is ExpressionSyntax wrapped))
                    continue;
                context.RegisterCodeFix(
                    CodeAction.Create(Title, _ => Task.FromResult(context.Document.WithSyntaxRoot(
                        root.ReplaceNode(wrapped, WithValue(wrapped)))), equivalenceKey: Title),
                    diagnostic);
            }
        }

        private static ExpressionSyntax WithValue(ExpressionSyntax wrapped)
        {
            // A primary expression takes '.Value' as it stands; anything looser ('a ?? b', a cast) is parenthesised first.
            ExpressionSyntax receiver = wrapped is IdentifierNameSyntax || wrapped is MemberAccessExpressionSyntax
                || wrapped is InvocationExpressionSyntax || wrapped is ElementAccessExpressionSyntax
                || wrapped is ParenthesizedExpressionSyntax
                ? wrapped.WithoutTrivia()
                : SyntaxFactory.ParenthesizedExpression(wrapped.WithoutTrivia());
            return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, receiver,
                    SyntaxFactory.IdentifierName("Value"))
                .WithTriviaFrom(wrapped);
        }
    }
}

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace tik4net.Analyzers
{
    /// <summary>
    /// TIK002: a <c>TikField&lt;T&gt;</c> handed to a null check that takes <c>object</c>.
    /// </summary>
    /// <remarks>
    /// The wrapper is a struct: as <c>object</c> it is boxed, and a box is never null. <c>Assert.IsNull(entity.X)</c>
    /// therefore fails for every value and <c>Assert.IsNotNull(entity.X)</c> passes for every value — including an
    /// absent one, so the assertion checks nothing at all.
    /// <para>
    /// Reported where a method named <c>IsNull</c>, <c>IsNotNull</c>, <c>Null</c> or <c>NotNull</c> (MSTest, NUnit and
    /// xUnit spell them so) receives a <c>TikField&lt;T&gt;</c> or <c>TikValue&lt;T&gt;</c> as an <c>object</c>
    /// argument. A constraint such as NUnit's <c>Assert.That(x, Is.Null)</c> is not a call the analyzer can see.
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TikFieldNullCheckAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic id.</summary>
        public const string DiagnosticId = "TIK002";

        private static readonly ImmutableHashSet<string> NullChecks = ImmutableHashSet.Create("IsNull", "IsNotNull", "Null", "NotNull");

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            title: "A TikField<T> or TikValue<T> passed to a null check as object is never null",
            messageFormat: "'{0}' is a {1}, boxed by {2} and never null; check '{0}.Value'",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "TikField<T> and TikValue<T> are structs. A null check that takes object boxes them, and a box is never "
                + "null: IsNull always fails and IsNotNull always passes, whatever the router returned. Check the "
                + "wrapper's .Value, or compare it with == null.",
            helpLinkUri: "https://github.com/danikf/tik4net/wiki/TikField#pitfalls");

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(start =>
            {
                var wrappers = new[] { TikFieldObjectEqualityAnalyzer.TikFieldMetadataName, TikFieldObjectEqualityAnalyzer.TikValueMetadataName }
                    .Select(start.Compilation.GetTypeByMetadataName)
                    .Where(t => t != null)
                    .Select(t => t!)
                    .ToImmutableArray();
                if (wrappers.IsEmpty)
                    return; // tik4net.objects is not referenced: nothing here can hold a TikField or a TikValue.
                start.RegisterOperationAction(ctx => AnalyzeInvocation(ctx, wrappers), OperationKind.Invocation);
            });
        }

        private static void AnalyzeInvocation(OperationAnalysisContext context, ImmutableArray<INamedTypeSymbol> wrappers)
        {
            var invocation = (IInvocationOperation)context.Operation;
            if (!NullChecks.Contains(invocation.TargetMethod.Name))
                return;

            foreach (var argument in invocation.Arguments)
            {
                if (argument.Parameter?.Type.SpecialType != SpecialType.System_Object)
                    continue;
                var operand = TikFieldObjectEqualityAnalyzer.Unconvert(argument.Value);
                if (operand.Type == null || !TikFieldObjectEqualityAnalyzer.IsWrapper(operand.Type, wrappers))
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(Rule, operand.Syntax.GetLocation(),
                    operand.Syntax.ToString(),
                    operand.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    invocation.TargetMethod.Name));
            }
        }
    }
}

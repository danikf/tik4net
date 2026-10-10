using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace tik4net.Analyzers
{
    /// <summary>
    /// TIK003: a string constant containing <c>,</c> handed to a <c>TikValueList&lt;T&gt;</c> as one item.
    /// </summary>
    /// <remarks>
    /// The router separates a list's items with <c>,</c>, so <c>new TikValueList&lt;string&gt;("a,b")</c> is not the
    /// two-item list it looks like: the constructor refuses it with an <c>ArgumentException</c> at run time. This reports
    /// the constant case at compile time — an item argument of the constructor or of <c>With</c>, given directly or
    /// through <c>TikValue&lt;T&gt;.Not</c> / <c>FromWire</c>. A string only known at run time is the constructor's to
    /// refuse; text that holds several items goes through <c>TikValueList&lt;T&gt;.Parse</c>.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TikValueListCommaItemAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic id.</summary>
        public const string DiagnosticId = "TIK003";

        internal const string TikValueListMetadataName = "tik4net.Objects.TikValueList`1";

        /// <summary>Property set on a diagnostic whose item is a plain literal the code fix can split.</summary>
        internal const string SplittableProperty = "splittable";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            title: "A TikValueList item contains ','",
            messageFormat: "{0} is one list item containing ','; the router separates items with ',' — pass one argument per item, or use TikValueList<T>.Parse",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "A TikValueList<T> item never contains ',': the router separates the items with it, so the "
                + "constructor and With refuse such an item with an ArgumentException. Pass one argument per item "
                + "(new TikValueList<string>(\"a\", \"b\")), or parse text in the router's spelling with "
                + "TikValueList<T>.Parse(\"a,b\").",
            helpLinkUri: "https://github.com/danikf/tik4net/wiki/TikField#value-lists-tikvaluelistt");

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(start =>
            {
                var list = start.Compilation.GetTypeByMetadataName(TikValueListMetadataName);
                var item = start.Compilation.GetTypeByMetadataName(TikFieldObjectEqualityAnalyzer.TikValueMetadataName);
                if (list == null)
                    return; // tik4net.objects is not referenced.
                start.RegisterOperationAction(ctx =>
                {
                    var creation = (IObjectCreationOperation)ctx.Operation;
                    if (SymbolEqualityComparer.Default.Equals(creation.Type?.OriginalDefinition, list))
                        AnalyzeArguments(ctx, creation.Arguments, item);
                }, OperationKind.ObjectCreation);
                start.RegisterOperationAction(ctx =>
                {
                    var invocation = (IInvocationOperation)ctx.Operation;
                    if (invocation.TargetMethod.Name == "With"
                        && SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType?.OriginalDefinition, list))
                        AnalyzeArguments(ctx, invocation.Arguments, item);
                }, OperationKind.Invocation);
            });
        }

        private static void AnalyzeArguments(OperationAnalysisContext context, ImmutableArray<IArgumentOperation> arguments,
            INamedTypeSymbol? item)
        {
            foreach (var argument in arguments)
                foreach (var element in Elements(argument))
                    Check(context, TikFieldObjectEqualityAnalyzer.Unconvert(element), item);
        }

        // A params argument is an implicit array: its items are the initializer's elements.
        private static IEnumerable<IOperation> Elements(IArgumentOperation argument)
        {
            if (argument.ArgumentKind == ArgumentKind.ParamArray
                && argument.Value is IArrayCreationOperation array && array.Initializer != null)
                return array.Initializer.ElementValues;
            return new[] { argument.Value };
        }

        private static void Check(OperationAnalysisContext context, IOperation value, INamedTypeSymbol? item)
        {
            if (IsCommaConstant(value))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, value.Syntax.GetLocation(),
                    value is ILiteralOperation
                        ? ImmutableDictionary<string, string?>.Empty.Add(SplittableProperty, "true")
                        : ImmutableDictionary<string, string?>.Empty,
                    value.Syntax.ToString()));
                return;
            }

            // TikValue<T>.Not("a,b") / FromWire("a,b"): the item is the call's string argument.
            if (item != null && value is IInvocationOperation call
                && (call.TargetMethod.Name == "Not" || call.TargetMethod.Name == "FromWire")
                && SymbolEqualityComparer.Default.Equals(call.TargetMethod.ContainingType?.OriginalDefinition, item))
            {
                foreach (var argument in call.Arguments)
                {
                    var inner = TikFieldObjectEqualityAnalyzer.Unconvert(argument.Value);
                    if (IsCommaConstant(inner))
                        context.ReportDiagnostic(Diagnostic.Create(Rule, inner.Syntax.GetLocation(), inner.Syntax.ToString()));
                }
            }
        }

        private static bool IsCommaConstant(IOperation value)
            => value.Type?.SpecialType == SpecialType.System_String
               && value.ConstantValue.HasValue
               && value.ConstantValue.Value is string text
               && text.IndexOf(',') >= 0;
    }
}

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace tik4net.Analyzers
{
    /// <summary>
    /// TIK001: a <c>TikField&lt;T&gt;</c> compared with a plain value through <c>object</c>.
    /// </summary>
    /// <remarks>
    /// The wrapper equals a plain <c>T</c> through its <c>==</c> operators, which the compiler picks when it sees both
    /// types. Through <c>object</c> it cannot: <c>TikField&lt;T&gt;.Equals(object)</c> is true only for another
    /// <c>TikField&lt;T&gt;</c>, and the plain value's own <c>Equals</c> knows nothing of the wrapper, so the comparison
    /// is false for every value — and an assertion built on it fails, or passes, whatever the router returned.
    /// <para>
    /// Reported where an equality method (<c>Equals</c>, <c>AreEqual</c>, <c>AreNotEqual</c>, <c>Equal</c>,
    /// <c>NotEqual</c>, … — any name containing "Equal") receives a <c>TikField&lt;T&gt;</c> and a value of another,
    /// known type, both as <c>object</c>. A side typed <c>object</c>, a type parameter or a <c>null</c> literal says
    /// nothing about the value it holds, and is not reported. The generic <c>Assert.AreEqual&lt;T&gt;(expected,
    /// entity.X)</c> infers <c>T</c> as the wrapper and compares correctly, so it is not reported either.
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TikFieldObjectEqualityAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic id.</summary>
        public const string DiagnosticId = "TIK001";

        internal const string TikFieldMetadataName = "tik4net.Objects.TikField`1";
        internal const string TikValueMetadataName = "tik4net.Objects.TikValue`1";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            title: "A TikField<T> or TikValue<T> compared with a plain value through object is never equal",
            messageFormat: "'{0}' is a {1} compared with a {2} through object, which is never equal; compare '{0}.Value'",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "TikField<T> and TikValue<T> equal a plain T through their == operators. Through object — object.Equals, "
                + "Assert.AreEqual(object, object), a plain value's Equals(object) — the wrapper and the value are two "
                + "different types and never equal. Compare the wrapper's .Value.",
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
                var wrappers = new[] { TikFieldMetadataName, TikValueMetadataName }
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
            var method = invocation.TargetMethod;
            if (method.Name.IndexOf("Equal", System.StringComparison.Ordinal) < 0)
                return;

            // Every operand the method sees as object: the object-typed arguments, and for an instance Equals(object)
            // the receiver it is called on.
            var operands = new List<IOperation>();
            foreach (var argument in invocation.Arguments)
            {
                if (argument.Parameter?.Type.SpecialType == SpecialType.System_Object)
                    operands.Add(Unconvert(argument.Value));
            }
            if (!method.IsStatic && invocation.Instance != null && operands.Count == 1 && method.Parameters.Length == 1)
                operands.Add(invocation.Instance);
            if (operands.Count < 2)
                return;

            IOperation? wrapped = null;
            ITypeSymbol? plain = null;
            foreach (var operand in operands)
            {
                var type = operand.Type;
                if (type == null || type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.TypeParameter
                    || type.TypeKind == TypeKind.Error)
                    continue;
                if (IsWrapper(type, wrappers))
                    wrapped ??= operand;
                else
                    plain ??= type;
            }
            if (wrapped == null || plain == null)
                return;

            context.ReportDiagnostic(Diagnostic.Create(Rule, wrapped.Syntax.GetLocation(),
                wrapped.Syntax.ToString(),
                wrapped.Type!.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                plain.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }

        /// <summary>The operand before the implicit boxing or reference conversion to <c>object</c>.</summary>
        private static IOperation Unconvert(IOperation value)
        {
            while (value is IConversionOperation conversion && conversion.IsImplicit)
                value = conversion.Operand;
            return value;
        }

        internal static bool IsWrapper(ITypeSymbol type, ImmutableArray<INamedTypeSymbol> wrappers)
            => type is INamedTypeSymbol named && wrappers.Any(w => SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, w));
    }
}

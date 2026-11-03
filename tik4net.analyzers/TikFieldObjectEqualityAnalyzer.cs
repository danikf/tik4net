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
    /// <c>NotEqual</c>, … — any name containing "Equal") receives a wrapper and a value of another, known type, both as
    /// <c>object</c>. The wrappers are the four structs (<c>TikField&lt;T&gt;</c>, <c>TikNegatableField&lt;T&gt;</c>,
    /// <c>TikValue&lt;T&gt;</c>, <c>TikNegatableValue&lt;T&gt;</c>), a <c>Nullable&lt;&gt;</c> of one, which boxes the same
    /// way, and <c>TikValueList&lt;T&gt;</c>, which never equals its text either. A <c>null</c> literal against one of the
    /// structs is reported too: a box is never null. A side typed <c>object</c> or a type parameter says nothing about
    /// the value it holds, and is not reported, nor is a <c>null</c> literal against a value that can be null. The
    /// generic <c>Assert.AreEqual&lt;T&gt;(expected, entity.X)</c> infers <c>T</c> as the wrapper and compares correctly,
    /// so it is not reported either. NUnit's <c>Assert.That(entity.X, Is.EqualTo(expected))</c> is not a call the
    /// analyzer can see: the wrapper goes to a generic parameter and the constraint is built apart from it.
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TikFieldObjectEqualityAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic id.</summary>
        public const string DiagnosticId = "TIK001";

        internal const string TikFieldMetadataName = "tik4net.Objects.TikField`1";
        internal const string TikNegatableFieldMetadataName = "tik4net.Objects.TikNegatableField`1";
        internal const string TikValueMetadataName = "tik4net.Objects.TikValue`1";
        internal const string TikNegatableValueMetadataName = "tik4net.Objects.TikNegatableValue`1";

        /// <summary>The struct wrappers, which are never null as <c>object</c>: TIK001 and TIK002 both check these.</summary>
        internal static readonly ImmutableArray<string> StructWrapperMetadataNames = ImmutableArray.Create(
            TikFieldMetadataName, TikNegatableFieldMetadataName, TikValueMetadataName, TikNegatableValueMetadataName);

        /// <summary>Property set on a diagnostic whose wrapper is a <c>Nullable&lt;&gt;</c>: the fix uses <c>?.Value</c>.</summary>
        internal const string NullableProperty = "nullable";

        /// <summary>Property set on a diagnostic about a value list, which has no <c>.Value</c> for the fix to use.</summary>
        internal const string ListProperty = "list";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            title: "A TikField<T> or TikValue<T> compared with a plain value through object is never equal",
            messageFormat: "'{0}' is a {1} compared with {2} through object, which is never equal; {3}",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "TikField<T> and TikValue<T> equal a plain T through their == operators. Through object — object.Equals, "
                + "Assert.AreEqual(object, object), a plain value's Equals(object) — the wrapper and the value are two "
                + "different types and never equal, and a boxed wrapper is never null. Compare the wrapper's .Value. A "
                + "TikValueList<T> equals only another TikValueList<T>.",
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
                var wrappers = Resolve(start.Compilation, StructWrapperMetadataNames);
                if (wrappers.IsEmpty)
                    return; // tik4net.objects is not referenced: nothing here can hold a TikField or a TikValue.
                var list = start.Compilation.GetTypeByMetadataName(TikValueListCommaItemAnalyzer.TikValueListMetadataName);
                start.RegisterOperationAction(ctx => AnalyzeInvocation(ctx, wrappers, list), OperationKind.Invocation);
            });
        }

        private static void AnalyzeInvocation(OperationAnalysisContext context, ImmutableArray<INamedTypeSymbol> wrappers,
            INamedTypeSymbol? list)
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
            bool nullable = false, isList = false, nullLiteral = false;
            ITypeSymbol? plain = null;
            foreach (var operand in operands)
            {
                if (operand.Type == null && operand.ConstantValue.HasValue && operand.ConstantValue.Value == null)
                {
                    nullLiteral = true;
                    continue;
                }
                var type = operand.Type;
                if (type == null || type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.TypeParameter
                    || type.TypeKind == TypeKind.Error)
                    continue;
                bool operandIsList = list != null && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, list);
                if (IsWrapper(type, wrappers, out bool operandIsNullable) || operandIsList)
                {
                    // A second wrapper is no plain value: two wrappers compare as themselves.
                    if (wrapped == null)
                    {
                        wrapped = operand;
                        nullable = operandIsNullable;
                        isList = operandIsList;
                    }
                }
                else
                    plain ??= type;
            }
            // A null literal says something only against a struct wrapper: its box is never null.
            if (wrapped == null || (plain == null && !(nullLiteral && !nullable && !isList)))
                return;

            string text = wrapped.Syntax.ToString();
            var properties = ImmutableDictionary<string, string?>.Empty;
            if (nullable) properties = properties.Add(NullableProperty, "true");
            if (isList) properties = properties.Add(ListProperty, "true");
            context.ReportDiagnostic(Diagnostic.Create(Rule, wrapped.Syntax.GetLocation(), properties,
                text,
                wrapped.Type!.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                plain == null ? "null" : "a " + plain.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                isList ? "compare it with another TikValueList<T> (TikValueList<T>.Parse reads text)"
                    : nullable ? "compare '" + text + "?.Value'" : "compare '" + text + ".Value'"));
        }

        /// <summary>The types of <paramref name="metadataNames"/> the compilation has.</summary>
        internal static ImmutableArray<INamedTypeSymbol> Resolve(Compilation compilation, IEnumerable<string> metadataNames)
            => metadataNames.Select(compilation.GetTypeByMetadataName)
                .Where(t => t != null)
                .Select(t => t!)
                .ToImmutableArray();

        /// <summary>The operand before the implicit boxing or reference conversion to <c>object</c>.</summary>
        internal static IOperation Unconvert(IOperation value)
        {
            while (value is IConversionOperation conversion && conversion.IsImplicit)
                value = conversion.Operand;
            return value;
        }

        internal static bool IsWrapper(ITypeSymbol type, ImmutableArray<INamedTypeSymbol> wrappers)
            => type is INamedTypeSymbol named && wrappers.Any(w => SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, w));

        /// <summary>A wrapper, or a <c>Nullable&lt;&gt;</c> of one (<paramref name="nullable"/>), which boxes to the same.</summary>
        internal static bool IsWrapper(ITypeSymbol type, ImmutableArray<INamedTypeSymbol> wrappers, out bool nullable)
        {
            nullable = type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            var underlying = nullable ? ((INamedTypeSymbol)type).TypeArguments[0] : type;
            if (IsWrapper(underlying, wrappers))
                return true;
            nullable = false;
            return false;
        }
    }
}

using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace tik4net.Analyzers
{
    /// <summary>
    /// TIK004: a tik4net mapper generic called with a type argument that has no <c>[TikEntity]</c>.
    /// </summary>
    /// <remarks>
    /// Entities are plain classes with no base type, so the mapper's generics can say at most
    /// <c>where TEntity : new()</c>, and several say nothing: <c>connection.LoadAll&lt;MyDto&gt;()</c>,
    /// <c>"abc".EntityToString()</c> and <c>42.EnsureStrict()</c> compile and throw <c>ArgumentException</c>
    /// ("Entity class must be decorated by TikEntityAttribute attribute.") at run time.
    /// <para>
    /// Reported where a call or a method group binds the type parameter named <c>TEntity</c> of a public generic method
    /// in the <c>tik4net.Objects</c> or <c>tik4net.Testing</c> namespaces (nested ones included) to a type that carries no
    /// <c>[TikEntity]</c>, neither itself nor — when the attribute's <c>AttributeUsage</c> says it is inherited, as it
    /// does — on a base class. An extension call on a receiver of such a type is the same call. A type parameter of the
    /// caller's own generic code is not reported: the caller's constraint is not the analyzer's to judge.
    /// </para>
    /// <para>
    /// A method that takes a <c>TikEntityMetadata</c> parameter (the <c>TikChangeTracker</c> verbs) is not reported
    /// either: the caller hands it the metadata, and it never looks at the type argument's attributes, so
    /// <c>tracker.HasChanges((object)row, metadata)</c> is a correct call in reflection-driven code.
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TikEntityTypeArgumentAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The diagnostic id.</summary>
        public const string DiagnosticId = "TIK004";

        /// <summary>
        /// The type-parameter name the analyzer keys on. Every public mapper generic names its entity parameter so;
        /// <c>TikEntityTypeArgumentCoverageTests</c> fails when a new one does not.
        /// </summary>
        public const string EntityTypeParameterName = "TEntity";

        private const string EntityAttributeMetadataName = "tik4net.Objects.TikEntityAttribute";
        private const string EntityMetadataMetadataName = "tik4net.Objects.TikEntityMetadata";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            title: "A tik4net mapper method is called with a type that is not an entity",
            messageFormat: "'{0}' has no [TikEntity]; '{1}' throws ArgumentException for it at run time",
            category: "Usage",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "The O/R mapper reads the RouterOS path and the field names from the [TikEntity] and [TikProperty] "
                + "attributes. A type without [TikEntity] satisfies the method's constraint, compiles, and fails at run time. "
                + "Pass an entity class, or decorate the type with [TikEntity].",
            helpLinkUri: "https://github.com/danikf/tik4net/wiki/High-level-API-custom-entities#compile-time-check-tik004");

        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        /// <summary>
        /// Whether a namespace is one whose public generics the analyzer checks: <c>tik4net.Objects</c>,
        /// <c>tik4net.Testing</c>, or one nested in either.
        /// </summary>
        public static bool IsMapperNamespace(string? ns)
            => ns != null && (IsOrIsUnder(ns, "tik4net.Objects") || IsOrIsUnder(ns, "tik4net.Testing"));

        private static bool IsOrIsUnder(string ns, string root)
            => ns == root || ns.StartsWith(root + ".", StringComparison.Ordinal);

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(start =>
            {
                var attribute = start.Compilation.GetTypeByMetadataName(EntityAttributeMetadataName);
                if (attribute == null)
                    return; // tik4net.objects is not referenced: there is no mapper to call.
                var mapper = new Mapper(attribute, IsInherited(attribute), start.Compilation.GetTypeByMetadataName(EntityMetadataMetadataName));
                start.RegisterOperationAction(ctx => Analyze(ctx, ((IInvocationOperation)ctx.Operation).TargetMethod, mapper),
                    OperationKind.Invocation);
                start.RegisterOperationAction(ctx => Analyze(ctx, ((IMethodReferenceOperation)ctx.Operation).Method, mapper),
                    OperationKind.MethodReference);
            });
        }

        /// <summary>
        /// <c>AttributeUsage.Inherited</c> of the attribute as declared (the default is <see langword="true"/>). The
        /// mapper reads the attribute with <c>GetCustomAttributes(true)</c>, so an inherited one counts exactly when this
        /// says it can be inherited.
        /// </summary>
        private static bool IsInherited(INamedTypeSymbol attribute)
        {
            var usage = attribute.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.ToDisplayString() == "System.AttributeUsageAttribute");
            if (usage == null)
                return true;
            foreach (var named in usage.NamedArguments)
            {
                if (named.Key == nameof(AttributeUsageAttribute.Inherited) && named.Value.Value is bool value)
                    return value;
            }
            return true;
        }

        /// <summary>The symbols of the referenced tik4net.objects, resolved once per compilation.</summary>
        private sealed class Mapper
        {
            public Mapper(INamedTypeSymbol attribute, bool inherited, INamedTypeSymbol? metadata)
            {
                Attribute = attribute;
                Inherited = inherited;
                Metadata = metadata;
            }

            public INamedTypeSymbol Attribute { get; }
            public bool Inherited { get; }
            public INamedTypeSymbol? Metadata { get; }
        }

        private static void Analyze(OperationAnalysisContext context, IMethodSymbol method, Mapper mapper)
        {
            if (!method.IsGenericMethod || !IsCoveredMethod(method)
                || method.Parameters.Any(p => SymbolEqualityComparer.Default.Equals(p.Type, mapper.Metadata)))
                return;

            var parameters = method.OriginalDefinition.TypeParameters;
            for (int i = 0; i < parameters.Length && i < method.TypeArguments.Length; i++)
            {
                if (parameters[i].Name != EntityTypeParameterName)
                    continue;
                var argument = method.TypeArguments[i];
                if (argument.TypeKind == TypeKind.TypeParameter || argument.TypeKind == TypeKind.Error
                    || argument.TypeKind == TypeKind.Dynamic || IsEntity(argument, mapper.Attribute, mapper.Inherited))
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(Rule, NameLocation(context.Operation.Syntax),
                    argument.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), method.Name));
            }
        }

        private static bool IsCoveredMethod(IMethodSymbol method)
        {
            if (method.DeclaredAccessibility != Accessibility.Public)
                return false;
            for (var type = method.ContainingType; type != null; type = type.ContainingType)
            {
                if (type.DeclaredAccessibility != Accessibility.Public)
                    return false;
            }
            return IsMapperNamespace(method.ContainingNamespace?.ToDisplayString());
        }

        private static bool IsEntity(ITypeSymbol type, INamedTypeSymbol attribute, bool inherited)
        {
            for (var current = type; current != null; current = inherited ? current.BaseType : null)
            {
                if (current.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute)))
                    return true;
            }
            return false;
        }

        /// <summary>The method's name in the call (<c>LoadAll&lt;Dto&gt;</c> of <c>connection.LoadAll&lt;Dto&gt;()</c>).</summary>
        private static Location NameLocation(SyntaxNode syntax)
        {
            var expression = syntax is InvocationExpressionSyntax invocation ? invocation.Expression : syntax;
            return expression switch
            {
                MemberAccessExpressionSyntax member => member.Name.GetLocation(),
                MemberBindingExpressionSyntax binding => binding.Name.GetLocation(),
                _ => expression.GetLocation(),
            };
        }
    }
}

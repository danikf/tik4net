#if NETSTANDARD2_0
// The trimming/AOT annotations the O/R mapper carries are compiler-and-tool contracts that live in the
// framework from .NET 5 (RequiresUnreferencedCode) and .NET 7 (RequiresDynamicCode) onward. The
// netstandard2.0 leg has neither, and it does not need them at run time — no trimmer or AOT compiler
// consumes that target. Declaring them here keeps ONE copy of the source annotated for both legs, rather
// than wrapping every attribute in an #if at its use site.
//
// Internal on purpose: an assembly that publicly declares a framework type in the framework's own
// namespace collides with the real one the moment a consumer targets a framework that has it.

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class,
        Inherited = false)]
    internal sealed class RequiresUnreferencedCodeAttribute : Attribute
    {
        public RequiresUnreferencedCodeAttribute(string message) => Message = message;

        public string Message { get; }

        public string? Url { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class,
        Inherited = false)]
    internal sealed class RequiresDynamicCodeAttribute : Attribute
    {
        public RequiresDynamicCodeAttribute(string message) => Message = message;

        public string Message { get; }

        public string? Url { get; set; }
    }
}
#endif

#if !NET8_0_OR_GREATER
// netstandard2.0 has no System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute. The compiler binds
// the attribute by name, not by assembly, so declaring it here gives the netstandard2.0 leg the same
// flow analysis the net8.0 leg gets from the BCL. Guarded so the two never collide.
namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    /// Specifies that when the method returns <see cref="ReturnValue"/>, the associated out
    /// parameter may be null — the shape <c>Dictionary.TryGetValue</c> uses, so a caller who tested the
    /// bool gets no needless warning and one who did not gets the warning they should have.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class MaybeNullWhenAttribute : Attribute
    {
        /// <summary>Initializes the attribute with the specified return value condition.</summary>
        /// <param name="returnValue">The return value condition. If the method returns this value, the
        /// associated parameter may be null.</param>
        public MaybeNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;

        /// <summary>Gets the return value condition.</summary>
        public bool ReturnValue { get; }
    }

    /// <summary>Specifies that the output will be non-null if the named parameter is non-null.</summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue,
                    AllowMultiple = true, Inherited = false)]
    internal sealed class NotNullIfNotNullAttribute : Attribute
    {
        /// <summary>Initializes the attribute with the associated parameter name.</summary>
        /// <param name="parameterName">
        /// The associated parameter name. The output will be non-null if the argument to the parameter
        /// specified is non-null.
        /// </param>
        public NotNullIfNotNullAttribute(string parameterName) => ParameterName = parameterName;

        /// <summary>Gets the associated parameter name.</summary>
        public string ParameterName { get; }
    }
}
#endif

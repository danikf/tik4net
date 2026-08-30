namespace tik4net.Objects
{
    /// <summary>
    /// The one wording of "the O/R mapper is reflection-driven", so every annotation on the mapper surface
    /// says the same thing.
    /// </summary>
    /// <remarks>
    /// Attribute arguments must be compile-time constants, so this cannot be a resource or a property —
    /// and repeating the sentence at ~40 call sites is how the sentences drift apart.
    /// </remarks>
    internal static class TikTrimming
    {
        /// <summary>Why the mapper is not trim-safe, and what to do instead.</summary>
        internal const string MapperMessage =
            "The tik4net O/R mapper reflects over the entity type's properties and its [TikEntity] / "
            + "[TikProperty] attributes, so a trimmer cannot tell which members are used. Either keep the "
            + "entity types (a trimmer roots descriptor, or [DynamicDependency]), or use the low-level "
            + "ITikCommand API from tik4net.dll, which is trim- and AOT-safe.";

        /// <summary>Why the mapper needs runtime code generation.</summary>
        internal const string DynamicCodeMessage =
            "The tik4net O/R mapper builds generic converter calls with MethodInfo.MakeGenericMethod, whose "
            + "instantiations an AOT compiler cannot know in advance. Use the low-level ITikCommand API "
            + "from tik4net.dll, which is AOT-safe.";
    }
}

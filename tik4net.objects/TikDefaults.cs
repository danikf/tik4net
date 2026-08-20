namespace tik4net.Objects
{
    /// <summary>Save behavior when calling <see cref="TikConnectionExtensions.Save{TEntity}"/>.</summary>
    public enum TikSaveMode
    {
        /// <summary>Use the value of <see cref="TikDefaults.SaveMode"/>.</summary>
        Default,

        /// <summary>
        /// Send only properties that changed since the entity was loaded (diff against snapshot).
        /// When no snapshot is available the entity is sent in full.
        /// This is the default in 4.x.
        /// <para>
        /// The snapshot lives on the <see cref="ITikConnection"/> the entity was LOADED on, so an entity
        /// loaded on one connection and saved on another falls into the "sent in full" case — see the
        /// remarks on <see cref="TikConnectionExtensions.Save{TEntity}(ITikConnection, TEntity, System.Collections.Generic.IEnumerable{string}, TikSaveMode)"/>.
        /// Load and save on the same connection.
        /// </para>
        /// </summary>
        OnlyChanges,

        /// <summary>
        /// Send all writable properties regardless of what changed.
        /// Equivalent to 3.x behavior (performs an extra <c>LoadById</c> round-trip to compute the diff).
        /// </summary>
        FullUpdate,
    }

    /// <summary>
    /// Process-wide defaults for tik4net.objects behavior.
    /// </summary>
    public static class TikDefaults
    {
        /// <summary>
        /// Default save mode used when <see cref="TikSaveMode.Default"/> is passed to
        /// <see cref="TikConnectionExtensions.Save{TEntity}"/>.
        /// <para>4.x default: <see cref="TikSaveMode.OnlyChanges"/>.</para>
        /// <para>
        /// Set to <see cref="TikSaveMode.FullUpdate"/> to restore 3.x round-trip behavior
        /// for the whole application.
        /// </para>
        /// </summary>
        public static TikSaveMode SaveMode { get; set; } = TikSaveMode.OnlyChanges;
    }
}

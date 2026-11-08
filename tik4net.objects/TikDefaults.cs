namespace tik4net.Objects
{
    /// <summary>Save behavior when calling <see cref="TikConnectionExtensions.Save{TEntity}"/>.</summary>
    public enum TikSaveMode
    {
        /// <summary>Use the value of <see cref="TikDefaults.SaveMode"/>.</summary>
        Default,

        /// <summary>
        /// Send only properties that changed since the entity was loaded (diff against snapshot).
        /// When no snapshot is available the entity is sent in full. This is the default
        /// (<see cref="TikDefaults.SaveMode"/>).
        /// <para>
        /// The snapshot lives on the <see cref="ITikConnection"/> the entity was LOADED on, so an entity
        /// loaded on one connection and saved on another falls into the "sent in full" case — see the
        /// remarks on <c>TikConnectionExtensions.Save</c>.
        /// Load and save on the same connection.
        /// </para>
        /// </summary>
        OnlyChanges,

        /// <summary>
        /// Send all writable properties regardless of what changed, after an extra <c>LoadById</c> round-trip
        /// to compute the diff against the router's current row.
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
        /// <see cref="TikConnectionExtensions.Save{TEntity}"/>; <see cref="TikSaveMode.OnlyChanges"/> unless set.
        /// </summary>
        /// <remarks>
        /// Process-wide: one value for every connection and thread, read at each <c>Save</c>, so a change applies
        /// to the next save anywhere in the process. To choose for one call, pass its <c>saveMode</c> argument
        /// instead. The mapper option that is per connection is <see cref="TikConnectionSetup.ValidateWrites"/>.
        /// </remarks>
        /// <seealso cref="TikConnectionExtensions.Save{TEntity}"/>
        public static TikSaveMode SaveMode { get; set; } = TikSaveMode.OnlyChanges;
    }
}

namespace tik4net
{
    /// <summary>
    /// How parameter will be formated in mikrotik request
    /// </summary>
    public enum TikCommandParameterFormat
    {
        /// <summary>
        /// Depends on <see cref="ITikCommand.DefaultParameterFormat"/> (or on method which is executed on command).
        /// </summary>
        Default,

        /// <summary>
        /// Format: ?name=value  (query).
        /// </summary>
        Filter,

        /// <summary>
        /// Format: =name=value (set, execute)
        /// </summary>
        NameValue,

        /// <summary>
        /// Format .tag=value. <seealso cref="TikSpecialProperties.Tag"/>
        /// Binary API only. The other transports ignore a <c>.tag</c> parameter, like every API sentence marker.
        /// </summary>
        Tag,
    }
}

namespace tik4net.Connection
{
    /// <summary>
    /// The client-side markers: parameters the O/R mapper adds for one transport's own handling, which never reach any
    /// router as a word. Each command builder strips them, and adds its own transport's exceptions on top (REST and the
    /// CLI strip <c>.proplist</c> and <c>.tag</c> too; the binary API sends both).
    /// </summary>
    internal static class TikTransportMarkers
    {
        /// <summary>
        /// True for <see cref="TikSpecialProperties.CliStats"/> (the CLI's two-query stats merge),
        /// <see cref="TikSpecialProperties.CliJson"/> (its <c>:serialize</c> read),
        /// <see cref="TikSpecialProperties.CliFlags"/> (the flag list read separately before RouterOS 7.20),
        /// <see cref="TikSpecialProperties.CliSensitive"/> (its <c>show-sensitive</c> word) and
        /// <see cref="TikSpecialProperties.WinboxLabels"/> (WinBox native's field labels).
        /// </summary>
        internal static bool IsClientMarker(string name)
            => name == TikSpecialProperties.CliStats
            || name == TikSpecialProperties.CliJson
            || name == TikSpecialProperties.CliFlags
            || name == TikSpecialProperties.CliSensitive
            || name == TikSpecialProperties.WinboxLabels;
    }
}

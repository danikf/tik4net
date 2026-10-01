namespace tik4net
{
    /// <summary>
    /// Implemented by the transports that keep what they learn about a router's menus on disk, so that the next
    /// connection — in this process or another — need not ask again: the WinBox native transports (the router's
    /// <c>.jg</c> catalog plugins) and the terminal (CLI/PTY) transports (the menu grammar <c>DescribeMenu</c> answers).
    /// </summary>
    /// <remarks>
    /// <para>The CLI grammar is kept per RouterOS build — version, architecture and enabled packages, asked the first
    /// time a connection describes a menu — and shared by every terminal transport talking to that build. What is
    /// stored is the router's grammar only: a menu's commands, sub-menus, the arguments of each command, and the fields
    /// it reads and can clear. The words an argument suggests (an interface name, a list) are the router's data and are
    /// always asked live, and so is anything the cache says a menu lacks before a request is refused for it.</para>
    /// <para>A cache problem never fails a connection: an unreadable or corrupt file is ignored and rewritten, and a
    /// failed save is retried. Deleting the directory is always safe.</para>
    /// </remarks>
    public interface ITikCatalogCacheConnection
    {
        /// <summary>
        /// The directory of the on-disk caches, or <c>null</c> (or empty) for none: everything is then asked again on
        /// each open. Environment variables (<c>%APPDATA%</c>) and relative paths are resolved when it is used.
        /// </summary>
        /// <remarks>Defaults to <see cref="TikConnectionSetup.DefaultCatalogCachePath"/>. Set it before the connection
        /// opens.</remarks>
        string? CatalogCachePath { get; set; }
    }
}

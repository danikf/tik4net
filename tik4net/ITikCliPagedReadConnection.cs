namespace tik4net
{
    /// <summary>
    /// Implemented by the terminal (CLI/PTY) transports, which can split a read into slices instead of
    /// asking the router for the whole table in one answer.
    /// </summary>
    /// <remarks>
    /// <para>A CLI read is one command whose answer is one line: <c>:put [/path print as-value]</c> hands
    /// back every row at once, and the router streams it as fast as the carrier will take it. That is fine
    /// over TCP and it is not fine over the MAC layer, where the client cannot keep up: RouterOS answers the
    /// resulting retransmissions by discarding a run of its own output and carrying on with an unbroken byte
    /// counter, so a 1672-row table came back as 413 rows that looked complete
    /// (see <see cref="TikConnectionResponseIncompleteException"/>).</para>
    /// <para>Paging removes the condition rather than detecting it: each slice is a separate command, the
    /// router never builds a backlog, and there is nothing for it to drop. Measured against that same table
    /// over MAC-Telnet — six runs at slice sizes 100, 50 and 25, all 1672 rows, where the single-command read
    /// had never once succeeded.</para>
    /// </remarks>
    public interface ITikCliPagedReadConnection
    {
        /// <summary>
        /// Rows per slice for a paged read, or <c>0</c> to read every table in a single command.
        /// </summary>
        /// <remarks>
        /// <para>Defaults to <see cref="TikConnectionSetup.DefaultCliReadPageSize"/> on the MAC-layer terminal
        /// transports, where a large single-command read is not merely slow but wrong, and to <c>0</c>
        /// everywhere else, where it is only slower: a paged read costs one extra round trip for the row
        /// count plus one per slice, measured at 7.5 s against 5.8 s for the same 1672-row table over
        /// Telnet.</para>
        /// <para>Paging is skipped for a table whose row count does not exceed the page size, so a small read
        /// costs the count query and nothing else.</para>
        /// </remarks>
        int CliReadPageSize { get; set; }
    }
}

namespace tik4net
{
    /// <summary>
    /// Implemented by the terminal (CLI/PTY) transports, which read a table as text and need a separator between
    /// its fields that the values themselves do not contain.
    /// </summary>
    /// <remarks>
    /// <para><c>print as-value</c> separates fields with <c>;</c> and writes every value raw, so a comment such as
    /// <c>a; b</c> or <c>a;b=c</c> cannot be told apart from the next field or from a list, whose elements are
    /// separated by <c>;</c> too. Where the router can serialize (RouterOS 7), a read therefore writes each row
    /// with <c>:serialize to=dsv delimiter="~^~" options=dsv.remap</c> — the row's own field names on one line,
    /// its values on the next, separated by this string — and a value keeps its <c>;</c>. A router that refuses
    /// it (RouterOS 6) is read as-value, where a value holding <c>;</c> is not read intact.</para>
    /// </remarks>
    public interface ITikCliFieldSeparatorConnection
    {
        /// <summary>
        /// The separator between fields of a terminal read, or <c>null</c> to read as-value only.
        /// </summary>
        /// <remarks>
        /// <para>Defaults to <see cref="TikConnectionSetup.DefaultCliFieldSeparator"/>. RouterOS takes one to three
        /// characters, printable ones only (a control character is an <c>invalid delimiter</c>); <c>"</c>,
        /// <c>\</c> and <c>$</c> would need escaping in the command and <c>;</c> is the one it replaces, so those
        /// are refused here. Choose something no value on the router contains: a value holding the separator reads
        /// as two fields.</para>
        /// </remarks>
        /// <exception cref="System.ArgumentException">The value is empty, longer than three characters, or holds a
        /// character RouterOS or the command line cannot take.</exception>
        string? CliFieldSeparator { get; set; }
    }
}

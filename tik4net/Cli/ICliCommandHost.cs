using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Connection;

namespace tik4net.Cli
{
    /// <summary>
    /// What the CLI connection's collaborators (<see cref="CliRouterKnowledge"/>, …) need from the connection that
    /// runs them. <see cref="CliConnectionBase"/> implements it explicitly, so none of it shows on the connection.
    /// </summary>
    internal interface ICliCommandHost
    {
        /// <summary>Where the menu grammar is cached (<see cref="CliConnectionBase.CatalogCachePath"/>).</summary>
        string? CatalogCachePath { get; }

        /// <summary>The DSV field separator (<see cref="CliConnectionBase.CliFieldSeparator"/>).</summary>
        string? CliFieldSeparator { get; }

        /// <summary>
        /// How many counted reads are on the wire: while above zero, the MAC-layer datagram-loss heuristic stands down
        /// (<c>IsCountedReadInFlight</c>).
        /// </summary>
        int CountedReadDepth { get; set; }

        /// <summary>Runs one CLI command under the command lock and returns its cleaned answer.</summary>
        string Execute(string cliText);

        /// <summary>Async counterpart of <see cref="Execute"/>.</summary>
        Task<string> ExecuteAsync(string cliText, CancellationToken ct);

        /// <summary>Describes a menu by asking the router (<c>/console/inspect</c>, else Tab completion).</summary>
        TikMenuSchema DescribeMenuLive(string path);

        /// <summary>The connection's per-open menu-schema cache.</summary>
        TikMenuSchemaCache MenuSchemas { get; }

        /// <summary>The open console; <c>null</c> while the connection is closed.</summary>
        ICliTerminal? Terminal { get; }

        /// <summary>Throws the not-open exception when the connection is closed.</summary>
        void EnsureOpened();

        /// <summary>The transport's name in messages ("Telnet", "WinBox CLI", …).</summary>
        string TransportName { get; }

        /// <summary>The connection's text encoding.</summary>
        System.Text.Encoding Encoding { get; }

        /// <summary>The lock a command holds while it owns the terminal.</summary>
        SemaphoreSlim CommandLock { get; }

        /// <summary>Reports a row written to the router (<c>OnWriteRow</c>).</summary>
        void FireWriteRow(string row);

        /// <summary>Reports a row read from the router (<c>OnReadRow</c>).</summary>
        void FireReadRow(string row);

        /// <summary>Sends keystrokes with no line end and reads to the next prompt.</summary>
        Task<string> SendRawAndReadAsync(byte[] raw, CancellationToken ct);

        /// <summary>What the connection has learnt about its router in this open.</summary>
        CliRouterKnowledge Knowledge { get; }

        /// <summary>The command an exception carries for <paramref name="descriptor"/>.</summary>
        ITikCommand CommandFor(TikCommandDescriptor descriptor);

        /// <summary>
        /// Runs one print query — windowed where the read may be, otherwise counted — and returns its records. See
        /// <c>CliConnectionBase.RunPrintQueryAsync</c>.
        /// </summary>
        Task<IList<TikRecordSentence>> RunPrintQueryAsync(TikCommandDescriptor descriptor, bool wantJson,
            Func<IList<ITikCommandParameter>, string?, string> buildExpression, CancellationToken cancellationToken);
    }
}

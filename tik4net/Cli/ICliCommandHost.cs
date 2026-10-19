using System.Threading;
using System.Threading.Tasks;

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
    }
}

using System;
using tik4net.Connection;

namespace tik4net.Cli
{
    /// <summary>How a read method runs a verb over a terminal (<see cref="CliVerbs.ReadShapeOf"/>).</summary>
    internal enum CliReadShape
    {
        /// <summary>A table: the parameters are a query, <c>where</c>, and the read is counted and as-value or DSV.</summary>
        Table,
        /// <summary><c>get</c>: one value, or one row as text.</summary>
        Get,
        /// <summary>A monitor that takes one reading (<c>monitor-traffic</c>, <c>profile</c>, <c>discover</c>).</summary>
        Monitor,
        /// <summary>A monitor that ends by itself (<c>ping</c>, <c>traceroute</c>), read through its table.</summary>
        SelfTerminatingMonitor,
        /// <summary><c>torch</c>: one frame, or frames until the deadline.</summary>
        Torch,
        /// <summary>An action the binary API answers with an empty row (<c>wol</c>).</summary>
        EmptyRowAction,
        /// <summary>A read whose arguments are the question (<c>inspect</c>).</summary>
        ArgumentRead,
        /// <summary>A verb with no result set over a terminal (<c>run</c>): refused, it belongs on <c>ExecuteNonQuery</c>.</summary>
        Action,
        /// <summary>A row write reached through a read method (<c>ExecuteScalar</c> of <c>set</c>): runs as the write it is.</summary>
        RowWrite,
        /// <summary>Anything else: <c>:put [/path verb inputs]</c>, with no <c>as-value</c>, which is the verb's first
        /// positional argument on most of them.</summary>
        Other,
    }

    /// <summary>
    /// What a verb is, in one table: which verbs address a row, which are silent when they succeed, which get an end
    /// marker, and what shape a read of each takes. <see cref="CliCommandBuilder"/>, <see cref="CliErrorParser"/> and
    /// <see cref="CliConnectionBase"/> ask here.
    /// </summary>
    internal static class CliVerbs
    {
        /// <summary>
        /// The verbs that address a row with <c>numbers=</c> and print nothing when they succeed. Each was measured: an
        /// action verb refuses <c>numbers=</c> (<c>/system/script/run numbers=*B</c> answers <i>bad parameter numbers
        /// (line 1 column 30)</i> on 7.24 where <c>run [find where .id=*B]</c> runs the script), and a verb that prints is
        /// reported as refused if it is listed here, so an unmeasured verb stays out.
        /// </summary>
        internal static bool IsRowWrite(string? verb)
        {
            switch (verb)
            {
                case "set":
                case "remove":
                case "enable":
                case "disable":
                case "move":
                case "unset":
                case "comment":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True for the verbs whose one-line form may carry an end marker: the row writes and <c>add</c>.</summary>
        internal static bool TakesEndMarker(string? verb) => IsRowWrite(verb) || verb == "add";

        /// <summary>The shape a read method runs <paramref name="verb"/> in.</summary>
        /// <remarks>
        /// One decision, so a verb nobody listed cannot lose its inputs in silence: only a table takes a <c>where</c>, and
        /// every other shape takes all parameters as inputs.
        /// </remarks>
        internal static CliReadShape ReadShapeOf(string verb)
        {
            switch (verb)
            {
                case "print":
                case "getall":
                    return CliReadShape.Table;
                case "get":
                    return CliReadShape.Get;
                case "wol":
                    return CliReadShape.EmptyRowAction;
                case "inspect":
                    return CliReadShape.ArgumentRead;
                case "run":
                    return CliReadShape.Action;
            }
            if (string.Equals(verb, "torch", StringComparison.OrdinalIgnoreCase))
                return CliReadShape.Torch;
            if (TikMonitorVerbs.SelfTerminating(verb))
                return CliReadShape.SelfTerminatingMonitor;
            if (TikMonitorVerbs.Contains(verb))
                return CliReadShape.Monitor;
            return IsRowWrite(verb) ? CliReadShape.RowWrite : CliReadShape.Other;
        }
    }
}

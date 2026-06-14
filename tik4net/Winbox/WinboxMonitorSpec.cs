namespace tik4net.Winbox
{
    /// <summary>
    /// Describes a streaming-monitor window harvested from the <c>.jg</c> catalog (a <c>type:'query'</c>
    /// window, or a <c>type:'action'</c> window with a <c>pollcmd</c>). A monitor is driven by re-polling the
    /// router over the normal request/reply channel — it is NOT a server push (see webfig
    /// <c>ObjectQuery</c>/<c>ObjectAction</c> and <c>_notes/winbox-native-m2-plan.md</c> §20):
    /// <list type="number">
    ///   <item><b>start</b> = SYS_CMD <see cref="StartCmd"/> (+ request fields) → reply carries the monitor
    ///         <c>.id</c> handle;</item>
    ///   <item><b>poll</b> = SYS_CMD <see cref="PollCmd"/> (+ the id) every <see cref="AutorefreshMs"/> ms →
    ///         rows (a query yields a record map under <c>Mfe0002</c>; a poll-action yields one status record);</item>
    ///   <item><b>cancel</b> = SYS_CMD <see cref="CancelCmd"/> (+ the id).</item>
    /// </list>
    /// </summary>
    internal sealed class WinboxMonitorSpec
    {
        internal int[] Handler { get; }

        /// <summary>SYS_CMD that starts the monitor; its reply returns the monitor <c>.id</c>.</summary>
        internal int StartCmd { get; }

        /// <summary>SYS_CMD issued each poll pass — a query window's <c>getallcmd</c> (default
        /// <see cref="WinboxM2Protocol.Command.GetAll"/>) or a poll-action's <c>pollcmd</c>.</summary>
        internal int PollCmd { get; }

        /// <summary>SYS_CMD that stops the monitor.</summary>
        internal int CancelCmd { get; }

        /// <summary>Poll interval in milliseconds (the <c>.jg</c> <c>autorefresh</c>, default 1000).</summary>
        internal int AutorefreshMs { get; }

        /// <summary><c>true</c> for a <c>type:'query'</c> window (rows under <c>Mfe0002</c>); <c>false</c> for a
        /// poll-action (a single status record per poll).</summary>
        internal bool IsQuery { get; }

        internal WinboxMonitorSpec(int[] handler, int startCmd, int pollCmd, int cancelCmd, int autorefreshMs, bool isQuery)
        {
            Handler = handler;
            StartCmd = startCmd;
            PollCmd = pollCmd;
            CancelCmd = cancelCmd;
            AutorefreshMs = autorefreshMs;
            IsQuery = isQuery;
        }
    }
}

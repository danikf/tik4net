using System;
using System.IO;

namespace tik4net.Winbox
{
    /// <summary>
    /// The router closed or reset a WinBox TCP connection before sending a single byte — how RouterOS refuses a
    /// connection while it holds its limit of WinBox/Dude sessions.
    /// </summary>
    /// <remarks>
    /// The limit is 100 sessions on RouterOS 7.24 and <c>/ip service winbox max-sessions</c> does not set it. On a
    /// RoMON agent the slots fill with dead WinBox relay links, which the agent reaps as soon as they are full; the
    /// refusal lasts a fraction of a second, so <see cref="Connection.TikOpenRetry"/> retries it after a pause
    /// (Docs/findings-romon.md, "The WinBox session limit").
    /// </remarks>
    internal sealed class WinboxSessionLimitException : IOException
    {
        internal WinboxSessionLimitException(Exception? inner)
            : base("The router closed the WinBox connection before sending anything. RouterOS does this when it "
                   + "already holds its limit of WinBox/Dude sessions (100 on RouterOS 7.24; /ip service winbox "
                   + "max-sessions does not change it), and logs 'too many winbox/dude sessions'. The open was retried "
                   + "and the router went on refusing; a new connection is accepted once sessions end.", inner)
        {
        }
    }
}

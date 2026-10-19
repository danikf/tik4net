using System;

namespace tik4net.Winbox
{
    /// <summary>
    /// The agent opened a WinBox link to the RoMON target, but nothing answered through it.
    /// </summary>
    /// <remarks>
    /// While <c>/tool romon ssh</c> relays start and end on the agent, now and then a link the agent has just opened
    /// never carries a reply — not to the first request, nor in the 30 s after (RouterOS 7.24 agent, 6.49.13 target;
    /// Docs/findings-romon.md, "Dead WinBox relay links hold the agent's slots"). The open checks the link with one
    /// request, and <see cref="Connection.TikOpenRetry"/> opens again on a new link when it is silent. Callers see the public
    /// <see cref="TikRomonRelayException"/> with <see cref="TikRomonRelayFailure.TargetDidNotRespond"/>.
    /// </remarks>
    internal sealed class WinboxRomonLinkSilentException : TikRomonRelayException
    {
        internal WinboxRomonLinkSilentException(string message, Exception? inner)
            : base(TikRomonRelayFailure.TargetDidNotRespond, message, inner)
        {
        }
    }
}

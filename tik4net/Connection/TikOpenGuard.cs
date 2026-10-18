using System;
using System.Net.Sockets;

namespace tik4net.Connection
{
    /// <summary>What a failed open lets out as itself, shared by the CLI transports and WinBox native.</summary>
    internal static class TikOpenGuard
    {
        /// <summary>
        /// What an open lets out unchanged; everything else the caller reports as a failed login
        /// (<see cref="TikConnectionLoginException"/>). The half-open client is closed either way.
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item>The caller's cancel (<see cref="OperationCanceledException"/>): not a login failure, the caller asked to
        /// stop. Wrapping it would break every <c>catch (OperationCanceledException)</c> written against the async
        /// surface.</item>
        /// <item>A refusal — the router's (<see cref="TikConnectionLoginException"/> and its kin) or the RoMON relay's
        /// (<see cref="TikRomonRelayException"/>): already the specific answer.</item>
        /// <item>A socket that never answered (<see cref="SocketException"/>: refused, unreachable, the connect timed
        /// out): the same exception the binary API throws for that port. A caller told to check credentials when the
        /// service is simply off is sent the wrong way.</item>
        /// </list>
        /// </remarks>
        internal static bool PassesThrough(Exception ex)
            => ex is OperationCanceledException || ex is TikConnectionLoginException
               || ex is TikRomonRelayException || ex is SocketException;
    }
}

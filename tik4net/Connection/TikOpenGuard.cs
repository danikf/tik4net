using System;
using System.Net.Sockets;

namespace tik4net.Connection
{
    /// <summary>
    /// What a failed open lets out as itself, on every transport: nothing answered is a <see cref="SocketException"/>; the
    /// router refused, or answered the login with silence, is a <see cref="TikConnectionLoginException"/> or its kin; it
    /// answered in another protocol is a <see cref="TikConnectionProtocolMismatchException"/> or
    /// <see cref="TikConnectionSSLErrorException"/>. Anything else is a <see cref="TikConnectionLoginException"/> wrapping it.
    /// </summary>
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
        /// <item>A peer that answered in another protocol (<see cref="TikConnectionProtocolMismatchException"/>,
        /// <see cref="TikConnectionSSLErrorException"/>): the wrong port or the wrong TLS setting, which a login failure
        /// would hide. Only the API's open raises them.</item>
        /// </list>
        /// </remarks>
        internal static bool PassesThrough(Exception ex)
            => ex is OperationCanceledException || ex is TikConnectionLoginException
               || ex is TikRomonRelayException || ex is SocketException
               || ex is TikConnectionProtocolMismatchException || ex is TikConnectionSSLErrorException;

        /// <summary>The exception an open throws for <paramref name="ex"/>: itself when it already says what happened, else a login failure wrapping it.</summary>
        internal static Exception Translate(Exception ex)
            => PassesThrough(ex) ? ex : new TikConnectionLoginException(ex);
    }
}

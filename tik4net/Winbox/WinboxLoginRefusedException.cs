using System;

namespace tik4net.Winbox
{
    /// <summary>
    /// The router answered the WinBox handshake with a refusal in its own words — on RouterOS 7.23.2,
    /// the 33-byte payload <c>"invalid user name or password (6)"</c> where a 32-byte confirmation
    /// digest belongs.
    /// </summary>
    /// <remarks>
    /// It exists because this refusal is <b>not</b> reliable evidence about the credentials, even though
    /// it is phrased as if it were. Measured on 7.23.2 (P2.41): about one WinBox login in one to two
    /// hundred is refused this way with credentials that are correct, and replaying the <b>identical</b>
    /// client key 50 ms later is accepted — nine replays out of nine. The bytes were never the problem,
    /// so the refusal is transient router state.
    /// <para>A genuinely wrong password produces this same message, so content cannot separate the two
    /// cases and only persistence can: <see cref="WinboxLoginRetry"/> retries a bounded number of times,
    /// and a refusal that survives that is reported as a credential failure. Do not "simplify" this into
    /// an immediate credential error — that is the behaviour P2.41 was filed against.</para>
    /// </remarks>
    internal sealed class WinboxLoginRefusedException : Exception
    {
        /// <summary>The router's own refusal text, verbatim.</summary>
        internal string RouterMessage { get; }

        internal WinboxLoginRefusedException(string routerMessage)
            : base("The router refused the WinBox login: \"" + routerMessage + "\".")
        {
            RouterMessage = routerMessage;
        }
    }
}

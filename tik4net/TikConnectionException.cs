using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace tik4net
{
    /// <summary>
    /// Any exception from mikrotik session.
    /// </summary>
    public abstract class TikConnectionException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionException"/> class.
        /// </summary>
        protected TikConnectionException()
            : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        protected TikConnectionException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The inner exception, if any.</param>
        protected TikConnectionException(string message, Exception? innerException)
            : base(message, innerException)
        {
        }

        ///// <summary>
        ///// Initializes a new instance of the <see cref="TikConnectionException"/> class.
        ///// </summary>
        ///// <param name="message">The exception message.</param>
        ///// <param name="command">The command sent to target.</param>
        //public TikConnectionException(string message, ITikCommand command)
        //    : this(FormatMessage(message, command, null))
        //{
        //}
    }

    /// <summary>
    /// Exception when command is performed via not opened <see cref="ITikConnection"/>.
    /// </summary>
    public class TikConnectionNotOpenException : TikConnectionException
    {
        /// <summary>
        /// .ctor
        /// </summary>
        /// <param name="message"></param>
        public TikConnectionNotOpenException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// As above, keeping whatever the torn-down transport threw.
        /// </summary>
        /// <remarks>
        /// Used when a <c>Close</c> on another thread pulls the socket out from under a running command: the
        /// framework exception that produced (an <see cref="ObjectDisposedException"/>, an
        /// <see cref="System.IO.IOException"/>) is worth keeping for a bug report, while the type the caller
        /// catches should still be a <see cref="TikConnectionException"/>.
        /// </remarks>
        /// <param name="message">Diagnostic message.</param>
        /// <param name="innerException">The underlying transport failure.</param>
        public TikConnectionNotOpenException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Exception when login failed (invalid credentials)
    /// </summary>
    public class TikConnectionLoginException : TikConnectionException
    {
        /// <summary>
        /// .ctor
        /// </summary>
        public TikConnectionLoginException(Exception innerException)
            : base("Cannot log in. " + innerException.Message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance for a subclass that composes its own message and has no underlying
        /// exception to carry — the router refusing a login is a reply, not a failure of something else.
        /// </summary>
        /// <param name="message">The complete message.</param>
        protected TikConnectionLoginException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance for a subclass that composes its own message AND has an underlying
        /// exception worth keeping — a login that timed out replaces the timeout with a description of
        /// what the session had done, and the timeout stays reachable underneath it.
        /// </summary>
        /// <param name="message">The complete message.</param>
        /// <param name="innerException">The exception this one replaces.</param>
        protected TikConnectionLoginException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when the router refused the login in its own words, and went on refusing it. Carries the
    /// router's verbatim message in <see cref="RouterMessage"/>.
    /// </summary>
    /// <remarks>
    /// <para>This is <b>not</b> reported on the router's first refusal. RouterOS occasionally refuses a
    /// login whose credentials are correct — measured on 7.23.2, about one EC-SRP5 login in one to two
    /// hundred, on every transport carrying that handshake (WinBox refuses inside the handshake with
    /// <c>"invalid user name or password (6)"</c>; MAC-Telnet completes the handshake and only then
    /// writes <c>"Login failed, incorrect username or password"</c> to the terminal and hangs up). The
    /// identical request, replayed 50 ms later, is accepted — nine replays out of nine.</para>
    /// <para>The connection therefore retries a bounded number of times by itself, and this exception is
    /// what is left when the refusal did <b>not</b> clear. So by the time a caller sees it, retrying is
    /// the one thing already known not to help: treat it as a credential failure, not as something to
    /// loop on.</para>
    /// <para>It derives from <see cref="TikConnectionLoginException"/>, so code that already catches that
    /// is unaffected.</para>
    /// </remarks>
    public class TikConnectionLoginRefusedException : TikConnectionLoginException
    {
        /// <summary>The router's own refusal text, verbatim — the router speaking, not our wording.</summary>
        public string RouterMessage { get; }

        /// <summary>Which login the router refused, e.g. <c>"WinBox"</c> or <c>"MAC-Telnet"</c>.</summary>
        public string Transport { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionLoginRefusedException"/> class.
        /// </summary>
        /// <param name="transport">Which login was refused, for the message.</param>
        /// <param name="routerMessage">The router's refusal text, verbatim.</param>
        public TikConnectionLoginRefusedException(string transport, string routerMessage)
            : base("Cannot log in. The router refused the " + transport + " login: \"" + routerMessage + "\".")
        {
            Transport     = transport;
            RouterMessage = routerMessage;
        }
    }

    /// <summary>
    /// Thrown when the router opened the session and then answered nothing at all to a correctly-formed
    /// login handshake — as opposed to <see cref="TikConnectionLoginRefusedException"/>, where it answered
    /// and said no.
    /// </summary>
    /// <remarks>
    /// A MAC-layer login reaches this only once the router has ACKNOWLEDGED the session start, so the
    /// router is demonstrably answering us and the silence is about the handshake rather than about
    /// reachability. Seen on RouterOS 7.24 roughly once per few hundred logins, and it clears on the next
    /// attempt — see <c>Winbox.RouterLoginRetry</c>, which treats it the same way it treats a refusal.
    /// <para><see cref="WaitDescription"/> carries what the session had done by then — whether our
    /// handshake packet was ever taken, how many resends were spent on it, what did arrive — because
    /// "timed out" on its own cannot tell a router that never took our bytes from one that took them and
    /// said nothing.</para>
    /// </remarks>
    public class TikConnectionLoginNoAnswerException : TikConnectionLoginException
    {
        /// <summary>Which login went unanswered, e.g. <c>"MAC-Telnet"</c>.</summary>
        public string Transport { get; }

        /// <summary>What the session had done by the time the wait expired.</summary>
        public string WaitDescription { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionLoginNoAnswerException"/> class.
        /// </summary>
        /// <param name="transport">Which login went unanswered, for the message.</param>
        /// <param name="waitDescription">What the session had done by the time the wait expired.</param>
        /// <param name="innerException">The timeout this replaces.</param>
        public TikConnectionLoginNoAnswerException(string transport, string waitDescription,
            Exception innerException)
            : base("Cannot log in. The router opened the " + transport
                   + " session and then answered nothing to the login handshake (" + waitDescription + ").",
                   innerException)
        {
            Transport       = transport;
            WaitDescription = waitDescription;
        }
    }

    /// <summary>
    /// Thrown when the peer on the other end of the socket is not speaking the protocol this connection
    /// is speaking — most often the plain binary API pointed at the API-SSL port, or the reverse.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is deliberately NOT a <see cref="TikConnectionSSLErrorException"/>: that one means TLS was
    /// negotiated and went wrong (an untrusted certificate, no common protocol version), whereas this one
    /// means there is no TLS on that port at all — or, in the other direction, that there is TLS where
    /// plain API words were sent. The two need opposite fixes, and a single "connection closed" told
    /// nobody which they were looking at.
    /// </para>
    /// <para>
    /// The router closing the connection mid-handshake is the only symptom either mistake produces, so the
    /// message names the port that was used and what normally listens there. A closed port raises
    /// <see cref="System.Net.Sockets.SocketException"/> instead and never reaches here.
    /// </para>
    /// </remarks>
    public class TikConnectionProtocolMismatchException : TikConnectionException
    {
        /// <summary>The TCP port that was connected to.</summary>
        public int Port { get; }

        /// <summary>Whether this connection tried to negotiate TLS (<c>ApiSsl</c>) or not (<c>Api</c>).</summary>
        public bool TlsAttempted { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionProtocolMismatchException"/> class.
        /// </summary>
        /// <param name="port">The TCP port that was connected to.</param>
        /// <param name="tlsAttempted">Whether TLS was attempted (<c>ApiSsl</c>) or not (<c>Api</c>).</param>
        /// <param name="plainPort">The port that serves the plain binary API on a default RouterOS.</param>
        /// <param name="sslPort">The port that serves API-SSL on a default RouterOS.</param>
        /// <param name="symptom">
        /// What was actually observed, as one sentence — the peer closed the socket, or it never answered.
        /// The advice that follows is derived from <paramref name="port"/>; this is the evidence for it.
        /// </param>
        /// <param name="innerException">The I/O failure this replaces, if there was one.</param>
        public TikConnectionProtocolMismatchException(int port, bool tlsAttempted,
            int plainPort, int sslPort, string symptom, Exception? innerException)
            : base(BuildMessage(port, tlsAttempted, plainPort, sslPort, symptom), innerException)
        {
            Port = port;
            TlsAttempted = tlsAttempted;
        }

        private static string BuildMessage(int port, bool tlsAttempted, int plainPort, int sslPort,
            string symptom)
        {
            // The likeliest single cause, named outright, because the port usually settles it.
            string likely;
            if (tlsAttempted && port == plainPort)
                likely = " Port " + plainPort.ToString(CultureInfo.InvariantCulture)
                       + " is the PLAIN API port — use TikConnectionType.Api for it, or connect to "
                       + sslPort.ToString(CultureInfo.InvariantCulture) + " for API-SSL.";
            else if (!tlsAttempted && port == sslPort)
                likely = " Port " + sslPort.ToString(CultureInfo.InvariantCulture)
                       + " is the API-SSL port — use TikConnectionType.ApiSsl for it, or connect to "
                       + plainPort.ToString(CultureInfo.InvariantCulture) + " for the plain API.";
            else if (tlsAttempted)
                likely = " Check that /ip/service has 'api-ssl' enabled on this port AND a certificate"
                       + " assigned to it — without a certificate RouterOS accepts the connection and then"
                       + " drops it exactly like this.";
            else
                likely = " Check that /ip/service has 'api' enabled on this port, and that whatever answers"
                       + " there is RouterOS rather than another service.";

            // The address list applies to both directions — a client outside it is dropped exactly like a
            // wrong port. The user's group policy only matters once a login is actually attempted.
            string alsoCheck = " Also check the address list on /ip/service"
                             + (tlsAttempted ? "." : ", and that the user's group has the 'api' policy.");

            return symptom + likely + alsoCheck
                 + " See https://github.com/danikf/tik4net/wiki/Exception-handling.";
        }
    }

    /// <summary>
    /// Thrown when API-SSL is not properly implemented on mikrotik.
    /// See https://github.com/danikf/tik4net/wiki/SSL-connection for details.
    /// </summary>
    public class TikConnectionSSLErrorException : TikConnectionException
    {
        /// <summary>
        /// .ctor
        /// </summary>
        public TikConnectionSSLErrorException(Exception innerException)
            : base("API-SSL error (see https://github.com/danikf/tik4net/wiki/SSL-connection). " + innerException.Message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when no response is received from the router within the configured
    /// <see cref="ITikConnection.ReceiveTimeout"/>. Distinct from a bare socket <see cref="System.IO.IOException"/>
    /// so callers can tell a stuck/unreachable peer apart from other I/O failures (e.g. connection reset).
    /// </summary>
    /// <remarks>
    /// On the binary API the <see cref="Exception.Message"/> also reports what the socket had been doing:
    /// how many bytes and complete sentences the connection has received, and how long ago each of those
    /// last happened. Read the two ages together — they name three different faults. Both as old as the
    /// timeout: the router stopped answering. Last byte recent, last sentence old: a reply is still being
    /// delivered and the deadline is too short rather than the connection broken. Both recent: the
    /// connection is busy and this command's <c>.tag</c> is the one not being served.
    /// <para>
    /// It also reports how many bytes are waiting <b>unread in the socket buffer</b>. The three counts above
    /// are the client's own; this one is the operating system's, and it is the only one that can say the
    /// router did answer and the client failed to collect it.
    /// </para>
    /// </remarks>
    public class TikConnectionReceiveTimeoutException : TikConnectionException
    {
        /// <summary>The configured receive timeout (milliseconds) that elapsed.</summary>
        public int TimeoutMilliseconds { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionReceiveTimeoutException"/> class.
        /// </summary>
        /// <param name="timeoutMilliseconds">The configured receive timeout (milliseconds) that elapsed.</param>
        /// <param name="innerException">The underlying socket timeout exception.</param>
        public TikConnectionReceiveTimeoutException(int timeoutMilliseconds, Exception innerException)
            : base($"No response received from the router within {timeoutMilliseconds} ms.", innerException)
        {
            TimeoutMilliseconds = timeoutMilliseconds;
        }

        /// <summary>
        /// Initializes a new instance with an explicit <paramref name="message"/>, for the case where a
        /// <b>partial</b> response arrived — the terminal transports can receive plenty of bytes and still
        /// never reach the end of the answer, which the default "no response received" wording would
        /// misdescribe. See <see cref="PartialResponse"/>.
        /// </summary>
        /// <param name="timeoutMilliseconds">The configured receive timeout (milliseconds) that elapsed.</param>
        /// <param name="message">Diagnostic message describing what was and was not received.</param>
        /// <param name="partialResponse">The incomplete text received before the timeout, if any.</param>
        /// <param name="innerException">The underlying exception, if any.</param>
        public TikConnectionReceiveTimeoutException(int timeoutMilliseconds, string message,
                                                    string? partialResponse = null, Exception? innerException = null)
            : base(message, innerException)
        {
            TimeoutMilliseconds = timeoutMilliseconds;
            PartialResponse = partialResponse;
        }

        /// <summary>
        /// The incomplete response received before the timeout, or <c>null</c> when nothing arrived (or the
        /// transport does not capture it). Exposed here rather than <i>returned as a successful result</i>: a
        /// truncated read is indistinguishable from a short one, so handing this text back would let a
        /// half-read table silently become "the table". Kept on the exception so a caller that wants the
        /// partial data can still reach it — deliberately, rather than by suppressing the error.
        /// </summary>
        public string? PartialResponse { get; }
    }

    /// <summary>
    /// Thrown when the router's session is no longer usable while the connection object is still open,
    /// so a command was never executed.
    /// </summary>
    /// <remarks>
    /// <para>The case this exists for is an idle MAC-layer session: RouterOS logs an idle terminal session
    /// out after roughly 30 seconds and says so in its own log (<c>user … logged out via mac-telnet</c>). It
    /// does not close the UDP socket and it sends no error, so from the client side the session simply
    /// stops answering — and before this type existed that surfaced as a full <see cref="ITikConnection.ReceiveTimeout"/>
    /// worth of waiting followed by "nothing was received", which names the symptom and not the cause.
    /// The same happens to a WinBox-MAC session carrying structured M2 rather than a terminal.</para>
    /// <para>It is only thrown when the router <b>never acknowledged the command's bytes</b>, which is what
    /// makes it safe to say the command did not run: the MAC layer acknowledges what it carries, so an
    /// unacknowledged command cannot have reached the router. The transport re-opens the session and retries
    /// once by itself; this exception means that retry also failed, or that the command was not one that may
    /// be retried — <c>WinboxNativeMacConnection</c> re-runs a READ, never an <c>add</c>/<c>set</c>, and
    /// nothing is re-run while Safe Mode is held (dropping the session is what rolls Safe Mode back).</para>
    /// <para>The binary API raises it for a different reason, and does not reconnect for you: a session that
    /// has answered <b>nothing at all</b> to two commands in a row is treated as no longer answering, and the
    /// third command is refused instead of sent. The socket is still open and RouterOS still executes what it
    /// receives — it is the replies that stop — so sending would risk a write that ran and could not be
    /// confirmed, and would cost another full <see cref="ITikConnection.ReceiveTimeout"/> to learn nothing.
    /// Refusing is why "did not run" is a statement of fact here too: this command was never written. The
    /// message carries the reader's byte and sentence counters, so it says whether the router went quiet or
    /// the client stopped collecting. Recover by opening a new connection — a fresh session to the same
    /// router is unaffected — and note that this is not the same as a partial read, which is
    /// <see cref="TikConnectionReceiveTimeoutException"/>.</para>
    /// </remarks>
    public class TikConnectionSessionClosedException : TikConnectionException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionSessionClosedException"/> class.
        /// </summary>
        /// <param name="message">Diagnostic message naming the transport and what the router did.</param>
        /// <param name="innerException">The underlying failure, if any.</param>
        public TikConnectionSessionClosedException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when a feature is invoked on a transport that does not report the required
    /// <see cref="TikConnectionCapability"/>. Check <see cref="ITikConnection"/> support up front with
    /// <see cref="TikConnectionCapabilityExtensions.Supports"/> to avoid it. See the
    /// <see href="https://github.com/danikf/tik4net/wiki/Connection-types-and-capabilities">capability matrix</see>
    /// for which transport supports what.
    /// </summary>
    public class TikConnectionCapabilityNotSupportedException : TikConnectionException
    {
        /// <summary>The capability the active transport does not support.</summary>
        public TikConnectionCapability Capability { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionCapabilityNotSupportedException"/> class.
        /// </summary>
        /// <param name="capability">The capability the transport does not support.</param>
        /// <param name="message">The message.</param>
        public TikConnectionCapabilityNotSupportedException(TikConnectionCapability capability, string message)
            : base(message)
        {
            Capability = capability;
        }
    }

    /// <summary>
    /// Thrown when the router answers with a sentence type this version of the library does not know —
    /// something other than <c>!re</c>, <c>!done</c>, <c>!trap</c>, <c>!fatal</c> or <c>!empty</c>.
    /// </summary>
    /// <remarks>
    /// This is a <b>forward-compatibility</b> signal, not a bug in the caller's code: RouterOS has added a
    /// sentence type before (<c>!empty</c> arrived in 7.18) and can do so again, and a client that predates
    /// the addition has no way to know what the new one means.
    /// <para>
    /// It is a <see cref="TikConnectionException"/> so that it is caught by code already handling connection
    /// failures. It replaces a bare <see cref="NotImplementedException"/>, which said nothing about which
    /// sentence had arrived and read as "the library author forgot to finish this" rather than "your router
    /// is newer than your client" — and, being outside the tik4net hierarchy, escaped every
    /// <c>catch (TikConnectionException)</c> a caller had written.
    /// </para>
    /// </remarks>
    public class TikUnknownSentenceTypeException : TikConnectionException
    {
        /// <summary>The sentence name the router sent, e.g. <c>!something-new</c>.</summary>
        public string SentenceName { get; }

        /// <summary>
        /// The words that followed it, so the unknown sentence can be reported without reproducing it.
        /// Empty when the sentence carried none.
        /// </summary>
        public IReadOnlyList<string> Words { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikUnknownSentenceTypeException"/> class.
        /// </summary>
        /// <param name="sentenceName">The unrecognised sentence name.</param>
        /// <param name="words">The words the sentence carried.</param>
        public TikUnknownSentenceTypeException(string sentenceName, IReadOnlyList<string>? words = null)
            : base("Router sent sentence type '" + sentenceName + "', which this version of tik4net does not "
                   + "know. This usually means the router is running a newer RouterOS than the library was "
                   + "written against. Words: "
                   + (words == null || words.Count == 0 ? "(none)" : string.Join(" ", words)))
        {
            SentenceName = sentenceName;
            Words = words ?? Array.Empty<string>();
        }
    }
}

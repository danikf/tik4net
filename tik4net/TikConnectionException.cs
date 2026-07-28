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
        /// <param name="innerException">The inner exception.</param>
        protected TikConnectionException(string message, Exception innerException)
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
                                                    string partialResponse = null, Exception innerException = null)
            : base(message, innerException)
        {
            TimeoutMilliseconds = timeoutMilliseconds;
            PartialResponse = partialResponse;
        }

        /// <summary>
        /// The incomplete response received before the timeout, or <c>null</c> when nothing arrived (or the
        /// transport does not capture it). Exposed because on the CLI transports this text used to be
        /// <i>returned to the caller as a successful result</i>: a truncated read is indistinguishable from a
        /// short one, so a half-read table silently became "the table". Kept on the exception so a caller
        /// that wants the partial data can still reach it — deliberately, rather than by suppressing the error.
        /// </summary>
        public string PartialResponse { get; }
    }

    /// <summary>
    /// Thrown when the router closed the transport session while the connection object was still open,
    /// so a command was never executed.
    /// </summary>
    /// <remarks>
    /// <para>The case this exists for is the MAC-Telnet console: RouterOS logs an idle terminal session out
    /// after roughly 30 seconds and says so in its own log (<c>user … logged out via mac-telnet</c>). It
    /// does not close the UDP socket and it sends no error, so from the client side the session simply
    /// stops answering — and before this type existed that surfaced as a full <see cref="ITikConnection.ReceiveTimeout"/>
    /// worth of waiting followed by "nothing was received", which names the symptom and not the cause.</para>
    /// <para>It is only thrown when the router <b>never acknowledged the command's bytes</b>, which is what
    /// makes it safe to say the command did not run: MAC-Telnet acknowledges the terminal byte stream, so an
    /// unacknowledged command cannot have reached the console. The transport re-opens the session and retries
    /// once by itself; this exception means that retry also failed.</para>
    /// </remarks>
    public class TikConnectionSessionClosedException : TikConnectionException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionSessionClosedException"/> class.
        /// </summary>
        /// <param name="message">Diagnostic message naming the transport and what the router did.</param>
        /// <param name="innerException">The underlying failure, if any.</param>
        public TikConnectionSessionClosedException(string message, Exception innerException = null)
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
}

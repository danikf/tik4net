using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using tik4net.Connection;

namespace tik4net
{
    /// <summary>
    /// Exception thrown if any error is returned from mikrotik router call or if any command related error occurs.
    /// </summary>
    public abstract class TikCommandException : TikConnectionException
    {
        /// <summary>
        /// Command which throws error.
        /// </summary>
        public ITikCommand Command { get; private set; }

        /// <summary>
        /// ctor.
        /// </summary>
        /// <param name="command">Commant that throws exception.</param>
        /// <param name="message">Message exception.</param>
        protected TikCommandException(ITikCommand command, string message)
            : base(message)
        {
            Command = command;
        }

        /// <summary>
        /// Returns exception description.
        /// </summary>
        /// <returns>Exception description.</returns>
        public override string ToString()
        {
            return
                (Command?.ToString() ?? "<no command>")
                + "\nMESSAGE: " + Message
                + "\n" + base.ToString();
        }
    }

    /// <summary>
    /// Exception thrown if any error is returned from mikrotik router call. (!TRAP)
    /// </summary>
    /// <seealso cref="ITikTrapSentence"/>
    public class TikCommandTrapException : TikCommandException
    {
        /// <summary>
        /// Code of the error.
        /// </summary>
        /// <seealso cref="ITikTrapSentence.CategoryCode"/>
        public string? Code { get; private set; }

        /// <summary>
        /// Code description of the error.
        /// </summary>
        /// <seealso cref="ITikTrapSentence.CategoryDescription"/>
        public string? CodeDescription { get; private set; }

        /// <summary>
        /// .ctor <see cref="Code"/> and <see cref="CodeDescription"/> are set from <paramref name="trapSentence"/>.
        /// </summary>
        /// <param name="command">Command that throws exception.</param>
        /// <param name="trapSentence">Error=trap sentence returned from mikrotik router as response to <paramref name="command"/> call.</param>
        public TikCommandTrapException(ITikCommand command, ITikTrapSentence trapSentence)
            : base(command, trapSentence.Message)
        {
            Code = trapSentence.CategoryCode;
            CodeDescription = trapSentence.CategoryDescription;
        }

        /// <summary>
        /// .ctor. <see cref="Code"/> and <see cref="CodeDescription"/> are null.
        /// </summary>
        /// <param name="command">Command that throws exception.</param>
        /// <param name="message">Additional message</param>
        protected TikCommandTrapException(ITikCommand command, string message)
            : base(command, message)
        {
            Code = null;
            CodeDescription = null;
        }
    }

    /// <summary>
    /// Exception thrown when invalid command is performed (invalid syntax). ('no such command' message from API)
    /// </summary>
    public class TikNoSuchCommandException : TikCommandTrapException
    {
        /// <summary>
        /// ctor.
        /// </summary>
        /// <param name="command">Commant that throws exception.</param>
        /// <param name="trapSentence">Error=trap sentence returned from mikrotik router as response to <paramref name="command"/> call.</param>
        public TikNoSuchCommandException(ITikCommand command, ITikTrapSentence trapSentence) : base(command, trapSentence)
        {
        }
    }


    /// <summary>
    /// Exception thrown when the <b>client</b> could not address an API path on the active transport — the
    /// router was never asked. Today this is the native WinBox M2 transport, which needs a path → M2 handler
    /// mapping (from the router's <c>.jg</c> menu catalog, a <c>PathAlias</c> or a <c>PathOverride</c>) before
    /// it can send anything at all.
    /// <para>
    /// It derives from <see cref="TikNoSuchCommandException"/> because the practical consequence is the same —
    /// the command cannot be run — but the <b>cause is ours, not the router's</b>. A plain
    /// <see cref="TikNoSuchCommandException"/> means the router itself refused ("no such command", e.g. a
    /// RouterOS package that is not installed); this one means our mapping does not cover the path, and the
    /// same command very likely works on the API and CLI transports. Callers that report "the router does not
    /// have this feature" must catch this type first and say something else, otherwise a gap in tik4net is
    /// reported as a fact about the router (which is how 142 unmapped-path skips hid behind "the required
    /// RouterOS package may not be installed").
    /// </para>
    /// </summary>
    /// <seealso cref="tik4net.WinboxNative.WinboxNativeConnection.PathAlias(string, string)"/>
    public class TikPathNotMappedException : TikNoSuchCommandException
    {
        /// <summary>The API path that could not be mapped, e.g. <c>/interface/eoip</c>.</summary>
        public string ApiPath { get; private set; }

        /// <summary>
        /// .ctor
        /// </summary>
        /// <param name="command">Command that throws exception.</param>
        /// <param name="apiPath">API path the transport could not map.</param>
        /// <param name="message">Description of the gap and how to bridge it.</param>
        public TikPathNotMappedException(ITikCommand command, string apiPath, string message)
            : base(command, new TikTrapSentenceResult(message))
        {
            ApiPath = apiPath;
        }
    }

    /// <summary>
    /// Exception thrown when item with identifier was not found. ('no such item' message from API)
    /// </summary>
    public class TikNoSuchItemException : TikCommandTrapException
    {
        /// <summary>
        /// ctor.
        /// </summary>
        /// <param name="command">Commant that throws exception.</param>
        /// <param name="trapSentence">Error=trap sentence returned from mikrotik router as response to <paramref name="command"/> call.</param>
        public TikNoSuchItemException(ITikCommand command, ITikTrapSentence trapSentence) : base(command, trapSentence)
        {
        }

        /// <summary>
        /// .ctor
        /// </summary>
        public TikNoSuchItemException(ITikCommand command)
            : base(command, $"no such item\n{command}")
        {
        }
    }

    /// <summary>
    /// Exception thrown when item with identifier alraedy exists. (e.q. 'already have device with such name' or 'failure: already have such address' message from API)
    /// </summary>
    public class TikAlreadyHaveSuchItemException : TikCommandTrapException
    {
        /// <summary>
        /// ctor.
        /// </summary>
        /// <param name="command">Commant that throws exception.</param>
        /// <param name="trapSentence">Error=trap sentence returned from mikrotik router as response to <paramref name="command"/> call.</param>
        public TikAlreadyHaveSuchItemException(ITikCommand command, ITikTrapSentence trapSentence) : base(command, trapSentence)
        {
        }
    }   

    /// <summary>
    /// Exception thrown if fatal  error is returned from mikrotik router call.  (!FATAL)
    /// </summary>
    public class TikCommandFatalException : TikCommandException
    {
        /// <summary>
        /// ctor.
        /// </summary>
        /// <param name="command">Commant that throws exception.</param>
        /// <param name="message">Message exception.</param>
        public TikCommandFatalException(ITikCommand command, string message)
            : base(command, message)
        {
        }
    }

    /// <summary>
    /// Exception thrown if command has been aborted.
    /// </summary>
    public class TikCommandAbortException : TikCommandException
    {
        /// <summary>
        /// ctor.
        /// </summary>
        /// <param name="command">Commant that throws exception.</param>
        /// <param name="message">Message exception.</param>
        public TikCommandAbortException(ITikCommand command, string message)
            : base(command, message)
        {
        }
    }

    /// <summary>
    /// Exception thrown if command returns unexpected error/fault.
    /// </summary>
    public class TikCommandUnexpectedResponseException : TikCommandException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TikCommandUnexpectedResponseException"/> class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="command">The command sent to target.</param>
        /// <param name="response">The response from target.</param>
        public TikCommandUnexpectedResponseException(string message, ITikCommand command, ITikSentence response)
            : base(command, FormatMessage(message, command, new ITikSentence[] { response }))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikCommandUnexpectedResponseException"/> class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="command">The command sent to target.</param>
        /// <param name="responseList">The response from target.</param>
        public TikCommandUnexpectedResponseException(string message, ITikCommand command, IEnumerable<ITikSentence> responseList)
            : base(command, FormatMessage(message, command, responseList))
        {
        }

        private static string FormatMessage(string message, ITikCommand command, IEnumerable<ITikSentence> responseList)
        {
            Guard.ArgumentNotNull(message, "message");
            StringBuilder result = new StringBuilder();
            result.AppendLine(message);
            if (command != null)
            {
                result.AppendLine("  COMMAND: " + command.CommandText);
                foreach (ITikCommandParameter param in command.Parameters)
                {
                    result.AppendLine("    " + param.ToString() + "    Format: " + param.ParameterFormat);
                }
            }

            if (responseList != null)
            {
                result.AppendLine("  RESPONSE:");
                foreach (ITikSentence sentence in responseList)
                {
                    result.AppendLine("    " + sentence.ToString());
                }
            }

            return result.ToString();
        }
    }

    /// <summary>
    /// Exception thrown when a value was requested (<see cref="ITikCommand.ExecuteScalar()"/>) from a command
    /// that the router accepted <b>without returning anything</b>. This is not an error reported by the router —
    /// it means the command produced no output at all, which is exactly what a successful write
    /// (<c>set</c>/<c>unset</c>/<c>remove</c>/<c>enable</c>/<c>comment</c>/…) does on every transport.
    /// <para>
    /// Use <see cref="ITikCommand.ExecuteNonQuery()"/> for commands that return nothing, or
    /// <see cref="ITikCommand.ExecuteScalarOrDefault()"/> when a value is optional.
    /// </para>
    /// <para>
    /// Distinct from <see cref="TikNoSuchItemException"/> ("no such item"): nothing is missing here. The
    /// item exists and the command succeeded — it simply had no value to return.
    /// </para>
    /// </summary>
    public class TikCommandEmptyResponseException : TikCommandException
    {
        /// <summary>
        /// .ctor
        /// </summary>
        /// <param name="command">Command that throws exception.</param>
        /// <param name="message">Description of what was expected and what the router returned instead.</param>
        public TikCommandEmptyResponseException(ITikCommand command, string message)
            : base(command, $"{message}\n{command}")
        {
        }
    }

    /// <summary>
    /// Exception thrown when exactly one item is expected but more than one was returned.
    /// </summary>
    public class TikCommandAmbiguousResultException : TikCommandException
    {
        /// <summary>
        /// .ctor
        /// </summary>
        public TikCommandAmbiguousResultException(ITikCommand command)
            : base(command, $"only one response item expected\n{command}")
        {
        }

        /// <summary>
        /// .ctor
        /// </summary>
        public TikCommandAmbiguousResultException(ITikCommand command, int ambiguousItemsCnt)
            : base(command, $"only one response item expected, returned {ambiguousItemsCnt} items\n{command}")
        {
        }
    }
}

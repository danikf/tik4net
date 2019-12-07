using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net
{
    /// <summary>
    /// Exception thrown if any error is returned from mikrotik router call or if any command related error occurs.
    /// </summary>
    public abstract class TikCommandException:TikConnectionException
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
        /// <param name="code">Code of the error.</param>
        /// <param name="codeDescription">Code description of the error.</param>
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
                Command.ToString()
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
        public string Code { get; private set; }

        /// <summary>
        /// Code description of the error.
        /// </summary>
        /// <seealso cref="ITikTrapSentence.CategoryDescription"/>
        public string CodeDescription { get; private set; }

        /// <summary>
        /// ctor.
        /// </summary>
        /// <param name="command">Commant that throws exception.</param>
        /// <param name="trapSentence">Error=trap sentence returned from mikrotik router as response to <paramref name="command"/> call.</param>
        public TikCommandTrapException(ITikCommand command, ITikTrapSentence trapSentence)
            : base(command, trapSentence.Message)
        {
            Code = trapSentence.CategoryCode;
            CodeDescription = trapSentence.CategoryDescription;
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

    public class TikCommandUnexpectedResponseException: TikCommandException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionException"/> class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="command">The command sent to target.</param>
        /// <param name="response">The response from target.</param>
        public TikCommandUnexpectedResponseException(string message, ITikCommand command, ITikSentence response)
            : base(command, FormatMessage(message, command, new ITikSentence[] { response }))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionException"/> class.
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
}

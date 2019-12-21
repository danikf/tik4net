using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
            : base(command, "no such item")
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

    public class TikCommandUnexpectedResponseException : TikCommandException
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

    /// <summary>
    /// Exception thrown when exactly one item is expected but more than one was returned.
    /// </summary>
    public class TikCommandAmbiguousResultException : TikCommandException
    {
        /// <summary>
        /// .ctor
        /// </summary>
        public TikCommandAmbiguousResultException(ITikCommand command)
            : base(command, "only one response item expected")
        {
        }

        /// <summary>
        /// .ctor
        /// </summary>
        public TikCommandAmbiguousResultException(ITikCommand command, int ambiguousItemsCnt)
            : base(command, $"only one response item expected, returned {ambiguousItemsCnt} items")
        {
        }
    }
}

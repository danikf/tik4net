using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace tik4net
{
    /// <summary>
    /// Any exception from mikrotik session.
    /// </summary>
    [Serializable]
    public class TikConnectionException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionException"/> class.
        /// </summary>
        /// <param name="info">The object that holds the serialized object data.</param>
        /// <param name="context">The contextual information about the source or destination.</param>
        protected TikConnectionException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionException"/> class.
        /// </summary>
        public TikConnectionException()
            : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        public TikConnectionException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The inner exception.</param>
        public TikConnectionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionException"/> class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="command">The command sent to target.</param>
        public TikConnectionException(string message, ITikCommand command)
            : this(FormatMessage(message, command, null))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionException"/> class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="command">The command sent to target.</param>
        /// <param name="response">The response from target.</param>
        public TikConnectionException(string message, ITikCommand command, ITikSentence response)
            : this(FormatMessage(message, command, new ITikSentence[] { response }))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TikConnectionException"/> class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="command">The command sent to target.</param>
        /// <param name="responseList">The response from target.</param>
        public TikConnectionException(string message, ITikCommand command, IEnumerable<ITikSentence> responseList)
            : this(FormatMessage(message, command, responseList))
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
                    result.AppendLine("    " + param.ToString());
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net
{
    /// <summary>
    /// Exception thrown if any error is returned from mikrotik router call or if any commadn related error occurs.
    /// </summary>
    /// <seealso cref="ITikTrapSentence"/>
    public class TikCommandException:Exception
    {
        /// <summary>
        /// Command which throws error.
        /// </summary>
        public ITikCommand Command { get; private set; }

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
        /// <param name="message">Message exception.</param>
        public TikCommandException(ITikCommand command, string message)
            :base(message)
        {
            Command = command;
        }

        /// <summary>
        /// ctor.
        /// </summary>
        /// <param name="command">Commant that throws exception.</param>
        /// <param name="message">Message exception.</param>
        /// <param name="code">Code of the error.</param>
        /// <param name="codeDescription">Code description of the error.</param>
        public TikCommandException(ITikCommand command, string code, string codeDescription, string message)
            : this(command, message)
        {
            Code = code;
            CodeDescription = codeDescription;
        }

        /// <summary>
        /// ctor.
        /// </summary>
        /// <param name="command">Commant that throws exception.</param>
        /// <param name="trapSentence">Error=trap sentence returned from mikrotik router as response to <paramref name="command"/> call.</param>
        public TikCommandException(ITikCommand command, ITikTrapSentence trapSentence)
            : this(command, trapSentence.CategoryCode, trapSentence.CategoryDescription, trapSentence.Message)
        {
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
}

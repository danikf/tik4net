using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Api
{
    internal class ApiFatalSentence : ApiSentence
    {
        public string Message { get; private set; }

        /// <summary>
        /// True when this <c>!fatal</c> is the library's own answer to the CALLER closing the connection,
        /// rather than anything the router or the network did.
        /// </summary>
        /// <remarks>
        /// The two have to be told apart before a monitor reports one. A connection that died under a
        /// running <c>ExecuteWithCallback</c> is news — without it the monitor just stops and the caller goes
        /// on believing it is listening. A connection the caller closed themselves is not: they know, and
        /// delivering it to their error callback turns an ordinary shutdown into a reported failure.
        /// Carried as a flag rather than matched out of the message text, because that text is a diagnostic
        /// and should stay free to change.
        /// </remarks>
        public bool ClientInitiated { get; }

        public ApiFatalSentence(IEnumerable<string> words, bool clientInitiated)
            : base(words)
        {
            Message = string.Join("\n", words.ToArray());
            ClientInitiated = clientInitiated;
        }

        /// <summary>
        /// The exception that ended the reader, when this <c>!fatal</c> is the library's report of it rather
        /// than a sentence the router sent. Handed on as the inner exception, because the flattened message
        /// is all a caller would otherwise get — and for a fault on the client (an assembly that failed to
        /// bind) the detail that diagnoses it lives only on the original.
        /// </summary>
        public Exception? Cause { get; }

        public ApiFatalSentence(IEnumerable<string> words, Exception? cause = null)
            : base(words)
        {
            Message = string.Join("\n", words.ToArray());
            Cause = cause;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using tik4net.Api;

namespace tik4net
{
    /// <summary>
    /// Exception called when response sentence from mikrotik router is not in proper format.
    /// </summary>
    public class TikSentenceException : TikConnectionException
    {
        private readonly ITikSentence? _sentence;

        /// <summary>
        /// The sentence that could not be read, or <c>null</c> when the failure happened before there was
        /// one to hand over.
        /// <para>
        /// Nullable deliberately, and not a formality: the binary API always has a parsed sentence to
        /// attach, but the CLI transports parse the router's raw <c>as-value</c>/JSON output, and a failure
        /// there is a failure to build a sentence at all. Declaring it non-nullable would have told a
        /// caller writing <c>ex.Sentence.Words</c> that it was safe on every transport, and it is not.
        /// </para>
        /// </summary>
        public ITikSentence? Sentence
        {
            get { return _sentence; }
        }

        /// <summary>
        /// ctor.
        /// </summary>
        /// <param name="message">Exception message.</param>
        /// <param name="sentence">
        /// Sentence with error - not proper format, or <c>null</c> when the failure happened before a
        /// sentence existed (see <see cref="Sentence"/>).
        /// </param>
        public TikSentenceException(string message, ITikSentence? sentence = null)
            : base(message)
        {
            _sentence = sentence;
        }
    }
}

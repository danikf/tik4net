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
    public class TikSentenceException : Exception
    {
        private ITikSentence _sentence;

        /// <summary>
        /// Sentence with error - not proper format.
        /// </summary>
        public ITikSentence Sentence
        {
            get { return _sentence; }
        }

        /// <summary>
        /// ctor.
        /// </summary>
        /// <param name="message">Exception message.</param>
        /// <param name="sentecne">Sentence with error - not proper format.</param>
        public TikSentenceException(string message, ITikSentence sentecne) 
            : base(message)
        {
            _sentence = sentecne;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net
{
    /// <summary>
    /// EventArgs used to pass written or read WORD to/from mikrotik router by <see cref="ITikConnection"/>.
    /// </summary>
    public class TikConnectionCommCallbackEventArgs: EventArgs
    {
        /// <summary>
        /// Read or written WORD by <see cref="ITikConnection"/>.
        /// </summary>
        public string Word { get; private set; }

        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="word">Read or written WORD by <see cref="ITikConnection"/></param>
        public TikConnectionCommCallbackEventArgs(string word)
        {
            Word = word;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net
{
    /// <summary>
    /// Base of all sentences returned from mikrotik router as response to request.
    /// </summary>
    public interface ITikSentence
    {
        /// <summary>
        /// All sentence words (properties). {fieldName, value}
        /// </summary>
#if V35 || V40
        IDictionary<string, string> Words { get; }
#else
        IReadOnlyDictionary<string, string> Words { get; }
#endif

        /// <summary>
        /// Tag of sentence (see asynchronous commands fro details).
        /// </summary>
        /// <seealso cref="ITikConnection.CallCommandAsync(IEnumerable{string}, string, Action{ITikSentence})"/>
        /// <seealso cref="ITikCommand.ExecuteAsync"/>
        string Tag { get; }
    }
}

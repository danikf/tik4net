using System.Collections.Generic;

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
        IReadOnlyDictionary<string, string> Words { get; }

        /// <summary>
        /// The binary API's <c>.tag</c>; empty on every other transport.
        /// </summary>
        /// <seealso cref="ITikCommand.ExecuteWithCallback"/>
        string Tag { get; }
    }
}

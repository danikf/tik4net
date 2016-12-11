using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net
{
    /// <summary>
    /// Response sentence (<see cref="ITikSentence"/>) from mikrotik router with !re data. 
    /// It is data sentence (typically when list of entities is requested).
    /// </summary>
    /// <seealso cref="ITikSentence"/>
    /// <seealso cref="ITikDoneSentence"/>
    /// <seealso cref="ITikTrapSentence"/>
    public interface ITikReSentence: ITikSentence
    {

        /// <summary>
        /// Gets the .id property. Throws exception if property .id has not been returned from mikrotik router as part of response sentence.
        /// </summary>
        /// <returns>Value of the .id property.  =.id=value</returns>
        /// <exception cref="TikSentenceException">When word/property has not been found in response sentence.</exception>
        /// <seealso cref="TikSpecialProperties.Id"/>
        string GetId();

        /// <summary>
        /// Gets the sentence word (one property). Throws exception if property with given name has not been returned from mikrotik router as part of response sentence.
        /// </summary>
        /// <param name="fieldName">Name of the word (property). =name=value</param>
        /// <returns>Value of the word (property) with given <paramref name="fieldName"/>.  =name=value</returns>
        /// <exception cref="TikSentenceException">When word/property has not been found in response sentence.</exception>
        string GetResponseField(string fieldName);

        /// <summary>
        /// Tries to get the sentence word (one property). Returns false if property with given name has not been returned from mikrotik router as part of response sentence.
        /// </summary>
        /// <param name="fieldName">Name of the word (property). =name=value</param>
        /// <param name="fieldValue">Value of the word (property) with given <paramref name="fieldName"/>.  =name=value</param>
        /// <returns>True if word (property) with given name has been found - has been returned from mikrotik router as part of response sentence</returns>
        bool TryGetResponseField(string fieldName, out string fieldValue);

        /// <summary>
        /// Gets the sentence word (one property). Resturns <paramref name="defaultValue"/> if property with given name has not been returned from mikrotik router as part of response sentence.
        /// </summary>
        /// <param name="fieldName">Name of the word (property). =name=value</param>
        /// <param name="defaultValue">Default value, which is returned if property with given name has not been returned from mikrotik router as part of response sentence.</param>
        /// <returns>Value of the word (property) with given <paramref name="fieldName"/> or <paramref name="defaultValue"/>.  =name=value</returns>
        string GetResponseFieldOrDefault(string fieldName, string defaultValue);
    }
}

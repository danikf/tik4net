using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net
{
    /// <summary>
    /// Response sentence (<see cref="ITikSentence"/>) from mikrotik router with !done status. 
    /// It is last sentence from successful operation.
    /// </summary>
    /// <seealso cref="ITikSentence"/>
    /// <seealso cref="ITikReSentence"/>
    /// <seealso cref="ITikTrapSentence"/>
    public interface ITikDoneSentence: ITikSentence
    {
        /// <summary>
        /// Gets the =ret sentence word (result). Throws exception if property with name =ret has not been returned from mikrotik router as part of done sentence.
        /// </summary>
        /// <seealso cref="TikSpecialProperties.Ret"/>        
        string GetResponseWord();

        /// <summary>
        /// Gets the =ret sentence word (result). Returns <paramref name="defaultValue"/> if property with name =ret has not been returned from mikrotik router as part of done sentence. 
        /// </summary>
        /// <seealso cref="TikSpecialProperties.Ret"/>        
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(defaultValue))]
        string? GetResponseWordOrDefault(string? defaultValue);
    }
}

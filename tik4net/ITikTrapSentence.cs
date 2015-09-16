using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net
{
    /// <summary>
    /// Response sentence (<see cref="ITikSentence"/>) from mikrotik router with !trap status. 
    /// This sentence is returned when any error occurs.
    /// </summary>
    /// <seealso cref="ITikSentence"/>
    /// <seealso cref="ITikReSentence"/>
    /// <seealso cref="ITikDoneSentence"/>
    /// <seealso cref="TikCommandException"/>
    public interface ITikTrapSentence: ITikSentence
    {
        /// <summary>
        /// Code of the error category.
        /// </summary>
        string CategoryCode { get; }

        /// <summary>
        /// Readable description of the <see cref="CategoryCode"/>. (taken from documentation)
        /// </summary>
        string CategoryDescription { get; }

        /// <summary>
        /// Message of the error from mikrotik router.
        /// </summary>
        string Message { get; }
    }
}

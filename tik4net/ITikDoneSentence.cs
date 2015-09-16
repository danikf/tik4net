using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net
{
    /// <summary>
    /// Response sentence (<see cref="ITikSentence"/>) from mikrotik router with !done status. 
    /// It is last sentence from sucessfull operation.
    /// </summary>
    /// <seealso cref="ITikSentence"/>
    /// <seealso cref="ITikReSentence"/>
    /// <seealso cref="ITikTrapSentence"/>
    public interface ITikDoneSentence: ITikSentence
    {
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace tik4net
{
    public class TikCommandException:Exception
    {
        public ITikCommand Command { get; private set; }

        public string Code { get; private set; }       
        
        public string CodeDescription { get; } 

        public TikCommandException(ITikCommand command, string message)
            :base(message)
        {
            Command = command;
        }

        public TikCommandException(ITikCommand command, string code, string codeDescription, string message)
            : this(command, message)
        {
            Code = code;
            CodeDescription = codeDescription;
        }

        public TikCommandException(ITikCommand command, ITikTrapSentence trapSentence)
            : this(command, trapSentence.CategoryCode, trapSentence.CategoryDescription, trapSentence.Message)
        {
        }
    }
}

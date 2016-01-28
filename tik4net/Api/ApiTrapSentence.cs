using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Api
{
    internal class ApiTrapSentence: ApiSentence, ITikTrapSentence
    {
        public string CategoryCode
        {
            get { return GetWordValueOrDefault("category", "-1"); }
        }

        public string CategoryDescription
        {
            get
            {
                switch(CategoryCode)
                {
                    case "-1": return "category not provided";
                    case "0": return "missing item or command";
                    case "1": return "argument value failure";
                    case "2": return "execution of command interrupted";
                    case "3": return "scripting related failure";
                    case "4": return "general failure";
                    case "5": return "API related failure";
                    case "6": return "TTY related failure";
                    case "7": return "value generated with :return command";
                    default: return "unknown";
                }
            }
        }

        public string Message
        {
            get { return GetWordValueOrDefault("message", string.Empty); }
        }

        public ApiTrapSentence(IEnumerable<string> words)
            : base(words)
        {

        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Api
{
    internal class ApiDoneSentence : ApiSentence, ITikDoneSentence
    {
        public ApiDoneSentence(IEnumerable<string> words) 
            : base(words)
        {
        }

        public string GetResponseWord(string wordName)
        {
            return GetWordValue(wordName);
        }
    }
}

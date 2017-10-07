using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tik4Net.Api
{
    internal class ApiDoneSentence : ApiSentence, ITikDoneSentence
    {
        public ApiDoneSentence(IEnumerable<string> words) 
            : base(words)
        {
        }

        public string GetResponseWord()
        {
            return GetWordValue(TikSpecialProperties.Ret);
        }
    }
}

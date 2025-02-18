using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace tik4net.Api
{
    internal class ApiEmptySentence : ApiReSentence, ITikDoneSentence
    {
        public ApiEmptySentence() 
            : base(Array.Empty<string>())
        {
        }

        public string GetResponseWord()
        {
            return GetWordValue(TikSpecialProperties.Ret);
        }

        public string GetResponseWordOrDefault(string defaultValue)
        {
            return GetWordValueOrDefault(TikSpecialProperties.Ret, defaultValue);
        }
    }
}

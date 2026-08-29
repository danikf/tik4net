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

        public string GetResponseWord()
        {
            return GetWordValue(TikSpecialProperties.Ret);
        }

        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(defaultValue))]
        public string? GetResponseWordOrDefault(string? defaultValue)
        {
            return GetWordValueOrDefault(TikSpecialProperties.Ret, defaultValue);
        }
    }
}
